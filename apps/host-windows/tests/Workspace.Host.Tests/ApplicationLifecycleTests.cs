using Workspace.Host.Applications;

namespace Workspace.Host.Tests;

public sealed class ApplicationLifecycleTests
{
    [Fact]
    public async Task LaunchUsesDescriptorWithoutEdgeSpecificLogic()
    {
        var app = new ApplicationDescriptor(
            "app:microsoft-edge",
            "Microsoft Edge",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            null);
        var launcher = new FakeProcessLauncher(4242);

        var result = await new ApplicationLauncher(launcher)
            .LaunchAsync(app, CancellationToken.None);

        Assert.Equal(app.Id, result.ApplicationId);
        Assert.Equal(4242, result.ProcessId);
        Assert.Equal(app.ExecutablePath, launcher.LastExecutablePath);
    }

    [Fact]
    public async Task CatalogLookupMatchesHumanFacingNameCaseInsensitively()
    {
        IApplicationCatalog catalog = new InMemoryApplicationCatalog(
        [
            new ApplicationDescriptor("app:microsoft-edge", "Microsoft Edge", @"C:\Edge\msedge.exe", null),
            new ApplicationDescriptor("app:notepad", "Notepad", @"C:\Windows\notepad.exe", null),
        ]);

        var result = await catalog.FindByNameAsync("microsoft edge", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("app:microsoft-edge", result.Id);
    }

    private sealed class FakeProcessLauncher(int processId) : IProcessLauncher
    {
        public string? LastExecutablePath { get; private set; }

        public Task<int> LaunchAsync(string executablePath, string? arguments, CancellationToken cancellationToken)
        {
            LastExecutablePath = executablePath;
            return Task.FromResult(processId);
        }
    }
}
