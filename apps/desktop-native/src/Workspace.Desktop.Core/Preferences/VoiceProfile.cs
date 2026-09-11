namespace Workspace.Desktop.Core.Preferences;

public enum ProactiveSpeechMode
{
    CriticalOnly,
    IncludeCompletion,
    Quiet,
    Custom,
}

public enum AgentNavigationMode
{
    GuideFreely,
    AskFirst,
    VoiceCommandsOnly,
}

public enum AgentProvider
{
    Codex,
    SpaceXAI,
    Cursor,
}

public sealed record VoiceProfile(
    int SchemaVersion,
    string? PreferredName,
    bool OnboardingCompleted,
    bool MicrophoneEnabled,
    bool CaptionsEnabled,
    bool TranscriptRetentionEnabled,
    ProactiveSpeechMode ProactiveMode,
    AgentNavigationMode NavigationMode,
    string WakePhrase,
    AgentProvider AgentProvider = AgentProvider.Codex)
{
    public const int CurrentSchemaVersion = 2;

    public static VoiceProfile Default { get; } = new(
        CurrentSchemaVersion,
        PreferredName: null,
        OnboardingCompleted: false,
        MicrophoneEnabled: true,
        CaptionsEnabled: true,
        TranscriptRetentionEnabled: false,
        ProactiveMode: ProactiveSpeechMode.CriticalOnly,
        NavigationMode: AgentNavigationMode.AskFirst,
        WakePhrase: "Hey Coda",
        AgentProvider: AgentProvider.Codex);

    public static VoiceProfile Migrate(VoiceProfile profile) => profile.SchemaVersion switch
    {
        CurrentSchemaVersion => profile,
        1 => profile with
        {
            SchemaVersion = CurrentSchemaVersion,
            AgentProvider = AgentProvider.Codex,
        },
        _ => Default,
    };
}
