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
        await using var fixture = await CapabilityFixture.CreateAsync();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output, fixture.Broker, fixture.WorkspaceIdentity);
        await orchestrator.BeginAsync(
            new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new { query = "Notepad" })),
            CancellationToken.None);
        var pending = orchestrator.RespondToApprovalAsync(WorkspaceApprovalDecision.AllowOnce, CancellationToken.None);

        await gateway.WaitUntilRequestedAsync();
        Assert.DoesNotContain(output.Messages, x => x.Contains("is open", StringComparison.OrdinalIgnoreCase));
        gateway.Complete(new
        {
            operationId = "op-1",
            applicationEntityId = "pc.application:notepad",
            windowEntityId = "pc.window:notepad",
            surfaceEntityId = "spatial.surface:west",
            disposition = "launched",
            surfaceState = "available",
            focused = true,
        });
        await pending;

        Assert.Contains("Notepad is open and focused.", output.Messages);
    }

    [Fact]
    public async Task Open_rejects_ambiguous_search_before_requesting_approval_or_mutating()
    {
        var gateway = new ImmediateWorkspaceGateway(new
        {
            status = "ambiguous",
            candidates = new[] { new { id = "pc.application:notepad", displayName = "Notepad" }, new { id = "pc.application:notepad-plus", displayName = "Notepad++" } },
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

    [Fact]
    public async Task Missing_capability_broker_fails_closed_without_a_mutation()
    {
        var gateway = new ImmediateWorkspaceGateway(ValidOpenResult());
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { applicationId = "pc.application:notepad" })), CancellationToken.None);

        Assert.Empty(gateway.Commands);
        Assert.Contains("Workspace application permissions are unavailable.", output.Messages);
    }

    [Fact]
    public async Task Replacement_requires_launch_and_replacement_approvals_before_mutating()
    {
        await using var fixture = await CapabilityFixture.CreateAsync();
        var gateway = new ImmediateWorkspaceGateway(ValidOpenResult());
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output, fixture.Broker, fixture.WorkspaceIdentity);
        var directive = new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new
        {
            applicationId = "pc.application:notepad",
            targetSurfaceId = "spatial.surface:west",
            replaceOccupied = true,
        }));

        await orchestrator.BeginAsync(directive, CancellationToken.None);
        await orchestrator.RespondToApprovalAsync(WorkspaceApprovalDecision.AllowOnce, CancellationToken.None);

        Assert.Empty(gateway.Commands);
        Assert.NotNull(orchestrator.PendingApproval);
        await orchestrator.RespondToApprovalAsync(WorkspaceApprovalDecision.AllowOnce, CancellationToken.None);

        Assert.Equal(new[] { "application.open" }, gateway.Commands);
        Assert.Equal(2, output.Approvals.Count);
    }

    [Fact]
    public async Task Invalid_host_result_never_produces_success_speech()
    {
        await using var fixture = await CapabilityFixture.CreateAsync();
        var gateway = new ImmediateWorkspaceGateway(new { ok = true });
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output, fixture.Broker, fixture.WorkspaceIdentity);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { applicationId = "pc.application:notepad" })), CancellationToken.None);
        await orchestrator.RespondToApprovalAsync(WorkspaceApprovalDecision.AllowOnce, CancellationToken.None);

        Assert.Contains(WorkspaceActionNarrator.DescribeFailure(), output.Messages);
        Assert.DoesNotContain(output.Messages, message => message.Contains("is open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Failed_approval_output_clears_the_pending_action()
    {
        await using var fixture = await CapabilityFixture.CreateAsync();
        var orchestrator = new WorkspaceActionOrchestrator(
            new ImmediateWorkspaceGateway(ValidOpenResult()), new FailingApprovalOutput(), fixture.Broker, fixture.WorkspaceIdentity);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { applicationId = "pc.application:notepad" })), CancellationToken.None);

        Assert.Null(orchestrator.PendingApproval);
    }

    [Fact]
    public async Task Profile_list_requires_an_observed_array_result_before_narration()
    {
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(new ImmediateWorkspaceGateway(Array.Empty<object>()), output);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.profile.list", JsonSerializer.SerializeToElement(new { })), CancellationToken.None);

        Assert.Contains("I found the saved application profiles.", output.Messages);
    }

    [Fact]
    public async Task Failed_search_envelope_cannot_be_used_to_authorize_an_open()
    {
        var gateway = new ImmediateWorkspaceGateway(new
        {
            ok = false,
            payload = new { status = "resolved", application = new { id = "pc.application:notepad", displayName = "Notepad" } },
            error = "ignored",
        });
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(gateway, output);

        await orchestrator.BeginAsync(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { query = "Notepad" })), CancellationToken.None);

        Assert.Empty(gateway.Commands.Skip(1));
        Assert.Contains("I couldn't find that application.", output.Messages);
    }

    [Fact]
    public async Task Profile_approval_names_the_profile_application_surface_and_each_argument()
    {
        await using var fixture = await CapabilityFixture.CreateAsync();
        var output = new RecordingWorkspaceActionOutput();
        var orchestrator = new WorkspaceActionOrchestrator(new ImmediateWorkspaceGateway(new { id = "profile:pythos" }), output,
            fixture.Broker, fixture.WorkspaceIdentity);
        var directive = new WorkspaceDirective("application.profile.save", JsonSerializer.SerializeToElement(new
        {
            id = "profile:pythos",
            displayName = "PythOS Codex",
            applicationId = "pc.application:terminal",
            arguments = new[] { "--new-window", "--title", "PythOS" },
            launchPolicy = "reuseOrLaunch",
            preferredSurfaceId = "spatial.surface:west",
        }));

        await orchestrator.BeginAsync(directive, CancellationToken.None);

        var approval = Assert.Single(output.Approvals);
        Assert.Contains("profile:pythos", approval.Description);
        Assert.Contains("pc.application:terminal", approval.Description);
        Assert.Contains("spatial.surface:west", approval.Description);
        Assert.Contains("--new-window, --title, PythOS", approval.Description);
    }

    private static object ValidOpenResult() => new
    {
        operationId = "op-1",
        applicationEntityId = "pc.application:notepad",
        windowEntityId = "pc.window:notepad",
        surfaceEntityId = "spatial.surface:west",
        disposition = "launched",
        surfaceState = "available",
        focused = true,
    };

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
                    application = new { id = "pc.application:notepad", displayName = "Notepad" },
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
        public ConcurrentQueue<WorkspacePendingApproval> Approvals { get; } = new();

        public Task RequestApprovalAsync(WorkspacePendingApproval approval, CancellationToken cancellationToken)
        {
            Approvals.Enqueue(approval);
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

    private sealed class FailingApprovalOutput : IWorkspaceActionOutput
    {
        public Task RequestApprovalAsync(WorkspacePendingApproval approval, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("output disconnected"));

        public Task ReportActivityAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SpeakAsync(string message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapabilityFixture : IAsyncDisposable
    {
        private CapabilityFixture(string directory, CapabilityBroker broker)
        {
            Directory = directory;
            Broker = broker;
        }

        public string Directory { get; }
        public string WorkspaceIdentity => "workspace:test";
        public CapabilityBroker Broker { get; }

        public static async Task<CapabilityFixture> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"workspace-action-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new CapabilityFixture(directory, await CapabilityBroker.OpenAsync(Path.Combine(directory, "grants.json")));
        }

        public ValueTask DisposeAsync()
        {
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }
}
