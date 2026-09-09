using System.Security.Cryptography;
using System.Text.Json;

namespace Workspace.Desktop.Core.Launcher;

public sealed record VersionManifest(
    int SchemaVersion,
    string VersionId,
    string EntryPoint,
    IReadOnlyDictionary<string, string> Files)
{
    public const int CurrentSchemaVersion = 1;

    public static async Task<VersionManifest> LoadAndValidateAsync(
        string versionDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(versionDirectory);
        var manifestPath = Path.Combine(root, "version-manifest.json");
        await using var input = File.OpenRead(manifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<VersionManifest>(
            input,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken) ?? throw new InvalidDataException("The version manifest is empty.");
        if (manifest.SchemaVersion != CurrentSchemaVersion
            || string.IsNullOrWhiteSpace(manifest.VersionId)
            || !manifest.VersionId.Equals(
                Path.GetFileName(root),
                StringComparison.OrdinalIgnoreCase)
            || !IsSafeRelativePath(manifest.EntryPoint)
            || manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The version manifest is invalid.");
        }

        foreach (var file in manifest.Files)
        {
            if (!IsSafeRelativePath(file.Key) || !IsSha256(file.Value))
                throw new InvalidDataException($"Invalid manifest file declaration: {file.Key}");
            var path = ResolveContained(root, file.Key);
            if (!File.Exists(path)) throw new FileNotFoundException("A staged file is missing.", path);
            await using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!actual.Equals(file.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Hash mismatch for {file.Key}.");
        }

        var entryPoint = ResolveContained(root, manifest.EntryPoint);
        if (!manifest.Files.Keys.Contains(manifest.EntryPoint, StringComparer.OrdinalIgnoreCase)
            || !File.Exists(entryPoint))
        {
            throw new InvalidDataException("The entry point is not a declared staged file.");
        }
        return manifest;
    }

    public static string ResolveContained(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!resolved.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A manifest path escapes its version directory.");
        return resolved;
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && path.Split(['/', '\\']).All(segment => segment is not "" and not "." and not "..");

    private static bool IsSha256(string hash) =>
        hash.Length == 64 && hash.All(Uri.IsHexDigit);
}
