using Workspace.Desktop.Core.Preferences;

namespace Workspace.Desktop.Core.Voice;

public sealed class VoiceConversationController : IAsyncDisposable
{
    public static IReadOnlyList<string> FirstRunNarration { get; } =
    [
        "Welcome to your workspace environment.",
        "This is the place where we'll build the way you work.",
        "The applications, files, projects, and tools on your computer can exist here, but they do not have to look or behave like a traditional desktop.",
        "This space is intentionally unfinished.",
        "Look around.",
        "When you're ready, we'll start by bringing something from your computer into the workspace.",
    ];

    private readonly IWakeWordEngine _wakeWord;
    private readonly ISpeechRecognizer _recognizer;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _silenceTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _silenceCancellation;
    private CancellationTokenSource? _speechCancellation;
    private VoiceProfile _profile = VoiceProfile.Default;
    private bool _running;
    private bool _wakeRunning;
    private bool _recognizerRunning;
    private bool _conversationActive;
    private bool _awaitingPreferredName;
    private string? _pendingPreferredName;
    private string? _currentUtteranceId;
    private long _utteranceSequence;
    private bool _disposed;

    public VoiceConversationController(
        IWakeWordEngine wakeWord,
        ISpeechRecognizer recognizer,
        ISpeechSynthesizer synthesizer,
        TimeProvider? timeProvider = null,
        TimeSpan? silenceTimeout = null)
    {
        _wakeWord = wakeWord;
        _recognizer = recognizer;
        _synthesizer = synthesizer;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _silenceTimeout = silenceTimeout ?? TimeSpan.FromSeconds(8);
        _wakeWord.Detected += OnWakeWordDetectedAsync;
        _recognizer.Recognized += OnRecognizedAsync;
        _recognizer.SpeechStarted += OnSpeechStartedAsync;
        _synthesizer.Progress += OnSpeechProgress;
    }

    public event Action<VoiceEvent>? EventRaised;

    public VoiceState State { get; private set; } = VoiceState.Paused;

    public async Task StartAsync(VoiceProfile profile, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _profile = profile;
            _running = true;
            _conversationActive = false;
            _awaitingPreferredName = false;
            _pendingPreferredName = null;
        }
        finally
        {
            _gate.Release();
        }

        if (!profile.OnboardingCompleted)
        {
            foreach (var line in FirstRunNarration)
            {
                await SpeakAsync(line, cancellationToken).ConfigureAwait(false);
            }

            await SpeakAsync("What should I call you?", cancellationToken).ConfigureAwait(false);
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _awaitingPreferredName = true;
                _conversationActive = true;
                if (_profile.MicrophoneEnabled)
                {
                    await EnterListeningAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    SetState(VoiceState.MicrophoneOff);
                }
            }
            finally
            {
                _gate.Release();
            }
            return;
        }

        var preferredName = profile.PreferredName?.Trim();
        await SpeakAsync(
            string.IsNullOrWhiteSpace(preferredName) ? "Welcome back." : $"Welcome back, {preferredName}.",
            cancellationToken).ConfigureAwait(false);
        await EnterDormantGuardedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string utteranceId;
        CancellationToken speechToken;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_conversationActive && _profile.MicrophoneEnabled && !_recognizerRunning)
            {
                await StartRecognizerAsync(cancellationToken).ConfigureAwait(false);
            }

            _speechCancellation?.Cancel();
            _speechCancellation?.Dispose();
            _speechCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            speechToken = _speechCancellation.Token;
            utteranceId = $"coda-{Interlocked.Increment(ref _utteranceSequence)}";
            _currentUtteranceId = utteranceId;
            SetState(VoiceState.Speaking);
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            await _synthesizer.SpeakAsync(text.Trim(), utteranceId, speechToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (speechToken.IsCancellationRequested)
        {
            // Barge-in and explicit stop are normal conversation control.
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_currentUtteranceId != utteranceId)
            {
                return;
            }

            _currentUtteranceId = null;
            if (State == VoiceState.Speaking)
            {
                if (!_profile.MicrophoneEnabled)
                {
                    SetState(VoiceState.MicrophoneOff);
                }
                else if (_conversationActive || _awaitingPreferredName)
                {
                    SetState(VoiceState.Listening);
                    ScheduleSilenceTimeout();
                }
                else
                {
                    SetState(VoiceState.Dormant);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task SubmitTextAsync(
        string text,
        CancellationToken cancellationToken = default) =>
        ProcessInputAsync(
            new SpeechRecognizedEventArgs(text, isFinal: true, confidence: 1f),
            isTyped: true,
            cancellationToken);

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _running = false;
            CancelSilenceTimeout();
            _speechCancellation?.Cancel();
            await _synthesizer.CancelAsync().ConfigureAwait(false);
            await StopRecognizerAsync(cancellationToken).ConfigureAwait(false);
            await StopWakeWordAsync(cancellationToken).ConfigureAwait(false);
            SetState(VoiceState.Paused);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _running = true;
            if (_profile.MicrophoneEnabled)
            {
                await EnterDormantAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                SetState(VoiceState.MicrophoneOff);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task BeginConversationAsync(CancellationToken cancellationToken = default)
    {
        var promptForName = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _running = true;
            if (!_profile.MicrophoneEnabled)
            {
                SetState(VoiceState.MicrophoneOff);
                return;
            }

            _conversationActive = true;
            if (!_profile.OnboardingCompleted && _pendingPreferredName is null)
            {
                _awaitingPreferredName = true;
                promptForName = true;
            }
            await EnterListeningAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        if (promptForName)
        {
            await SpeakAsync("What should I call you?", cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SetMicrophoneEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _profile = _profile with { MicrophoneEnabled = enabled };
            if (!enabled)
            {
                CancelSilenceTimeout();
                await StopRecognizerAsync(cancellationToken).ConfigureAwait(false);
                await StopWakeWordAsync(cancellationToken).ConfigureAwait(false);
                SetState(VoiceState.MicrophoneOff);
            }
            else if (_running)
            {
                if (_awaitingPreferredName)
                {
                    _conversationActive = true;
                    await EnterListeningAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await EnterDormantAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void SetCaptionsEnabled(bool enabled)
    {
        _profile = _profile with { CaptionsEnabled = enabled };
    }

    public async Task StopConversationAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _conversationActive = false;
            _awaitingPreferredName = false;
            _pendingPreferredName = null;
            _speechCancellation?.Cancel();
            await _synthesizer.CancelAsync().ConfigureAwait(false);
            await EnterDormantAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task OnWakeWordDetectedAsync(object? sender, WakeWordDetectedEventArgs args)
    {
        var promptForName = false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_running || State != VoiceState.Dormant || !_profile.MicrophoneEnabled)
            {
                return;
            }

            SetState(VoiceState.WakeDetected);
            await StopWakeWordAsync().ConfigureAwait(false);
            _conversationActive = true;
            if (!_profile.OnboardingCompleted && _pendingPreferredName is null)
            {
                _awaitingPreferredName = true;
                promptForName = true;
            }
            await EnterListeningAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        if (promptForName)
        {
            await SpeakAsync("What should I call you?").ConfigureAwait(false);
        }
    }

    private Task OnRecognizedAsync(object? sender, SpeechRecognizedEventArgs args) =>
        ProcessInputAsync(args, isTyped: false, CancellationToken.None);

    private async Task ProcessInputAsync(
        SpeechRecognizedEventArgs args,
        bool isTyped,
        CancellationToken cancellationToken)
    {
        string? capturedName = null;
        string? nameToConfirm = null;
        bool retryName = false;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_running || (!isTyped
                && (!_recognizerRunning || State is not (VoiceState.Listening or VoiceState.Speaking))))
            {
                return;
            }

            var text = args.Text.Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (!isTyped)
            {
                Raise(new VoiceTranscript(text, args.IsFinal, args.Confidence, _timeProvider.GetUtcNow()));
                ScheduleSilenceTimeout();
            }
            if (!args.IsFinal)
            {
                return;
            }

            CancelSilenceTimeout();
            if (_awaitingPreferredName)
            {
                nameToConfirm = text.Length > 80 ? text[..80] : text;
                _awaitingPreferredName = false;
                _pendingPreferredName = nameToConfirm;
                _conversationActive = true;
                await StopRecognizerAsync().ConfigureAwait(false);
            }
            else if (_pendingPreferredName is not null)
            {
                var answer = text.Trim().TrimEnd('.', '!', '?').ToLowerInvariant();
                if (answer is "yes" or "yes that's right" or "that's right" or "correct")
                {
                    capturedName = _pendingPreferredName;
                    _pendingPreferredName = null;
                    _conversationActive = false;
                    _profile = _profile with
                    {
                        PreferredName = capturedName,
                        OnboardingCompleted = true,
                    };
                    Raise(new PreferredNameCaptured(capturedName, _timeProvider.GetUtcNow()));
                    await StopRecognizerAsync().ConfigureAwait(false);
                }
                else if (answer is "no" or "no that's not right" or "that's not right")
                {
                    _pendingPreferredName = null;
                    _awaitingPreferredName = true;
                    _conversationActive = true;
                    retryName = true;
                    await StopRecognizerAsync().ConfigureAwait(false);
                }
                else
                {
                    nameToConfirm = text.Length > 80 ? text[..80] : text;
                    _pendingPreferredName = nameToConfirm;
                    _conversationActive = true;
                    await StopRecognizerAsync().ConfigureAwait(false);
                }
            }
            else
            {
                Raise(new VoiceCommandRecognized(text, _timeProvider.GetUtcNow()));
                SetState(VoiceState.Thinking);
                await StopRecognizerAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (nameToConfirm is not null)
        {
            await SpeakAsync($"I heard {nameToConfirm}. Is that right?").ConfigureAwait(false);
        }
        else if (retryName)
        {
            await SpeakAsync("Okay. What should I call you?").ConfigureAwait(false);
        }
        else if (capturedName is not null)
        {
            await SpeakAsync($"It's good to meet you, {capturedName}. Say Hey Coda whenever you need me.")
                .ConfigureAwait(false);
            await EnterDormantGuardedAsync().ConfigureAwait(false);
        }
    }

    private async Task OnSpeechStartedAsync(object? sender, EventArgs args)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != VoiceState.Speaking)
            {
                return;
            }

            _speechCancellation?.Cancel();
            await _synthesizer.CancelAsync().ConfigureAwait(false);
            _currentUtteranceId = null;
            SetState(VoiceState.Listening);
            ScheduleSilenceTimeout();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnSpeechProgress(object? sender, SpeechProgressEventArgs args)
    {
        if (args.UtteranceId != _currentUtteranceId)
        {
            return;
        }

        Raise(new VoiceCaption(
            args.Text,
            args.IsFinal,
            args.UtteranceId,
            _timeProvider.GetUtcNow()));
    }

    private async Task EnterListeningAsync(CancellationToken cancellationToken = default)
    {
        await StopWakeWordAsync(cancellationToken).ConfigureAwait(false);
        await StartRecognizerAsync(cancellationToken).ConfigureAwait(false);
        SetState(VoiceState.Listening);
        ScheduleSilenceTimeout();
    }

    private async Task EnterDormantGuardedAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnterDormantAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnterDormantAsync(CancellationToken cancellationToken = default)
    {
        CancelSilenceTimeout();
        _conversationActive = false;
        await StopRecognizerAsync(cancellationToken).ConfigureAwait(false);
        if (!_profile.MicrophoneEnabled || !_running)
        {
            SetState(_profile.MicrophoneEnabled ? VoiceState.Paused : VoiceState.MicrophoneOff);
            return;
        }

        await StartWakeWordAsync(cancellationToken).ConfigureAwait(false);
        SetState(VoiceState.Dormant);
    }

    private async Task StartWakeWordAsync(CancellationToken cancellationToken = default)
    {
        if (_wakeRunning)
        {
            return;
        }

        await _wakeWord.StartAsync(_profile.WakePhrase, cancellationToken).ConfigureAwait(false);
        _wakeRunning = true;
    }

    private async Task StopWakeWordAsync(CancellationToken cancellationToken = default)
    {
        if (!_wakeRunning)
        {
            return;
        }

        await _wakeWord.StopAsync(cancellationToken).ConfigureAwait(false);
        _wakeRunning = false;
    }

    private async Task StartRecognizerAsync(CancellationToken cancellationToken = default)
    {
        if (_recognizerRunning)
        {
            return;
        }

        await _recognizer.StartAsync(cancellationToken).ConfigureAwait(false);
        _recognizerRunning = true;
    }

    private async Task StopRecognizerAsync(CancellationToken cancellationToken = default)
    {
        if (!_recognizerRunning)
        {
            return;
        }

        await _recognizer.StopAsync(cancellationToken).ConfigureAwait(false);
        _recognizerRunning = false;
    }

    private void ScheduleSilenceTimeout()
    {
        CancelSilenceTimeout();
        var cancellation = new CancellationTokenSource();
        _silenceCancellation = cancellation;
        _ = ReturnToDormantAfterSilenceAsync(cancellation);
    }

    private async Task ReturnToDormantAfterSilenceAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(_silenceTimeout, _timeProvider, source.Token).ConfigureAwait(false);
            await _gate.WaitAsync(source.Token).ConfigureAwait(false);
            try
            {
                if (_silenceCancellation == source && State == VoiceState.Listening)
                {
                    _pendingPreferredName = null;
                    _awaitingPreferredName = !_profile.OnboardingCompleted;
                    await EnterDormantAsync(source.Token).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A new utterance or explicit control replaced this timeout.
        }
        finally
        {
            source.Dispose();
        }
    }

    private void CancelSilenceTimeout()
    {
        var cancellation = Interlocked.Exchange(ref _silenceCancellation, null);
        cancellation?.Cancel();
    }

    private void SetState(VoiceState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        Raise(new VoiceStateChanged(state, _timeProvider.GetUtcNow()));
    }

    private void Raise(VoiceEvent voiceEvent) => EventRaised?.Invoke(voiceEvent);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await PauseAsync().ConfigureAwait(false);
        _disposed = true;
        _wakeWord.Detected -= OnWakeWordDetectedAsync;
        _recognizer.Recognized -= OnRecognizedAsync;
        _recognizer.SpeechStarted -= OnSpeechStartedAsync;
        _synthesizer.Progress -= OnSpeechProgress;
        _speechCancellation?.Dispose();
        _silenceCancellation?.Dispose();
        _gate.Dispose();
        await _wakeWord.DisposeAsync().ConfigureAwait(false);
        await _recognizer.DisposeAsync().ConfigureAwait(false);
        await _synthesizer.DisposeAsync().ConfigureAwait(false);
    }
}
