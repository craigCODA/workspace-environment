using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: InternalsVisibleTo("Workspace.Host.Tests")]

namespace Workspace.Host.Persistence;

public sealed class AtomicWorkspaceStore : IWorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Func<string, CancellationToken, Task>? _beforeTemporaryFileWriteAsync;

    public AtomicWorkspaceStore(string path)
        : this(path, null)
    {
    }

    internal AtomicWorkspaceStore(
        string path,
        Func<string, CancellationToken, Task>? beforeTemporaryFileWriteAsync)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _beforeTemporaryFileWriteAsync = beforeTemporaryFileWriteAsync;
    }

    public async Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return WorkspaceDocument.Empty;
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var document = await JsonSerializer.DeserializeAsync<WorkspaceDocument>(
            stream,
            JsonOptions,
            cancellationToken);

        var loadedDocument = document
            ?? throw new InvalidDataException($"Workspace document '{_path}' was empty or invalid.");
        WorkspaceMigrator.ValidateDocument(loadedDocument);
        return loadedDocument;
    }

    public async Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                if (_beforeTemporaryFileWriteAsync is not null)
                {
                    await _beforeTemporaryFileWriteAsync(tempPath, cancellationToken);
                }
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                File.Replace(tempPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
