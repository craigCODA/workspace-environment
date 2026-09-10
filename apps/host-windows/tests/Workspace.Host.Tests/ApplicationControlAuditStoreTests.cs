using Workspace.Host.Applications;

namespace Workspace.Host.Tests;

public sealed class ApplicationControlAuditStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"workspace-audit-{Guid.NewGuid():N}");

    [Fact]
    public async Task Record_persists_a_bounded_redacted_open_audit_entry()
    {
        var path = Path.Combine(_directory, "application-control-audit.json");
        var store = new ApplicationControlAuditStore(path);

        await store.RecordAsync(new ApplicationControlAuditRecord(
            "op-99", "application.open", "app:notepad", "pc.window:notepad",
            "spatial.surface:right", "user-approved", ApplicationLifecycleState.Open,
            "capture_failed", "captured-image-data", "raw microphone words"), CancellationToken.None);

        var records = await store.ListAsync(CancellationToken.None);
        var record = Assert.Single(records);
        Assert.Equal("op-99", record.OperationId);
        Assert.Equal("app:notepad", record.ApplicationEntityId);
        Assert.Equal("pc.window:notepad", record.WindowEntityId);
        Assert.Equal("spatial.surface:right", record.SurfaceEntityId);
        Assert.Equal("user-approved", record.ApprovalSource);
        Assert.Equal(ApplicationLifecycleState.Open, record.LifecycleState);
        Assert.Equal("capture_failed", record.ErrorCategory);
        var persisted = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("captured-image-data", persisted);
        Assert.DoesNotContain("raw microphone words", persisted);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
