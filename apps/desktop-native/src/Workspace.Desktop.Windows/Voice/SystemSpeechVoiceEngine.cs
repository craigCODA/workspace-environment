using System.Globalization;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using Workspace.Desktop.Core.Voice;
using CoreSpeechProgressEventArgs = Workspace.Desktop.Core.Voice.SpeechProgressEventArgs;
using RecognitionEventArgs = System.Speech.Recognition.SpeechRecognizedEventArgs;
using SynthesisProgressEventArgs = System.Speech.Synthesis.SpeakProgressEventArgs;

namespace Workspace.Desktop.Windows.Voice;

public sealed class SystemSpeechVoiceEngine :
    IWakeWordEngine,
    ISpeechRecognizer,
    ISpeechSynthesizer
{
    private readonly SpeechRecognitionEngine _wakeRecognizer;
    private readonly SpeechRecognitionEngine _dictationRecognizer;
    private readonly SpeechSynthesizer _synthesizer;
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private TaskCompletionSource? _speechCompletion;
    private CancellationTokenRegistration _speechCancellationRegistration;
    private string? _currentUtteranceId;
    private string _currentSpeech = string.Empty;
    private bool _wakeRunning;
    private bool _dictationRunning;
    private bool _disposed;

    public SystemSpeechVoiceEngine()
    {
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            _wakeRecognizer = CreateRecognizer(culture);
            _dictationRecognizer = CreateRecognizer(culture);
            _synthesizer = new SpeechSynthesizer();
            _synthesizer.SetOutputToDefaultAudioDevice();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or PlatformNotSupportedException
                or ArgumentException)
        {
            throw new VoiceRuntimeUnavailableException(
                "windows-speech-unavailable",
                "Windows does not have a compatible local speech recognizer or audio device. Captions and typed recovery remain available.",
                exception);
        }

        _wakeRecognizer.SpeechRecognized += OnWakeRecognized;
        _dictationRecognizer.SpeechDetected += OnSpeechDetected;
        _dictationRecognizer.SpeechRecognized += OnDictationRecognized;
        _synthesizer.SpeakProgress += OnSpeakProgress;
        _synthesizer.SpeakCompleted += OnSpeakCompleted;
    }

    public event Func<object?, WakeWordDetectedEventArgs, Task>? Detected;

    public event Func<object?, Workspace.Desktop.Core.Voice.SpeechRecognizedEventArgs, Task>? Recognized;

    public event Func<object?, EventArgs, Task>? SpeechStarted;

    public event EventHandler<CoreSpeechProgressEventArgs>? Progress;

    async Task IWakeWordEngine.StartAsync(
        string wakePhrase,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        await StopDictationAsync(cancellationToken).ConfigureAwait(false);
        StopRecognition(_wakeRecognizer, ref _wakeRunning);
        _wakeRecognizer.UnloadAllGrammars();
        var grammar = new Grammar(new GrammarBuilder(wakePhrase)) { Name = "Coda wake phrase" };
        _wakeRecognizer.LoadGrammar(grammar);
        _wakeRecognizer.RecognizeAsync(RecognizeMode.Multiple);
        _wakeRunning = true;
    }

    Task IWakeWordEngine.StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopRecognition(_wakeRecognizer, ref _wakeRunning);
        return Task.CompletedTask;
    }

    async Task ISpeechRecognizer.StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        StopRecognition(_wakeRecognizer, ref _wakeRunning);
        await StopDictationAsync(cancellationToken).ConfigureAwait(false);
        _dictationRecognizer.UnloadAllGrammars();
        _dictationRecognizer.LoadGrammar(new DictationGrammar { Name = "Coda local dictation" });
        _dictationRecognizer.RecognizeAsync(RecognizeMode.Multiple);
        _dictationRunning = true;
    }

    Task ISpeechRecognizer.StopAsync(CancellationToken cancellationToken) =>
        StopDictationAsync(cancellationToken);

    public async Task SpeakAsync(
        string text,
        string utteranceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _currentSpeech = text;
            _currentUtteranceId = utteranceId;
            _speechCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _speechCancellationRegistration = cancellationToken.Register(() =>
            {
                _synthesizer.SpeakAsyncCancelAll();
                _speechCompletion?.TrySetCanceled(cancellationToken);
            });
            _synthesizer.SpeakAsync(text);
            await _speechCompletion.Task.ConfigureAwait(false);
        }
        finally
        {
            _speechCancellationRegistration.Dispose();
            _speechCompletion = null;
            _currentUtteranceId = null;
            _currentSpeech = string.Empty;
            _speechGate.Release();
        }
    }

    public Task CancelAsync()
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        _synthesizer.SpeakAsyncCancelAll();
        _speechCompletion?.TrySetResult();
        return Task.CompletedTask;
    }

    private static SpeechRecognitionEngine CreateRecognizer(CultureInfo preferredCulture)
    {
        var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(item => item.Culture.Equals(preferredCulture))
            ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault()
            ?? throw new InvalidOperationException("No installed Windows speech recognizer was found.");
        var engine = new SpeechRecognitionEngine(recognizer);
        engine.SetInputToDefaultAudioDevice();
        return engine;
    }

    private Task StopDictationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StopRecognition(_dictationRecognizer, ref _dictationRunning);
        return Task.CompletedTask;
    }

    private static void StopRecognition(SpeechRecognitionEngine engine, ref bool running)
    {
        if (!running)
        {
            return;
        }

        engine.RecognizeAsyncCancel();
        running = false;
    }

    private async void OnWakeRecognized(object? sender, RecognitionEventArgs args)
    {
        if (args.Result.Confidence < 0.65f || Detected is null)
        {
            return;
        }

        await InvokeAsync(Detected, new WakeWordDetectedEventArgs(args.Result.Text)).ConfigureAwait(false);
    }

    private async void OnSpeechDetected(object? sender, SpeechDetectedEventArgs args)
    {
        if (SpeechStarted is not null)
        {
            await InvokeAsync(SpeechStarted, EventArgs.Empty).ConfigureAwait(false);
        }
    }

    private async void OnDictationRecognized(object? sender, RecognitionEventArgs args)
    {
        if (Recognized is null)
        {
            return;
        }

        var result = new Workspace.Desktop.Core.Voice.SpeechRecognizedEventArgs(
            args.Result.Text,
            isFinal: true,
            args.Result.Confidence);
        await InvokeAsync(Recognized, result).ConfigureAwait(false);
    }

    private void OnSpeakProgress(object? sender, SynthesisProgressEventArgs args)
    {
        var utteranceId = _currentUtteranceId;
        if (utteranceId is null)
        {
            return;
        }

        var end = Math.Min(_currentSpeech.Length, args.CharacterPosition + args.CharacterCount);
        var caption = end > 0 ? _currentSpeech[..end] : args.Text;
        Progress?.Invoke(this, new CoreSpeechProgressEventArgs(
            caption,
            end >= _currentSpeech.Length,
            utteranceId));
    }

    private void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs args)
    {
        if (args.Error is not null)
        {
            _speechCompletion?.TrySetException(args.Error);
        }
        else if (args.Cancelled)
        {
            _speechCompletion?.TrySetResult();
        }
        else
        {
            _speechCompletion?.TrySetResult();
        }
    }

    private static async Task InvokeAsync<TEventArgs>(
        Func<object?, TEventArgs, Task> handlers,
        TEventArgs args)
        where TEventArgs : EventArgs
    {
        foreach (var handler in handlers.GetInvocationList().Cast<Func<object?, TEventArgs, Task>>())
        {
            await handler(null, args).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        StopRecognition(_wakeRecognizer, ref _wakeRunning);
        StopRecognition(_dictationRecognizer, ref _dictationRunning);
        _synthesizer.SpeakAsyncCancelAll();
        _wakeRecognizer.Dispose();
        _dictationRecognizer.Dispose();
        _synthesizer.Dispose();
        _speechGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
