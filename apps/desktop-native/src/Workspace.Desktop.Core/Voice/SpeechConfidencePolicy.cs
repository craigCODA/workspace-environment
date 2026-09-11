namespace Workspace.Desktop.Core.Voice;

public static class SpeechConfidencePolicy
{
    public const float WakePhraseMinimum = 0.70f;
    public const float DictationMinimum = 0.62f;

    public static bool AcceptWakePhrase(float confidence) =>
        confidence >= WakePhraseMinimum;

    public static bool AcceptDictation(float confidence) =>
        confidence >= DictationMinimum;
}
