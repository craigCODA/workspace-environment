using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace.Host.Applications;

public sealed record ApplicationControlAuditRecord(
    string OperationId,
    string Operation,
    string? ApplicationEntityId,
    string? WindowEntityId,
    string? SurfaceEntityId,
    string? ApprovalSource,
    ApplicationLifecycleState LifecycleState,
    string? ErrorCategory,
    [property: JsonIgnore] string? CapturedContent = null,
    [property: JsonIgnore] string? RawMicrophoneText = null);

public sealed class ApplicationControlAuditStore
{
    private const int MaximumRecords = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public ApplicationControlAuditStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<ApplicationControlAuditRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordAsync(ApplicationControlAuditRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        Validate(record);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var records = (await LoadCoreAsync(cancellationToken)).ToList();
            records.Add(record with { CapturedContent = null, RawMicrophoneText = null });
            if (records.Count > MaximumRecords)
            {
                records.RemoveRange(0, records.Count - MaximumRecords);
            }
            await SaveCoreAsync(records, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<ApplicationControlAuditRecord>> LoadCoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return [];
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<List<ApplicationControlAuditRecord>>(
            stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("Application-control audit document was empty or invalid.");
    }

    private async Task SaveCoreAsync(IReadOnlyList<ApplicationControlAuditRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, records, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(_path)) File.Replace(temporaryPath, _path, null, true);
            else File.Move(temporaryPath, _path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void Validate(ApplicationControlAuditRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(record.OperationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.Operation);
        if (!Enum.IsDefined(record.LifecycleState))
        {
            throw new ArgumentException("Lifecycle state is invalid.", nameof(record));
        }
    }
}
