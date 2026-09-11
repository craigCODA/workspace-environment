using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class AgentStartupRecoveryTests
{
    [Fact]
    public async Task Successful_agent_startup_reports_ready_without_cleanup()
    {
        var cleaned = false;

        var result = await AgentStartupRecovery.TryStartAsync(
            _ => Task.CompletedTask,
            () =>
            {
                cleaned = true;
                return ValueTask.CompletedTask;
            });

        Assert.True(result.Ready);
        Assert.Null(result.Error);
        Assert.False(cleaned);
    }

    [Fact]
    public async Task Failed_agent_startup_is_nonfatal_and_cleans_partial_runtime()
    {
        var cleaned = false;

        var result = await AgentStartupRecovery.TryStartAsync(
            _ => throw new FileNotFoundException("codex command missing"),
            () =>
            {
                cleaned = true;
                return ValueTask.CompletedTask;
            });

        Assert.False(result.Ready);
        Assert.Equal("codex command missing", result.Error);
        Assert.True(cleaned);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_converted_into_provider_unavailability()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AgentStartupRecovery.TryStartAsync(
                token => Task.FromCanceled(token),
                () => ValueTask.CompletedTask,
                cancellation.Token));
    }
}
