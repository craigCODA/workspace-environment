using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workspace.Desktop.Core.Runtime;

public sealed record WorkspaceDirective(string Command, JsonElement Arguments);

public sealed record WorkspaceDirectiveResult(string SpokenText, IReadOnlyList<WorkspaceDirective> Directives);

public static class WorkspaceDirectiveParser
{
    public const int MaximumDirectivePayloadBytes = 64 * 1024;

    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "application.search", "application.profile.list", "application.profile.save",
        "application.profile.delete", "application.open", "application.close",
        "application.restart", "window.focus", "surface.bindWindow",
    };

    public static WorkspaceDirectiveResult Parse(string response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var directives = new List<WorkspaceDirective>();
        var spoken = new StringBuilder(response.Length);
        var cursor = 0;
        while (cursor < response.Length)
        {
            var start = response.IndexOf("[[workspace:", cursor, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                spoken.Append(response, cursor, response.Length - cursor);
                break;
            }

            spoken.Append(response, cursor, start - cursor);
            var payloadStart = start + "[[workspace:".Length;
            if (!TryFindDirectiveEnd(response, payloadStart, out var end))
            {
                // An unterminated private directive is never eligible for speech.
                break;
            }

            var payload = response[payloadStart..end];
            if (Encoding.UTF8.GetByteCount(payload) <= MaximumDirectivePayloadBytes
                && TryParseDirective(payload, out var directive))
            {
                directives.Add(directive!);
            }
            cursor = end + 2;
        }

        return new WorkspaceDirectiveResult(
            Regex.Replace(spoken.ToString(), @"\s+", " ").Trim(), directives);
    }

    public static bool TryValidate(WorkspaceDirective directive, out string error)
    {
        if (!AllowedCommands.Contains(directive.Command)
            || directive.Arguments.ValueKind != JsonValueKind.Object)
        {
            error = "Unsupported workspace directive.";
            return false;
        }

        var args = directive.Arguments;
        var valid = directive.Command switch
        {
            "application.search" => HasOnly(args, "query", "limit") && HasText(args, "query") && OptionalInteger(args, "limit", 1, 10),
            "application.profile.list" => HasOnly(args),
            "application.profile.save" => ValidProfile(args),
            "application.profile.delete" => HasOnly(args, "profileId") && IsId(args, "profileId", "profile:"),
            "application.open" => ValidOpen(args),
            "application.close" => HasOnly(args, "windowEntityId") && IsId(args, "windowEntityId", "pc.window:"),
            "application.restart" => ValidRestart(args),
            "window.focus" => ValidFocus(args),
            "surface.bindWindow" => HasOnly(args, "surfaceEntityId", "windowEntityId", "replaceOccupied")
                && IsId(args, "surfaceEntityId", "spatial.surface:")
                && IsId(args, "windowEntityId", "pc.window:")
                && OptionalBoolean(args, "replaceOccupied"),
            _ => false,
        };
        error = valid ? string.Empty : "Workspace directive arguments are invalid.";
        return valid;
    }

    private static bool TryFindDirectiveEnd(string value, int start, out int end)
    {
        var quoted = false;
        var escaped = false;
        for (var index = start; index < value.Length - 1; index++)
        {
            var character = value[index];
            if (quoted)
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"')
            {
                quoted = true;
                continue;
            }
            if (character == ']' && value[index + 1] == ']')
            {
                end = index;
                return true;
            }
        }
        end = -1;
        return false;
    }

    private static bool TryParseDirective(string payload, out WorkspaceDirective? directive)
    {
        directive = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!HasOnly(root, "command", "args")
                || !root.TryGetProperty("command", out var commandElement)
                || commandElement.ValueKind != JsonValueKind.String
                || commandElement.GetString() is not { Length: > 0 } command
                || !root.TryGetProperty("args", out var arguments)) return false;

            var candidate = new WorkspaceDirective(command, arguments.Clone());
            if (!TryValidate(candidate, out _)) return false;
            directive = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ValidOpen(JsonElement args)
    {
        if (!HasOnly(args, "query", "applicationId", "profileId", "launchPolicy", "targetSurfaceId", "presentation", "replaceOccupied")
            || !OptionalQuery(args)
            || !OptionalId(args, "applicationId", "pc.application:")
            || !OptionalId(args, "profileId", "profile:")
            || !OptionalBoolean(args, "replaceOccupied")
            || !OptionalText(args, "targetSurfaceId", "spatial.surface:")
            || !OptionalLaunchPolicy(args, "launchPolicy")) return false;

        var targets = (HasText(args, "query") ? 1 : 0)
            + (IsId(args, "applicationId", "pc.application:") ? 1 : 0)
            + (IsId(args, "profileId", "profile:") ? 1 : 0);
        if (targets != 1) return false;
        if (args.TryGetProperty("presentation", out var presentation)
            && (!ValidPresentation(presentation) || args.TryGetProperty("targetSurfaceId", out _))) return false;
        return true;
    }

    private static bool ValidRestart(JsonElement args)
    {
        if (!HasOnly(args, "windowEntityId", "profileId")) return false;
        return (IsId(args, "windowEntityId", "pc.window:") ? 1 : 0)
            + (IsId(args, "profileId", "profile:") ? 1 : 0) == 1;
    }

    private static bool ValidFocus(JsonElement args) =>
        HasOnly(args, "windowEntityId", "applicationId")
        && IsId(args, "windowEntityId", "pc.window:")
        && IsId(args, "applicationId", "pc.application:");

    private static bool ValidProfile(JsonElement args)
    {
        if (!HasOnly(args, "id", "displayName", "applicationId", "arguments", "workingDirectory", "launchPolicy", "preferredSurfaceId", "preferredPresentation")
            || !IsId(args, "id", "profile:") || !HasText(args, "displayName")
            || !IsId(args, "applicationId", "pc.application:")
            || !OptionalNullableText(args, "preferredSurfaceId", "spatial.surface:")
            || !HasLaunchPolicy(args, "launchPolicy")
            || !args.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Array
            || arguments.EnumerateArray().Any(argument => argument.ValueKind != JsonValueKind.String || argument.GetString()?.Contains('\0') == true)) return false;

        if (args.TryGetProperty("workingDirectory", out var workingDirectory)
            && workingDirectory.ValueKind != JsonValueKind.Null
            && (workingDirectory.ValueKind != JsonValueKind.String || !Path.IsPathRooted(workingDirectory.GetString())
                || workingDirectory.GetString()?.Contains('\0') == true)) return false;
        return !args.TryGetProperty("preferredPresentation", out var presentation)
            || presentation.ValueKind == JsonValueKind.Null || ValidPresentation(presentation);
    }

    private static bool ValidPresentation(JsonElement value)
    {
        if (!HasOnly(value, "position", "rotation", "size", "parentPresentationId", "representation")
            || !value.TryGetProperty("position", out var position) || !HasOnly(position, "x", "y", "z")
            || !value.TryGetProperty("rotation", out var rotation) || !HasOnly(rotation, "x", "y", "z", "w")
            || !value.TryGetProperty("size", out var size) || !HasOnly(size, "x", "y", "z")
            || !Finite(position, "x") || !Finite(position, "y") || !Finite(position, "z")
            || !Finite(rotation, "x") || !Finite(rotation, "y") || !Finite(rotation, "z") || !Finite(rotation, "w")
            || !Finite(size, "x") || !Finite(size, "y") || !Finite(size, "z")) return false;
        if (size.GetProperty("x").GetDouble() <= 0 || size.GetProperty("y").GetDouble() <= 0 || size.GetProperty("z").GetDouble() <= 0) return false;
        var magnitude = rotation.EnumerateObject().Sum(component => component.Value.GetDouble() * component.Value.GetDouble());
        return magnitude > 0
            && OptionalText(value, "parentPresentationId", "")
            && OptionalText(value, "representation", "");
    }

    private static bool HasOnly(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var properties = value.EnumerateObject().ToArray();
        return properties.All(property => allowed.Contains(property.Name, StringComparer.Ordinal))
            && properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length;
    }

    private static bool HasText(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
        && element.GetString() is { } text && !string.IsNullOrWhiteSpace(text) && !text.Contains('\0');

    private static bool IsId(JsonElement value, string name, string prefix) =>
        HasText(value, name) && value.GetProperty(name).GetString()!.StartsWith(prefix, StringComparison.Ordinal);

    private static bool OptionalId(JsonElement value, string name, string prefix) => !value.TryGetProperty(name, out _) || IsId(value, name, prefix);

    private static bool OptionalQuery(JsonElement value) => !value.TryGetProperty("query", out _) || HasText(value, "query");

    private static bool OptionalText(JsonElement value, string name, string prefix) =>
        !value.TryGetProperty(name, out var element) || (element.ValueKind == JsonValueKind.String
            && element.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            && !text.Contains('\0') && text.StartsWith(prefix, StringComparison.Ordinal));

    private static bool OptionalNullableText(JsonElement value, string name, string prefix) =>
        !value.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null || OptionalText(value, name, prefix);

    private static bool OptionalBoolean(JsonElement value, string name) =>
        !value.TryGetProperty(name, out var element) || element.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool OptionalInteger(JsonElement value, string name, int minimum, int maximum) =>
        !value.TryGetProperty(name, out var element) || (element.TryGetInt32(out var integer) && integer >= minimum && integer <= maximum);

    private static bool OptionalLaunchPolicy(JsonElement value, string name) =>
        !value.TryGetProperty(name, out var element) || (element.ValueKind == JsonValueKind.String
            && element.GetString() is "reuseOrLaunch" or "newInstance");

    private static bool HasLaunchPolicy(JsonElement value, string name) =>
        value.TryGetProperty(name, out _) && OptionalLaunchPolicy(value, name);

    private static bool Finite(JsonElement value, string name) =>
        value.TryGetProperty(name, out var component) && component.TryGetDouble(out var number) && double.IsFinite(number);
}
