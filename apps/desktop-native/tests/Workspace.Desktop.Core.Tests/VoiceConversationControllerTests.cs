using Workspace.Desktop.Core.Preferences;
using Workspace.Desktop.Core.Voice;

namespace Workspace.Desktop.Core.Tests;

public sealed class VoiceConversationControllerTests
{
    [Fact]
    public async Task Ambient_speech_is_ignored_until_the_wake_phrase()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true));

        await fixture.Recognizer.EmitRecognizedAsync("delete everything");

        Assert.DoesNotContain(fixture.Events, item => item is VoiceCommandRecognized);
        Assert.Equal(VoiceState.Dormant, fixture.Controller.State);
    }

    [Fact]
    public async Task Wake_phrase_enters_active_listening_and_final_speech_becomes_a_command()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true));

        await fixture.Wake.EmitAsync("Hey Coda");
        await fixture.Recognizer.EmitRecognizedAsync("build the workspace");

        Assert.Contains(fixture.Events, item => item is VoiceCommandRecognized command
            && command.Text == "build the workspace");
        Assert.Equal(VoiceState.Thinking, fixture.Controller.State);
    }

    [Fact]
    public async Task Talk_control_begins_a_conversation_without_the_wake_phrase()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true));

        await fixture.Controller.BeginConversationAsync();
        await fixture.Recognizer.EmitRecognizedAsync("show terminal");

        Assert.Contains(fixture.Events, item => item is VoiceCommandRecognized command
            && command.Text == "show terminal");
    }

    [Fact]
    public async Task Active_listening_returns_to_dormant_after_silence()
    {
        var fixture = new VoiceFixture(TimeSpan.FromMilliseconds(15));
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true));
        await fixture.Wake.EmitAsync("Hey Coda");

        await Task.Delay(80);

        Assert.Equal(VoiceState.Dormant, fixture.Controller.State);
        Assert.True(fixture.Wake.IsRunning);
        Assert.False(fixture.Recognizer.IsRunning);
    }

    [Fact]
    public async Task Speech_during_synthesis_barges_in_and_cancels_the_voice()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true));
        await fixture.Wake.EmitAsync("Hey Coda");
        fixture.Synthesizer.HoldSpeech = true;
        var speaking = fixture.Controller.SpeakAsync("I am explaining the build.");

        await fixture.Synthesizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await fixture.Recognizer.EmitSpeechStartedAsync();
        await speaking;

        Assert.True(fixture.Synthesizer.WasCancelled);
        Assert.Equal(VoiceState.Listening, fixture.Controller.State);
    }

    [Fact]
    public async Task Microphone_and_caption_preferences_are_independent()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true, captionsEnabled: false));

        Assert.DoesNotContain(fixture.Events, item => item is VoiceCaption);
        await fixture.Controller.SetMicrophoneEnabledAsync(false);
        Assert.Equal(VoiceState.MicrophoneOff, fixture.Controller.State);
        Assert.False(fixture.Wake.IsRunning);
    }

    [Fact]
    public async Task First_run_narrates_in_order_then_requests_a_preferred_name()
    {
        var fixture = new VoiceFixture();

        await fixture.Controller.StartAsync(Profile(onboardingCompleted: false));

        Assert.Equal(VoiceConversationController.FirstRunNarration, fixture.Synthesizer.Spoken.Take(6));
        Assert.Equal("What should I call you?", fixture.Synthesizer.Spoken[6]);
        Assert.Equal(VoiceState.Listening, fixture.Controller.State);
        await fixture.Recognizer.EmitRecognizedAsync("Morgan");
        Assert.DoesNotContain(fixture.Events, item => item is PreferredNameCaptured);
        Assert.Equal("I heard Morgan. Is that right?", fixture.Synthesizer.Spoken[7]);
        await fixture.Recognizer.EmitRecognizedAsync("yes");
        Assert.Contains(fixture.Events, item => item is PreferredNameCaptured captured
            && captured.Name == "Morgan");
    }

    [Fact]
    public async Task Rejected_name_is_not_persisted_and_the_question_is_repeated()
    {
        var fixture = new VoiceFixture();
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: false));

        await fixture.Recognizer.EmitRecognizedAsync("background television");
        await fixture.Recognizer.EmitRecognizedAsync("no");

        Assert.DoesNotContain(fixture.Events, item => item is PreferredNameCaptured);
        Assert.Equal("Okay. What should I call you?", fixture.Synthesizer.Spoken[^1]);
        Assert.Equal(VoiceState.Listening, fixture.Controller.State);
    }

    [Fact]
    public async Task Unconfirmed_name_times_out_and_click_to_talk_restarts_name_setup()
    {
        var fixture = new VoiceFixture(TimeSpan.FromMilliseconds(15));
        await fixture.Controller.StartAsync(Profile(onboardingCompleted: false));
        await fixture.Recognizer.EmitRecognizedAsync("room noise");

        await Task.Delay(80);
        Assert.Equal(VoiceState.Dormant, fixture.Controller.State);
        await fixture.Controller.BeginConversationAsync();

        Assert.Equal("What should I call you?", fixture.Synthesizer.Spoken[^1]);
        Assert.DoesNotContain(fixture.Events, item => item is PreferredNameCaptured);
    }

    [Fact]
    public async Task Returning_launch_uses_the_saved_name_and_waits_for_wake_word()
    {
        var fixture = new VoiceFixture();

        await fixture.Controller.StartAsync(Profile(onboardingCompleted: true, preferredName: "Morgan"));

        Assert.Equal(new[] { "Welcome back, Morgan." }, fixture.Synthesizer.Spoken);
        Assert.Equal(VoiceState.Dormant, fixture.Controller.State);
        Assert.True(fixture.Wake.IsRunning);
    }

    private static VoiceProfile Profile(
        bool onboardingCompleted,
        string preferredName = "",
        bool captionsEnabled = true) => VoiceProfile.Default with
        {
            OnboardingCompleted = onboardingCompleted,
            PreferredName = preferredName,
            CaptionsEnabled = captionsEnabled,
        };

    private sealed class VoiceFixture
    {
        public VoiceFixture(TimeSpan? silenceTimeout = null)
        {
            Controller = new VoiceConversationController(
                Wake,
                Recognizer,
                Synthesizer,
                TimeProvider.System,
                silenceTimeout ?? TimeSpan.FromSeconds(8));
            Controller.EventRaised += Events.Add;
        }

        public FakeWakeWordEngine Wake { get; } = new();
        public FakeSpeechRecognizer Recognizer { get; } = new();
        public FakeSpeechSynthesizer Synthesizer { get; } = new();
        public VoiceConversationController Controller { get; }
        public List<VoiceEvent> Events { get; } = [];
    }

    private sealed class FakeWakeWordEngine : IWakeWordEngine
    {
        public event Func<object?, WakeWordDetectedEventArgs, Task>? Detected;
        public bool IsRunning { get; private set; }
        public Task StartAsync(string wakePhrase, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
        public async Task EmitAsync(string phrase)
        {
            await (Detected?.Invoke(this, new WakeWordDetectedEventArgs(phrase)) ?? Task.CompletedTask);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSpeechRecognizer : ISpeechRecognizer
    {
        public event Func<object?, SpeechRecognizedEventArgs, Task>? Recognized;
        public event Func<object?, EventArgs, Task>? SpeechStarted;
        public bool IsRunning { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
        public async Task EmitRecognizedAsync(string text)
        {
            if (IsRunning)
            {
                await (Recognized?.Invoke(this, new SpeechRecognizedEventArgs(text, true, 0.98f))
                    ?? Task.CompletedTask);
            }
        }
        public async Task EmitSpeechStartedAsync()
        {
            if (IsRunning)
            {
                await (SpeechStarted?.Invoke(this, EventArgs.Empty) ?? Task.CompletedTask);
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeSpeechSynthesizer : ISpeechSynthesizer
    {
        private TaskCompletionSource? _held;
        public event EventHandler<SpeechProgressEventArgs>? Progress;
        public List<string> Spoken { get; } = [];
        public bool HoldSpeech { get; set; }
        public bool WasCancelled { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task SpeakAsync(string text, string utteranceId, CancellationToken cancellationToken = default)
        {
            Spoken.Add(text);
            Progress?.Invoke(this, new SpeechProgressEventArgs(text, true, utteranceId));
            Started.TrySetResult();
            if (!HoldSpeech) return;
            _held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(() => _held.TrySetCanceled(cancellationToken));
            try { await _held.Task; } catch (OperationCanceledException) { }
        }

        public Task CancelAsync()
        {
            WasCancelled = true;
            _held?.TrySetResult();
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
