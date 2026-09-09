using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workspace.Desktop.Core.Agent;

public sealed partial class CodexMessageTranslator(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public AgentEvent? Translate(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var method = methodElement.GetString() ?? string.Empty;
            var parameters = root.TryGetProperty("params", out var paramsElement)
                && paramsElement.ValueKind == JsonValueKind.Object
                ? paramsElement
                : default;
            var now = _timeProvider.GetUtcNow();
            return method switch
            {
                "thread/started" => new AgentThreadStarted(
                    ReadNestedString(parameters, "thread", "id"),
                    now),
                "turn/started" => new AgentTurnStarted(
                    ReadString(parameters, "threadId"),
                    ReadNestedString(parameters, "turn", "id"),
                    now),
                "item/agentMessage/delta" => new AgentAssistantDelta(
                    ReadString(parameters, "threadId"),
                    ReadString(parameters, "turnId"),
                    ReadString(parameters, "itemId"),
                    Redact(ReadString(parameters, "delta")),
                    now),
                "item/commandExecution/outputDelta" => new AgentTerminalDelta(
                    ReadString(parameters, "threadId"),
                    ReadString(parameters, "turnId"),
                    ReadString(parameters, "itemId"),
                    Redact(ReadString(parameters, "delta")),
                    now),
                "item/started" => TranslateItemStarted(parameters, now),
                "item/commandExecution/requestApproval"
                    or "item/fileChange/requestApproval"
                    or "item/permissions/requestApproval" =>
                    TranslateApproval(root, parameters, method, now),
                "turn/completed" => TranslateTurnCompleted(parameters, now),
                "error" => new AgentProtocolFailure(
                    Redact(ReadNestedString(parameters, "error", "message")),
                    now),
                _ => null,
            };
        }
        catch (JsonException exception)
        {
            return new AgentProtocolFailure(
                $"Codex sent malformed JSON: {exception.Message}",
                _timeProvider.GetUtcNow());
        }
    }

    public AgentProcessExited TranslateExit(int exitCode, string standardError) =>
        new AgentProcessExited(
            exitCode,
            Redact(string.IsNullOrWhiteSpace(standardError)
                ? "Codex App Server exited unexpectedly."
                : standardError.Trim()),
            _timeProvider.GetUtcNow());

    public static string Redact(string text)
    {
        var bounded = text.Length > 16_384 ? text[..16_384] + "…" : text;
        bounded = SecretAssignmentPattern().Replace(
            bounded,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}=[redacted]");
        bounded = JsonSecretPattern().Replace(
            bounded,
            match => $"\"{match.Groups[1].Value}\":\"[redacted]\"");
        return BearerPattern().Replace(bounded, "Bearer [redacted]");
    }

    private static AgentEvent? TranslateItemStarted(JsonElement parameters, DateTimeOffset now)
    {
        if (!parameters.TryGetProperty("item", out var item)
            || item.ValueKind != JsonValueKind.Object
            || ReadString(item, "type") != "commandExecution")
        {
            return null;
        }

        return new AgentCommandStarted(
            ReadString(parameters, "threadId"),
            ReadString(parameters, "turnId"),
            ReadString(item, "id"),
            Redact(ReadString(item, "command")),
            ReadNullableString(item, "cwd"),
            now);
    }

    private static AgentEvent TranslateApproval(
        JsonElement root,
        JsonElement parameters,
        string method,
        DateTimeOffset now) =>
        new AgentApprovalRequested(
            ReadRequestId(root),
            method,
            ReadString(parameters, "threadId"),
            ReadString(parameters, "turnId"),
            ReadString(parameters, "itemId"),
            Redact(ReadNullableString(parameters, "command") ?? FriendlyApprovalSummary(method)),
            ReadNullableString(parameters, "cwd"),
            RedactNullable(ReadNullableString(parameters, "reason")),
            now);

    private static AgentEvent TranslateTurnCompleted(JsonElement parameters, DateTimeOffset now)
    {
        var turn = parameters.TryGetProperty("turn", out var turnElement)
            && turnElement.ValueKind == JsonValueKind.Object
            ? turnElement
            : default;
        return new AgentTurnCompleted(
            ReadString(parameters, "threadId"),
            ReadString(turn, "id"),
            ReadString(turn, "status"),
            RedactNullable(ReadNestedNullableString(turn, "error", "message")),
            now);
    }

    private static string ReadRequestId(JsonElement root)
    {
        if (!root.TryGetProperty("id", out var id))
        {
            return string.Empty;
        }

        return id.ValueKind == JsonValueKind.String ? id.GetString() ?? string.Empty : id.GetRawText();
    }

    private static string ReadString(JsonElement element, string name) =>
        ReadNullableString(element, name) ?? string.Empty;

    private static string? ReadNullableString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ReadNestedString(JsonElement element, string parent, string name) =>
        ReadNestedNullableString(element, parent, name) ?? string.Empty;

    private static string? ReadNestedNullableString(
        JsonElement element,
        string parent,
        string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(parent, out var nested)
            ? ReadNullableString(nested, name)
            : null;

    private static string FriendlyApprovalSummary(string method) => method switch
    {
        "item/fileChange/requestApproval" => "Apply proposed workspace file changes",
        "item/permissions/requestApproval" => "Use additional workspace permissions",
        _ => "Run the proposed command",
    };

    private static string? RedactNullable(string? value) => value is null ? null : Redact(value);

    [GeneratedRegex(
        @"(?i)(\$env:)?([A-Z0-9_]*(?:API[_-]?KEY|TOKEN|PASSWORD|SECRET)[A-Z0-9_]*)\s*=\s*(?:'[^']*'|""[^""]*""|\S+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretAssignmentPattern();

    [GeneratedRegex(
        "(?i)\\\"([A-Z0-9_]*(?:API[_-]?KEY|TOKEN|PASSWORD|SECRET)[A-Z0-9_]*)\\\"\\s*:\\s*\\\"[^\\\"]*\\\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretPattern();

    [GeneratedRegex(@"(?i)Bearer\s+[A-Za-z0-9._~+\-/]+=*", RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();
}

public static class CodexProtocolMessages
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Initialize(long requestId) => SerializeRequest(
        requestId,
        "initialize",
        new
        {
            clientInfo = new
            {
                name = "workspace_environment",
                title = "Workspace Environment",
                version = "0.2.0",
            },
            capabilities = new { experimentalApi = false },
        });

    public static string Initialized() => JsonSerializer.Serialize(new { method = "initialized" }, JsonOptions);

    public static string StartThread(long requestId, string cwd, AgentSandbox sandbox) =>
        SerializeRequest(requestId, "thread/start", new
        {
            cwd,
            approvalPolicy = "on-request",
            approvalsReviewer = "user",
            sandbox = SandboxName(sandbox),
        });

    public static string ResumeThread(
        long requestId,
        string threadId,
        string cwd,
        AgentSandbox sandbox) => SerializeRequest(requestId, "thread/resume", new
        {
            threadId,
            cwd,
            approvalPolicy = "on-request",
            approvalsReviewer = "user",
            sandbox = SandboxName(sandbox),
        });

    public static string StartTurn(long requestId, string threadId, string prompt) =>
        SerializeRequest(requestId, "turn/start", new
        {
            threadId,
            input = new[] { new { type = "text", text = prompt } },
        });

    public static string SteerTurn(
        long requestId,
        string threadId,
        string turnId,
        string instruction) => SerializeRequest(requestId, "turn/steer", new
        {
            threadId,
            expectedTurnId = turnId,
            input = new[] { new { type = "text", text = instruction } },
        });

    public static string InterruptTurn(long requestId, string threadId, string turnId) =>
        SerializeRequest(requestId, "turn/interrupt", new { threadId, turnId });

    public static string ApprovalResponse(string requestId, AgentApprovalDecision decision)
    {
        object id = long.TryParse(requestId, out var numericId) ? numericId : requestId;
        return JsonSerializer.Serialize(new
        {
            id,
            result = new { decision = ApprovalDecisionName(decision) },
        }, JsonOptions);
    }

    private static string SerializeRequest(long id, string method, object parameters) =>
        JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonOptions);

    private static string SandboxName(AgentSandbox sandbox) => sandbox switch
    {
        AgentSandbox.ReadOnly => "read-only",
        AgentSandbox.WorkspaceWrite => "workspace-write",
        AgentSandbox.DangerFullAccess => "danger-full-access",
        _ => throw new ArgumentOutOfRangeException(nameof(sandbox)),
    };

    private static string ApprovalDecisionName(AgentApprovalDecision decision) => decision switch
    {
        AgentApprovalDecision.Accept => "accept",
        AgentApprovalDecision.AcceptForSession => "acceptForSession",
        AgentApprovalDecision.Decline => "decline",
        AgentApprovalDecision.Cancel => "cancel",
        _ => throw new ArgumentOutOfRangeException(nameof(decision)),
    };
}
