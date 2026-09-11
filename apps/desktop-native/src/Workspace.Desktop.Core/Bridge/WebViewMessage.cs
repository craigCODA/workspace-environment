using System.Text;
using System.Text.Json;

namespace Workspace.Desktop.Core.Bridge;

public sealed record WebViewMessage(int Version, string Type, JsonElement Payload);

public sealed class RendererMessageValidator
{
    public const int MaximumMessageBytes = 256 * 1024;

    private static readonly HashSet<string> KnownTypes = new(StringComparer.Ordinal)
    {
        "renderer.ready",
        "scene.command.result",
        "workspace.command.result",
        "preference.change.request",
        "voice.control",
        "agent.instruction",
        "agent.approval.response",
    };

    private static readonly HashSet<string> TopLevelProperties = new(StringComparer.Ordinal)
    {
        "version",
        "type",
        "payload",
    };

    private readonly HashSet<string> _pendingSceneResults = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingWorkspaceResults = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public void ExpectSceneResult(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        lock (_sync)
        {
            _pendingSceneResults.Add(requestId);
        }
    }

    public void ExpectWorkspaceResult(string requestId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        lock (_sync)
        {
            _pendingWorkspaceResults.Add(requestId);
        }
    }

    public bool TryParse(string json, out WebViewMessage? message, out string error)
    {
        message = null;
        if (Encoding.UTF8.GetByteCount(json) > MaximumMessageBytes)
        {
            error = "Renderer message exceeds the 256 KiB safety limit.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Renderer message must be an object.";
                return false;
            }

            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != TopLevelProperties.Count
                || properties.Any(property => !TopLevelProperties.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count()
                    != properties.Length)
            {
                error = "Renderer message contains missing, duplicate, or unexpected properties.";
                return false;
            }

            if (!root.TryGetProperty("version", out var versionElement)
                || !versionElement.TryGetInt32(out var version)
                || version != 1)
            {
                error = "Renderer message uses an unsupported protocol version.";
                return false;
            }

            if (!root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || typeElement.GetString() is not { } type
                || !KnownTypes.Contains(type))
            {
                error = "Renderer message type is not allowed.";
                return false;
            }

            if (!root.TryGetProperty("payload", out var payload))
            {
                error = "Renderer message payload is required.";
                return false;
            }

            if (type == "scene.command.result" && !ConsumeExpectedSceneResult(payload, out error))
            {
                return false;
            }
            if (type == "workspace.command.result" && !ConsumeExpectedWorkspaceResult(payload, out error))
            {
                return false;
            }

            message = new WebViewMessage(version, type, payload.Clone());
            error = string.Empty;
            return true;
        }
        catch (JsonException exception)
        {
            error = $"Renderer message is not valid JSON: {exception.Message}";
            return false;
        }
    }

    private bool ConsumeExpectedSceneResult(JsonElement payload, out string error)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || idElement.GetString() is not { Length: > 0 } id)
        {
            error = "Scene result requires a string request id.";
            return false;
        }

        lock (_sync)
        {
            if (!_pendingSceneResults.Remove(id))
            {
                error = "Scene result does not match a pending native request.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private bool ConsumeExpectedWorkspaceResult(JsonElement payload, out string error)
    {
        if (payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || idElement.GetString() is not { Length: > 0 } id)
        {
            error = "Workspace result requires a string request id.";
            return false;
        }

        lock (_sync)
        {
            if (!_pendingWorkspaceResults.Remove(id))
            {
                error = "Workspace result does not match a pending native request.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
