using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Workspace.Desktop.Bridge;
using Workspace.Desktop.Core.Agent;
using Workspace.Desktop.Core.Bridge;
using Workspace.Desktop.Core.Capabilities;
using Workspace.Desktop.Core.Preferences;
using Workspace.Desktop.Core.Runtime;
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
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _sceneResults = new();
    private VoiceProfileStore? _profileStore;
    private VoiceProfile _profile = VoiceProfile.Default;
    private CapabilityBroker? _capabilities;
    private VoiceConversationController? _voice;
    private CodexAppServerClient? _agent;
    private PendingApproval? _pendingApproval;
    private IReadOnlyList<SceneDirective>? _pendingNavigation;
    private Task? _agentEvents;
    private Task? _voiceStartup;
    private string? _sourceRoot;
    private string? _lastSpokenText;
    private long _sceneRequestSequence;
    private bool _agentTurnActive;
    private bool _started;
    private bool _disposed;
    private readonly string? _configuredSourceRoot;
    private readonly string? _configuredStateRoot;

    public DesktopCoordinator(
        WebView2 webView,
        DispatcherQueue dispatcher,
        string? sourceRoot = null,
        string? stateRoot = null)
    {
        _dispatcher = dispatcher;
        _configuredSourceRoot = string.IsNullOrWhiteSpace(sourceRoot)
            ? null
            : Path.GetFullPath(sourceRoot);
        _configuredStateRoot = string.IsNullOrWhiteSpace(stateRoot)
            ? null
            : Path.GetFullPath(stateRoot);
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

        _sourceRoot = _configuredSourceRoot ?? ResolveSourceRoot();
        var workspaceStateRoot = _configuredStateRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceEnvironment");
        var stateDirectory = Path.Combine(workspaceStateRoot, "Coda");
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
            ISpeechSynthesizer speechOutput;
            try
            {
                var modernOutput = new WindowsMediaSpeechSynthesizer();
                speechOutput = modernOutput;
                AddTerminalEvent($"Voice output: {modernOutput.VoiceDisplayName} (Windows OneCore)");
            }
            catch (Exception)
            {
                speechOutput = engine;
                AddTerminalEvent("Voice output: legacy Windows SAPI fallback");
            }

            _voice = new VoiceConversationController(engine, engine, speechOutput);
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
        var local = CodaLocalCommandParser.Parse(text);
        if (local.Kind == CodaLocalCommandKind.Stop)
        {
            await HandleLocalCommandAsync(local);
            return;
        }

        if (_pendingApproval is not null)
        {
            await HandleApprovalAnswerAsync(text);
            return;
        }

        if (_pendingNavigation is not null)
        {
            await HandleNavigationAnswerAsync(text);
            return;
        }

        if (local.Kind != CodaLocalCommandKind.AgentRequest)
        {
            await HandleLocalCommandAsync(local);
            return;
        }

        var agent = _agent;
        if (agent is null)
        {
            await SpeakAsync("The coding agent is not ready yet.");
            return;
        }

        PostOnUi("voice.state", new { state = "thinking" });
        _assistantResponse.Clear();
        try
        {
            var snapshot = await InspectSceneAsync(_lifetime.Token);
            await agent.StartTurnAsync(
                "You are Coda, the voice-first guide inside Workspace Environment. "
                + "Act on the user's request in the current workspace when allowed. "
                + "Use only brokered capabilities and never request or infer scene pixels. "
                + "Keep the final response concise and natural to speak aloud. "
                + "You may control the Three.js space with private directives shaped exactly like "
                + "[[scene:{\"command\":\"camera.focus\",\"args\":{\"entityId\":\"exact id from snapshot\"}}]]. "
                + "Allowed commands: camera.navigate, camera.focus, camera.stop, camera.return-home, "
                + "surface.move, surface.resize. Directive markup is removed before speech. "
                + $"Current structured scene snapshot: {snapshot}. User request: {text}",
                _lifetime.Token);
            _agentTurnActive = true;
        }
        catch (Exception exception)
        {
            await SpeakAsync($"I couldn't start that work. {exception.Message}");
        }
    }

    private async Task HandleApprovalAnswerAsync(string text)
    {
        var pending = _pendingApproval;
        var agent = _agent;
        var capabilities = _capabilities;
        if (pending is null || agent is null || capabilities is null)
        {
            return;
        }

        var answer = text.Trim().ToLowerInvariant();
        if (answer.Contains("remember this", StringComparison.Ordinal))
        {
            try
            {
                await capabilities.RememberAsync(
                    new CapabilityGrant(pending.Capability.Capability, pending.Capability.Scope, null),
                    _lifetime.Token);
                _pendingApproval = null;
                await agent.RespondToApprovalAsync(
                    pending.Request.RequestId,
                    AgentApprovalDecision.AcceptForSession,
                    _lifetime.Token);
                await SpeakAsync("Remembered for this project. I'll continue.");
            }
            catch (InvalidOperationException)
            {
                await SpeakAsync("That action always needs fresh approval. Say allow once or deny.");
            }
            return;
        }

        AgentApprovalDecision? decision = answer.Contains("allow once", StringComparison.Ordinal)
            ? AgentApprovalDecision.Accept
            : answer.Contains("deny", StringComparison.Ordinal)
                ? AgentApprovalDecision.Decline
                : null;
        if (decision is null)
        {
            await SpeakAsync("Please say allow once, remember this, or deny.");
            return;
        }

        _pendingApproval = null;
        await agent.RespondToApprovalAsync(pending.Request.RequestId, decision.Value, _lifetime.Token);
        await SpeakAsync(decision == AgentApprovalDecision.Decline
            ? "Denied."
            : "Allowed once.");
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
                        PostOnUi("voice.state", new { state = "working" });
                        AddTerminalEvent($"> {command.CommandSummary}");
                        break;
                    case AgentTerminalDelta output:
                        AddTerminalEvent(output.Text.TrimEnd());
                        break;
                    case AgentApprovalRequested approval:
                        var classified = ApprovalCapabilityClassifier.Classify(
                            approval,
                            _sourceRoot ?? approval.WorkingDirectory ?? AppContext.BaseDirectory);
                        if (_capabilities?.IsGranted(classified.Capability, classified.Scope) == true)
                        {
                            await agent.RespondToApprovalAsync(
                                approval.RequestId,
                                AgentApprovalDecision.AcceptForSession,
                                cancellationToken);
                            AddTerminalEvent(
                                $"Allowed remembered {classified.Capability} for {classified.Scope}");
                            break;
                        }
                        _pendingApproval = new PendingApproval(approval, classified);
                        PostOnUi("voice.state", new { state = "needs-attention" });
                        await SpeakAsync(
                            $"I need approval to {approval.CommandSummary}, in {classified.Scope}. "
                            + "Say allow once, remember this, or deny.");
                        break;
                    case AgentTurnCompleted completed:
                        _agentTurnActive = false;
                        var response = _assistantResponse.ToString().Trim();
                        _assistantResponse.Clear();
                        if (completed.Status == "completed" && response.Length > 0)
                        {
                            await HandleAgentResponseAsync(response);
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
                        _agentTurnActive = false;
                        if (ProactiveSpeechPolicy.ShouldSpeak(
                            _profile.ProactiveMode,
                            ProactiveEventKind.Failure))
                        {
                            await SpeakAsync($"The coding agent stopped. {exited.Message}");
                        }
                        break;
                    case AgentProtocolFailure failure:
                        AddTerminalEvent($"Agent protocol: {failure.Message}");
                        if (ProactiveSpeechPolicy.ShouldSpeak(
                            _profile.ProactiveMode,
                            ProactiveEventKind.Failure))
                        {
                            await SpeakAsync("Coda's coding connection needs attention.");
                        }
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

    private async Task HandleLocalCommandAsync(CodaLocalCommand command)
    {
        switch (command.Kind)
        {
            case CodaLocalCommandKind.Stop:
                if (_pendingApproval is { } pending && _agent is not null)
                {
                    _pendingApproval = null;
                    await _agent.RespondToApprovalAsync(
                        pending.Request.RequestId,
                        AgentApprovalDecision.Cancel,
                        _lifetime.Token);
                }
                _pendingNavigation = null;
                if (_agentTurnActive && _agent is not null)
                {
                    await _agent.InterruptAsync(_lifetime.Token);
                    _agentTurnActive = false;
                }
                if (_voice is not null)
                {
                    await _voice.StopConversationAsync(_lifetime.Token);
                }
                return;
            case CodaLocalCommandKind.Pause:
                if (_voice is not null) await _voice.PauseAsync(_lifetime.Token);
                return;
            case CodaLocalCommandKind.Resume:
                if (_voice is not null) await _voice.ResumeAsync(_lifetime.Token);
                await SpeakAsync("Listening is on. Say Hey Coda when you need me.");
                return;
            case CodaLocalCommandKind.Repeat:
                await SpeakAsync(_lastSpokenText ?? "I don't have anything to repeat yet.");
                return;
            case CodaLocalCommandKind.ShowTerminal:
                PostOnUi("ui.command", new { action = "show-terminal" });
                await SpeakAsync("Activity is open.");
                return;
            case CodaLocalCommandKind.HideTerminal:
                PostOnUi("ui.command", new { action = "hide-terminal" });
                await SpeakAsync("Activity is hidden.");
                return;
            case CodaLocalCommandKind.ListPermissions:
                var grants = _capabilities?.Grants ?? [];
                await SpeakAsync(grants.Count == 0
                    ? "I don't have any remembered project permissions."
                    : "I remember " + string.Join(
                        "; ",
                        grants.Select(grant => $"{grant.Capability} for {grant.Scope}")) + ".");
                return;
            case CodaLocalCommandKind.ForgetPermissions:
                if (_capabilities is not null)
                {
                    foreach (var grant in _capabilities.Grants.ToArray())
                    {
                        await _capabilities.RevokeAsync(
                            grant.Capability,
                            grant.Scope,
                            _lifetime.Token);
                    }
                }
                await SpeakAsync("I forgot the remembered project permissions.");
                return;
            case CodaLocalCommandKind.ResetOnboarding:
                _profile = _profile with { PreferredName = null, OnboardingCompleted = false };
                await SaveProfileAsync();
                await SpeakAsync("Onboarding will start again the next time Workspace opens.");
                return;
            case CodaLocalCommandKind.ChangeName:
                if (!string.IsNullOrWhiteSpace(command.Argument))
                {
                    _profile = _profile with
                    {
                        PreferredName = command.Argument.Trim(),
                        OnboardingCompleted = true,
                    };
                    await SaveProfileAsync();
                    await SpeakAsync($"I'll call you {_profile.PreferredName}.");
                }
                return;
            case CodaLocalCommandKind.MicrophoneOn:
                await SetMicrophoneAsync(true);
                await SpeakAsync("Microphone on.");
                return;
            case CodaLocalCommandKind.MicrophoneOff:
                await SetMicrophoneAsync(false);
                await SpeakAsync("Microphone off.");
                return;
            case CodaLocalCommandKind.CaptionsOn:
                await SetCaptionsAsync(true);
                await SpeakAsync("Captions on.");
                return;
            case CodaLocalCommandKind.CaptionsOff:
                await SetCaptionsAsync(false);
                await SpeakAsync("Captions off.");
                return;
            case CodaLocalCommandKind.TranscriptOn:
                _profile = _profile with { TranscriptRetentionEnabled = true };
                await SaveProfileAsync();
                await SpeakAsync("Transcript display on.");
                return;
            case CodaLocalCommandKind.TranscriptOff:
                _profile = _profile with { TranscriptRetentionEnabled = false };
                await SaveProfileAsync();
                await SpeakAsync("Transcript display off.");
                return;
            case CodaLocalCommandKind.ProactiveCritical:
                await SetProactiveModeAsync(ProactiveSpeechMode.CriticalOnly, "Only critical alerts will interrupt you.");
                return;
            case CodaLocalCommandKind.ProactiveCompletion:
                await SetProactiveModeAsync(ProactiveSpeechMode.IncludeCompletion, "I'll also announce completed work.");
                return;
            case CodaLocalCommandKind.ProactiveQuiet:
                await SetProactiveModeAsync(ProactiveSpeechMode.Quiet, "Proactive alerts are quiet.");
                return;
            case CodaLocalCommandKind.NavigationGuide:
                await SetNavigationModeAsync(AgentNavigationMode.GuideFreely, "I can guide you through the space freely.");
                return;
            case CodaLocalCommandKind.NavigationAsk:
                await SetNavigationModeAsync(AgentNavigationMode.AskFirst, "I'll ask before moving you.");
                return;
            case CodaLocalCommandKind.NavigationVoiceOnly:
                await SetNavigationModeAsync(AgentNavigationMode.VoiceCommandsOnly, "I'll move you only on a direct voice command.");
                return;
            case CodaLocalCommandKind.ReturnHome:
                await SendSceneCommandAsync(
                    "camera.return-home",
                    new { options = new { mode = "glide", durationMs = 900 } },
                    _lifetime.Token);
                await SpeakAsync("Taking you home.");
                return;
            case CodaLocalCommandKind.StopCamera:
                await SendSceneCommandAsync("camera.stop", new { }, _lifetime.Token);
                return;
            case CodaLocalCommandKind.FocusEntity:
                await FocusEntityAsync(command.Argument ?? string.Empty);
                return;
            case CodaLocalCommandKind.AgentRequest:
            default:
                return;
        }
    }

    private async Task HandleAgentResponseAsync(string response)
    {
        var parsed = SceneDirectiveParser.Parse(response);
        if (parsed.SpokenText.Length > 0)
        {
            await SpeakResponseAsync(parsed.SpokenText);
        }
        if (parsed.Directives.Count == 0)
        {
            return;
        }

        switch (_profile.NavigationMode)
        {
            case AgentNavigationMode.GuideFreely:
                await ExecuteSceneDirectivesAsync(parsed.Directives);
                break;
            case AgentNavigationMode.AskFirst:
                _pendingNavigation = parsed.Directives;
                await SpeakAsync(
                    $"I am ready to {DescribeSceneDirective(parsed.Directives[0])}. Say allow once or deny.");
                break;
            case AgentNavigationMode.VoiceCommandsOnly:
                await SpeakAsync("I left the view where it is because navigation is set to voice commands only.");
                break;
        }
    }

    private async Task HandleNavigationAnswerAsync(string text)
    {
        var directives = _pendingNavigation;
        if (directives is null) return;
        var answer = text.Trim().ToLowerInvariant();
        if (answer.Contains("deny", StringComparison.Ordinal))
        {
            _pendingNavigation = null;
            await SpeakAsync("Navigation cancelled.");
            return;
        }
        if (!answer.Contains("allow once", StringComparison.Ordinal))
        {
            await SpeakAsync("Please say allow once or deny.");
            return;
        }
        _pendingNavigation = null;
        await ExecuteSceneDirectivesAsync(directives);
    }

    private async Task ExecuteSceneDirectivesAsync(IReadOnlyList<SceneDirective> directives)
    {
        foreach (var directive in directives)
        {
            var result = await SendSceneCommandAsync(
                directive.Command,
                directive.Arguments,
                _lifetime.Token);
            if (result.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                var error = ReadString(result, "error") ?? "The scene rejected that movement.";
                await SpeakAsync(error);
                return;
            }
        }
    }

    private async Task FocusEntityAsync(string requestedName)
    {
        var inspection = await SendSceneCommandAsync("scene.inspect", new { }, _lifetime.Token);
        if (!inspection.TryGetProperty("payload", out var snapshot)
            || !snapshot.TryGetProperty("entities", out var entities)
            || entities.ValueKind != JsonValueKind.Array)
        {
            await SpeakAsync("I couldn't inspect the space just now.");
            return;
        }

        var query = requestedName.Trim();
        JsonElement? match = entities.EnumerateArray().FirstOrDefault(entity =>
            (ReadString(entity, "name")?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (ReadString(entity, "id")?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        var entityId = match is { } entity ? ReadString(entity, "id") : null;
        if (string.IsNullOrWhiteSpace(entityId))
        {
            await SpeakAsync($"I couldn't find {query} in the current space.");
            return;
        }

        await SendSceneCommandAsync(
            "camera.focus",
            new { entityId, options = new { mode = "glide", durationMs = 900 } },
            _lifetime.Token);
        await SpeakAsync($"Taking you to {ReadString(match!.Value, "name") ?? query}.");
    }

    private async Task<string> InspectSceneAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await SendSceneCommandAsync("scene.inspect", new { }, cancellationToken);
            if (result.TryGetProperty("payload", out var payload))
            {
                var json = payload.GetRawText();
                return json.Length <= 32_000 ? json : json[..32_000];
            }
        }
        catch (Exception exception)
        {
            AddTerminalEvent($"Scene inspection: {exception.Message}");
        }
        return "{\"camera\":null,\"entities\":[],\"status\":\"unavailable\"}";
    }

    private async Task<JsonElement> SendSceneCommandAsync(
        string command,
        object? arguments,
        CancellationToken cancellationToken)
    {
        var id = $"native-scene-{Interlocked.Increment(ref _sceneRequestSequence)}";
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_sceneResults.TryAdd(id, completion))
        {
            throw new InvalidOperationException("A duplicate scene request was generated.");
        }
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    _bridge.PostSceneCommand(id, command, arguments);
                }
                catch (Exception exception)
                {
                    _sceneResults.TryRemove(id, out _);
                    completion.TrySetException(exception);
                }
            }))
        {
            _sceneResults.TryRemove(id, out _);
            throw new InvalidOperationException("The workspace view is not available.");
        }

        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
        }
        finally
        {
            _sceneResults.TryRemove(id, out _);
        }
    }

    private void CompleteSceneCommand(JsonElement payload)
    {
        var id = ReadString(payload, "id");
        if (id is not null && _sceneResults.TryRemove(id, out var completion))
        {
            completion.TrySetResult(payload.Clone());
        }
    }

    private async Task SpeakResponseAsync(string text)
    {
        var spoken = text.Length <= 3_000 ? text : text[..3_000] + " The rest is in Coda activity.";
        _lastSpokenText = spoken;
        if (_voice is null)
        {
            PostOnUi("voice.caption", new { text = spoken, final = true, utteranceId = "text-recovery" });
            return;
        }
        foreach (var sentence in Regex.Split(spoken, @"(?<=[.!?])\s+")
                     .Where(sentence => !string.IsNullOrWhiteSpace(sentence)))
        {
            await _voice.SpeakAsync(sentence, _lifetime.Token);
        }
    }

    private async Task SetMicrophoneAsync(bool enabled)
    {
        _profile = _profile with { MicrophoneEnabled = enabled };
        if (_voice is not null)
        {
            await _voice.SetMicrophoneEnabledAsync(enabled, _lifetime.Token);
        }
        await SaveProfileAsync();
    }

    private async Task SetCaptionsAsync(bool enabled)
    {
        _profile = _profile with { CaptionsEnabled = enabled };
        _voice?.SetCaptionsEnabled(enabled);
        await SaveProfileAsync();
    }

    private async Task SetProactiveModeAsync(ProactiveSpeechMode mode, string acknowledgement)
    {
        _profile = _profile with { ProactiveMode = mode };
        await SaveProfileAsync();
        await SpeakAsync(acknowledgement);
    }

    private async Task SetNavigationModeAsync(AgentNavigationMode mode, string acknowledgement)
    {
        _profile = _profile with { NavigationMode = mode };
        await SaveProfileAsync();
        await SpeakAsync(acknowledgement);
    }

    private async Task SaveProfileAsync()
    {
        if (_profileStore is not null)
        {
            await _profileStore.SaveAsync(_profile, _lifetime.Token);
            PostPreferences();
        }
    }

    private static string DescribeSceneDirective(SceneDirective directive) => directive.Command switch
    {
        "camera.focus" => "move your view to the requested surface",
        "camera.navigate" => "move your viewpoint",
        "camera.return-home" => "return your view home",
        "surface.move" => "move a surface",
        "surface.resize" => "resize a surface",
        _ => "adjust the workspace view",
    };

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
                    _ = _voice?.SubmitTextAsync(instruction, _lifetime.Token)
                        ?? HandleVoiceCommandAsync(instruction);
                }
                break;
            case "agent.approval.response":
                if (ReadString(message.Payload, "answer") is { Length: > 0 } answer)
                {
                    _ = HandleApprovalAnswerAsync(answer);
                }
                break;
            case "scene.command.result":
                CompleteSceneCommand(message.Payload);
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
            case "listen":
                await voice.BeginConversationAsync(_lifetime.Token);
                break;
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
        if (ReadString(payload, "proactiveMode") is { } proactive
            && Enum.TryParse<ProactiveSpeechMode>(proactive, out var proactiveMode))
        {
            _profile = _profile with { ProactiveMode = proactiveMode };
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

    private Task SpeakAsync(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            _lastSpokenText = text.Trim();
        }
        if (_voice is not null)
        {
            return _voice.SpeakAsync(text, _lifetime.Token);
        }
        PostOnUi("voice.caption", new
        {
            text,
            final = true,
            utteranceId = "text-recovery",
        });
        return Task.CompletedTask;
    }

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
        foreach (var completion in _sceneResults.Values)
        {
            completion.TrySetCanceled();
        }
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

    private sealed record PendingApproval(
        AgentApprovalRequested Request,
        ApprovalCapability Capability);
}
