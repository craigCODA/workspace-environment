using System.Text.Json;

namespace Workspace.Host.Applications;

public sealed class AtomicApplicationProfileStore : IApplicationProfileStore
{
    private const int SchemaVersion = 1;
    private const int MaximumArgumentCount = 64;
    private const int MaximumArgumentLength = 4_096;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;

    public AtomicApplicationProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<ApplicationLaunchProfile>> ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadDocumentAsync(cancellationToken);
        return document.Profiles
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<ApplicationLaunchProfile?> FindAsync(string profileId, CancellationToken cancellationToken)
    {
        ValidateRequiredId(profileId, nameof(profileId));
        var document = await LoadDocumentAsync(cancellationToken);
        return document.Profiles.SingleOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.Ordinal));
    }

    public async Task SaveAsync(ApplicationLaunchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);

        var document = await LoadDocumentAsync(cancellationToken);
        var profiles = document.Profiles
            .Where(existing => !string.Equals(existing.Id, profile.Id, StringComparison.Ordinal))
            .Append(profile)
            .OrderBy(existing => existing.Id, StringComparer.Ordinal)
            .ToList();
        await SaveDocumentAsync(new ApplicationProfileDocument(SchemaVersion, profiles), cancellationToken);
    }

    public async Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken)
    {
        ValidateRequiredId(profileId, nameof(profileId));
        var document = await LoadDocumentAsync(cancellationToken);
        var profiles = document.Profiles
            .Where(profile => !string.Equals(profile.Id, profileId, StringComparison.Ordinal))
            .ToList();
        if (profiles.Count == document.Profiles.Count)
        {
            return false;
        }

        await SaveDocumentAsync(new ApplicationProfileDocument(SchemaVersion, profiles), cancellationToken);
        return true;
    }

    private async Task<ApplicationProfileDocument> LoadDocumentAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return new ApplicationProfileDocument(SchemaVersion, []);
        }

        await using var stream = new FileStream(
            _path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<ApplicationProfileDocument>(
            stream,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException($"Application profile document '{_path}' was empty or invalid.");
        if (document.Version != SchemaVersion || document.Profiles is null)
        {
            throw new InvalidDataException($"Application profile document '{_path}' has an unsupported schema.");
        }

        foreach (var profile in document.Profiles)
        {
            ValidateProfile(profile);
        }

        return document;
    }

    private async Task SaveDocumentAsync(
        ApplicationProfileDocument document,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                File.Replace(temporaryPath, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _path);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateProfile(ApplicationLaunchProfile profile)
    {
        ValidateRequiredId(profile.Id, nameof(profile.Id));
        ValidateRequiredText(profile.DisplayName, nameof(profile.DisplayName));
        ValidateRequiredId(profile.ApplicationId, nameof(profile.ApplicationId));
        ArgumentNullException.ThrowIfNull(profile.Arguments);
        if (profile.Arguments.Count > MaximumArgumentCount)
        {
            throw new ArgumentException($"Profiles may contain at most {MaximumArgumentCount} arguments.", nameof(profile));
        }

        foreach (var argument in profile.Arguments)
        {
            if (argument is null || argument.Length > MaximumArgumentLength || argument.Contains('\0'))
            {
                throw new ArgumentException(
                    $"Profile arguments must not contain NUL characters and must be at most {MaximumArgumentLength} characters.",
                    nameof(profile));
            }
        }

        if (profile.WorkingDirectory is not null)
        {
            ValidateNoNul(profile.WorkingDirectory, nameof(profile.WorkingDirectory));
            if (!Path.IsPathFullyQualified(profile.WorkingDirectory))
            {
                throw new ArgumentException("Profile working directories must be absolute.", nameof(profile));
            }
        }

        if (!Enum.IsDefined(profile.LaunchPolicy))
        {
            throw new ArgumentException("Profile launch policy is invalid.", nameof(profile));
        }

        if (profile.PreferredSurfaceId is not null)
        {
            ValidateRequiredId(profile.PreferredSurfaceId, nameof(profile.PreferredSurfaceId));
        }
    }

    private static void ValidateRequiredId(string value, string parameterName)
    {
        ValidateRequiredText(value, parameterName);
    }

    private static void ValidateRequiredText(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        ValidateNoNul(value, parameterName);
    }

    private static void ValidateNoNul(string value, string parameterName)
    {
        if (value.Contains('\0'))
        {
            throw new ArgumentException("Profile text must not contain NUL characters.", parameterName);
        }
    }

    private sealed record ApplicationProfileDocument(
        int Version,
        List<ApplicationLaunchProfile> Profiles);
}
