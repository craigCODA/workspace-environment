using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workspace.Desktop.Core.Persistence;

public sealed class AtomicJsonStore<T>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public AtomicJsonStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<T> LoadOrDefaultAsync(
        Func<T> defaultFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return defaultFactory();

            try
            {
                await using var input = File.OpenRead(_path);
                return await JsonSerializer.DeserializeAsync<T>(
                    input,
                    SerializerOptions,
                    cancellationToken) ?? throw new JsonException("The JSON document was empty.");
            }
            catch (Exception error) when (error is JsonException or NotSupportedException)
            {
                QuarantineInvalidFile();
                return defaultFactory();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("The store path must have a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    output,
                    value,
                    SerializerOptions,
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            _gate.Release();
        }
    }

    private void QuarantineInvalidFile()
    {
        var quarantinePath = $"{_path}.corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        File.Move(_path, quarantinePath);
    }
}
