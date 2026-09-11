using Workspace.Desktop.Core.Preferences;

namespace Workspace.Desktop.Core.Tests;

public sealed class VoiceProfileStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        $"workspace-voice-profile-{Guid.NewGuid():N}");

    public VoiceProfileStoreTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public async Task MissingProfileReturnsSafeDefaults()
    {
        var store = new VoiceProfileStore(Path.Combine(_tempDirectory, "voice-profile.json"));

        var loaded = await store.LoadAsync();

        Assert.Equal(VoiceProfile.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Null(loaded.PreferredName);
        Assert.False(loaded.OnboardingCompleted);
        Assert.True(loaded.MicrophoneEnabled);
        Assert.True(loaded.CaptionsEnabled);
        Assert.False(loaded.TranscriptRetentionEnabled);
        Assert.Equal("Hey Coda", loaded.WakePhrase);
    }

    [Fact]
    public async Task ReturningProfilePreservesNameAndVoiceToggles()
    {
        var path = Path.Combine(_tempDirectory, "voice-profile.json");
        var store = new VoiceProfileStore(path);
        await store.SaveAsync(VoiceProfile.Default with
        {
            PreferredName = "Craig",
            OnboardingCompleted = true,
            CaptionsEnabled = true,
            ProactiveMode = ProactiveSpeechMode.IncludeCompletion,
        });

        var loaded = await new VoiceProfileStore(path).LoadAsync();

        Assert.Equal("Craig", loaded.PreferredName);
        Assert.True(loaded.OnboardingCompleted);
        Assert.True(loaded.CaptionsEnabled);
        Assert.Equal(ProactiveSpeechMode.IncludeCompletion, loaded.ProactiveMode);
    }

    [Fact]
    public async Task InvalidProfileIsQuarantinedAndDefaultsAreReturned()
    {
        var path = Path.Combine(_tempDirectory, "voice-profile.json");
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new VoiceProfileStore(path);

        var loaded = await store.LoadAsync();

        Assert.Equal(VoiceProfile.Default, loaded);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_tempDirectory, "voice-profile.json.corrupt-*"));
    }

    [Fact]
    public async Task SchemaV1ProfilesMigrateToCodexProviderDefault()
    {
        var path = Path.Combine(_tempDirectory, "voice-profile.json");
        await File.WriteAllTextAsync(path, """
            {
              "SchemaVersion": 1,
              "PreferredName": "Craig",
              "OnboardingCompleted": true,
              "MicrophoneEnabled": true,
              "CaptionsEnabled": false,
              "TranscriptRetentionEnabled": true,
              "ProactiveMode": "Quiet",
              "NavigationMode": "AskFirst",
              "WakePhrase": "Hey Coda"
            }
            """);

        var loaded = await new VoiceProfileStore(path).LoadAsync();

        Assert.Equal(VoiceProfile.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal("Craig", loaded.PreferredName);
        Assert.True(loaded.OnboardingCompleted);
        Assert.False(loaded.CaptionsEnabled);
        Assert.True(loaded.TranscriptRetentionEnabled);
        Assert.Equal(ProactiveSpeechMode.Quiet, loaded.ProactiveMode);
        Assert.Equal(AgentProvider.Codex, loaded.AgentProvider);
    }

    [Fact]
    public async Task ReturningProfilePreservesAgentProvider()
    {
        var path = Path.Combine(_tempDirectory, "voice-profile.json");
        var store = new VoiceProfileStore(path);
        await store.SaveAsync(VoiceProfile.Default with
        {
            PreferredName = "Craig",
            OnboardingCompleted = true,
            AgentProvider = AgentProvider.SpaceXAI,
        });

        var loaded = await new VoiceProfileStore(path).LoadAsync();

        Assert.Equal(AgentProvider.SpaceXAI, loaded.AgentProvider);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, recursive: true);
    }
}
