using Microsoft.UI.Xaml;
using Workspace.Desktop.Runtime;

namespace Workspace.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopCoordinator _coordinator;
    private readonly string? _healthPipe;
    private readonly string? _healthToken;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var arguments = Environment.GetCommandLineArgs();
        _healthPipe = ArgumentValue(arguments, "--health-pipe");
        _healthToken = ArgumentValue(arguments, "--health-token");
        _coordinator = new DesktopCoordinator(
            WorkspaceWebView,
            DispatcherQueue,
            ArgumentValue(arguments, "--source-root"),
            ArgumentValue(arguments, "--state-root"));
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
            await LauncherHealthSignal.ReportAsync(_healthPipe, _healthToken);
        }
        catch (Exception exception)
        {
            StartupStatusText.Text = $"Coda could not start. {exception.Message}";
        }
    }

    private static string? ArgumentValue(string[] arguments, string name)
    {
        var index = Array.FindIndex(arguments, argument =>
            argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
}
