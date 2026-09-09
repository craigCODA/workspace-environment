using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workspace.Desktop.Core.Runtime;

public sealed record SceneDirective(string Command, JsonElement Arguments);

public sealed record SceneDirectiveResult(string SpokenText, IReadOnlyList<SceneDirective> Directives);

public static partial class SceneDirectiveParser
{
    private static readonly HashSet<string> AllowedCommands = new(StringComparer.Ordinal)
    {
        "camera.navigate",
        "camera.focus",
        "camera.stop",
        "camera.return-home",
        "surface.move",
        "surface.resize",
    };

    public static SceneDirectiveResult Parse(string response)
    {
        var directives = new List<SceneDirective>();
        var spoken = DirectivePattern().Replace(response, match =>
        {
            try
            {
                using var document = JsonDocument.Parse(match.Groups[1].Value);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("command", out var commandElement)
                    || commandElement.GetString() is not { } command
                    || !AllowedCommands.Contains(command))
                {
                    return string.Empty;
                }
                var arguments = root.TryGetProperty("args", out var args)
                    ? args.Clone()
                    : JsonSerializer.SerializeToElement(new { });
                directives.Add(new SceneDirective(command, arguments));
            }
            catch (JsonException)
            {
                // Malformed directives are not spoken or executed.
            }
            return string.Empty;
        });

        return new SceneDirectiveResult(
            Regex.Replace(spoken, @"\s+", " ").Trim(),
            directives);
    }

    [GeneratedRegex(@"\[\[scene:(.*?)\]\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DirectivePattern();
}
