using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Workspace.Desktop.Bridge;
using Workspace.Desktop.Core.Agent;
using Workspace.Desktop.Core.Bridge;
using Workspace.Desktop.Core.Capabilities;
using Workspace.Desktop.Core.Preferences;
using Workspace.Desktop.Core.Voice;
using Workspace.Desktop.Windows.Agent;
using Workspace.Desktop.Windows.Voice;

namespace Workspace.Desktop.Runtime;

public sealed class DesktopCoordinator : IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly WorkspaceHostProcess _host = new();
    private readonly WebViewBridge _bridge;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly StringBuilder _assistantResponse = new();
    private readonly List<string> _terminalEvents = [];
    private VoiceProfileStore? _profileStore;
    private VoiceProfile _profile = VoiceProfile.Default;
    private CapabilityBroker? _capabilities;
    private VoiceConversationController? _voice;
    private CodexAppServerClient? _agent;
    private AgentApprovalRequested? _pendingApproval;
    private Task? _agentEvents;
    private Task? _voiceStartup;
    private string? _sourceRoot;
    private bool _started;
    private bool _disposed;

    public DesktopCoordinator(WebView2 webView, DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _bridge = new WebViewBridge(webView);
        _bridge.MessageReceived += OnRendererMessage;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        _sourceRoot = ResolveSourceRoot();
        var stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceEnvironment",
            "Coda");
        _profileStore = new VoiceProfileStore(Path.Combine(stateDirectory, "voice-profile.json"));
        _profile = await _profileStore.LoadAsync(cancellationToken);
        _capabilities = await CapabilityBroker.OpenAsync(
            Path.Combine(stateDirectory, "capability-grants.json"),
            cancellationToken: cancellationToken);

        await _host.StartAsync(cancellationToken);
        await _bridge.InitializeAsync(ResolveSpatialClientDirectory(), cancellationToken);
        await _bridge.WaitForRendererAsync(cancellationToken);
        PostPreferences();

        await StartAgentAsync(cancellationToken);
        StartVoice();
        PostOnUi("runtime.health", new
        {
            state = "healthy",
            host = "ready",
            renderer = "ready",
            agent = "ready",
            voice = _voice is null ? "recovery" : "ready",
        });
        _started = true;
    }

    private async Task StartAgentAsync(CancellationToken cancellationToken)
    {
        var capabilities = _capabilities
            ?? throw new InvalidOperationException("The capability broker is unavailable.");
        var sourceRoot = _sourceRoot
            ?? throw new InvalidOperationException("The source root is unavailable.");
        _agent = new CodexAppServerClient((root, sandbox) => sandbox switch
        {
            AgentSandbox.ReadOnly => true,
            AgentSandbox.WorkspaceWrite => capabilities.IsGranted("agent.workspace-write", root),
            AgentSandbox.DangerFullAccess => capabilities.IsGranted("agent.full-access", root),
            _ => false,
        });
        _agentEvents = PumpAgentEventsAsync(_agent, _lifetime.Token);
        await _agent.StartAsync(cancellationToken);
        await _agent.StartOrResumeThreadAsync(
            sourceRoot,
            threadId: null,
            AgentSandbox.ReadOnly,
            cancellationToken);
    }

    private void StartVoice()
    {
        try
        {
            var engine = new SystemSpeechVoiceEngine();
            _voice = new VoiceConversationController(engine, engine, engine);
            _voice.EventRaised += OnVoiceEvent;
            _voiceStartup = RunVoiceStartupAsync(_voice, _profile, _lifetime.Token);
        }
        catch (VoiceRuntimeUnavailableException exception)
        {
            PostOnUi("voice.state", new { state = "needs-attention" });
            PostOnUi("voice.caption", new
            {
                text = exception.Message,
                final = true,
                utteranceId = "voice-recovery",
            });
        }
    }

    private async Task RunVoiceStartupAsync(
        VoiceConversationController voice,
        VoiceProfile profile,
        CancellationToken cancellationToken)
    {
        try
        {
            await voice.StartAsync(profile, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PostOnUi("voice.state", new { state = "needs-attention" });
            PostOnUi("voice.caption", new
            {
                text = $"Local voice stopped. {exception.Message}",
                final = true,
                utteranceId = "voice-error",
            });
        }
    }

    private void OnVoiceEvent(VoiceEvent voiceEvent)
    {
        switch (voiceEvent)
        {
            case VoiceStateChanged state:
                PostOnUi("voice.state", new { state = RendererVoiceState(state.State) });
                break;
            case VoiceCaption caption:
                PostOnUi("voice.caption", new
                {
                    text = caption.Text,
                    final = caption.IsFinal,
                    utteranceId = caption.UtteranceId,
                });
                break;
            case VoiceTranscript transcript:
                PostOnUi("voice.transcript", new
                {
                    text = transcript.Text,
                    final = transcript.IsFinal,
                });
                break;
            case PreferredNameCaptured captured:
                _ = SavePreferredNameAsync(captured.Name);
                break;
            case VoiceCommandRecognized command:
                _ = HandleVoiceCommandAsync(command.Text);
                break;
            case VoiceFailure failure:
                PostOnUi("voice.state", new { state = "needs-attention" });
                PostOnUi("voice.caption", new { text = failure.Message, final = true });
                break;
        }
    }

    private async Task HandleVoiceCommandAsync(string text)
    {
        if (_pendingApproval is not null)
        {
            await HandleApprovalAnswerAsync(text);
            return;
        }

        var agent = _agent;
        if (agent is null)
        {
            await SpeakAsync("The coding agent is not ready yet.");
            return;
        }

        _assistantResponse.Clear();
        try
        {
            await agent.StartTurnAsync(
                "You are Coda, the voice-first guide inside Workspace Environment. "
                + "Act on the user's request in the current workspace when allowed. "
                + "Keep the final response concise and natural to speak aloud. User request: "
                + text,
                _lifetime.Token);
        }
        catch (Exception exception)
        {
            await SpeakAsync($"I couldn't start that work. {exception.Message}");
        }
    }

    private async Task HandleApprovalAnswerAsync(string text)
    {
        var approval = _pendingApproval;
        var agent = _agent;
        if (approval is null || agent is null)
        {
            return;
        }

        var answer = text.Trim().ToLowerInvariant();
        AgentApprovalDecision? decision = answer switch
        {
            var value when value.Contains("approve for this session", StringComparison.Ordinal)
                || value.Contains("always", StringComparison.Ordinal) =>
                    AgentApprovalDecision.AcceptForSession,
            var value when value.Contains("approve", StringComparison.Ordinal)
                || value is "yes" or "do it" => AgentApprovalDecision.Accept,
            var value when value.Contains("deny", StringComparison.Ordinal)
                || value is "no" or "cancel" => AgentApprovalDecision.Decline,
            _ => null,
        };
        if (decision is null)
        {
            await SpeakAsync("Please say approve, approve for this session, or deny.");
            return;
        }

        _pendingApproval = null;
        await agent.RespondToApprovalAsync(approval.RequestId, decision.Value, _lifetime.Token);
        await SpeakAsync(decision == AgentApprovalDecision.Decline
            ? "Denied."
            : "Approved. I'll continue.");
    }

    private async Task PumpAgentEventsAsync(
        ICodingAgent agent,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var agentEvent in agent.ReadEventsAsync(cancellationToken))
            {
                switch (agentEvent)
                {
                    case AgentAssistantDelta delta:
                        _assistantResponse.Append(delta.Text);
                        break;
                    case AgentCommandStarted command:
                        AddTerminalEvent($"> {command.CommandSummary}");
                        break;
                    case AgentTerminalDelta output:
                        AddTerminalEvent(output.Text.TrimEnd());
                        break;
                    case AgentApprovalRequested approval:
                        _pendingApproval = approval;
                        PostOnUi("voice.state", new { state = "needs-attention" });
                        await SpeakAsync(
                            $"I need approval to {approval.CommandSummary}. "
                            + "Say approve, approve for this session, or deny.");
                        break;
                    case AgentTurnCompleted completed:
                        var response = _assistantResponse.ToString().Trim();
                        _assistantResponse.Clear();
                        if (completed.Status == "completed" && response.Length > 0)
                        {
                            await SpeakAsync(response);
                        }
                        else if (completed.Status != "completed")
                        {
                            await SpeakAsync(completed.Error ?? "The agent could not finish that work.");
                        }
                        break;
                    case AgentAuthenticationRequired authentication:
                        await SpeakAsync(authentication.Message);
                        break;
                    case AgentProcessExited exited:
                        await SpeakAsync($"The coding agent stopped. {exited.Message}");
                        break;
                    case AgentProtocolFailure failure:
                        AddTerminalEvent($"Agent protocol: {failure.Message}");
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void AddTerminalEvent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        _terminalEvents.Add(value.Length > 600 ? value[..600] + "…" : value);
        if (_terminalEvents.Count > 24)
        {
            _terminalEvents.RemoveRange(0, _terminalEvents.Count - 24);
        }
        PostOnUi("agent.event", new { terminalEvents = _terminalEvents.ToArray() });
    }

    private async Task SavePreferredNameAsync(string name)
    {
        var store = _profileStore;
        if (store is null)
        {
            return;
        }
        _profile = _profile with { PreferredName = name, OnboardingCompleted = true };
        await store.SaveAsync(_profile, _lifetime.Token);
        PostPreferences();
    }

    private void OnRendererMessage(WebViewMessage message)
    {
        switch (message.Type)
        {
            case "voice.control":
                _ = HandleVoiceControlAsync(message.Payload);
                break;
            case "preference.change.request":
                _ = HandlePreferenceChangeAsync(message.Payload);
                break;
            case "agent.instruction":
                if (ReadString(message.Payload, "text") is { Length: > 0 } instruction)
                {
                    _ = HandleVoiceCommandAsync(instruction);
                }
                break;
            case "agent.approval.response":
                if (ReadString(message.Payload, "answer") is { Length: > 0 } answer)
                {
                    _ = HandleApprovalAnswerAsync(answer);
                }
                break;
        }
    }

    private async Task HandleVoiceControlAsync(JsonElement payload)
    {
        var voice = _voice;
        if (voice is null)
        {
            return;
        }
        switch (ReadString(payload, "action"))
        {
            case "pause":
                await voice.PauseAsync(_lifetime.Token);
                break;
            case "resume":
                await voice.ResumeAsync(_lifetime.Token);
                break;
            case "stop":
                await voice.StopConversationAsync(_lifetime.Token);
                break;
        }
    }

    private async Task HandlePreferenceChangeAsync(JsonElement payload)
    {
        var store = _profileStore;
        if (store is null)
        {
            return;
        }
        if (ReadBoolean(payload, "microphoneEnabled") is { } microphone)
        {
            _profile = _profile with { MicrophoneEnabled = microphone };
            if (_voice is not null)
            {
                await _voice.SetMicrophoneEnabledAsync(microphone, _lifetime.Token);
            }
        }
        if (ReadBoolean(payload, "captionsEnabled") is { } captions)
        {
            _profile = _profile with { CaptionsEnabled = captions };
            _voice?.SetCaptionsEnabled(captions);
        }
        if (ReadBoolean(payload, "transcriptRetentionEnabled") is { } transcript)
        {
            _profile = _profile with { TranscriptRetentionEnabled = transcript };
        }
        await store.SaveAsync(_profile, _lifetime.Token);
        PostPreferences();
    }

    private void PostPreferences() => PostOnUi("preference.changed", new
    {
        microphoneEnabled = _profile.MicrophoneEnabled,
        captionsEnabled = _profile.CaptionsEnabled,
        transcriptRetentionEnabled = _profile.TranscriptRetentionEnabled,
        proactiveMode = _profile.ProactiveMode.ToString(),
        navigationMode = _profile.NavigationMode.ToString(),
        wakePhrase = _profile.WakePhrase,
    });

    private Task SpeakAsync(string text) => _voice?.SpeakAsync(text, _lifetime.Token)
        ?? Task.CompletedTask;

    private void PostOnUi(string type, object payload)
    {
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                _bridge.Post(type, payload);
            }
        });
    }

    private static string RendererVoiceState(VoiceState state) => state switch
    {
        VoiceState.Dormant => "waiting",
        VoiceState.WakeDetected => "wake-detected",
        VoiceState.Listening => "listening",
        VoiceState.Thinking => "thinking",
        VoiceState.Speaking => "speaking",
        VoiceState.Paused or VoiceState.MicrophoneOff => "mic-off",
        VoiceState.Faulted => "needs-attention",
        _ => "waiting",
    };

    private static string? ReadString(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object
        && payload.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string ResolveSourceRoot() => FindRepositoryRoot()
        ?? Path.GetFullPath(AppContext.BaseDirectory);

    private static string ResolveSpatialClientDirectory()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "spatial-client");
        if (File.Exists(Path.Combine(bundled, "index.html")))
        {
            return bundled;
        }
        var repository = FindRepositoryRoot()
            ?? throw new DirectoryNotFoundException("The built spatial client could not be located.");
        return Path.Combine(repository, "apps", "spatial-client", "dist");
    }

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "package.json"))
                && Directory.Exists(Path.Combine(directory.FullName, "apps", "spatial-client")))
            {
                return directory.FullName;
            }
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        _bridge.MessageReceived -= OnRendererMessage;
        if (_voice is not null)
        {
            _voice.EventRaised -= OnVoiceEvent;
            await _voice.DisposeAsync();
        }
        if (_agent is not null)
        {
            await _agent.DisposeAsync();
        }
        await _host.DisposeAsync();
        _lifetime.Dispose();
    }
}
