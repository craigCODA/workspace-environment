namespace Workspace.Desktop.Core.Agent;

public abstract record AgentEvent(DateTimeOffset Timestamp);

public sealed record AgentStatus(string Message, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);

public sealed record AgentThreadStarted(string ThreadId, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);

public sealed record AgentTurnStarted(string ThreadId, string TurnId, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);

public sealed record AgentAssistantDelta(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Text,
    DateTimeOffset Timestamp) : AgentEvent(Timestamp);

public sealed record AgentTerminalDelta(
    string ThreadId,
    string TurnId,
    string ItemId,
    string Text,
    DateTimeOffset Timestamp) : AgentEvent(Timestamp);

public sealed record AgentCommandStarted(
    string ThreadId,
    string TurnId,
    string ItemId,
    string CommandSummary,
    string? WorkingDirectory,
    DateTimeOffset Timestamp) : AgentEvent(Timestamp);

public sealed record AgentApprovalRequested(
    string RequestId,
    string Method,
    string ThreadId,
    string TurnId,
    string ItemId,
    string CommandSummary,
    string? WorkingDirectory,
    string? Reason,
    DateTimeOffset Timestamp) : AgentEvent(Timestamp);

public sealed record AgentTurnCompleted(
    string ThreadId,
    string TurnId,
    string Status,
    string? Error,
    DateTimeOffset Timestamp) : AgentEvent(Timestamp);

public sealed record AgentAuthenticationRequired(string Message, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);

public sealed record AgentProtocolFailure(string Message, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);

public sealed record AgentProcessExited(int ExitCode, string Message, DateTimeOffset Timestamp)
    : AgentEvent(Timestamp);
