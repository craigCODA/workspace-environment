using Workspace.Desktop.Core.Voice;

namespace Workspace.Desktop.Core.Tests;

public sealed class SpeechConfidencePolicyTests
{
    [Theory]
    [InlineData(0.69f, false)]
    [InlineData(0.70f, true)]
    [InlineData(0.95f, true)]
    public void Wake_phrase_rejects_low_confidence_audio(float confidence, bool expected) =>
        Assert.Equal(expected, SpeechConfidencePolicy.AcceptWakePhrase(confidence));

    [Theory]
    [InlineData(0.61f, false)]
    [InlineData(0.62f, true)]
    [InlineData(0.90f, true)]
    public void Dictation_rejects_low_confidence_background_fragments(float confidence, bool expected) =>
        Assert.Equal(expected, SpeechConfidencePolicy.AcceptDictation(confidence));
}
