namespace Workspace.Host.Applications;

public enum WindowCloseState
{
    Closed,
    ClosePending,
    NotRunning,
}

public interface IWindowLifecycleService
{
    Task<WindowCloseState> RequestCloseAsync(
        string windowEntityId,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
