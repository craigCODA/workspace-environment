using Microsoft.UI.Xaml;
using Workspace.Desktop.Runtime;

namespace Workspace.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopCoordinator _coordinator;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _coordinator = new DesktopCoordinator(WorkspaceWebView, DispatcherQueue);
        WorkspaceWebView.Loaded += OnWorkspaceLoaded;
        AppWindow.Closing += (_, _) => _ = _coordinator.DisposeAsync();
    }

    private async void OnWorkspaceLoaded(object sender, RoutedEventArgs e)
    {
        WorkspaceWebView.Loaded -= OnWorkspaceLoaded;
        try
        {
            StartupStatusText.Text = "Starting Windows workspace…";
            await _coordinator.StartAsync();
            StartupStatus.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            StartupStatusText.Text = $"Coda could not start. {exception.Message}";
        }
    }
}
