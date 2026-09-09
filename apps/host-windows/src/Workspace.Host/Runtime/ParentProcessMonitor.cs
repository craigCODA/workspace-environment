using System.Diagnostics;

namespace Workspace.Host.Runtime;

public static class ParentProcessMonitor
{
    public static async Task CancelWhenParentExitsAsync(
        int parentProcessId,
        CancellationTokenSource shutdown,
        CancellationToken cancellationToken)
    {
        if (parentProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentProcessId));
        }
        ArgumentNullException.ThrowIfNull(shutdown);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            shutdown.Token,
            cancellationToken);
        try
        {
            using var parent = Process.GetProcessById(parentProcessId);
            await parent.WaitForExitAsync(linked.Token);
        }
        catch (ArgumentException)
        {
            // A parent that vanished before it could be observed has already exited.
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            return;
        }

        if (!shutdown.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            shutdown.Cancel();
        }
    }
}
