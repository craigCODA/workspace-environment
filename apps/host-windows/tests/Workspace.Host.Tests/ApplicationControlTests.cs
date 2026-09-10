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
    public async Task Restart_propagates_a_launched_without_window_final_state()
    {
        var fixture = ApplicationControlFixture.WithCloseResult(WindowCloseState.Closed);

        var result = await fixture.Service.RestartAsync(
            new ApplicationRestartRequest("op-restart-no-window", "pc.window:notepad"), CancellationToken.None);

        Assert.Equal(ApplicationLifecycleState.LaunchedWithoutWindow, result.State);
        Assert.NotNull(result.OpenResult);
        Assert.Equal(ApplicationOpenDisposition.LaunchedWithoutWindow, result.OpenResult!.Disposition);
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
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
        Assert.Equal(0, fixture.Focus.FocusCount);
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

    [Fact]
    public async Task Open_idempotently_reuses_the_window_already_displayed_by_the_target_surface()
    {
        var fixture = ApplicationControlFixture.WithSurfaceBoundToVisibleWindow();

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-idempotent", "app:notepad", null, ApplicationLaunchPolicy.ReuseOrLaunch,
            "spatial.surface:right", null), CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Reused, result.Disposition);
        Assert.Equal("pc.window:notepad", result.WindowEntityId);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task Open_uses_explicit_presentation_only_when_it_creates_a_new_surface()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindow(
            "app:notepad", "pc.window:notepad", "spatial.surface:right");
        var presentation = PresentationState.Default with { Position = new Vec3(4.2, 1.4, -3) };

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-new-surface", "app:notepad", null, ApplicationLaunchPolicy.ReuseOrLaunch,
            null, null, Presentation: presentation), CancellationToken.None);

        Assert.Equal("spatial.surface:pc.window:notepad", result.SurfaceEntityId);
        Assert.Equal(presentation, fixture.Store.Document.Entities
            .Single(entity => entity.Id == result.SurfaceEntityId).Presentation);
    }

    [Fact]
    public async Task Open_does_not_apply_explicit_presentation_to_an_existing_target_surface()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindow(
            "app:notepad", "pc.window:notepad", "spatial.surface:right");
        var original = fixture.Store.Document.Entities.Single(entity => entity.Id == "spatial.surface:right").Presentation;
        var requested = PresentationState.Default with { Position = new Vec3(9, 9, 9) };

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-existing-surface", "app:notepad", null, ApplicationLaunchPolicy.ReuseOrLaunch,
            "spatial.surface:right", null, Presentation: requested), CancellationToken.None);

        Assert.Equal("spatial.surface:right", result.SurfaceEntityId);
        Assert.Equal(original, fixture.Store.Document.Entities
            .Single(entity => entity.Id == "spatial.surface:right").Presentation);
    }

    [Fact]
    public async Task Open_rejects_invalid_explicit_presentation_before_focus_or_persistence()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindow(
            "app:notepad", "pc.window:notepad", "spatial.surface:right");
        var original = fixture.Store.Document.Entities.ToArray();

        var exception = await Assert.ThrowsAsync<ApplicationControlException>(() => fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-invalid-presentation", "app:notepad", null,
                ApplicationLaunchPolicy.ReuseOrLaunch, null, null,
                Presentation: PresentationState.Default with { Size = new Vec3(0, 1, 1) }), CancellationToken.None));

        Assert.Equal("invalid_payload", exception.Code);
        Assert.Equal(0, fixture.Focus.FocusCount);
        Assert.Equal(original, fixture.Store.Document.Entities);
    }

    [Fact]
    public async Task Open_rejects_an_occupied_surface_before_attempting_a_new_instance_launch()
    {
        var fixture = ApplicationControlFixture.WithOccupiedSurface();

        var exception = await Assert.ThrowsAsync<ApplicationControlException>(() => fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-preflight", "app:notepad", null,
                ApplicationLaunchPolicy.NewInstance, "spatial.surface:right", null), CancellationToken.None));

        Assert.Equal("surface_occupied", exception.Code);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
        Assert.Equal(0, fixture.Focus.FocusCount);
    }

    [Fact]
    public async Task Open_applies_all_profile_defaults_when_no_policy_override_is_supplied()
    {
        var profile = new ApplicationLaunchProfile("profile:notepad", "Notepad profile", "app:notepad",
            ["/a"], @"C:\work", ApplicationLaunchPolicy.NewInstance, "spatial.surface:right",
            PresentationState.Default with { Position = new Vec3(2, 3, 4) });
        var fixture = ApplicationControlFixture.WithProfileAndLaunchedWindow(profile);

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-profile", null, profile.Id, null, null, null), CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Launched, result.Disposition);
        Assert.Equal(["/a"], fixture.ProcessLauncher.LastRequest!.Arguments);
        Assert.Equal(@"C:\work", fixture.ProcessLauncher.LastRequest.WorkingDirectory);
        Assert.Equal(1, fixture.ProcessLauncher.LaunchCount);
        Assert.Equal(profile.PreferredSurfaceId, result.SurfaceEntityId);
        Assert.Equal(profile.PreferredPresentation, fixture.Store.Document.Entities
            .Single(entity => entity.Id == profile.PreferredSurfaceId).Presentation);
    }

    [Fact]
    public async Task Open_rejects_combining_a_profile_with_an_application_identity()
    {
        var fixture = ApplicationControlFixture.WithProfile(new ApplicationLaunchProfile("profile:notepad", "Notepad",
            "app:notepad", [], null, ApplicationLaunchPolicy.ReuseOrLaunch, null, null));

        var exception = await Assert.ThrowsAsync<ApplicationControlException>(() => fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-identity", "app:notepad", "profile:notepad", null, null, null),
            CancellationToken.None));

        Assert.Equal("invalid_target", exception.Code);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task Open_uses_an_explicit_request_policy_over_the_saved_profile_policy()
    {
        var profile = new ApplicationLaunchProfile("profile:notepad", "Notepad", "app:notepad", [], null,
            ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", null);
        var fixture = ApplicationControlFixture.WithProfileAndLaunchedWindow(profile);

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-profile-override", null, profile.Id, ApplicationLaunchPolicy.NewInstance, null, null),
            CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Launched, result.Disposition);
        Assert.Equal(1, fixture.ProcessLauncher.LaunchCount);
    }

    [Fact]
    public async Task Open_rejects_ambiguous_visible_reuse_without_a_side_effect()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindows(
        [
            new WindowSnapshot((nint)47, 4700, "Notepad A", new WindowBounds(1, 2, 640, 480), true, false, "app:notepad"),
            new WindowSnapshot((nint)48, 4800, "Notepad B", new WindowBounds(1, 2, 640, 480), true, false, "app:notepad"),
        ]);

        var exception = await Assert.ThrowsAsync<ApplicationControlException>(() => fixture.Service.OpenAsync(
            new ApplicationOpenRequest("op-ambiguous", "app:notepad", null,
                ApplicationLaunchPolicy.ReuseOrLaunch, null, null), CancellationToken.None));

        Assert.Equal("application_window_ambiguous", exception.Code);
        Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
        Assert.Equal(0, fixture.Focus.FocusCount);
    }

    [Fact]
    public async Task New_instance_with_no_pid_does_not_guess_a_preexisting_window()
    {
        var fixture = ApplicationControlFixture.WithVisibleWindowAndLaunchPid(null);

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest("op-pidless", "app:notepad", null,
            ApplicationLaunchPolicy.NewInstance, null, null), CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.LaunchedWithoutWindow, result.Disposition);
        Assert.Null(result.WindowEntityId);
    }

    [Fact]
    public async Task Open_accepts_a_descendant_process_window_when_the_process_tree_confirms_it()
    {
        var fixture = ApplicationControlFixture.WithLaunchedDescendantWindow();

        var result = await fixture.Service.OpenAsync(new ApplicationOpenRequest(
            "op-descendant", "app:notepad", null, ApplicationLaunchPolicy.NewInstance, null, null),
            CancellationToken.None);

        Assert.Equal(ApplicationOpenDisposition.Launched, result.Disposition);
        Assert.Equal(8800, result.ProcessId);
    }

    [Fact]
    public async Task Win32_close_targets_the_exact_persisted_window_hwnd()
    {
        var target = new WindowSnapshot((nint)0x2a, 4200, "Target", new WindowBounds(0, 0, 10, 10), true, false, "app:notepad");
        var other = new WindowSnapshot((nint)0x2b, 4201, "Other", new WindowBounds(0, 0, 10, 10), true, false, "app:notepad");
        var document = new WorkspaceDocument(2,
        [
            WorkspaceEntity.CreateWindow("pc.window:target", "Target", "app:notepad") with
            { HostBinding = new HostBinding("window", "hwnd:2A") },
        ]);
        var sent = new List<nint>();
        var lifecycle = new Win32WindowLifecycleService(
            new FixedWindowCatalog([target, other]),
            new InMemoryWorkspaceStore(document),
            hwnd =>
            {
                sent.Add(hwnd);
                return true;
            });

        var state = await lifecycle.RequestCloseAsync("pc.window:target", TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Equal(WindowCloseState.ClosePending, state);
        Assert.Equal([(nint)0x2a], sent);
    }

    [Fact]
    public async Task Win32_close_resolves_a_legacy_main_binding_only_when_one_compatible_window_is_live()
    {
        var target = new WindowSnapshot((nint)0x2a, 4200, "Target", new WindowBounds(0, 0, 10, 10), true, false, "app:notepad");
        var document = new WorkspaceDocument(2,
        [
            WorkspaceEntity.CreateWindow("pc.window:notepad", "Target", "app:notepad"),
        ]);
        var sent = new List<nint>();
        var lifecycle = new Win32WindowLifecycleService(new FixedWindowCatalog([target]),
            new InMemoryWorkspaceStore(document), hwnd => { sent.Add(hwnd); return true; });

        var state = await lifecycle.RequestCloseAsync("pc.window:notepad", TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Equal(WindowCloseState.ClosePending, state);
        Assert.Equal([(nint)0x2a], sent);
    }

    [Fact]
    public async Task Win32_close_refuses_a_reused_hwnd_with_a_different_application_identity()
    {
        var reused = new WindowSnapshot((nint)0x2a, 4200, "Other", new WindowBounds(0, 0, 10, 10), true, false, "app:other");
        var document = new WorkspaceDocument(2,
        [
            WorkspaceEntity.CreateWindow("pc.window:notepad", "Target", "app:notepad") with
            { HostBinding = new HostBinding("window", "hwnd:2A") },
        ]);
        var sent = new List<nint>();
        var lifecycle = new Win32WindowLifecycleService(new FixedWindowCatalog([reused]),
            new InMemoryWorkspaceStore(document), hwnd => { sent.Add(hwnd); return true; });

        var state = await lifecycle.RequestCloseAsync("pc.window:notepad", TimeSpan.FromMilliseconds(1), CancellationToken.None);

        Assert.Equal(WindowCloseState.NotRunning, state);
        Assert.Empty(sent);
    }

    private sealed class ApplicationControlFixture
    {
        private ApplicationControlFixture(
            ApplicationControlService service,
            RecordingProcessLauncher processLauncher,
            RecordingLifecycle lifecycle,
            RecordingFocus focus,
            InMemoryWorkspaceStore store)
        {
            Service = service;
            ProcessLauncher = processLauncher;
            Lifecycle = lifecycle;
            Focus = focus;
            Store = store;
        }

        public ApplicationControlService Service { get; }

        public RecordingProcessLauncher ProcessLauncher { get; }

        public RecordingLifecycle Lifecycle { get; }

        public RecordingFocus Focus { get; }

        public InMemoryWorkspaceStore Store { get; }

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

        public static ApplicationControlFixture WithSurfaceBoundToVisibleWindow() => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null,
            occupied: true, occupiedWindowId: "pc.window:notepad");

        public static ApplicationControlFixture WithLaunchedDescendantWindow() => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null,
            windowCatalog: new SequencedWindowCatalog(
            [
                [],
                [],
                [new WindowSnapshot((nint)99, 9900, "Notepad", new WindowBounds(1, 2, 640, 480), true, false, "app:notepad")],
            ]),
            processTree: new DescendantProcessTree(9900, 8800));

        public static ApplicationControlFixture WithVisibleWindowAndLaunchPid(int? processId) => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null, launchProcessId: processId);

        public static ApplicationControlFixture WithProfile(ApplicationLaunchProfile profile) => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null, profile: profile);

        public static ApplicationControlFixture WithProfileAndLaunchedWindow(ApplicationLaunchProfile profile) => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null, profile: profile,
            windowCatalog: new SequencedWindowCatalog(
            [
                [],
                [],
                [new WindowSnapshot((nint)88, 8800, "Notepad", new WindowBounds(1, 2, 640, 480), true, false, "app:notepad")],
            ]));

        public static ApplicationControlFixture WithVisibleWindows(IReadOnlyList<WindowSnapshot> windows) => Create(
            "app:notepad", "pc.window:notepad", "spatial.surface:right", null, windows: windows);

        private static ApplicationControlFixture Create(
            string applicationId,
            string windowId,
            string surfaceId,
            WindowCloseState? closeState,
            bool occupied = false,
            string? occupiedWindowId = null,
            int? launchProcessId = 8800,
            ApplicationLaunchProfile? profile = null,
            IReadOnlyList<WindowSnapshot>? windows = null,
            IWindowCatalog? windowCatalog = null,
            IProcessTree? processTree = null)
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
                    occupied ? occupiedWindowId ?? "pc.window:other" : null),
                WorkspaceEntity.CreateWindow("pc.window:other", "Other", "app:other"),
            ]);
            var process = new RecordingProcessLauncher(launchProcessId);
            var lifecycle = new RecordingLifecycle(closeState ?? WindowCloseState.Closed);
            var focus = new RecordingFocus();
            var store = new InMemoryWorkspaceStore(document);
            var service = new ApplicationControlService(
                new InMemoryApplicationCatalog([application]),
                new ApplicationLauncher(process),
                store,
                windowCatalog ?? new FixedWindowCatalog(windows ?? [window]),
                lifecycle,
                focus,
                profile is null ? null : new InMemoryProfileStore(profile),
                processTree: processTree);
            return new ApplicationControlFixture(service, process, lifecycle, focus, store);
        }
    }

    private sealed class RecordingProcessLauncher(int? processId) : IProcessLauncher
    {
        public int LaunchCount { get; private set; }

        public ApplicationStartRequest? LastRequest { get; private set; }

        public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
        {
            LaunchCount++;
            LastRequest = request;
            return Task.FromResult(processId);
        }
    }

    private sealed class FixedWindowCatalog(IReadOnlyList<WindowSnapshot> windows) : IWindowCatalog
    {
        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(windows);
    }

    private sealed class SequencedWindowCatalog(IReadOnlyList<IReadOnlyList<WindowSnapshot>> observations) : IWindowCatalog
    {
        private int _index;

        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            var index = Math.Min(_index++, observations.Count - 1);
            return Task.FromResult(observations[index]);
        }
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
        public int FocusCount { get; private set; }

        public Task FocusAsync(string entityId, CancellationToken cancellationToken)
        {
            FocusCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryWorkspaceStore(WorkspaceDocument document) : IWorkspaceStore
    {
        private WorkspaceDocument _document = document;

        public WorkspaceDocument Document => _document;

        public Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(_document);

        public Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            _document = document;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryProfileStore(ApplicationLaunchProfile profile) : IApplicationProfileStore
    {
        public Task<IReadOnlyList<ApplicationLaunchProfile>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ApplicationLaunchProfile>>([profile]);

        public Task<ApplicationLaunchProfile?> FindAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult<ApplicationLaunchProfile?>(profile.Id == profileId ? profile : null);

        public Task SaveAsync(ApplicationLaunchProfile value, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class DescendantProcessTree(int childProcessId, int parentProcessId) : IProcessTree
    {
        public bool IsDescendantOf(int processId, int ancestorProcessId) =>
            processId == childProcessId && ancestorProcessId == parentProcessId;
    }
}
