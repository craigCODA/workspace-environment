using Workspace.Desktop.Core.Capabilities;

namespace Workspace.Desktop.Core.Tests;

public sealed class CapabilityBrokerTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"workspace-capabilities-{Guid.NewGuid():N}");

    public CapabilityBrokerTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task RememberedGrantMatchesOnlyItsWorkspaceScope()
    {
        var broker = await CapabilityBroker.OpenAsync(Path.Combine(_tempDirectory, "grants.json"));
        var firstWorkspace = Path.Combine(_tempDirectory, "one");
        var secondWorkspace = Path.Combine(_tempDirectory, "two");
        Directory.CreateDirectory(firstWorkspace);
        Directory.CreateDirectory(secondWorkspace);

        await broker.RememberAsync(new CapabilityGrant("build.run", firstWorkspace, null));

        Assert.True(broker.IsGranted("build.run", firstWorkspace));
        Assert.False(broker.IsGranted("build.run", secondWorkspace));
    }

    [Theory]
    [InlineData("credential.read")]
    [InlineData("remote.publish")]
    [InlineData("system.configure")]
    [InlineData("filesystem.destructive-outside-workspace")]
    public async Task FreshConfirmationCapabilitiesCannotBeRemembered(string capability)
    {
        var broker = await CapabilityBroker.OpenAsync(Path.Combine(_tempDirectory, "grants.json"));
        var workspace = Path.Combine(_tempDirectory, "project");
        Directory.CreateDirectory(workspace);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            broker.RememberAsync(new CapabilityGrant(capability, workspace, null)));
    }

    [Fact]
    public async Task FilesystemRootCannotBecomeARememberedScope()
    {
        var broker = await CapabilityBroker.OpenAsync(Path.Combine(_tempDirectory, "grants.json"));
        var root = Path.GetPathRoot(_tempDirectory)!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            broker.RememberAsync(new CapabilityGrant("workspace.edit", root, null)));
    }

    [Fact]
    public async Task RevocationPersistsAcrossBrokerRestart()
    {
        var path = Path.Combine(_tempDirectory, "grants.json");
        var workspace = Path.Combine(_tempDirectory, "project");
        Directory.CreateDirectory(workspace);
        var broker = await CapabilityBroker.OpenAsync(path);
        await broker.RememberAsync(new CapabilityGrant("test.run", workspace, null));

        Assert.True(await broker.RevokeAsync("test.run", workspace));
        var restarted = await CapabilityBroker.OpenAsync(path);

        Assert.False(restarted.IsGranted("test.run", workspace));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
