using Workspace.Host.Applications;
using System.Diagnostics;

namespace Workspace.Host.Tests;

public sealed class ApplicationLifecycleTests
{
    [Fact]
    public void Resolve_prefers_an_exact_alias_over_a_prefix()
    {
        var apps = new[]
        {
            new ApplicationDescriptor("app:edge", "Microsoft Edge", ApplicationLaunchKind.Executable,
                @"C:\Edge\msedge.exe", ["Edge"]),
            new ApplicationDescriptor("app:edge-beta", "Microsoft Edge Beta", ApplicationLaunchKind.Executable,
                @"C:\EdgeBeta\msedge.exe", ["Edge Beta"]),
        };

        var result = ApplicationResolver.Resolve("Edge", apps);

        Assert.Equal(ApplicationResolutionStatus.Resolved, result.Status);
        Assert.Equal("app:edge", result.Application!.Id);
    }

    [Fact]
    public void Resolve_returns_candidates_for_an_ambiguous_prefix()
    {
        var apps = new[]
        {
            new ApplicationDescriptor("app:vs", "Visual Studio", ApplicationLaunchKind.Executable, @"C:\VS\devenv.exe", []),
            new ApplicationDescriptor("app:vscode", "Visual Studio Code", ApplicationLaunchKind.Executable, @"C:\Code\Code.exe", []),
        };

        var result = ApplicationResolver.Resolve("Visual", apps);

        Assert.Equal(ApplicationResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(["app:vs", "app:vscode"], result.Candidates.Select(x => x.Id));
    }

    [Fact]
    public void Resolve_normalizes_compatibility_characters_and_whitespace()
    {
        var apps = new[]
        {
            new ApplicationDescriptor("app:vscode", "Visual Studio", ApplicationLaunchKind.Executable, @"C:\Code\Code.exe", ["Code"]),
        };

        var result = ApplicationResolver.Resolve("  Ｖｉｓｕａｌ\u00a0  Studio  ", apps);

        Assert.Equal(ApplicationResolutionStatus.Resolved, result.Status);
        Assert.Equal("app:vscode", result.Application!.Id);
    }

    [Fact]
    public void Resolve_uses_a_unique_token_match_when_no_exact_or_prefix_matches()
    {
        var apps = new[]
        {
            new ApplicationDescriptor("app:vscode", "Visual Studio Code", ApplicationLaunchKind.Executable, @"C:\Code\Code.exe", []),
            new ApplicationDescriptor("app:terminal", "Windows Terminal", ApplicationLaunchKind.Executable, @"C:\Terminal\WindowsTerminal.exe", []),
        };

        var result = ApplicationResolver.Resolve("Code", apps);

        Assert.Equal(ApplicationResolutionStatus.Resolved, result.Status);
        Assert.Equal("app:vscode", result.Application!.Id);
    }

    [Fact]
    public void Resolve_returns_not_found_when_no_identifier_name_alias_prefix_or_token_matches()
    {
        var apps = new[]
        {
            new ApplicationDescriptor("app:notepad", "Notepad", ApplicationLaunchKind.Executable, @"C:\Windows\notepad.exe", []),
        };

        var result = ApplicationResolver.Resolve("Paint", apps);

        Assert.Equal(ApplicationResolutionStatus.NotFound, result.Status);
        Assert.Null(result.Application);
        Assert.Empty(result.Candidates);
    }

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
    public async Task Launcher_keeps_arguments_and_working_directory_structured()
    {
        var process = new RecordingProcessLauncher(8100);

        await new ApplicationLauncher(process).LaunchAsync(
            new ApplicationDescriptor("app:terminal", "Terminal", ApplicationLaunchKind.Executable, @"C:\wt.exe", []),
            ["new-tab", "codex"],
            @"D:\PythOS-Workspace",
            CancellationToken.None);

        Assert.Equal(["new-tab", "codex"], process.Request!.Arguments);
        Assert.Equal(@"D:\PythOS-Workspace", process.Request.WorkingDirectory);
    }

    [Fact]
    public async Task Launcher_parses_legacy_quoted_arguments_into_structured_tokens()
    {
        var process = new RecordingProcessLauncher(8100);
        var application = new ApplicationDescriptor(
            "app:terminal",
            "Terminal",
            @"C:\wt.exe",
            "new-tab --title \"Codex Workspace\"");

        await new ApplicationLauncher(process).LaunchAsync(application, CancellationToken.None);

        Assert.Equal(["new-tab", "--title", "Codex Workspace"], process.Request!.Arguments);
    }

    [Fact]
    public async Task Launcher_validates_a_legacy_descriptor_before_reading_its_arguments()
    {
        var launcher = new ApplicationLauncher(new RecordingProcessLauncher(8100));

        await Assert.ThrowsAsync<ArgumentNullException>(() => launcher.LaunchAsync(null!, CancellationToken.None));
    }

    [Theory]
    [InlineData(ApplicationLaunchKind.Executable, false)]
    [InlineData(ApplicationLaunchKind.Shortcut, true)]
    [InlineData(ApplicationLaunchKind.Packaged, true)]
    public async Task System_launcher_uses_the_correct_start_strategy_for_each_application_kind(
        ApplicationLaunchKind launchKind,
        bool expectedUseShellExecute)
    {
        ProcessStartInfo? captured = null;
        var launcher = new SystemProcessLauncher(startInfo =>
        {
            captured = startInfo;
            return null;
        });
        var locator = launchKind == ApplicationLaunchKind.Packaged
            ? WindowsApplicationCatalog.CreateAppsFolderLocator("Contoso.Sample_123!App")
            : @"C:\Tools\sample.exe";

        var processId = await launcher.LaunchAsync(
            new ApplicationStartRequest(launchKind, locator, ["--profile", "Codex Workspace"], @"D:\PythOS-Workspace"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(expectedUseShellExecute, captured.UseShellExecute);
        Assert.Equal(locator, captured.FileName);
        Assert.Equal(["--profile", "Codex Workspace"], captured.ArgumentList);
        Assert.Equal(@"D:\PythOS-Workspace", captured.WorkingDirectory);
        Assert.Null(processId);
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

        public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
        {
            LastExecutablePath = request.Locator;
            return Task.FromResult<int?>(processId);
        }
    }

    private sealed class RecordingProcessLauncher(int processId) : IProcessLauncher
    {
        public ApplicationStartRequest? Request { get; private set; }

        public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult<int?>(processId);
        }
    }
}
