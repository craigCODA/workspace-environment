using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using Workspace.Host.Applications;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Runtime;
using Workspace.Host.Windows;

const string listenerPrefix = "http://127.0.0.1:41771/";
const string workspacePath = "/workspace";

HostRuntimeOptions runtimeOptions;
try
{
    runtimeOptions = HostRuntimeOptions.Parse(args);
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 2;
    return;
}

var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var store = new AtomicWorkspaceStore(Path.Combine(
    localData,
    "WorkspaceEnvironment",
    "workspace.json"));
await WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None);
var applicationProfileStore = new AtomicApplicationProfileStore(Path.Combine(
    localData,
    "WorkspaceEnvironment",
    "application-profiles.json"));
var applicationCatalog = new WindowsApplicationCatalog();
var knownApplications = (await applicationCatalog.ListAsync(CancellationToken.None))
    .ToDictionary(
        application => Path.GetFullPath(application.ExecutablePath),
        application => application.Id,
        StringComparer.OrdinalIgnoreCase);
var windowCatalog = new Win32WindowCatalog(processId =>
    ResolveApplicationId(processId, knownApplications));

IWindowCapture windowCapture;
try
{
    windowCapture = new GraphicsCaptureWindowCapture();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Windows Graphics Capture unavailable: {exception.Message}");
    windowCapture = new UnavailableWindowCapture(exception.Message);
}

await using var windowReconciler = new WindowReconciler(windowCapture);
var inputRouter = new Win32InputRouter();
var dispatcher = new CommandDispatcher(
    applicationCatalog,
    new ApplicationLauncher(new SystemProcessLauncher()),
    store,
    new Win32WindowFocusService(windowCatalog, windowReconciler),
    windowCatalog,
    windowReconciler,
    inputRouter);
var protocolServer = new WorkspaceProtocolServer(dispatcher, store);

using var shutdown = new CancellationTokenSource();
var parentMonitor = runtimeOptions.ParentProcessId is { } parentProcessId
    ? ParentProcessMonitor.CancelWhenParentExitsAsync(
        parentProcessId,
        shutdown,
        CancellationToken.None)
    : Task.CompletedTask;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using var listener = new HttpListener();
listener.Prefixes.Add(listenerPrefix);
listener.Start();
Console.WriteLine("Workspace Host listening at ws://127.0.0.1:41771/workspace");
var windowMonitor = MonitorWindowsAsync(windowCatalog, windowReconciler, shutdown.Token);

try
{
    while (!shutdown.IsCancellationRequested)
    {
        var context = await listener.GetContextAsync().WaitAsync(shutdown.Token);
        _ = HandleRequestAsync(context, protocolServer, shutdown.Token);
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
finally
{
    shutdown.Cancel();
    listener.Stop();
    try
    {
        await windowMonitor;
    }
    catch (OperationCanceledException)
    {
    }
    await parentMonitor;
}

static string? ResolveApplicationId(
    int processId,
    IReadOnlyDictionary<string, string> knownApplications)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        var executablePath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        executablePath = Path.GetFullPath(executablePath);
        return knownApplications.TryGetValue(executablePath, out var applicationId)
            ? applicationId
            : WindowsApplicationCatalog.CreateStableId(executablePath);
    }
    catch (Exception exception) when (
        exception is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or NotSupportedException)
    {
        return null;
    }
}

static async Task MonitorWindowsAsync(
    IWindowCatalog windowCatalog,
    WindowReconciler windowReconciler,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var windows = await windowCatalog.ListAsync(cancellationToken);
            await windowReconciler.ReconcileAsync(windows, cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Window lifecycle monitor failed: {exception.Message}");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }
}

static async Task HandleRequestAsync(
    HttpListenerContext context,
    WorkspaceProtocolServer protocolServer,
    CancellationToken cancellationToken)
{
    if (!string.Equals(context.Request.Url?.AbsolutePath, workspacePath, StringComparison.Ordinal)
        || !context.Request.IsWebSocketRequest
        || !LoopbackOriginPolicy.IsTrusted(context.Request.Headers["Origin"]))
    {
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.Close();
        return;
    }

    try
    {
        var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
        using var socket = webSocketContext.WebSocket;
        await protocolServer.RunConnectionAsync(socket, cancellationToken);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Workspace connection failed: {exception.Message}");
        context.Response.Abort();
    }
}
