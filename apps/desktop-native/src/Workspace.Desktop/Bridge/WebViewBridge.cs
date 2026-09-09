using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Workspace.Desktop.Core.Bridge;

namespace Workspace.Desktop.Bridge;

public sealed class WebViewBridge
{
    private readonly WebView2 _webView;
    private readonly RendererMessageValidator _validator = new();
    private readonly TaskCompletionSource _rendererReady = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _initialized;

    public WebViewBridge(WebView2 webView)
    {
        _webView = webView;
    }

    public event Action<WebViewMessage>? MessageReceived;

    public async Task InitializeAsync(
        string spatialClientDirectory,
        CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }
        var root = Path.GetFullPath(spatialClientDirectory);
        if (!File.Exists(Path.Combine(root, "index.html")))
        {
            throw new FileNotFoundException(
                "The built spatial client is missing index.html.",
                Path.Combine(root, "index.html"));
        }

        await _webView.EnsureCoreWebView2Async();
        var core = _webView.CoreWebView2;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(
            "workspace.local",
            root,
            CoreWebView2HostResourceAccessKind.DenyCors);
        core.NavigationStarting += (_, args) =>
        {
            if (!IsWorkspaceUri(args.Uri))
            {
                args.Cancel = true;
            }
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.WebMessageReceived += OnWebMessageReceived;
        core.Navigate("https://workspace.local/index.html");
        _initialized = true;
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task WaitForRendererAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _rendererReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException("The spatial renderer did not become ready within 30 seconds.");
        }
    }

    public void Post(string type, object? payload)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("The WebView bridge is not initialized.");
        }
        var json = JsonSerializer.Serialize(new { version = 1, type, payload },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    public void PostSceneCommand(string requestId, string command, object? arguments)
    {
        _validator.ExpectSceneResult(requestId);
        Post("scene.command", new { id = requestId, command, args = arguments });
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!_validator.TryParse(args.WebMessageAsJson, out var message, out _)
            || message is null)
        {
            return;
        }
        if (message.Type == "renderer.ready")
        {
            _rendererReady.TrySetResult();
        }
        MessageReceived?.Invoke(message);
    }

    private static bool IsWorkspaceUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("workspace.local", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;
}
