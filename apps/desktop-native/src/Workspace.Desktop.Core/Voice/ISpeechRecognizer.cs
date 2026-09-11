namespace Workspace.Desktop.Core.Voice;

public sealed class SpeechRecognizedEventArgs(
    string text,
    bool isFinal,
    float confidence) : EventArgs
{
    public string Text { get; } = text;
    public bool IsFinal { get; } = isFinal;
    public float Confidence { get; } = confidence;
}

public interface ISpeechRecognizer : IAsyncDisposable
{
    event Func<object?, SpeechRecognizedEventArgs, Task>? Recognized;

    event Func<object?, EventArgs, Task>? SpeechStarted;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
