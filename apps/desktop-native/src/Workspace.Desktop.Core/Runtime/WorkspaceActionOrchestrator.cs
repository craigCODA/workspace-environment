using System.Text.Json;
using Workspace.Desktop.Core.Capabilities;

namespace Workspace.Desktop.Core.Runtime;

public interface IWorkspaceCommandGateway
{
    Task<JsonElement> SendAsync(string command, object? arguments, CancellationToken cancellationToken);
}

public interface IWorkspaceActionOutput
{
    Task RequestApprovalAsync(WorkspacePendingApproval approval, CancellationToken cancellationToken);

    Task ReportActivityAsync(string message, CancellationToken cancellationToken);

    Task SpeakAsync(string message, CancellationToken cancellationToken);
}

public enum WorkspaceApprovalDecision
{
    AllowOnce,
    Remember,
    Deny,
}

public sealed record WorkspacePendingApproval(
    WorkspaceDirective Directive,
    WorkspaceActionPolicyDecision Policy,
    string Scope,
    string Description);

public sealed class WorkspaceActionOrchestrator
{
    private readonly IWorkspaceCommandGateway _gateway;
    private readonly IWorkspaceActionOutput _output;
    private readonly CapabilityBroker? _capabilities;
    private WorkspacePendingApproval? _pendingApproval;

    public WorkspaceActionOrchestrator(
        IWorkspaceCommandGateway gateway,
        IWorkspaceActionOutput output,
        CapabilityBroker? capabilities = null)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _capabilities = capabilities;
    }

    public WorkspacePendingApproval? PendingApproval => _pendingApproval;

    public async Task BeginAsync(WorkspaceDirective directive, CancellationToken cancellationToken)
    {
        if (!WorkspaceDirectiveParser.TryValidate(directive, out _))
        {
            await _output.SpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
            return;
        }

        var resolved = await ResolveAsync(directive, cancellationToken);
        if (resolved is null) return;

        var policy = WorkspaceActionPolicy.Classify(resolved.Directive);
        if (policy.Confirmation == WorkspaceConfirmation.None)
        {
            await ExecuteAsync(resolved.Directive, resolved.DisplayName, cancellationToken);
            return;
        }

        var scope = WorkspaceActionPolicy.ScopeFor(resolved.Directive);
        if (_capabilities is null)
        {
            await ExecuteAsync(resolved.Directive, resolved.DisplayName, cancellationToken);
            return;
        }

        if (policy.Confirmation == WorkspaceConfirmation.Rememberable
            && _capabilities.IsGranted(policy.Capability, scope))
        {
            await ExecuteAsync(resolved.Directive, resolved.DisplayName, cancellationToken);
            return;
        }

        _pendingApproval = new WorkspacePendingApproval(
            resolved.Directive,
            policy,
            scope,
            DescribeApproval(resolved.Directive, resolved.DisplayName));
        await _output.RequestApprovalAsync(_pendingApproval, cancellationToken);
    }

    public async Task RespondToApprovalAsync(
        WorkspaceApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        var pending = _pendingApproval;
        if (pending is null)
        {
            return;
        }

        if (decision == WorkspaceApprovalDecision.Deny)
        {
            _pendingApproval = null;
            await _output.SpeakAsync("Workspace action cancelled.", cancellationToken);
            return;
        }

        if (decision == WorkspaceApprovalDecision.Remember)
        {
            if (pending.Policy.Confirmation != WorkspaceConfirmation.Rememberable || _capabilities is null)
            {
                await _output.SpeakAsync("That action always needs fresh approval. Say allow once or deny.", cancellationToken);
                return;
            }

            await _capabilities.RememberAsync(
                new CapabilityGrant(pending.Policy.Capability, pending.Scope, null),
                cancellationToken);
        }

        _pendingApproval = null;
        await ExecuteAsync(pending.Directive, null, cancellationToken);
    }

    private async Task<ResolvedWorkspaceDirective?> ResolveAsync(
        WorkspaceDirective directive,
        CancellationToken cancellationToken)
    {
        if (directive.Command != "application.open"
            || !directive.Arguments.TryGetProperty("query", out var queryElement)
            || queryElement.ValueKind != JsonValueKind.String)
        {
            return new ResolvedWorkspaceDirective(directive, null);
        }

        var query = queryElement.GetString()!;
        JsonElement result;
        try
        {
            result = await _gateway.SendAsync("application.search", new { query, limit = 10 }, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await _output.SpeakAsync("I couldn't resolve that application.", cancellationToken);
            return null;
        }

        var search = UnwrapPayload(result);
        if (IsAmbiguous(search))
        {
            await _output.SpeakAsync("I found more than one application. Please choose one.", cancellationToken);
            return null;
        }

        if (!TryReadResolvedApplication(search, out var applicationId, out var displayName))
        {
            await _output.SpeakAsync("I couldn't find that application.", cancellationToken);
            return null;
        }

        var arguments = ToDictionary(directive.Arguments);
        arguments.Remove("query");
        arguments["applicationId"] = applicationId;
        return new ResolvedWorkspaceDirective(
            new WorkspaceDirective(directive.Command, JsonSerializer.SerializeToElement(arguments)),
            displayName);
    }

    private async Task ExecuteAsync(
        WorkspaceDirective directive,
        string? displayName,
        CancellationToken cancellationToken)
    {
        await _output.ReportActivityAsync("Workspace request started.", cancellationToken);
        try
        {
            var result = UnwrapPayload(await _gateway.SendAsync(
                directive.Command,
                JsonSerializer.Deserialize<object>(directive.Arguments.GetRawText()),
                cancellationToken));
            if (IsFailure(result))
            {
                await _output.ReportActivityAsync("Workspace request failed.", cancellationToken);
                await _output.SpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
                return;
            }

            await _output.ReportActivityAsync("Workspace request completed.", cancellationToken);
            await _output.SpeakAsync(
                WorkspaceActionNarrator.DescribeSuccess(directive.Command, result, displayName),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await _output.ReportActivityAsync("Workspace request failed.", cancellationToken);
            await _output.SpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
        }
    }

    private static bool TryReadResolvedApplication(
        JsonElement search,
        out string applicationId,
        out string? displayName)
    {
        applicationId = string.Empty;
        displayName = null;
        if (search.ValueKind != JsonValueKind.Object
            || !search.TryGetProperty("application", out var application)
            || application.ValueKind != JsonValueKind.Object
            || !application.TryGetProperty("id", out var id)
            || id.ValueKind != JsonValueKind.String
            || id.GetString() is not { Length: > 0 } candidate
            || !candidate.StartsWith("app:", StringComparison.Ordinal))
        {
            return false;
        }

        applicationId = candidate;
        displayName = ReadText(application, "displayName");
        return true;
    }

    private static bool IsAmbiguous(JsonElement value) =>
        string.Equals(ReadText(value, "status"), "ambiguous", StringComparison.OrdinalIgnoreCase)
        || (value.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 1);

    private static bool IsFailure(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && ((value.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            || value.TryGetProperty("error", out _));

    private static JsonElement UnwrapPayload(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object
        && result.TryGetProperty("payload", out var payload)
            ? payload.Clone()
            : result;

    private static Dictionary<string, object?> ToDictionary(JsonElement objectElement) =>
        objectElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => JsonSerializer.Deserialize<object>(property.Value.GetRawText()),
            StringComparer.Ordinal);

    private static string DescribeApproval(WorkspaceDirective directive, string? displayName)
    {
        var target = displayName ?? FirstText(directive.Arguments, "applicationId", "profileId", "windowEntityId") ?? "that workspace item";
        return directive.Command switch
        {
            "application.open" => $"I need approval to open {target}.",
            "application.close" => $"I need approval to close {target}.",
            "application.restart" => $"I need approval to restart {target}.",
            "window.focus" => $"I need approval to focus {target}.",
            "surface.bindWindow" => "I need approval to bind that window to the selected display.",
            "application.profile.save" or "application.profile.delete" => $"I need approval to edit profile {target}.",
            _ => "I need approval for that workspace action.",
        };
    }

    private static string? FirstText(JsonElement value, params string[] names) =>
        names.Select(name => value.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

    private static string? ReadText(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private sealed record ResolvedWorkspaceDirective(WorkspaceDirective Directive, string? DisplayName);
}
