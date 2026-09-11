namespace Workspace.Desktop.Core.Voice;

public sealed class SpeechProgressEventArgs(
    string text,
    bool isFinal,
    string utteranceId) : EventArgs
{
    public string Text { get; } = text;
    public bool IsFinal { get; } = isFinal;
    public string UtteranceId { get; } = utteranceId;
}

public interface ISpeechSynthesizer : IAsyncDisposable
{
    event EventHandler<SpeechProgressEventArgs>? Progress;

    Task SpeakAsync(
        string text,
        string utteranceId,
        CancellationToken cancellationToken = default);

    Task CancelAsync();
}
