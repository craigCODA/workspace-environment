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

public enum WorkspaceApprovalDecision { AllowOnce, Remember, Deny }

public sealed record WorkspacePendingApproval(
    WorkspaceDirective Directive,
    WorkspaceActionPolicyDecision Policy,
    string Scope,
    string Description,
    string? DisplayName,
    IReadOnlyList<WorkspaceActionPolicyDecision> RemainingRequirements);

public sealed class WorkspaceActionOrchestrator
{
    private readonly IWorkspaceCommandGateway _gateway;
    private readonly IWorkspaceActionOutput _output;
    private readonly CapabilityBroker? _capabilities;
    private readonly string _workspaceIdentity;
    private WorkspacePendingApproval? _pendingApproval;

    public WorkspaceActionOrchestrator(
        IWorkspaceCommandGateway gateway,
        IWorkspaceActionOutput output,
        CapabilityBroker? capabilities = null,
        string workspaceIdentity = "workspace:default")
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _capabilities = capabilities;
        _workspaceIdentity = string.IsNullOrWhiteSpace(workspaceIdentity)
            ? throw new ArgumentException("A workspace identity is required.", nameof(workspaceIdentity))
            : workspaceIdentity.Trim();
    }

    public WorkspacePendingApproval? PendingApproval => _pendingApproval;

    public async Task BeginAsync(WorkspaceDirective directive, CancellationToken cancellationToken)
    {
        if (!WorkspaceDirectiveParser.TryValidate(directive, out _))
        {
            await SafeSpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
            return;
        }

        var resolved = await ResolveAsync(directive, cancellationToken);
        if (resolved is null) return;
        await AdvanceAsync(resolved.Directive, resolved.DisplayName,
            WorkspaceActionPolicy.Requirements(resolved.Directive), cancellationToken);
    }

    public async Task RespondToApprovalAsync(WorkspaceApprovalDecision decision, CancellationToken cancellationToken)
    {
        var pending = _pendingApproval;
        if (pending is null) return;

        if (decision == WorkspaceApprovalDecision.Deny)
        {
            _pendingApproval = null;
            await SafeSpeakAsync("Workspace action cancelled.", cancellationToken);
            return;
        }

        if (decision == WorkspaceApprovalDecision.Remember)
        {
            if (pending.Policy.Confirmation != WorkspaceConfirmation.Rememberable || _capabilities is null)
            {
                await SafeSpeakAsync("That action always needs fresh approval. Say allow once or deny.", cancellationToken);
                return;
            }
            try
            {
                await _capabilities.RememberAsync(new CapabilityGrant(pending.Policy.Capability, pending.Scope, null), cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                await SafeSpeakAsync("I couldn't remember that workspace permission. Say allow once or deny.", cancellationToken);
                return;
            }
        }

        _pendingApproval = null;
        await AdvanceAsync(pending.Directive, pending.DisplayName, pending.RemainingRequirements, cancellationToken);
    }

    private async Task AdvanceAsync(
        WorkspaceDirective directive,
        string? displayName,
        IReadOnlyList<WorkspaceActionPolicyDecision> requirements,
        CancellationToken cancellationToken)
    {
        if (requirements.Count == 0)
        {
            await ExecuteAsync(directive, displayName, cancellationToken);
            return;
        }

        var policy = requirements[0];
        if (policy.Confirmation == WorkspaceConfirmation.None)
        {
            await AdvanceAsync(directive, displayName, requirements.Skip(1).ToArray(), cancellationToken);
            return;
        }
        if (_capabilities is null)
        {
            await SafeSpeakAsync("Workspace application permissions are unavailable.", cancellationToken);
            return;
        }

        var scope = WorkspaceActionPolicy.ScopeFor(directive, _workspaceIdentity, policy);
        if (policy.Confirmation == WorkspaceConfirmation.Rememberable
            && _capabilities.IsGranted(policy.Capability, scope))
        {
            await AdvanceAsync(directive, displayName, requirements.Skip(1).ToArray(), cancellationToken);
            return;
        }

        var pending = new WorkspacePendingApproval(
            directive, policy, scope, DescribeApproval(directive, displayName, policy), displayName,
            requirements.Skip(1).ToArray());
        _pendingApproval = pending;
        try
        {
            await _output.RequestApprovalAsync(pending, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            if (ReferenceEquals(_pendingApproval, pending)) _pendingApproval = null;
            await SafeSpeakAsync("I couldn't request workspace approval.", cancellationToken);
        }
    }

    private async Task<ResolvedWorkspaceDirective?> ResolveAsync(WorkspaceDirective directive, CancellationToken cancellationToken)
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
            await SafeSpeakAsync("I couldn't resolve that application.", cancellationToken);
            return null;
        }

        if (!TryUnwrapSuccessfulPayload(result, out var search))
        {
            await SafeSpeakAsync("I couldn't find that application.", cancellationToken);
            return null;
        }
        if (IsAmbiguous(search))
        {
            await SafeSpeakAsync("I found more than one application. Please choose one.", cancellationToken);
            return null;
        }
        if (!TryReadResolvedApplication(search, out var applicationId, out var displayName))
        {
            await SafeSpeakAsync("I couldn't find that application.", cancellationToken);
            return null;
        }

        var arguments = ToDictionary(directive.Arguments);
        arguments.Remove("query");
        arguments["applicationId"] = applicationId;
        return new ResolvedWorkspaceDirective(new WorkspaceDirective(directive.Command,
            JsonSerializer.SerializeToElement(arguments)), displayName);
    }

    private async Task ExecuteAsync(WorkspaceDirective directive, string? displayName, CancellationToken cancellationToken)
    {
        try
        {
            await _output.ReportActivityAsync("Workspace request started.", cancellationToken);
            var raw = await _gateway.SendAsync(directive.Command,
                JsonSerializer.Deserialize<object>(directive.Arguments.GetRawText()), cancellationToken);
            if (!TryValidateObservedResult(directive.Command, raw, out var result))
            {
                await _output.ReportActivityAsync("Workspace request failed.", cancellationToken);
                await SafeSpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
                return;
            }

            await _output.ReportActivityAsync("Workspace request completed.", cancellationToken);
            await SafeSpeakAsync(WorkspaceActionNarrator.DescribeSuccess(directive.Command, result, displayName), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            try { await _output.ReportActivityAsync("Workspace request failed.", cancellationToken); }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
            await SafeSpeakAsync(WorkspaceActionNarrator.DescribeFailure(), cancellationToken);
        }
    }

    private async Task SafeSpeakAsync(string message, CancellationToken cancellationToken)
    {
        try { await _output.SpeakAsync(message, cancellationToken); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
    }

    private static bool TryValidateObservedResult(string command, JsonElement raw, out JsonElement result)
    {
        result = default;
        if (raw.ValueKind != JsonValueKind.Object && raw.ValueKind != JsonValueKind.Array) return false;
        var value = raw;
        if (raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("error", out _)) return false;
        if (raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("ok", out var ok))
        {
            if (ok.ValueKind != JsonValueKind.True || !raw.TryGetProperty("payload", out value)) return false;
        }
        if (value.ValueKind != JsonValueKind.Object && command != "application.profile.list") return false;
        var valid = command switch
        {
            "application.open" => HasText(value, "operationId") && HasId(value, "applicationEntityId", "pc.application:")
                && OptionalId(value, "windowEntityId", "pc.window:") && OptionalId(value, "surfaceEntityId", "spatial.surface:")
                && OneOf(value, "disposition", "reused", "launched", "launchedWithoutWindow", "launched-without-window")
                && OneOf(value, "surfaceState", "available", "unavailable", "notResolved", "not-resolved") && HasBoolean(value, "focused"),
            "application.close" => HasText(value, "operationId") && HasId(value, "windowEntityId", "pc.window:")
                && OneOf(value, "state", "closed", "closePending", "close-pending", "notRunning", "not-running"),
            "application.restart" => HasText(value, "operationId")
                && OneOf(value, "state", "open", "closed", "closePending", "close-pending", "notRunning", "not-running", "launchedWithoutWindow", "launched-without-window"),
            "window.focus" => HasId(value, "entityId", "pc.window:"),
            "surface.bindWindow" => HasId(value, "surfaceEntityId", "spatial.surface:") && HasId(value, "windowEntityId", "pc.window:"),
            "application.profile.save" => HasId(value, "id", "profile:"),
            "application.profile.delete" => HasId(value, "profileId", "profile:") && HasBoolean(value, "deleted"),
            "application.search" => HasText(value, "status") && value.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array,
            "application.profile.list" => value.ValueKind == JsonValueKind.Array,
            _ => false,
        };
        if (!valid) return false;
        result = value.Clone();
        return true;
    }

    private static bool TryReadResolvedApplication(JsonElement search, out string applicationId, out string? displayName)
    {
        applicationId = string.Empty;
        displayName = null;
        if (search.ValueKind != JsonValueKind.Object || !search.TryGetProperty("application", out var application)
            || application.ValueKind != JsonValueKind.Object || !HasId(application, "id", "pc.application:")) return false;
        applicationId = application.GetProperty("id").GetString()!;
        displayName = ReadText(application, "displayName");
        return true;
    }

    private static bool IsAmbiguous(JsonElement value) =>
        string.Equals(ReadText(value, "status"), "ambiguous", StringComparison.OrdinalIgnoreCase)
        || (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 1);

    private static bool TryUnwrapSuccessfulPayload(JsonElement result, out JsonElement payload)
    {
        payload = default;
        if (result.ValueKind != JsonValueKind.Object || result.TryGetProperty("error", out _)) return false;
        if (result.TryGetProperty("ok", out var ok))
        {
            if (ok.ValueKind != JsonValueKind.True || !result.TryGetProperty("payload", out payload)) return false;
            payload = payload.Clone();
            return true;
        }
        payload = result.Clone();
        return true;
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement objectElement) => objectElement.EnumerateObject().ToDictionary(
        property => property.Name, property => JsonSerializer.Deserialize<object>(property.Value.GetRawText()), StringComparer.Ordinal);

    private static string DescribeApproval(WorkspaceDirective directive, string? displayName, WorkspaceActionPolicyDecision policy)
    {
        var application = displayName ?? FirstText(directive.Arguments, "applicationId", "profileId") ?? "that application";
        var profile = FirstText(directive.Arguments, "id", "profileId") ?? "that profile";
        var surface = FirstText(directive.Arguments, "targetSurfaceId", "surfaceEntityId", "preferredSurfaceId");
        var arguments = directive.Arguments.TryGetProperty("arguments", out var items) && items.ValueKind == JsonValueKind.Array
            ? string.Join(", ", items.EnumerateArray().Select(item => item.GetString() ?? string.Empty)) : null;
        var target = surface is null ? application : $"{application} on {surface}";
        return policy.Capability switch
        {
            "application.launch" => $"I need approval to open {target}. It may start a new process."
                + (arguments is null ? string.Empty : $" Arguments: {arguments}."),
            "surface.replace" => $"I need separate approval to replace the current content on {surface ?? "that display"}.",
            "window.focus" => $"I need approval to focus {application}.",
            "surface.bind" => $"I need approval to bind {application} to {surface ?? "that display"}.",
            "application.close" => $"I need approval to close {FirstText(directive.Arguments, "windowEntityId") ?? "that application"}.",
            "application.restart" => $"I need approval to restart {FirstText(directive.Arguments, "windowEntityId", "profileId") ?? "that application"}.",
            "application.profile.edit" => $"I need approval to edit profile {profile} for {FirstText(directive.Arguments, "applicationId") ?? application}"
                + (surface is null ? "." : $" on {surface}.")
                + (arguments is null ? string.Empty : $" Arguments: {arguments}.")
                + " This does not start a process.",
            _ => "I need approval for that workspace action.",
        };
    }

    private static bool HasText(JsonElement value, string name) => ReadText(value, name) is { Length: > 0 } text && !string.IsNullOrWhiteSpace(text);
    private static bool HasId(JsonElement value, string name, string prefix) => HasText(value, name) && ReadText(value, name)!.StartsWith(prefix, StringComparison.Ordinal);
    private static bool OptionalId(JsonElement value, string name, string prefix) => !value.TryGetProperty(name, out _) || HasId(value, name, prefix);
    private static bool HasBoolean(JsonElement value, string name) => value.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False;
    private static bool OneOf(JsonElement value, string name, params string[] allowed) => allowed.Contains(ReadText(value, name), StringComparer.Ordinal);
    private static string? FirstText(JsonElement value, params string[] names) => names.Select(name => ReadText(value, name)).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    private static string? ReadText(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private sealed record ResolvedWorkspaceDirective(WorkspaceDirective Directive, string? DisplayName);
}
