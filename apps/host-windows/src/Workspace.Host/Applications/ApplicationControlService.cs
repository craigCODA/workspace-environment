using System.Text.Json;
using System.Text.Json.Serialization;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Windows;

namespace Workspace.Host.Applications;

public enum ApplicationOpenDisposition { Reused, Launched, LaunchedWithoutWindow }

public enum ApplicationSurfaceState { Available, Unavailable, NotResolved }

public enum ApplicationLifecycleState { Open, Closed, ClosePending, NotRunning, Failed }

public sealed record ApplicationOpenRequest(
    string OperationId,
    string? ApplicationId,
    string? ProfileId,
    ApplicationLaunchPolicy LaunchPolicy,
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
    ApplicationLaunchPolicy LaunchPolicy,
    string? SurfaceEntityId,
    bool? ReplaceOccupied,
    string? ApprovalSource);

public sealed record ApplicationProfileProtocolRequest(
    string Id,
    string DisplayName,
    string ApplicationId,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    ApplicationLaunchPolicy LaunchPolicy,
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

    public static ApplicationOpenProtocolRequest ParseOpen(JsonElement payload) =>
        Deserialize<ApplicationOpenProtocolRequest>(payload);

    public static ApplicationCloseRequest ParseClose(string operationId, JsonElement payload)
    {
        var request = Deserialize<WindowRequest>(payload);
        return new ApplicationCloseRequest(operationId, request.WindowEntityId, request.ApprovalSource);
    }

    public static ApplicationRestartRequest ParseRestart(string operationId, JsonElement payload)
    {
        var request = Deserialize<WindowRequest>(payload);
        return new ApplicationRestartRequest(operationId, request.WindowEntityId, request.ApprovalSource);
    }

    public static ApplicationProfileProtocolRequest ParseProfile(JsonElement payload) =>
        Deserialize<ApplicationProfileProtocolRequest>(payload);

    public static string ParseProfileId(JsonElement payload) => Deserialize<ProfileIdRequest>(payload).ProfileId;

    public static SurfaceBindRequest ParseSurfaceBind(JsonElement payload) => Deserialize<SurfaceBindRequest>(payload);

    public static string ParseSearch(JsonElement payload) => Deserialize<SearchRequest>(payload).Query;

    private static T Deserialize<T>(JsonElement payload) where T : class =>
        JsonSerializer.Deserialize<T>(payload.GetRawText(), JsonOptions)
        ?? throw new JsonException("Request payload was empty.");

    private sealed record WindowRequest(string WindowEntityId, string? ApprovalSource);

    private sealed record ProfileIdRequest(string ProfileId);

    private sealed record SearchRequest(string Query);
}

public sealed record SurfaceBindRequest(string SurfaceEntityId, string WindowEntityId, bool? ReplaceOccupied);

public sealed class ApplicationControlService(
    IApplicationCatalog applicationCatalog,
    ApplicationLauncher applicationLauncher,
    IWorkspaceStore workspaceStore,
    IWindowCatalog windowCatalog,
    IWindowLifecycleService windowLifecycleService,
    IWindowFocusService windowFocusService,
    IApplicationProfileStore? applicationProfileStore = null,
    ApplicationControlAuditStore? auditStore = null)
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
            var existing = FindVisibleWindow(
                await windowCatalog.ListAsync(cancellationToken), launch.Application.Id);
            if (request.LaunchPolicy == ApplicationLaunchPolicy.ReuseOrLaunch && existing is not null)
            {
                var reused = await BindAndFocusAsync(request, launch.Application, existing, null,
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
                await AuditAsync(request, noWindow, ApplicationLifecycleState.Open, null, cancellationToken);
                return noWindow;
            }

            var disposition = observedHwnds.Contains(appeared.Hwnd)
                ? ApplicationOpenDisposition.Reused
                : ApplicationOpenDisposition.Launched;
            var result = await BindAndFocusAsync(request, launch.Application, appeared, launched.ProcessId,
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

        var open = await OpenAsync(new ApplicationOpenRequest(request.OperationId, applicationId, profileId,
            ApplicationLaunchPolicy.NewInstance, surfaceId, true, request.ApprovalSource), cancellationToken);
        return new ApplicationRestartResult(request.OperationId, request.WindowEntityId,
            ApplicationLifecycleState.Open, open);
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
        ApplicationDescriptor application,
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
            windowId = document.Entities.FirstOrDefault(entity => entity.Kind == EntityKinds.Window
                && entity.Relationships.Any(relationship => relationship.Type == "belongs-to"
                    && string.Equals(relationship.TargetId, application.Id, StringComparison.Ordinal)))?.Id
                ?? $"pc.window:{window.ApplicationId}";
            EnsureApplicationAndWindow(document, application, window, windowId, request.ProfileId);
            surfaceId = ResolveSurfaceId(request, document, windowId);
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
        return new ApplicationOpenResult(request.OperationId, application.Id, windowId, surfaceId, processId,
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
            var processMatch = launchedProcessId is { } processId
                ? candidates.FirstOrDefault(window => window.ProcessId == processId)
                : null;
            if (processMatch is not null) return processMatch;
            var newlyAppeared = candidates.Where(window => !observedHwnds.Contains(window.Hwnd)).ToArray();
            if (newlyAppeared.Length == 1) return newlyAppeared[0];
            if (launchedProcessId is null && candidates.Length == 1) return candidates[0];
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
        return null;
    }

    private async Task<(ApplicationDescriptor Application, IReadOnlyList<string> Arguments, string? WorkingDirectory)>
        ResolveLaunchAsync(ApplicationOpenRequest request, CancellationToken cancellationToken)
    {
        ApplicationLaunchProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(request.ProfileId))
        {
            profile = await RequireProfileStore().FindAsync(request.ProfileId, cancellationToken)
                ?? throw new ApplicationControlException("profile_not_found", "Launch profile was not found.");
        }
        var query = request.ApplicationId ?? profile?.ApplicationId;
        if (string.IsNullOrWhiteSpace(query))
            throw new ApplicationControlException("invalid_target", "An application id or profile id is required.");
        var resolution = ApplicationResolver.Resolve(query, await applicationCatalog.ListAsync(cancellationToken));
        if (resolution.Status == ApplicationResolutionStatus.NotFound)
            throw new ApplicationControlException("application_not_found", $"Application '{query}' was not found.");
        if (resolution.Status == ApplicationResolutionStatus.Ambiguous)
            throw new ApplicationControlException("application_ambiguous", "Application query was ambiguous.");
        return (resolution.Application!, profile?.Arguments ?? [], profile?.WorkingDirectory);
    }

    private static WindowSnapshot? FindVisibleWindow(IEnumerable<WindowSnapshot> windows, string applicationId) =>
        windows.FirstOrDefault(window => IsVisibleForSurface(window)
            && string.Equals(window.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));

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
            };
        }
        else document.Entities.Add(window);
    }

    private static string ResolveSurfaceId(ApplicationOpenRequest request, WorkspaceDocument document, string windowId)
    {
        var surfaceId = request.SurfaceEntityId
            ?? document.Entities.FirstOrDefault(entity => entity.Kind == EntityKinds.Surface
                && entity.Relationships.Any(relationship => relationship.Type == "displays" && relationship.TargetId == windowId))?.Id
            ?? $"spatial.surface:{windowId}";
        if (document.Entities.All(entity => entity.Id != surfaceId))
            document.Entities.Add(WorkspaceEntity.CreateDisplaySurface(surfaceId, surfaceId,
                PresentationState.Default));
        return surfaceId;
    }

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
