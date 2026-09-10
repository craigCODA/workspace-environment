using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class ApplicationControlAuditStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"workspace-audit-{Guid.NewGuid():N}");

    [Fact]
    public async Task Record_persists_a_bounded_redacted_open_audit_entry()
    {
        var path = Path.Combine(_directory, "application-control-audit.json");
        var store = new ApplicationControlAuditStore(path);

        var app = new ApplicationDescriptor("app:notepad", "Notepad", ApplicationLaunchKind.Executable,
            @"C:\Windows\notepad.exe", []);
        var window = new WindowSnapshot((nint)44, 4400, "Notepad", new WindowBounds(0, 0, 640, 480), true, false, app.Id);
        var workspace = new AuditWorkspaceStore(new WorkspaceDocument(2,
        [
            WorkspaceEntity.CreateApplication(app.Id, app.DisplayName),
            WorkspaceEntity.CreateWindow("pc.window:notepad", "Notepad", app.Id),
            WorkspaceEntity.CreateDisplaySurface("spatial.surface:right", "Right", PresentationState.Default),
        ]));
        var service = new ApplicationControlService(
            new InMemoryApplicationCatalog([app]),
            new ApplicationLauncher(new AuditProcessLauncher()),
            workspace,
            new AuditWindowCatalog(window),
            new AuditLifecycle(),
            new AuditFocus(),
            auditStore: store);

        var open = await service.OpenAsync(new ApplicationOpenRequest("op-99", app.Id, null,
            ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", null, "user-approved"),
            CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Reused, open.Disposition);

        await store.RecordAsync(new ApplicationControlAuditRecord(
            "op-sensitive", "application.open", "app:notepad", "pc.window:notepad",
            "spatial.surface:right", "user-approved", ApplicationLifecycleState.Open,
            "capture_failed", "captured-image-data", "raw microphone words"), CancellationToken.None);

        for (var index = 0; index < 499; index++)
        {
            await store.RecordAsync(new ApplicationControlAuditRecord(
                $"op-{index}", "application.open", "app:notepad", "pc.window:notepad",
                "spatial.surface:right", "user-approved", ApplicationLifecycleState.Open, null),
                CancellationToken.None);
        }

        var records = await store.ListAsync(CancellationToken.None);
        Assert.Equal(500, records.Count);
        var record = records.Single(record => record.OperationId == "op-sensitive");
        Assert.Equal("app:notepad", record.ApplicationEntityId);
        Assert.Equal("pc.window:notepad", record.WindowEntityId);
        Assert.Equal("spatial.surface:right", record.SurfaceEntityId);
        Assert.Equal("user-approved", record.ApprovalSource);
        Assert.Equal(ApplicationLifecycleState.Open, record.LifecycleState);
        Assert.Equal("capture_failed", record.ErrorCategory);
        var persisted = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("captured-image-data", persisted);
        Assert.DoesNotContain("raw microphone words", persisted);
    }

    [Fact]
    public async Task Restart_records_its_own_final_lifecycle_state()
    {
        var store = new ApplicationControlAuditStore(Path.Combine(_directory, "restart-audit.json"));
        var app = new ApplicationDescriptor("app:notepad", "Notepad", ApplicationLaunchKind.Executable,
            @"C:\Windows\notepad.exe", []);
        var window = new WindowSnapshot((nint)45, 4500, "Notepad", new WindowBounds(0, 0, 640, 480), true, false, app.Id);
        var service = new ApplicationControlService(
            new InMemoryApplicationCatalog([app]),
            new ApplicationLauncher(new AuditProcessLauncher()),
            new AuditWorkspaceStore(new WorkspaceDocument(2,
            [
                WorkspaceEntity.CreateApplication(app.Id, app.DisplayName),
                WorkspaceEntity.CreateWindow("pc.window:notepad", "Notepad", app.Id),
                WorkspaceEntity.CreateDisplaySurface("spatial.surface:right", "Right", PresentationState.Default, "pc.window:notepad"),
            ])),
            new AuditWindowCatalog(window),
            new AuditLifecycle(),
            new AuditFocus(),
            auditStore: store);

        var restart = await service.RestartAsync(
            new ApplicationRestartRequest("restart-99", "pc.window:notepad", "user-approved"), CancellationToken.None);

        Assert.Equal(ApplicationLifecycleState.LaunchedWithoutWindow, restart.State);
        var restartAudit = (await store.ListAsync(CancellationToken.None))
            .Single(record => record.Operation == "application.restart");
        Assert.Equal(ApplicationLifecycleState.LaunchedWithoutWindow, restartAudit.LifecycleState);
        var openAudit = (await store.ListAsync(CancellationToken.None))
            .Single(record => record.Operation == "application.open");
        Assert.Equal(ApplicationLifecycleState.LaunchedWithoutWindow, openAudit.LifecycleState);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private sealed class AuditProcessLauncher : IProcessLauncher
    {
        public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<int?>(null);
    }

    private sealed class AuditWindowCatalog(WindowSnapshot window) : IWindowCatalog
    {
        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WindowSnapshot>>([window]);
    }

    private sealed class AuditLifecycle : IWindowLifecycleService
    {
        public Task<WindowCloseState> RequestCloseAsync(string windowEntityId, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(WindowCloseState.Closed);
    }

    private sealed class AuditFocus : IWindowFocusService
    {
        public Task FocusAsync(string entityId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class AuditWorkspaceStore(WorkspaceDocument document) : IWorkspaceStore
    {
        public Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(document);

        public Task SaveAsync(WorkspaceDocument saved, CancellationToken cancellationToken)
        {
            document = saved;
            return Task.CompletedTask;
        }
    }
}
