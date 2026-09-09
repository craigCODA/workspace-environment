namespace Workspace.Desktop.Core.Voice;

public abstract record VoiceEvent(DateTimeOffset Timestamp);

public sealed record VoiceStateChanged(VoiceState State, DateTimeOffset Timestamp)
    : VoiceEvent(Timestamp);

public sealed record VoiceCaption(
    string Text,
    bool IsFinal,
    string UtteranceId,
    DateTimeOffset Timestamp) : VoiceEvent(Timestamp);

public sealed record VoiceTranscript(
    string Text,
    bool IsFinal,
    float Confidence,
    DateTimeOffset Timestamp) : VoiceEvent(Timestamp);

public sealed record VoiceCommandRecognized(string Text, DateTimeOffset Timestamp)
    : VoiceEvent(Timestamp);

public sealed record PreferredNameCaptured(string Name, DateTimeOffset Timestamp)
    : VoiceEvent(Timestamp);

public sealed record VoiceFailure(
    string Code,
    string Message,
    bool IsRecoverable,
    DateTimeOffset Timestamp) : VoiceEvent(Timestamp);
