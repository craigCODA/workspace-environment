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

public sealed record VoiceProfile(
    int SchemaVersion,
    string? PreferredName,
    bool OnboardingCompleted,
    bool MicrophoneEnabled,
    bool CaptionsEnabled,
    bool TranscriptRetentionEnabled,
    ProactiveSpeechMode ProactiveMode,
    AgentNavigationMode NavigationMode,
    string WakePhrase)
{
    public const int CurrentSchemaVersion = 1;

    public static VoiceProfile Default { get; } = new(
        CurrentSchemaVersion,
        PreferredName: null,
        OnboardingCompleted: false,
        MicrophoneEnabled: true,
        CaptionsEnabled: true,
        TranscriptRetentionEnabled: false,
        ProactiveMode: ProactiveSpeechMode.CriticalOnly,
        NavigationMode: AgentNavigationMode.AskFirst,
        WakePhrase: "Hey Coda");
}
