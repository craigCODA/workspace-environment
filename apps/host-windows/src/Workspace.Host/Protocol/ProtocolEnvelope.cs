using System.Text.Json;
using System.Text.Json.Serialization;
using Workspace.Host.Domain;

namespace Workspace.Host.Protocol;

public sealed record ProtocolEnvelope
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public int Protocol { get; init; } = CurrentVersion;

    public required string Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Target { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Payload { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Success { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Event { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WorkspaceEntity>? Entities { get; init; }

    public static ProtocolEnvelope Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("protocol", out var protocolElement)
            || !protocolElement.TryGetInt32(out var protocolVersion))
        {
            throw new InvalidProtocolEnvelopeException("Workspace protocol version is required.");
        }

        if (protocolVersion != CurrentVersion)
        {
            throw new UnsupportedProtocolVersionException(protocolVersion);
        }

        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json, JsonOptions)
            ?? throw new InvalidProtocolEnvelopeException("Workspace protocol envelope was empty.");

        if (string.IsNullOrWhiteSpace(envelope.Type))
        {
            throw new InvalidProtocolEnvelopeException("Workspace protocol envelope type is required.");
        }

        return envelope;
    }

    public static ProtocolEnvelope Command(
        string id,
        string operation,
        string? target = null,
        JsonElement? payload = null) =>
        new()
        {
            Type = "command",
            Id = id,
            Operation = operation,
            Target = target,
            Payload = payload,
        };

    public static ProtocolEnvelope Result(string id, object? payload = null) =>
        new()
        {
            Type = "result",
            Id = id,
            Success = true,
            Payload = ToJsonElement(payload),
        };

    public static ProtocolEnvelope Error(string? id, string code, string message) =>
        new()
        {
            Type = "error",
            Id = id,
            Code = code,
            Message = message,
        };

    public static ProtocolEnvelope EventMessage(string eventName, object? payload = null) =>
        new()
        {
            Type = "event",
            Event = eventName,
            Payload = ToJsonElement(payload),
        };

    public static ProtocolEnvelope Snapshot(IReadOnlyList<WorkspaceEntity> entities) =>
        new()
        {
            Type = "snapshot",
            Entities = entities,
        };

    internal static JsonSerializerOptions SerializerOptions => JsonOptions;

    private static JsonElement? ToJsonElement(object? value) =>
        value is null ? null : JsonSerializer.SerializeToElement(value, JsonOptions);
}

public sealed class UnsupportedProtocolVersionException(int protocolVersion)
    : Exception($"Unsupported workspace protocol version: {protocolVersion}.")
{
    public int ProtocolVersion { get; } = protocolVersion;
}

public sealed class InvalidProtocolEnvelopeException(string message) : Exception(message);
