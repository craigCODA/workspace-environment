using Workspace.Host.Domain;
using Workspace.Host.Persistence;

namespace Workspace.Host.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"workspace-host-tests-{Guid.NewGuid():N}");

    public PersistenceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsWorkspaceDocument()
    {
        var store = new AtomicWorkspaceStore(Path.Combine(_tempDir, "workspace.json"));
        var expected = WorkspaceDocumentFixtures.SingleApplication();

        await store.SaveAsync(expected, CancellationToken.None);

        Assert.Equal(expected, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingStoresReturnIndependentEmptyDocuments()
    {
        var first = new AtomicWorkspaceStore(Path.Combine(_tempDir, "first.json"));
        var second = new AtomicWorkspaceStore(Path.Combine(_tempDir, "second.json"));

        var firstDocument = await first.LoadAsync(CancellationToken.None);
        firstDocument.Entities.Add(
            WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad"));

        var secondDocument = await second.LoadAsync(CancellationToken.None);

        Assert.Empty(secondDocument.Entities);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}

internal static class WorkspaceDocumentFixtures
{
    public static WorkspaceDocument SingleApplication()
    {
        return new WorkspaceDocument(
            1,
            [WorkspaceEntity.CreateApplication("app:microsoft-edge", "Microsoft Edge")]);
    }
}
