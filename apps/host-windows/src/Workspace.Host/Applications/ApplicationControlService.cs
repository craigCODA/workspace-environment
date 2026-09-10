using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using System.Diagnostics.CodeAnalysis;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Windows;

namespace Workspace.Host.Applications;

public enum ApplicationOpenDisposition { Reused, Launched, LaunchedWithoutWindow }

public enum ApplicationSurfaceState { Available, Unavailable, NotResolved }

public enum ApplicationLifecycleState { Open, Closed, ClosePending, NotRunning, LaunchedWithoutWindow, Failed }

public sealed record ApplicationOpenRequest(
    string OperationId,
    string? ApplicationId,
    string? ProfileId,
    ApplicationLaunchPolicy? LaunchPolicy,
    string? SurfaceEntityId,
    bool? ReplaceOccupied,
    string? ApprovalSource = null);

public sealed record ApplicationOpenResult(
    string OperationId,
    string ApplicationEntityId,
    string? WindowEntityId,
    string? SurfaceEntityId,
    int? ProcessId,
    ApplicationOpenDisposition Disposition,
    ApplicationSurfaceState SurfaceState,
    bool Focused);

public sealed record ApplicationCloseRequest(string OperationId, string WindowEntityId, string? ApprovalSource = null);

public sealed record ApplicationCloseResult(
    string OperationId,
    string WindowEntityId,
    ApplicationLifecycleState State);

public sealed record ApplicationRestartRequest(string OperationId, string WindowEntityId, string? ApprovalSource = null);

public sealed record ApplicationRestartResult(
    string OperationId,
    string WindowEntityId,
    ApplicationLifecycleState State,
    ApplicationOpenResult? OpenResult);

public sealed record ApplicationSearchResult(
    ApplicationResolutionStatus Status,
    ApplicationDescriptor? Application,
    IReadOnlyList<ApplicationDescriptor> Candidates);

public sealed class ApplicationControlException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record ApplicationOpenProtocolRequest(
    string? ApplicationId,
    string? ProfileId,
    ApplicationLaunchPolicy? LaunchPolicy,
    string? SurfaceEntityId,
    bool? ReplaceOccupied,
    string? ApprovalSource);

public sealed record ApplicationProfileProtocolRequest(
    string Id,
    string DisplayName,
    string ApplicationId,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    ApplicationLaunchPolicy? LaunchPolicy,
    string? PreferredSurfaceId,
    PresentationState? PreferredPresentation);

public static class ApplicationControlRequestParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static ApplicationOpenProtocolRequest ParseOpen(JsonElement payload)
    {
        var request = Deserialize<ApplicationOpenProtocolRequest>(payload);
        if (HasText(request.ApplicationId) == HasText(request.ProfileId))
            throw new JsonException("Application open requires exactly one applicationId or profileId.");
        return request;
    }

    public static ApplicationCloseRequest ParseClose(string operationId, JsonElement payload)
    {
        var request = Deserialize<WindowRequest>(payload);
        if (!HasText(request.WindowEntityId)) throw new JsonException("windowEntityId is required.");
        return new ApplicationCloseRequest(operationId, request.WindowEntityId, request.ApprovalSource);
    }

    public static ApplicationRestartRequest ParseRestart(string operationId, JsonElement payload)
    {
        var request = Deserialize<WindowRequest>(payload);
        if (!HasText(request.WindowEntityId)) throw new JsonException("windowEntityId is required.");
        return new ApplicationRestartRequest(operationId, request.WindowEntityId, request.ApprovalSource);
    }

    public static ApplicationProfileProtocolRequest ParseProfile(JsonElement payload)
    {
        var request = Deserialize<ApplicationProfileProtocolRequest>(payload);
        if (!HasText(request.Id) || !HasText(request.DisplayName) || !HasText(request.ApplicationId)
            || request.Arguments is null || request.LaunchPolicy is null)
            throw new JsonException("Profile payload has required fields missing.");
        return request;
    }

    public static string ParseProfileId(JsonElement payload)
    {
        var profileId = Deserialize<ProfileIdRequest>(payload).ProfileId;
        return HasText(profileId) ? profileId : throw new JsonException("profileId is required.");
    }

    public static SurfaceBindRequest ParseSurfaceBind(JsonElement payload)
    {
        var request = Deserialize<SurfaceBindRequest>(payload);
        if (!HasText(request.SurfaceEntityId) || !HasText(request.WindowEntityId))
            throw new JsonException("Surface binding requires surfaceEntityId and windowEntityId.");
        return request;
    }

    public static string ParseSearch(JsonElement payload)
    {
        var query = Deserialize<SearchRequest>(payload).Query;
        return HasText(query) ? query : throw new JsonException("query is required.");
    }

    private static T Deserialize<T>(JsonElement payload) where T : class =>
        JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions)
        ?? throw new JsonException("Request payload was empty.");

    private static bool HasText([NotNullWhen(true)] string? value) => !string.IsNullOrWhiteSpace(value);

    private sealed record WindowRequest(string? WindowEntityId, string? ApprovalSource);

    private sealed record ProfileIdRequest(string? ProfileId);

    private sealed record SearchRequest(string? Query);
}

public sealed record SurfaceBindRequest(string SurfaceEntityId, string WindowEntityId, bool? ReplaceOccupied);

public interface IProcessTree
{
    bool IsDescendantOf(int processId, int ancestorProcessId);
}

internal sealed class WindowsProcessTree : IProcessTree
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly nint InvalidHandleValue = new(-1);

    public static WindowsProcessTree Instance { get; } = new();

    public bool IsDescendantOf(int processId, int ancestorProcessId)
    {
        if (processId <= 0 || ancestorProcessId <= 0 || processId == ancestorProcessId) return false;
        try
        {
            var parents = SnapshotParents();
            var current = processId;
            var visited = new HashSet<int>();
            while (parents.TryGetValue(current, out var parent) && parent > 0 && visited.Add(current))
            {
                if (parent == ancestorProcessId) return true;
                current = parent;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        return false;
    }

    private static Dictionary<int, int> SnapshotParents()
    {
        var handle = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (handle == InvalidHandleValue) return [];
        try
        {
            var entry = new ProcessEntry32 { DwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            var parents = new Dictionary<int, int>();
            if (!Process32First(handle, ref entry)) return parents;
            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(handle, ref entry));
            return parents;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);
}

public sealed class ApplicationControlService(
    IApplicationCatalog applicationCatalog,
    ApplicationLauncher applicationLauncher,
    IWorkspaceStore workspaceStore,
    IWindowCatalog windowCatalog,
    IWindowLifecycleService windowLifecycleService,
    IWindowFocusService windowFocusService,
    IApplicationProfileStore? applicationProfileStore = null,
    ApplicationControlAuditStore? auditStore = null,
    IProcessTree? processTree = null)
{
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public async Task<ApplicationSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var applications = await applicationCatalog.ListAsync(cancellationToken);
        var resolution = ApplicationResolver.Resolve(query, applications);
        return new ApplicationSearchResult(resolution.Status, resolution.Application, resolution.Candidates);
    }

    public Task<IReadOnlyList<ApplicationLaunchProfile>> ListProfilesAsync(CancellationToken cancellationToken) =>
        RequireProfileStore().ListAsync(cancellationToken);

    public Task SaveProfileAsync(ApplicationLaunchProfile profile, CancellationToken cancellationToken) =>
        RequireProfileStore().SaveAsync(profile, cancellationToken);

    public Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken) =>
        RequireProfileStore().DeleteAsync(profileId, cancellationToken);

    public async Task<ApplicationOpenResult> OpenAsync(ApplicationOpenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OperationId);
        try
        {
            var launch = await ResolveLaunchAsync(request, cancellationToken);
            var preflight = await workspaceStore.LoadAsync(cancellationToken);
            EnsureRequestedSurfaceIsAvailable(preflight, launch.SurfaceEntityId, request.ReplaceOccupied == true);

            var existing = FindVisibleWindows(
                await windowCatalog.ListAsync(cancellationToken), launch.Application.Id);
            if (launch.LaunchPolicy == ApplicationLaunchPolicy.ReuseOrLaunch && existing.Count > 1)
                throw new ApplicationControlException("application_window_ambiguous", "Multiple visible application windows match the request.");
            if (launch.LaunchPolicy == ApplicationLaunchPolicy.ReuseOrLaunch && existing.Count == 1)
            {
                var reused = await BindAndFocusAsync(request, launch, existing[0], null,
                    ApplicationOpenDisposition.Reused, cancellationToken);
                await AuditAsync(request, reused, ApplicationLifecycleState.Open, null, cancellationToken);
                return reused;
            }

            var observedHwnds = (await windowCatalog.ListAsync(cancellationToken))
                .Select(window => window.Hwnd)
                .ToHashSet();
            var launched = await applicationLauncher.LaunchAsync(
                launch.Application, launch.Arguments, launch.WorkingDirectory, cancellationToken);
            var appeared = await WaitForWindowAsync(
                launch.Application.Id, launched.ProcessId, observedHwnds, cancellationToken);
            if (appeared is null)
            {
                var noWindow = new ApplicationOpenResult(request.OperationId, launch.Application.Id, null,
                    null, launched.ProcessId, ApplicationOpenDisposition.LaunchedWithoutWindow,
                    ApplicationSurfaceState.NotResolved, false);
                await AuditAsync(request, noWindow, ApplicationLifecycleState.LaunchedWithoutWindow, null, cancellationToken);
                return noWindow;
            }

            var disposition = observedHwnds.Contains(appeared.Hwnd)
                ? ApplicationOpenDisposition.Reused
                : ApplicationOpenDisposition.Launched;
            var result = await BindAndFocusAsync(request, launch, appeared, launched.ProcessId,
                disposition, cancellationToken);
            await AuditAsync(request, result, ApplicationLifecycleState.Open, null, cancellationToken);
            return result;
        }
        catch (ApplicationControlException exception)
        {
            await AuditFailureAsync(request, exception.Code, cancellationToken);
            throw;
        }
        catch
        {
            await AuditFailureAsync(request, "operation_failed", cancellationToken);
            throw;
        }
    }

    public async Task<ApplicationCloseResult> CloseAsync(ApplicationCloseRequest request, CancellationToken cancellationToken)
    {
        var state = await windowLifecycleService.RequestCloseAsync(request.WindowEntityId, CloseTimeout, cancellationToken);
        var result = new ApplicationCloseResult(request.OperationId, request.WindowEntityId, ToLifecycleState(state));
        await AuditAsync(request.OperationId, "application.close", null, request.WindowEntityId, null,
            request.ApprovalSource, result.State, null, cancellationToken);
        return result;
    }

    public async Task<ApplicationRestartResult> RestartAsync(ApplicationRestartRequest request, CancellationToken cancellationToken)
    {
        var document = await workspaceStore.LoadAsync(cancellationToken);
        var window = document.Entities.FirstOrDefault(entity =>
            entity.Kind == EntityKinds.Window && string.Equals(entity.Id, request.WindowEntityId, StringComparison.Ordinal));
        if (window is null) throw new ApplicationControlException("window_not_found", "Window entity was not found.");
        var applicationId = window.Properties.TryGetValue("applicationId", out var applicationProperty)
            ? applicationProperty.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(applicationId))
            throw new ApplicationControlException("invalid_window", "Window entity has no application identity.");
        var surfaceId = document.Entities.FirstOrDefault(entity => entity.Kind == EntityKinds.Surface
            && entity.Relationships.Any(relationship => relationship.Type == "displays"
                && relationship.TargetId == request.WindowEntityId))?.Id;
        var profileId = window.Properties.TryGetValue("profileId", out var profileProperty)
            ? profileProperty.GetString()
            : null;

        var close = await windowLifecycleService.RequestCloseAsync(request.WindowEntityId, CloseTimeout, cancellationToken);
        var closeState = ToLifecycleState(close);
        if (close == WindowCloseState.ClosePending)
        {
            var pending = new ApplicationRestartResult(request.OperationId, request.WindowEntityId, closeState, null);
            await AuditAsync(request.OperationId, "application.restart", applicationId, request.WindowEntityId, surfaceId,
                request.ApprovalSource, closeState, null, cancellationToken);
            return pending;
        }

        var open = await OpenAsync(new ApplicationOpenRequest(request.OperationId,
            profileId is null ? applicationId : null, profileId,
            ApplicationLaunchPolicy.NewInstance, surfaceId, true, request.ApprovalSource), cancellationToken);
        var finalState = open.Disposition == ApplicationOpenDisposition.LaunchedWithoutWindow
            ? ApplicationLifecycleState.LaunchedWithoutWindow
            : ApplicationLifecycleState.Open;
        await AuditAsync(request.OperationId, "application.restart", applicationId, request.WindowEntityId,
            surfaceId, request.ApprovalSource, finalState, null, cancellationToken);
        return new ApplicationRestartResult(request.OperationId, request.WindowEntityId, finalState, open);
    }

    public async Task BindWindowAsync(SurfaceBindRequest request, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var document = await workspaceStore.LoadAsync(cancellationToken);
            EnsureSurfaceCanBind(document, request.SurfaceEntityId, request.WindowEntityId, request.ReplaceOccupied == true);
            if (!document.TryBindWindow(request.SurfaceEntityId, request.WindowEntityId, out _))
                throw new ApplicationControlException("invalid_binding", "Surface and window entities must exist.");
            await workspaceStore.SaveAsync(document, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<ApplicationOpenResult> BindAndFocusAsync(
        ApplicationOpenRequest request,
        ResolvedLaunch launch,
        WindowSnapshot window,
        int? processId,
        ApplicationOpenDisposition disposition,
        CancellationToken cancellationToken)
    {
        string windowId;
        string? surfaceId;
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            var document = await workspaceStore.LoadAsync(cancellationToken);
            windowId = ResolveWindowEntityId(document, launch.Application, window);
            EnsureApplicationAndWindow(document, launch.Application, window, windowId, launch.ProfileId);
            surfaceId = ResolveSurfaceId(launch, document, windowId);
            EnsureSurfaceCanBind(document, surfaceId, windowId, request.ReplaceOccupied == true);
            if (!document.TryBindWindow(surfaceId, windowId, out _))
                throw new ApplicationControlException("invalid_binding", "Surface binding could not be persisted.");
            await workspaceStore.SaveAsync(document, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }

        await windowFocusService.FocusAsync(windowId, cancellationToken);
        return new ApplicationOpenResult(request.OperationId, launch.Application.Id, windowId, surfaceId, processId,
            disposition, ApplicationSurfaceState.Available, true);
    }

    private async Task<WindowSnapshot?> WaitForWindowAsync(
        string applicationId,
        int? launchedProcessId,
        IReadOnlySet<nint> observedHwnds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + WindowWaitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var candidates = (await windowCatalog.ListAsync(cancellationToken))
                .Where(window => IsVisibleForSurface(window)
                    && string.Equals(window.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var processMatches = launchedProcessId is { } processId
                ? candidates.Where(window => window.ProcessId == processId
                    || (processTree ?? WindowsProcessTree.Instance).IsDescendantOf(window.ProcessId, processId)).ToArray()
                : [];
            if (processMatches.Length == 1) return processMatches[0];
            var newlyAppeared = candidates.Where(window => !observedHwnds.Contains(window.Hwnd)).ToArray();
            if (newlyAppeared.Length == 1) return newlyAppeared[0];
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return null;
    }

    private async Task<ResolvedLaunch>
        ResolveLaunchAsync(ApplicationOpenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationId) == string.IsNullOrWhiteSpace(request.ProfileId))
            throw new ApplicationControlException("invalid_target", "Application open requires exactly one application id or profile id.");
        ApplicationLaunchProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(request.ProfileId))
        {
            profile = await RequireProfileStore().FindAsync(request.ProfileId, cancellationToken)
                ?? throw new ApplicationControlException("profile_not_found", "Launch profile was not found.");
        }
        var query = profile?.ApplicationId ?? request.ApplicationId;
        if (string.IsNullOrWhiteSpace(query))
            throw new ApplicationControlException("invalid_target", "An application id or profile id is required.");
        var resolution = ApplicationResolver.Resolve(query, await applicationCatalog.ListAsync(cancellationToken));
        if (resolution.Status == ApplicationResolutionStatus.NotFound)
            throw new ApplicationControlException("application_not_found", $"Application '{query}' was not found.");
        if (resolution.Status == ApplicationResolutionStatus.Ambiguous)
            throw new ApplicationControlException("application_ambiguous", "Application query was ambiguous.");
        return new ResolvedLaunch(
            resolution.Application!,
            profile?.Arguments ?? [],
            profile?.WorkingDirectory,
            request.LaunchPolicy ?? profile?.LaunchPolicy ?? ApplicationLaunchPolicy.ReuseOrLaunch,
            request.SurfaceEntityId ?? profile?.PreferredSurfaceId,
            profile?.PreferredPresentation,
            profile?.Id);
    }

    private static IReadOnlyList<WindowSnapshot> FindVisibleWindows(IEnumerable<WindowSnapshot> windows, string applicationId) =>
        windows.Where(window => IsVisibleForSurface(window)
                && string.Equals(window.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(window => window.Hwnd.ToInt64())
            .ToArray();

    private static bool IsVisibleForSurface(WindowSnapshot window) =>
        window.Hwnd != nint.Zero && window.IsVisible && !window.IsMinimized
        && window.Bounds.Width > 0 && window.Bounds.Height > 0;

    private static ApplicationLifecycleState ToLifecycleState(WindowCloseState state) => state switch
    {
        WindowCloseState.Closed => ApplicationLifecycleState.Closed,
        WindowCloseState.ClosePending => ApplicationLifecycleState.ClosePending,
        WindowCloseState.NotRunning => ApplicationLifecycleState.NotRunning,
        _ => ApplicationLifecycleState.Failed,
    };

    private static void EnsureApplicationAndWindow(
        WorkspaceDocument document, ApplicationDescriptor application, WindowSnapshot snapshot, string windowId,
        string? profileId)
    {
        if (document.Entities.All(entity => entity.Id != application.Id))
            document.Entities.Add(WorkspaceEntity.CreateApplication(application.Id, application.DisplayName));
        var window = WorkspaceEntity.CreateWindow(windowId,
            string.IsNullOrWhiteSpace(snapshot.Title) ? application.DisplayName : snapshot.Title, application.Id);
        var index = document.Entities.FindIndex(entity => entity.Id == windowId);
        if (index >= 0)
        {
            var properties = new Dictionary<string, JsonElement>(window.Properties);
            if (!string.IsNullOrWhiteSpace(profileId)) properties["profileId"] = JsonSerializer.SerializeToElement(profileId);
            else if (document.Entities[index].Properties.TryGetValue("profileId", out var existingProfile))
                properties["profileId"] = existingProfile;
            document.Entities[index] = window with
            {
                Presentation = document.Entities[index].Presentation,
                Properties = properties,
                HostBinding = new HostBinding("window", HwndLocator(snapshot.Hwnd)),
            };
        }
        else document.Entities.Add(window with { HostBinding = new HostBinding("window", HwndLocator(snapshot.Hwnd)) });
    }

    private static string ResolveSurfaceId(ResolvedLaunch launch, WorkspaceDocument document, string windowId)
    {
        var surfaceId = launch.SurfaceEntityId
            ?? document.Entities.FirstOrDefault(entity => entity.Kind == EntityKinds.Surface
                && entity.Relationships.Any(relationship => relationship.Type == "displays" && relationship.TargetId == windowId))?.Id
            ?? $"spatial.surface:{windowId}";
        if (document.Entities.All(entity => entity.Id != surfaceId))
            document.Entities.Add(WorkspaceEntity.CreateDisplaySurface(surfaceId, surfaceId,
                launch.PreferredPresentation ?? PresentationState.Default));
        else if (launch.PreferredPresentation is not null)
        {
            var index = document.Entities.FindIndex(entity => entity.Id == surfaceId);
            document.Entities[index] = document.Entities[index] with { Presentation = launch.PreferredPresentation };
        }
        return surfaceId;
    }

    private static void EnsureRequestedSurfaceIsAvailable(
        WorkspaceDocument document, string? surfaceId, bool replaceOccupied)
    {
        if (surfaceId is null) return;
        var surface = document.Entities.FirstOrDefault(entity => entity.Id == surfaceId);
        if (surface is null || surface.Kind != EntityKinds.Surface)
            throw new ApplicationControlException("surface_not_found", "Target surface was not found.");
        if (!replaceOccupied && surface.Relationships.Any(relationship => relationship.Type == "displays"))
            throw new ApplicationControlException("surface_occupied", "Target surface is already occupied.");
    }

    private static string ResolveWindowEntityId(
        WorkspaceDocument document, ApplicationDescriptor application, WindowSnapshot snapshot)
    {
        var locator = HwndLocator(snapshot.Hwnd);
        var exact = document.Entities.FirstOrDefault(entity => entity.Kind == EntityKinds.Window
            && string.Equals(entity.HostBinding?.Locator, locator, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Id;
        var legacy = document.Entities.Where(entity => entity.Kind == EntityKinds.Window
            && entity.HostBinding?.Locator.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase) != true
            && entity.Relationships.Any(relationship => relationship.Type == "belongs-to"
                && string.Equals(relationship.TargetId, application.Id, StringComparison.Ordinal))).ToArray();
        return legacy.Length == 1
            ? legacy[0].Id
            : $"pc.window:{application.Id}:{snapshot.Hwnd.ToInt64():X}";
    }

    private static string HwndLocator(nint hwnd) => $"hwnd:{hwnd.ToInt64():X}";

    private sealed record ResolvedLaunch(
        ApplicationDescriptor Application,
        IReadOnlyList<string> Arguments,
        string? WorkingDirectory,
        ApplicationLaunchPolicy LaunchPolicy,
        string? SurfaceEntityId,
        PresentationState? PreferredPresentation,
        string? ProfileId);

    private static void EnsureSurfaceCanBind(
        WorkspaceDocument document, string surfaceId, string windowId, bool replaceOccupied)
    {
        var surface = document.Entities.FirstOrDefault(entity => entity.Id == surfaceId);
        if (surface is null || surface.Kind != EntityKinds.Surface)
            throw new ApplicationControlException("surface_not_found", "Target surface was not found.");
        var occupiedWindow = surface.Relationships.FirstOrDefault(relationship => relationship.Type == "displays")?.TargetId;
        if (occupiedWindow is not null && !string.Equals(occupiedWindow, windowId, StringComparison.Ordinal)
            && !replaceOccupied)
            throw new ApplicationControlException("surface_occupied", "Target surface is already occupied.");
    }

    private IApplicationProfileStore RequireProfileStore() => applicationProfileStore
        ?? throw new ApplicationControlException("profile_store_unavailable", "Application profiles are not configured.");

    private Task AuditAsync(
        ApplicationOpenRequest request,
        ApplicationOpenResult result,
        ApplicationLifecycleState state,
        string? errorCategory,
        CancellationToken cancellationToken) =>
        AuditAsync(request.OperationId, "application.open", result.ApplicationEntityId, result.WindowEntityId,
            result.SurfaceEntityId, request.ApprovalSource, state, errorCategory, cancellationToken);

    private Task AuditFailureAsync(ApplicationOpenRequest request, string errorCategory, CancellationToken cancellationToken) =>
        AuditAsync(request.OperationId, "application.open", request.ApplicationId, null, request.SurfaceEntityId,
            request.ApprovalSource, ApplicationLifecycleState.Failed, errorCategory, cancellationToken);

    private Task AuditAsync(string operationId, string operation, string? applicationId, string? windowId,
        string? surfaceId, string? approvalSource, ApplicationLifecycleState state, string? errorCategory,
        CancellationToken cancellationToken) =>
        auditStore?.RecordAsync(new ApplicationControlAuditRecord(operationId, operation, applicationId, windowId,
            surfaceId, approvalSource, state, errorCategory), cancellationToken) ?? Task.CompletedTask;
}
