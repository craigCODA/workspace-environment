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

    public static WorkspaceActionPolicyDecision Classify(WorkspaceDirective directive)
    {
        var policy = Classify(directive.Command);
        return IsOccupiedReplacement(directive)
            ? new WorkspaceActionPolicyDecision("surface.replace", WorkspaceConfirmation.Fresh)
            : policy;
    }

    public static string ScopeFor(WorkspaceDirective directive)
    {
        var args = directive.Arguments;
        return directive.Command switch
        {
            "application.open" => Prefix("application", FirstText(args, "applicationId", "profileId")),
            "window.focus" or "application.close" or "application.restart" => Prefix("window", FirstText(args, "windowEntityId", "profileId")),
            "surface.bindWindow" => Prefix("surface", FirstText(args, "surfaceEntityId")),
            "application.profile.save" => Prefix("profile", FirstText(args, "id")),
            "application.profile.delete" => Prefix("profile", FirstText(args, "profileId")),
            _ => "workspace",
        };
    }

    private static bool IsOccupiedReplacement(WorkspaceDirective directive) =>
        (directive.Command is "application.open" or "surface.bindWindow")
        && directive.Arguments.TryGetProperty("replaceOccupied", out var replace)
        && replace.ValueKind == JsonValueKind.True;

    private static string Prefix(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? "workspace" : $"{prefix}:{value}";

    private static string? FirstText(JsonElement value, params string[] names) =>
        names.Select(name => value.TryGetProperty(name, out var property)
                && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
}
