using Workspace.Desktop.Core.Preferences;

namespace Workspace.Desktop.Core.Runtime;

public enum ProactiveEventKind
{
    Critical,
    Failure,
    Risk,
    Blocked,
    Completion,
    Information,
}

public static class ProactiveSpeechPolicy
{
    public static bool ShouldSpeak(
        ProactiveSpeechMode mode,
        ProactiveEventKind eventKind,
        IEnumerable<ProactiveEventKind>? customEvents = null) => mode switch
    {
        ProactiveSpeechMode.CriticalOnly => eventKind is
            ProactiveEventKind.Critical
            or ProactiveEventKind.Failure
            or ProactiveEventKind.Risk
            or ProactiveEventKind.Blocked,
        ProactiveSpeechMode.IncludeCompletion => eventKind is
            ProactiveEventKind.Critical
            or ProactiveEventKind.Failure
            or ProactiveEventKind.Risk
            or ProactiveEventKind.Blocked
            or ProactiveEventKind.Completion,
        ProactiveSpeechMode.Quiet => false,
        ProactiveSpeechMode.Custom => customEvents?.Contains(eventKind) == true,
        _ => false,
    };
}
