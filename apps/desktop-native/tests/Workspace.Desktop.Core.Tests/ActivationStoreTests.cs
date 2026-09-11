using System.Security.Cryptography;
using System.Text.Json;
using Workspace.Desktop.Core.Launcher;

namespace Workspace.Desktop.Core.Tests;

public sealed class ActivationStoreTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), $"workspace-activation-{Guid.NewGuid():N}");

    [Fact]
    public async Task Activation_state_survives_a_fresh_store_instance()
    {
        var path = Path.Combine(_temp, "activation.json");
        var first = new ActivationStore(path);
        await first.SaveAsync(new ActivationState(1, "v1", "v2", "v1"));

        var loaded = await new ActivationStore(path).LoadAsync();

        Assert.Equal("v2", loaded.PendingVersion);
        Assert.Equal("v1", loaded.KnownGoodVersion);
    }

    [Fact]
    public async Task Manifest_validation_rejects_an_executable_hash_mismatch()
    {
        Directory.CreateDirectory(_temp);
        var executable = Path.Combine(_temp, "Workspace.Desktop.exe");
        await File.WriteAllTextAsync(executable, "not the declared bytes");
        var manifest = new VersionManifest(1, "v1", "Workspace.Desktop.exe",
            new Dictionary<string, string> { ["Workspace.Desktop.exe"] = new string('0', 64) });
        await File.WriteAllTextAsync(
            Path.Combine(_temp, "version-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => VersionManifest.LoadAndValidateAsync(_temp));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }
}
