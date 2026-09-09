using System.Text.RegularExpressions;

namespace Workspace.Desktop.Core.Runtime;

public enum CodaLocalCommandKind
{
    AgentRequest,
    Stop,
    Pause,
    Resume,
    Repeat,
    ShowTerminal,
    HideTerminal,
    ListPermissions,
    ForgetPermissions,
    ResetOnboarding,
    ChangeName,
    MicrophoneOn,
    MicrophoneOff,
    CaptionsOn,
    CaptionsOff,
    TranscriptOn,
    TranscriptOff,
    ProactiveCritical,
    ProactiveCompletion,
    ProactiveQuiet,
    NavigationGuide,
    NavigationAsk,
    NavigationVoiceOnly,
    ReturnHome,
    StopCamera,
    FocusEntity,
}

public sealed record CodaLocalCommand(CodaLocalCommandKind Kind, string? Argument = null);

public static partial class CodaLocalCommandParser
{
    public static CodaLocalCommand Parse(string text)
    {
        var spoken = Regex.Replace(text.Trim(), @"\s+", " ");
        var normalized = spoken.TrimEnd('.', '!', '?').ToLowerInvariant();
        if (normalized is "stop moving" or "stop navigation" or "stop camera")
            return new(CodaLocalCommandKind.StopCamera);
        if (normalized is "stop" or "stop that" or "cancel that")
            return new(CodaLocalCommandKind.Stop);
        if (normalized is "pause" or "pause listening" or "go quiet")
            return new(CodaLocalCommandKind.Pause);
        if (normalized is "resume" or "resume listening" or "start listening")
            return new(CodaLocalCommandKind.Resume);
        if (normalized is "repeat" or "say that again" or "repeat that")
            return new(CodaLocalCommandKind.Repeat);
        if (normalized.Contains("show terminal", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.ShowTerminal);
        if (normalized.Contains("hide terminal", StringComparison.Ordinal)
            || normalized.Contains("hide the terminal", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.HideTerminal);
        if (normalized is "list permissions" or "what permissions do you remember")
            return new(CodaLocalCommandKind.ListPermissions);
        if (normalized.StartsWith("forget permission", StringComparison.Ordinal)
            || normalized is "forget permissions" or "forget all permissions")
            return new(CodaLocalCommandKind.ForgetPermissions);
        if (normalized is "reset onboarding" or "start onboarding over")
            return new(CodaLocalCommandKind.ResetOnboarding);

        var name = NamePattern().Match(spoken);
        if (name.Success)
            return new(CodaLocalCommandKind.ChangeName, name.Groups[1].Value.Trim());

        if (HasToggle(normalized, "microphone", enabled: false))
            return new(CodaLocalCommandKind.MicrophoneOff);
        if (HasToggle(normalized, "microphone", enabled: true))
            return new(CodaLocalCommandKind.MicrophoneOn);
        if (HasToggle(normalized, "captions", enabled: false))
            return new(CodaLocalCommandKind.CaptionsOff);
        if (HasToggle(normalized, "captions", enabled: true))
            return new(CodaLocalCommandKind.CaptionsOn);
        if (HasToggle(normalized, "transcript", enabled: false))
            return new(CodaLocalCommandKind.TranscriptOff);
        if (HasToggle(normalized, "transcript", enabled: true))
            return new(CodaLocalCommandKind.TranscriptOn);

        if (normalized.Contains("critical alerts only", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.ProactiveCritical);
        if (normalized.Contains("include completion", StringComparison.Ordinal)
            || normalized.Contains("announce completion", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.ProactiveCompletion);
        if (normalized.Contains("quiet alerts", StringComparison.Ordinal)
            || normalized.Contains("no proactive", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.ProactiveQuiet);

        if (normalized.Contains("guide me freely", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.NavigationGuide);
        if (normalized.Contains("ask before moving", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.NavigationAsk);
        if (normalized.Contains("voice navigation only", StringComparison.Ordinal))
            return new(CodaLocalCommandKind.NavigationVoiceOnly);

        if (normalized is "go home" or "return home" or "take me home")
            return new(CodaLocalCommandKind.ReturnHome);
        var focus = FocusPattern().Match(spoken);
        if (focus.Success)
            return new(CodaLocalCommandKind.FocusEntity, focus.Groups[1].Value.Trim());

        return new(CodaLocalCommandKind.AgentRequest, spoken);
    }

    private static bool HasToggle(string normalized, string feature, bool enabled)
    {
        var direction = enabled ? "on" : "off";
        return normalized.Contains($"{feature} {direction}", StringComparison.Ordinal)
            || normalized.Contains($"turn {direction} {feature}", StringComparison.Ordinal)
            || normalized.Contains($"turn {feature} {direction}", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"^(?:call me|change my name to|my name is)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^(?:take me to|focus on|go to)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FocusPattern();
}
