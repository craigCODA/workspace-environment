using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class ApplicationControlTests
{
    [Fact]
    public async Task Open_reuses_a_visible_window_and_binds_the_selected_surface()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindow(
            "app:notepad", "pc.window:notepad", "spatial.surface:right");

        var result = await fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-1", "app:notepad", null,
                ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", null),
            CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Reused, result.Disposition);
        Assert.Equal("spatial.surface:right", result.SurfaceEntityId);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task Restart_stops_when_close_remains_pending()
    {
        var fixture = ApplicationControlFixture.WithCloseResult(WindowCloseState.ClosePending);

        var result = await fixture.Service.RestartAsync(
            new ApplicationRestartRequest("op-2", "pc.window:notepad"), CancellationToken.None);

        Assert.Equal(ApplicationLifecycleState.ClosePending, result.State);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task New_instance_attempts_launch_even_when_a_window_is_visible()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindow(
            "app:notepad", "pc.window:notepad", "spatial.surface:right");

        await fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-3", "app:notepad", null,
                ApplicationLaunchPolicy.NewInstance, null, null),
            CancellationToken.None);

        Assert.Equal(1, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task Open_rejects_an_occupied_surface_without_explicit_replacement()
    {
        var fixture = ApplicationControlFixture.WithOccupiedSurface();

        var exception = await Assert.ThrowsAsync<ApplicationControlException>(() => fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-4", "app:notepad", null,
                ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", null),
            CancellationToken.None));

        Assert.Equal("surface_occupied", exception.Code);
    }

    [Fact]
    public async Task Open_replaces_an_occupied_surface_without_closing_the_displaced_window()
    {
        var fixture = ApplicationControlFixture.WithOccupiedSurface();

        var result = await fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-5", "app:notepad", null,
                ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", true),
            CancellationToken.None);

        Assert.Equal("pc.window:notepad", result.WindowEntityId);
        Assert.Empty(fixture.Lifecycle.RequestedCloseIds);
    }

    private sealed class ApplicationControlFixture
    {
        private ApplicationControlFixture(
            ApplicationControlService service,
            RecordingProcessLauncher processLauncher,
            RecordingLifecycle lifecycle)
        {
            Service = service;
            ProcessLauncher = processLauncher;
            Lifecycle = lifecycle;
        }

        public ApplicationControlService Service { get; }

        public RecordingProcessLauncher ProcessLauncher { get; }

        public RecordingLifecycle Lifecycle { get; }

        public static ApplicationControlFixture WithVisibleWindow(
            string applicationId,
            string windowId,
            string surfaceId) => Create(
                applicationId,
                windowId,
                surfaceId,
                null);

        public static ApplicationControlFixture WithCloseResult(WindowCloseState closeState) => Create(
            "app:notepad",
            "pc.window:notepad",
            "spatial.surface:right",
            closeState);

        public static ApplicationControlFixture WithOccupiedSurface() => Create(
            "app:notepad",
            "pc.window:notepad",
            "spatial.surface:right",
            null,
            occupied: true);

        private static ApplicationControlFixture Create(
            string applicationId,
            string windowId,
            string surfaceId,
            WindowCloseState? closeState,
            bool occupied = false)
        {
            var application = new ApplicationDescriptor(
                applicationId, "Notepad", ApplicationLaunchKind.Executable, @"C:\Windows\notepad.exe", []);
            var window = new WindowSnapshot(
                (nint)47, 4700, "Notepad", new WindowBounds(1, 2, 640, 480), true, false, applicationId);
            var document = new WorkspaceDocument(2,
            [
                WorkspaceEntity.CreateApplication(applicationId, "Notepad"),
                WorkspaceEntity.CreateWindow(windowId, "Notepad", applicationId),
                WorkspaceEntity.CreateDisplaySurface(
                    surfaceId,
                    "Right",
                    PresentationState.Default,
                    occupied ? "pc.window:other" : null),
                WorkspaceEntity.CreateWindow("pc.window:other", "Other", "app:other"),
            ]);
            var process = new RecordingProcessLauncher(8800);
            var lifecycle = new RecordingLifecycle(closeState ?? WindowCloseState.Closed);
            var service = new ApplicationControlService(
                new InMemoryApplicationCatalog([application]),
                new ApplicationLauncher(process),
                new InMemoryWorkspaceStore(document),
                new FixedWindowCatalog([window]),
                lifecycle,
                new RecordingFocus());
            return new ApplicationControlFixture(service, process, lifecycle);
        }
    }

    private sealed class RecordingProcessLauncher(int? processId) : IProcessLauncher
    {
        public int LaunchCount { get; private set; }

        public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
        {
            LaunchCount++;
            return Task.FromResult(processId);
        }
    }

    private sealed class FixedWindowCatalog(IReadOnlyList<WindowSnapshot> windows) : IWindowCatalog
    {
        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(windows);
    }

    private sealed class RecordingLifecycle(WindowCloseState state) : IWindowLifecycleService
    {
        public List<string> RequestedCloseIds { get; } = [];

        public Task<WindowCloseState> RequestCloseAsync(
            string windowEntityId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            RequestedCloseIds.Add(windowEntityId);
            return Task.FromResult(state);
        }
    }

    private sealed class RecordingFocus : IWindowFocusService
    {
        public Task FocusAsync(string entityId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryWorkspaceStore(WorkspaceDocument document) : IWorkspaceStore
    {
        private WorkspaceDocument _document = document;

        public Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_document);

        public Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            _document = document;
            return Task.CompletedTask;
        }
    }
}
