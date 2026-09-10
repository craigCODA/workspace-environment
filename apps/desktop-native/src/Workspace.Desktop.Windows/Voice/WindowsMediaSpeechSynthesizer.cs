using System.Globalization;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;
using Workspace.Desktop.Core.Voice;

namespace Workspace.Desktop.Windows.Voice;

/// <summary>
/// Uses the Windows OneCore speech catalog and media pipeline instead of the
/// legacy desktop SAPI voice. If a Natural or HD voice is installed, it wins.
/// </summary>
public sealed class WindowsMediaSpeechSynthesizer : ISpeechSynthesizer
{
    private static readonly string[] PreferredVoiceNames =
    [
        "Ava", "Andrew", "Aria", "Jenny", "Guy", "Natural", "HD", "Mark", "Zira", "David",
    ];

    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly MediaPlayer _player = new()
    {
        AutoPlay = false,
        AudioCategory = MediaPlayerAudioCategory.Speech,
    };
    private readonly SemaphoreSlim _speechGate = new(1, 1);
    private TaskCompletionSource? _activePlayback;
    private bool _disposed;

    public WindowsMediaSpeechSynthesizer()
    {
        _synthesizer.Voice = SelectPreferredVoice();
        _synthesizer.Options.SpeakingRate = 0.94;
    }

    public event EventHandler<SpeechProgressEventArgs>? Progress;

    public string VoiceDisplayName => _synthesizer.Voice.DisplayName;

    public async Task SpeakAsync(
        string text,
        string utteranceId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _speechGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var stream = await _synthesizer
                .SynthesizeTextToStreamAsync(text)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            using var source = MediaSource.CreateFromStream(stream, stream.ContentType);
            var playback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _activePlayback = playback;

            void OnEnded(MediaPlayer sender, object args) => playback.TrySetResult();
            void OnFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args) =>
                playback.TrySetException(new InvalidOperationException(
                    $"Windows could not play the Coda voice: {args.ErrorMessage}"));

            _player.MediaEnded += OnEnded;
            _player.MediaFailed += OnFailed;
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                _player.Pause();
                playback.TrySetCanceled(cancellationToken);
            });
            try
            {
                _player.Source = source;
                Progress?.Invoke(this, new SpeechProgressEventArgs(text, isFinal: true, utteranceId));
                _player.Play();
                await playback.Task.ConfigureAwait(false);
            }
            finally
            {
                _player.MediaEnded -= OnEnded;
                _player.MediaFailed -= OnFailed;
                _player.Source = null;
                _activePlayback = null;
            }
        }
        finally
        {
            _speechGate.Release();
        }
    }

    public Task CancelAsync()
    {
        if (!_disposed)
        {
            _player.Pause();
            _activePlayback?.TrySetResult();
        }

        return Task.CompletedTask;
    }

    private static VoiceInformation SelectPreferredVoice()
    {
        var culture = CultureInfo.CurrentUICulture;
        return SpeechSynthesizer.AllVoices
            .OrderByDescending(voice => ScoreVoice(voice, culture))
            .FirstOrDefault()
            ?? SpeechSynthesizer.DefaultVoice;
    }

    private static int ScoreVoice(VoiceInformation voice, CultureInfo culture)
    {
        var score = string.Equals(voice.Language, culture.Name, StringComparison.OrdinalIgnoreCase)
            ? 1_000
            : voice.Language.StartsWith(culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)
                ? 500
                : 0;
        var name = $"{voice.DisplayName} {voice.Id}";
        for (var index = 0; index < PreferredVoiceNames.Length; index++)
        {
            if (name.Contains(PreferredVoiceNames[index], StringComparison.OrdinalIgnoreCase))
            {
                score += 100 - index;
                break;
            }
        }

        return score;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _player.Pause();
        _activePlayback?.TrySetResult();
        _player.Dispose();
        _synthesizer.Dispose();
        _speechGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
