using System.Collections.Concurrent;
using System.Text.Json;
using Workspace.Desktop.Core.Capabilities;
using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class WorkspaceActionOrchestratorTests
{
    [Fact]
    public async Task Mutation_speaks_no_success_before_host_completion()
    {
        var gateway = new ControllableWorkspaceGateway();
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output);
        var pending = orchestrator.BeginAsync(
            new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new { query = "Notepad" })),
            CancellationToken.None);

        await gateway.WaitUntilRequestedAsync();
        Assert.DoesNotContain(output.Messages, x => x.Contains("open", StringComparison.OrdinalIgnoreCase));
        gateway.Complete(new { disposition = "launched", applicationName = "Notepad", focused = true });
        await pending;

        Assert.Contains("Notepad is open and focused.", output.Messages);
    }

    [Fact]
    public async Task Open_rejects_ambiguous_search_before_requesting_approval_or_mutating()
    {
        var gateway = new ImmediateWorkspaceGateway(new
        {
            status = "ambiguous",
            candidates = new[] { new { id = "app:notepad", displayName = "Notepad" }, new { id = "app:notepad-plus", displayName = "Notepad++" } },
        });
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { query = "Note" })), CancellationToken.None);

        Assert.Equal(new[] { "application.search" }, gateway.Commands);
        Assert.Contains(output.Messages, message => message.StartsWith("I found more than one application", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Remembered_grant_never_authorizes_close()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"workspace-action-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var broker = await CapabilityBroker.OpenAsync(Path.Combine(directory, "grants.json"));
            await broker.RememberAsync(new CapabilityGrant("application.close", "window:pc.window:notepad", null));
            var gateway = new ImmediateWorkspaceGateway(new { state = "closed" });
            var output = new RecordingWorkspaceActionOutput();
            var orchestrator = new WorkspaceActionOrchestrator(gateway, output, broker);

            await orchestrator.BeginAsync(new WorkspaceDirective("application.close",
                JsonSerializer.SerializeToElement(new { windowEntityId = "pc.window:notepad" })), CancellationToken.None);

            Assert.Empty(gateway.Commands);
            Assert.NotNull(orchestrator.PendingApproval);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ControllableWorkspaceGateway : IWorkspaceCommandGateway
    {
        private readonly TaskCompletionSource<JsonElement> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<JsonElement> SendAsync(string command, object? arguments, CancellationToken cancellationToken)
        {
            if (command == "application.search")
            {
                return Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    status = "resolved",
                    application = new { id = "app:notepad", displayName = "Notepad" },
                }));
            }
            _requested.TrySetResult();
            return _completion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilRequestedAsync() => _requested.Task;

        public void Complete(object result) => _completion.TrySetResult(JsonSerializer.SerializeToElement(result));
    }

    private sealed class ImmediateWorkspaceGateway(object result) : IWorkspaceCommandGateway
    {
        public List<string> Commands { get; } = [];

        public Task<JsonElement> SendAsync(string command, object? arguments, CancellationToken cancellationToken)
        {
            Commands.Add(command);
            return Task.FromResult(JsonSerializer.SerializeToElement(result));
        }
    }

    private sealed class RecordingWorkspaceActionOutput : IWorkspaceActionOutput
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public Task RequestApprovalAsync(WorkspacePendingApproval approval, CancellationToken cancellationToken)
        {
            Messages.Enqueue(approval.Description);
            return Task.CompletedTask;
        }

        public Task ReportActivityAsync(string message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }

        public Task SpeakAsync(string message, CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            return Task.CompletedTask;
        }
    }
}
