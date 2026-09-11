using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Workspace.Desktop.Core.Agent;

public sealed class SpaceXAICodingAgent : ICodingAgent
{
    public const string DefaultModel = "grok-4.6";
    public const string DefaultBaseUrl = "https://api.x.ai/v1/";

    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKeyProvider;
    private readonly string _model;
    private readonly List<(string Role, string Content)> _history = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _turnCancellation;
    private string? _threadId;
    private string? _turnId;
    private bool _started;
    private bool _disposed;

    public SpaceXAICodingAgent(
        HttpMessageHandler? handler = null,
        Func<string?>? apiKeyProvider = null,
        string? model = null,
        Uri? baseAddress = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = baseAddress ?? new Uri(DefaultBaseUrl);
        _apiKeyProvider = apiKeyProvider ?? (() => Environment.GetEnvironmentVariable("XAI_API_KEY"));
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            return;
        }

        var apiKey = _apiKeyProvider();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            await _events.Writer.WriteAsync(new AgentAuthenticationRequired(
                "SpaceXAI needs XAI_API_KEY before Coda can use that provider.",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("XAI_API_KEY is not configured.");
        }

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        _started = true;
        await _events.Writer.WriteAsync(new AgentStatus(
            "SpaceXAI connected with the configured XAI_API_KEY.",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> StartOrResumeThreadAsync(
        string sourceRoot,
        string? threadId,
        AgentSandbox sandbox,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        _ = sourceRoot;
        _ = sandbox;
        _threadId = string.IsNullOrWhiteSpace(threadId) ? $"spacexai-{Guid.NewGuid():N}" : threadId;
        lock (_gate)
        {
            _history.Clear();
        }
        await _events.Writer.WriteAsync(new AgentThreadStarted(_threadId, DateTimeOffset.UtcNow), cancellationToken)
            .ConfigureAwait(false);
        return _threadId;
    }

    public async Task<string> StartTurnAsync(string prompt, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var threadId = _threadId
            ?? throw new InvalidOperationException("Start a SpaceXAI thread before starting a turn.");
        var turnId = $"turn-{Guid.NewGuid():N}";
        _turnId = turnId;
        var itemId = $"msg-{Guid.NewGuid():N}";

        CancellationTokenSource turnCancellation;
        lock (_gate)
        {
            _turnCancellation?.Cancel();
            _turnCancellation?.Dispose();
            turnCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _turnCancellation = turnCancellation;
            _history.Add(("user", prompt));
        }

        await _events.Writer.WriteAsync(
            new AgentTurnStarted(threadId, turnId, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);

        try
        {
            var assistant = await CompleteAsync(turnCancellation.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(assistant))
            {
                await _events.Writer.WriteAsync(
                    new AgentAssistantDelta(threadId, turnId, itemId, assistant, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
                lock (_gate)
                {
                    _history.Add(("assistant", assistant));
                }
            }

            await _events.Writer.WriteAsync(
                new AgentTurnCompleted(threadId, turnId, "completed", null, DateTimeOffset.UtcNow),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (turnCancellation.IsCancellationRequested)
        {
            await _events.Writer.WriteAsync(
                new AgentTurnCompleted(threadId, turnId, "interrupted", null, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await _events.Writer.WriteAsync(
                new AgentTurnCompleted(threadId, turnId, "failed", exception.Message, DateTimeOffset.UtcNow),
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return turnId;
    }

    public Task SteerAsync(string instruction, CancellationToken cancellationToken = default) =>
        StartTurnAsync(instruction, cancellationToken);

    public Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _turnCancellation?.Cancel();
        }
        return Task.CompletedTask;
    }

    public Task RespondToApprovalAsync(
        string requestId,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = requestId;
        _ = decision;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AgentEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_gate)
        {
            _turnCancellation?.Cancel();
            _turnCancellation?.Dispose();
            _turnCancellation = null;
        }
        _events.Writer.TryComplete();
        _http.Dispose();
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<string> CompleteAsync(CancellationToken cancellationToken)
    {
        object[] messages;
        lock (_gate)
        {
            messages = _history
                .Select(entry => (object)new { role = entry.Role, content = entry.Content })
                .ToArray();
        }

        using var content = new StringContent(
            JsonSerializer.Serialize(new
            {
                model = _model,
                messages,
                stream = false,
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _http.PostAsync("chat/completions", content, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"SpaceXAI request failed ({(int)response.StatusCode}): {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var message = choices[0].GetProperty("message");
        return message.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString() ?? string.Empty
            : string.Empty;
    }
}
