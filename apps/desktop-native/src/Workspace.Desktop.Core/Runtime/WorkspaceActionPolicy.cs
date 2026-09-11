using System.Text.Json;

namespace Workspace.Desktop.Core.Runtime;

public enum WorkspaceConfirmation
{
    None,
    Rememberable,
    Fresh,
}

public sealed record WorkspaceActionPolicyDecision(string Capability, WorkspaceConfirmation Confirmation);

public static class WorkspaceActionPolicy
{
    public static WorkspaceActionPolicyDecision Classify(string command) => command switch
    {
        "application.search" or "application.profile.list" => new("application.search", WorkspaceConfirmation.None),
        "application.open" => new("application.launch", WorkspaceConfirmation.Rememberable),
        "window.focus" => new("window.focus", WorkspaceConfirmation.Rememberable),
        "surface.bindWindow" => new("surface.bind", WorkspaceConfirmation.Rememberable),
        "application.profile.save" or "application.profile.delete" => new("application.profile.edit", WorkspaceConfirmation.Rememberable),
        "application.close" => new("application.close", WorkspaceConfirmation.Fresh),
        "application.restart" => new("application.restart", WorkspaceConfirmation.Fresh),
        _ => throw new ArgumentException("Unsupported workspace action.", nameof(command)),
    };

    public static WorkspaceActionPolicyDecision Classify(WorkspaceDirective directive) =>
        IsOccupiedReplacement(directive)
            ? new WorkspaceActionPolicyDecision("surface.replace", WorkspaceConfirmation.Fresh)
            : Classify(directive.Command);

    public static IReadOnlyList<WorkspaceActionPolicyDecision> Requirements(WorkspaceDirective directive)
    {
        var primary = Classify(directive.Command);
        return IsOccupiedReplacement(directive)
            ? [primary, new WorkspaceActionPolicyDecision("surface.replace", WorkspaceConfirmation.Fresh)]
            : [primary];
    }

    public static string ScopeFor(WorkspaceDirective directive, string workspaceIdentity) =>
        ScopeFor(directive, workspaceIdentity, Classify(directive.Command));

    public static string ScopeFor(
        WorkspaceDirective directive,
        string workspaceIdentity,
        WorkspaceActionPolicyDecision policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceIdentity);
        var semantic = policy.Capability switch
        {
            "application.launch" => FirstText(directive.Arguments, "applicationId", "profileId"),
            "window.focus" => FirstText(directive.Arguments, "windowEntityId"),
            "surface.bind" or "surface.replace" => FirstText(directive.Arguments, "surfaceEntityId", "targetSurfaceId"),
            "application.profile.edit" => FirstText(directive.Arguments, "id", "profileId"),
            "application.close" or "application.restart" => FirstText(directive.Arguments, "windowEntityId", "profileId"),
            _ => null,
        };
        var kind = policy.Capability switch
        {
            "application.launch" => "application",
            "window.focus" => "window",
            "application.profile.edit" => "profile",
            "surface.bind" or "surface.replace" => "surface",
            "application.close" or "application.restart" => "window",
            _ => "workspace",
        };
        return string.IsNullOrWhiteSpace(semantic)
            ? $"{workspaceIdentity}|{kind}"
            : $"{workspaceIdentity}|{kind}:{semantic}";
    }

    private static bool IsOccupiedReplacement(WorkspaceDirective directive) =>
        (directive.Command is "application.open" or "surface.bindWindow")
        && directive.Arguments.TryGetProperty("replaceOccupied", out var replace)
        && replace.ValueKind == JsonValueKind.True;

    private static string? FirstText(JsonElement value, params string[] names) =>
        names.Select(name => value.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
}
