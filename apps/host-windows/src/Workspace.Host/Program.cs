using System.Net;
using Workspace.Host.Applications;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;

const string listenerPrefix = "http://127.0.0.1:41771/";
const string workspacePath = "/workspace";

var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var store = new AtomicWorkspaceStore(Path.Combine(
    localData,
    "WorkspaceEnvironment",
    "workspace.json"));
var dispatcher = new CommandDispatcher(
    new WindowsApplicationCatalog(),
    new ApplicationLauncher(new SystemProcessLauncher()),
    store,
    new UnavailableWindowFocusService());
var protocolServer = new WorkspaceProtocolServer(dispatcher, store);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

using var listener = new HttpListener();
listener.Prefixes.Add(listenerPrefix);
listener.Start();
Console.WriteLine("Workspace Host listening at ws://127.0.0.1:41771/workspace");

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
    listener.Stop();
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
