using System.Text.Json;

namespace Workspace.Desktop.Core.Runtime;

public static class WorkspaceActionNarrator
{
    public static string DescribeSuccess(string command, JsonElement result, string? fallbackName = null)
    {
        var name = ReadText(result, "applicationName") ?? fallbackName ?? "The application";
        return command switch
        {
            "application.open" => DescribeOpen(name, result),
            "window.focus" => $"{name} is focused.",
            "application.close" => DescribeClose(name, result),
            "application.restart" => DescribeRestart(name, result),
            "surface.bindWindow" => "The window is bound to that display.",
            "application.profile.save" => "The application profile is saved.",
            "application.profile.delete" => DescribeProfileDelete(result),
            "application.search" => DescribeSearch(result),
            "application.profile.list" => "I found the saved application profiles.",
            _ => "The workspace action completed.",
        };
    }

    public static string DescribeFailure() => "The workspace action did not complete.";

    private static string DescribeOpen(string name, JsonElement result)
    {
        var disposition = ReadText(result, "disposition");
        var focused = result.TryGetProperty("focused", out var focusedElement)
            && focusedElement.ValueKind == JsonValueKind.True;
        var surfaceState = ReadText(result, "surfaceState");
        if (disposition == "launchedWithoutWindow")
            return $"{name} started, but its window is not available yet.";
        if (surfaceState is "unavailable")
            return $"{name} is open, but its display is unavailable.";
        if (disposition is "reused")
            return focused ? $"{name} is already open and focused." : $"{name} is already open.";
        return focused ? $"{name} is open and focused." : $"{name} is open.";
    }

    private static string DescribeClose(string name, JsonElement result) => ReadText(result, "state") switch
    {
        "closed" => $"{name} is closed.",
        "closePending" => $"{name} is waiting for its normal close.",
        "notRunning" => $"{name} is not running.",
        _ => DescribeFailure(),
    };

    private static string DescribeRestart(string name, JsonElement result) => ReadText(result, "state") switch
    {
        "open" => $"{name} restarted.",
        "closePending" => $"{name} is waiting for its normal close, so it was not restarted.",
        _ => DescribeFailure(),
    };

    private static string DescribeProfileDelete(JsonElement result) =>
        result.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True
            ? "The application profile is deleted."
            : "The application profile was not found.";

    private static string DescribeSearch(JsonElement result) => ReadText(result, "status") switch
    {
        "resolved" => "I found the matching application.",
        "ambiguous" => "I found more than one application. Please choose one.",
        "notFound" => "I couldn't find that application.",
        _ => DescribeFailure(),
    };

    private static string? ReadText(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
