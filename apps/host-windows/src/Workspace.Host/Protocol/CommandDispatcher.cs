using System.Text.Json;
using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;

namespace Workspace.Host.Protocol;

public sealed record DispatchOutcome(
    ProtocolEnvelope Response,
    IReadOnlyList<ProtocolEnvelope> Events);

public interface IWindowFocusService
{
    Task FocusAsync(string entityId, CancellationToken cancellationToken);
}

public sealed class UnavailableWindowFocusService : IWindowFocusService
{
    public Task FocusAsync(string entityId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("Window focus is not available until input routing is connected.");
    }
}

public sealed class CommandDispatcher(
    IApplicationCatalog applicationCatalog,
    ApplicationLauncher applicationLauncher,
    AtomicWorkspaceStore workspaceStore,
    IWindowFocusService windowFocusService)
{
    public async Task<DispatchOutcome> DispatchAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!string.Equals(command.Type, "command", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(command.Id)
            || string.IsNullOrWhiteSpace(command.Operation))
        {
            return Error(command.Id, "invalid_command", "A command requires an id and operation.");
        }

        try
        {
            return command.Operation switch
            {
                "application.list" => await ListApplicationsAsync(command.Id, cancellationToken),
                "application.launch" => await LaunchApplicationAsync(command, cancellationToken),
                "entity.setPresentation" => await SetPresentationAsync(command, cancellationToken),
                "window.focus" => await FocusWindowAsync(command, cancellationToken),
                _ => Error(
                    command.Id,
                    "unsupported_operation",
                    $"Unsupported workspace operation: {command.Operation}."),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Error(command.Id, "operation_failed", exception.Message);
        }
    }

    private async Task<DispatchOutcome> ListApplicationsAsync(
        string commandId,
        CancellationToken cancellationToken)
    {
        var applications = await applicationCatalog.ListAsync(cancellationToken);
        return Result(commandId, applications);
    }

    private async Task<DispatchOutcome> LaunchApplicationAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        var requestedApplication = GetRequestedApplication(command);
        if (string.IsNullOrWhiteSpace(requestedApplication))
        {
            return Error(command.Id, "invalid_target", "Application launch requires a name or application id.");
        }

        var applications = await applicationCatalog.ListAsync(cancellationToken);
        var application = applications.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, requestedApplication, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.DisplayName, requestedApplication, StringComparison.OrdinalIgnoreCase));

        if (application is null)
        {
            return Error(
                command.Id,
                "application_not_found",
                $"Application '{requestedApplication}' was not found.");
        }

        var launch = await applicationLauncher.LaunchAsync(application, cancellationToken);
        var payload = new { applicationId = launch.ApplicationId };

        return new DispatchOutcome(
            ProtocolEnvelope.Result(command.Id!, payload),
            [ProtocolEnvelope.EventMessage("APPLICATION_LAUNCHED", payload)]);
    }

    private async Task<DispatchOutcome> SetPresentationAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target) || command.Payload is null)
        {
            return Error(
                command.Id,
                "invalid_target",
                "Presentation updates require an entity target and presentation payload.");
        }

        PresentationState? presentation;
        try
        {
            presentation = command.Payload.Value.Deserialize<PresentationState>(
                ProtocolEnvelope.SerializerOptions);
        }
        catch (JsonException)
        {
            return Error(command.Id, "invalid_payload", "Presentation payload is invalid.");
        }

        if (presentation is null)
        {
            return Error(command.Id, "invalid_payload", "Presentation payload is invalid.");
        }

        var document = await workspaceStore.LoadAsync(cancellationToken);
        var entityIndex = document.Entities.FindIndex(entity =>
            string.Equals(entity.Id, command.Target, StringComparison.Ordinal));

        if (entityIndex < 0)
        {
            return Error(command.Id, "entity_not_found", $"Entity '{command.Target}' was not found.");
        }

        document.Entities[entityIndex] = document.Entities[entityIndex] with
        {
            Presentation = presentation,
        };

        await workspaceStore.SaveAsync(document, cancellationToken);

        var payload = new { entityId = command.Target, presentation };
        return new DispatchOutcome(
            ProtocolEnvelope.Result(command.Id!, payload),
            [ProtocolEnvelope.EventMessage("PRESENTATION_UPDATED", payload)]);
    }

    private async Task<DispatchOutcome> FocusWindowAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return Error(command.Id, "invalid_target", "Window focus requires a semantic window entity id.");
        }

        await windowFocusService.FocusAsync(command.Target, cancellationToken);
        return Result(command.Id!, new { entityId = command.Target });
    }

    private static string? GetRequestedApplication(ProtocolEnvelope command)
    {
        if (!string.IsNullOrWhiteSpace(command.Target))
        {
            return command.Target.Trim();
        }

        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
        {
            return null;
        }

        if (payload.TryGetProperty("applicationId", out var id)
            && id.ValueKind == JsonValueKind.String)
        {
            return id.GetString()?.Trim();
        }

        if (payload.TryGetProperty("displayName", out var name)
            && name.ValueKind == JsonValueKind.String)
        {
            return name.GetString()?.Trim();
        }

        return null;
    }

    private static DispatchOutcome Result(string commandId, object? payload = null) =>
        new(ProtocolEnvelope.Result(commandId, payload), []);

    private static DispatchOutcome Error(string? commandId, string code, string message) =>
        new(ProtocolEnvelope.Error(commandId, code, message), []);
}
