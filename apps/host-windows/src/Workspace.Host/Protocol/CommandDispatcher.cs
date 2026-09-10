using System.Text.Json;
using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Windows;

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
    IWorkspaceStore workspaceStore,
    IWindowFocusService windowFocusService,
    IWindowCatalog? windowCatalog = null,
    WindowReconciler? windowReconciler = null,
    IInputRouter? inputRouter = null,
    ApplicationControlService? applicationControlService = null)
{
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public Task ReleaseInputAsync(CancellationToken cancellationToken) =>
        inputRouter?.ReleaseAllAsync(cancellationToken) ?? Task.CompletedTask;

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
                "application.search" => await SearchApplicationsAsync(command, cancellationToken),
                "application.profile.list" => await ListProfilesAsync(command.Id, cancellationToken),
                "application.profile.save" => await SaveProfileAsync(command, cancellationToken),
                "application.profile.delete" => await DeleteProfileAsync(command, cancellationToken),
                "application.open" => await OpenApplicationAsync(command, cancellationToken),
                "application.close" => await CloseApplicationAsync(command, cancellationToken),
                "application.restart" => await RestartApplicationAsync(command, cancellationToken),
                "surface.bindWindow" => await BindSurfaceWindowAsync(command, cancellationToken),
                "entity.setPresentation" => await SetPresentationAsync(command, cancellationToken),
                "window.focus" => await FocusWindowAsync(command, cancellationToken),
                "window.input" => await RouteWindowInputAsync(command, cancellationToken),
                "surface.open" => await OpenSurfaceAsync(command, cancellationToken),
                "surface.frame" => await ReadSurfaceFrameAsync(command, cancellationToken),
                "surface.close" => await CloseSurfaceAsync(command, cancellationToken),
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
        catch (InputTargetNotPermittedException exception)
        {
            return Error(command.Id, "INPUT_TARGET_NOT_PERMITTED", exception.Message);
        }
        catch (JsonException)
        {
            return Error(command.Id, "invalid_payload", "Request payload is invalid.");
        }
        catch (ApplicationControlException exception)
        {
            return Error(command.Id, exception.Code, exception.Message);
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

    private async Task<DispatchOutcome> SearchApplicationsAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        var control = RequireApplicationControl();
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Application search requires a query payload.");
        return Result(command.Id!, await control.SearchAsync(ApplicationControlRequestParser.ParseSearch(payload), cancellationToken));
    }

    private async Task<DispatchOutcome> ListProfilesAsync(string commandId, CancellationToken cancellationToken) =>
        Result(commandId, await RequireApplicationControl().ListProfilesAsync(cancellationToken));

    private async Task<DispatchOutcome> SaveProfileAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Profile save requires a profile payload.");
        var request = ApplicationControlRequestParser.ParseProfile(payload);
        var profile = new ApplicationLaunchProfile(request.Id, request.DisplayName, request.ApplicationId,
            request.Arguments, request.WorkingDirectory, request.LaunchPolicy!.Value, request.PreferredSurfaceId,
            request.PreferredPresentation);
        await RequireApplicationControl().SaveProfileAsync(profile, cancellationToken);
        return Result(command.Id!, profile);
    }

    private async Task<DispatchOutcome> DeleteProfileAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Profile delete requires a profile id payload.");
        var profileId = ApplicationControlRequestParser.ParseProfileId(payload);
        var deleted = await RequireApplicationControl().DeleteProfileAsync(profileId, cancellationToken);
        return Result(command.Id!, new { profileId, deleted });
    }

    private async Task<DispatchOutcome> OpenApplicationAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Application open requires a structured payload.");
        var request = ApplicationControlRequestParser.ParseOpen(payload);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) && string.IsNullOrWhiteSpace(request.ProfileId))
            return Error(command.Id, "invalid_target", "Application open requires an application id or profile id.");
        var result = await RequireApplicationControl().OpenAsync(new ApplicationOpenRequest(command.Id!,
            request.ApplicationId, request.ProfileId, request.LaunchPolicy, request.SurfaceEntityId,
            request.ReplaceOccupied, request.ApprovalSource), cancellationToken);
        return Result(command.Id!, result);
    }

    private async Task<DispatchOutcome> CloseApplicationAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Application close requires a structured payload.");
        return Result(command.Id!, await RequireApplicationControl().CloseAsync(
            ApplicationControlRequestParser.ParseClose(command.Id!, payload), cancellationToken));
    }

    private async Task<DispatchOutcome> RestartApplicationAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Application restart requires a structured payload.");
        return Result(command.Id!, await RequireApplicationControl().RestartAsync(
            ApplicationControlRequestParser.ParseRestart(command.Id!, payload), cancellationToken));
    }

    private async Task<DispatchOutcome> BindSurfaceWindowAsync(ProtocolEnvelope command, CancellationToken cancellationToken)
    {
        if (command.Payload is not { ValueKind: JsonValueKind.Object } payload)
            return Error(command.Id, "invalid_payload", "Surface binding requires a structured payload.");
        var request = ApplicationControlRequestParser.ParseSurfaceBind(payload);
        await RequireApplicationControl().BindWindowAsync(request, cancellationToken);
        return Result(command.Id!, new { request.SurfaceEntityId, request.WindowEntityId });
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
        var events = new List<ProtocolEnvelope>
        {
            ProtocolEnvelope.EventMessage(
                "APPLICATION_LAUNCHED",
                new { applicationId = launch.ApplicationId }),
        };

        string? windowEntityId = null;
        var surfaceAvailable = false;
        if (windowCatalog is not null && windowReconciler is not null)
        {
            var snapshot = await WaitForWindowAsync(
                application.Id,
                launch.ProcessId,
                cancellationToken);
            if (snapshot is not null)
            {
                windowEntityId = windowReconciler.ResolveEntityId(snapshot);
                try
                {
                    await windowReconciler.TrackAsync(snapshot, cancellationToken);
                    surfaceAvailable = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    events.Add(ProtocolEnvelope.EventMessage(
                        "SURFACE_UNAVAILABLE",
                        new { entityId = windowEntityId, message = exception.Message }));
                }

                events.AddRange(await PersistLaunchedWindowAsync(
                    application,
                    snapshot,
                    windowEntityId,
                    surfaceAvailable,
                    cancellationToken));
            }
        }

        var payload = new
        {
            applicationId = launch.ApplicationId,
            windowEntityId,
            surfaceAvailable,
        };

        return new DispatchOutcome(
            ProtocolEnvelope.Result(command.Id!, payload),
            events);
    }

    private async Task<WindowSnapshot?> WaitForWindowAsync(
        string applicationId,
        int launchedProcessId,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 50;
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            var windows = await windowCatalog!.ListAsync(cancellationToken);
            var snapshot = windows.FirstOrDefault(candidate =>
                candidate.IsVisible
                && !candidate.IsMinimized
                && candidate.Bounds.Width > 0
                && candidate.Bounds.Height > 0
                && (candidate.ProcessId == launchedProcessId
                    || string.Equals(
                        candidate.ApplicationId,
                        applicationId,
                        StringComparison.OrdinalIgnoreCase)));
            if (snapshot is not null)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        return null;
    }

    private async Task<IReadOnlyList<ProtocolEnvelope>> PersistLaunchedWindowAsync(
        ApplicationDescriptor application,
        WindowSnapshot snapshot,
        string windowEntityId,
        bool surfaceAvailable,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var document = await workspaceStore.LoadAsync(cancellationToken);
            var events = new List<ProtocolEnvelope>();

            if (document.Entities.All(entity => entity.Id != application.Id))
            {
                var applicationEntity = WorkspaceEntity.CreateApplication(
                    application.Id,
                    application.DisplayName);
                document.Entities.Add(applicationEntity);
                events.Add(ProtocolEnvelope.EventMessage("ENTITY_CREATED", applicationEntity));
            }

            var candidate = WorkspaceEntity.CreateWindow(
                windowEntityId,
                string.IsNullOrWhiteSpace(snapshot.Title) ? application.DisplayName : snapshot.Title,
                application.Id,
                surfaceAvailable ? "available" : "unavailable");
            var existingIndex = document.Entities.FindIndex(entity => entity.Id == windowEntityId);
            var eventName = "ENTITY_CREATED";
            if (existingIndex >= 0)
            {
                candidate = candidate with
                {
                    Presentation = document.Entities[existingIndex].Presentation,
                };
                document.Entities[existingIndex] = candidate;
                eventName = "ENTITY_UPDATED";
            }
            else
            {
                document.Entities.Add(candidate);
            }

            await workspaceStore.SaveAsync(document, cancellationToken);
            events.Add(ProtocolEnvelope.EventMessage(eventName, candidate));
            return events;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<DispatchOutcome> OpenSurfaceAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (windowReconciler is null)
        {
            return Error(command.Id, "capture_unavailable", "Window capture is not configured.");
        }
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return Error(command.Id, "invalid_target", "Opening a surface requires a window entity target.");
        }

        var handle = await windowReconciler.OpenSurfaceAsync(command.Target, cancellationToken);
        return Result(command.Id!, handle);
    }

    private async Task<DispatchOutcome> ReadSurfaceFrameAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (windowReconciler is null)
        {
            return Error(command.Id, "capture_unavailable", "Window capture is not configured.");
        }
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return Error(command.Id, "invalid_target", "Reading a surface requires a stream target.");
        }

        var afterSequence = -1L;
        if (command.Payload is { ValueKind: JsonValueKind.Object } payload
            && payload.TryGetProperty("afterSequence", out var sequenceElement)
            && !sequenceElement.TryGetInt64(out afterSequence))
        {
            return Error(command.Id, "invalid_payload", "afterSequence must be an integer.");
        }

        if (!windowReconciler.ActiveCaptureStreams.Contains(command.Target, StringComparer.Ordinal))
        {
            return Result(command.Id!, new { available = false });
        }

        var frame = await windowReconciler.ReadLatestFrameAsync(
            command.Target,
            afterSequence,
            cancellationToken);
        if (frame is null)
        {
            return Result(command.Id!, new { available = true, frame = (object?)null });
        }

        return Result(command.Id!, new
        {
            available = true,
            frame = new
            {
                frame.StreamId,
                frame.Sequence,
                frame.Width,
                frame.Height,
                frame.MimeType,
                dataBase64 = Convert.ToBase64String(frame.Data),
            },
        });
    }

    private async Task<DispatchOutcome> CloseSurfaceAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (windowReconciler is null)
        {
            return Error(command.Id, "capture_unavailable", "Window capture is not configured.");
        }
        if (string.IsNullOrWhiteSpace(command.Target))
        {
            return Error(command.Id, "invalid_target", "Closing a surface requires a stream target.");
        }

        await windowReconciler.CloseSurfaceAsync(command.Target, cancellationToken);
        return Result(command.Id!, new { streamId = command.Target });
    }

    private async Task<DispatchOutcome> SetPresentationAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await SetPresentationCoreAsync(command, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<DispatchOutcome> SetPresentationCoreAsync(
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

        if (presentation is null || !IsValidPresentation(presentation))
        {
            return Error(command.Id, "invalid_payload", "Presentation payload is invalid.");
        }

        var document = await workspaceStore.LoadAsync(cancellationToken);
        if (!document.TrySetPresentation(command.Target, presentation, out _))
        {
            return Error(command.Id, "entity_not_found", $"Entity '{command.Target}' was not found.");
        }

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

    private async Task<DispatchOutcome> RouteWindowInputAsync(
        ProtocolEnvelope command,
        CancellationToken cancellationToken)
    {
        if (windowCatalog is null || windowReconciler is null || inputRouter is null)
        {
            return Error(command.Id, "input_unavailable", "Windows input routing is not configured.");
        }
        if (string.IsNullOrWhiteSpace(command.Target) || command.Payload is null)
        {
            return Error(command.Id, "invalid_target", "Window input requires a semantic target and intent.");
        }

        WindowInputIntent? intent;
        try
        {
            intent = command.Payload.Value.Deserialize<WindowInputIntent>(
                ProtocolEnvelope.SerializerOptions);
        }
        catch (JsonException)
        {
            return Error(command.Id, "invalid_payload", "Window input intent is invalid.");
        }

        if (intent is null || !IsValidInputIntent(intent))
        {
            return Error(command.Id, "invalid_payload", "Window input intent is invalid.");
        }

        var windows = await windowCatalog.ListAsync(cancellationToken);
        var window = windows.FirstOrDefault(candidate =>
            candidate.IsVisible
            && !candidate.IsMinimized
            && candidate.Bounds.Width > 0
            && candidate.Bounds.Height > 0
            && string.Equals(
                windowReconciler.ResolveEntityId(candidate),
                command.Target,
                StringComparison.Ordinal));
        if (window is null)
        {
            return Error(command.Id, "input_target_unavailable", "The Windows window is not currently available.");
        }

        try
        {
            await inputRouter.RouteAsync(window, intent, cancellationToken);
        }
        catch (InputTargetNotPermittedException exception)
        {
            return Error(command.Id, "INPUT_TARGET_NOT_PERMITTED", exception.Message);
        }

        return Result(command.Id!, new { entityId = command.Target, accepted = true });
    }

    private static bool IsValidInputIntent(WindowInputIntent intent)
    {
        static bool IsCoordinate(double? value) =>
            value is >= 0 and <= 1 && double.IsFinite(value.Value);

        return intent.Kind switch
        {
            "pointer" => intent.Phase is "move" or "down" or "up"
                && IsCoordinate(intent.X)
                && IsCoordinate(intent.Y)
                && (intent.Phase == "move" || intent.Button is "primary" or "secondary"),
            "wheel" => IsCoordinate(intent.X)
                && IsCoordinate(intent.Y)
                && intent.DeltaX is not null
                && intent.DeltaY is not null
                && double.IsFinite(intent.DeltaX.Value)
                && double.IsFinite(intent.DeltaY.Value),
            "key" => intent.Phase is "down" or "up" && !string.IsNullOrWhiteSpace(intent.Key),
            "text" => !string.IsNullOrEmpty(intent.Text)
                && intent.Text.Length <= WindowInputLimits.MaximumTextLength,
            _ => false,
        };
    }

    private static bool IsValidPresentation(PresentationState presentation)
    {
        static bool FiniteVector(Vec3 value) =>
            double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
        static bool FiniteQuaternion(Quaternion value) =>
            double.IsFinite(value.X)
            && double.IsFinite(value.Y)
            && double.IsFinite(value.Z)
            && double.IsFinite(value.W);

        var rotationLengthSquared = presentation.Rotation.X * presentation.Rotation.X
            + presentation.Rotation.Y * presentation.Rotation.Y
            + presentation.Rotation.Z * presentation.Rotation.Z
            + presentation.Rotation.W * presentation.Rotation.W;
        return FiniteVector(presentation.Position)
            && FiniteVector(presentation.Size)
            && presentation.Size.X > 0
            && presentation.Size.Y > 0
            && presentation.Size.Z > 0
            && FiniteQuaternion(presentation.Rotation)
            && rotationLengthSquared > 1e-12;
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

    private ApplicationControlService RequireApplicationControl() => applicationControlService
        ?? throw new ApplicationControlException("application_control_unavailable", "Application control is not configured.");

    private static DispatchOutcome Result(string commandId, object? payload = null) =>
        new(ProtocolEnvelope.Result(commandId, payload), []);

    private static DispatchOutcome Error(string? commandId, string code, string message) =>
        new(ProtocolEnvelope.Error(commandId, code, message), []);
}
