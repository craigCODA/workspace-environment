namespace Workspace.Desktop.Core.Voice;

public sealed class WakeWordDetectedEventArgs(string phrase) : EventArgs
{
    public string Phrase { get; } = phrase;
}

public interface IWakeWordEngine : IAsyncDisposable
{
    event Func<object?, WakeWordDetectedEventArgs, Task>? Detected;

    Task StartAsync(string wakePhrase, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
