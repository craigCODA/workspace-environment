namespace Workspace.Desktop.Core.Runtime;

public sealed record AgentStartupRecoveryResult(bool Ready, string? Error);

public static class AgentStartupRecovery
{
    public static async Task<AgentStartupRecoveryResult> TryStartAsync(
        Func<CancellationToken, Task> start,
        Func<ValueTask> cleanup,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await start(cancellationToken).ConfigureAwait(false);
            return new AgentStartupRecoveryResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await cleanup().ConfigureAwait(false);
            return new AgentStartupRecoveryResult(false, exception.Message);
        }
    }
}
