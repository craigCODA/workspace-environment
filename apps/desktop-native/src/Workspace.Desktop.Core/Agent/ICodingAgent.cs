namespace Workspace.Desktop.Core.Agent;

public enum AgentSandbox
{
    ReadOnly,
    WorkspaceWrite,
    DangerFullAccess,
}

public enum AgentApprovalDecision
{
    Accept,
    AcceptForSession,
    Decline,
    Cancel,
}

public interface ICodingAgent : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task<string> StartOrResumeThreadAsync(
        string sourceRoot,
        string? threadId,
        AgentSandbox sandbox,
        CancellationToken cancellationToken = default);

    Task<string> StartTurnAsync(string prompt, CancellationToken cancellationToken = default);

    Task SteerAsync(string instruction, CancellationToken cancellationToken = default);

    Task InterruptAsync(CancellationToken cancellationToken = default);

    Task RespondToApprovalAsync(
        string requestId,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<AgentEvent> ReadEventsAsync(CancellationToken cancellationToken = default);
}
