using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Workspace.Desktop.Core.Agent;

namespace Workspace.Desktop.Windows.Agent;

public sealed class CodexAppServerClient : ICodingAgent
{
    private readonly CodexMessageTranslator _translator;
    private readonly Func<string, AgentSandbox, bool> _sandboxAuthorizer;
    private readonly Channel<AgentEvent> _events = Channel.CreateUnbounded<AgentEvent>(
        new UnboundedChannelOptions { SingleWriter = false, SingleReader = false });
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly StringBuilder _standardError = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Process? _process;
    private StreamWriter? _input;
    private Task? _readTask;
    private Task? _errorTask;
    private Task? _exitTask;
    private ResolvedWindowsCommand? _codexCommand;
    private string? _threadId;
    private string? _turnId;
    private long _requestSequence;
    private bool _initialized;
    private bool _disposed;

    public CodexAppServerClient(
        Func<string, AgentSandbox, bool>? sandboxAuthorizer = null,
        CodexMessageTranslator? translator = null)
    {
        _sandboxAuthorizer = sandboxAuthorizer
            ?? ((_, sandbox) => sandbox != AgentSandbox.DangerFullAccess);
        _translator = translator ?? new CodexMessageTranslator();
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
        {
            return;
        }

        _codexCommand = WindowsCommandResolver.Resolve("codex");
        if (!await IsLoggedInAsync(_codexCommand, cancellationToken).ConfigureAwait(false))
        {
            await _events.Writer.WriteAsync(new AgentAuthenticationRequired(
                "Codex needs your ChatGPT sign-in before Coda can work on the workspace.",
                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
            await LoginInteractivelyAsync(_codexCommand, cancellationToken).ConfigureAwait(false);
            if (!await IsLoggedInAsync(_codexCommand, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("Codex is still signed out after login completed.");
            }
        }

        var startInfo = CreateProcessStartInfo(
            _codexCommand,
            useShellExecute: false,
            redirectStandardInput: true,
            redirectStandardOutput: true,
            redirectStandardError: true,
            createNoWindow: true);
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Codex App Server did not start.");
        _input = _process.StandardInput;
        _input.AutoFlush = true;
        _readTask = ReadLoopAsync(_process.StandardOutput, _lifetimeCancellation.Token);
        _errorTask = ReadErrorAsync(_process.StandardError, _lifetimeCancellation.Token);
        _exitTask = ObserveExitAsync(_process);

        await RequestAsync(CodexProtocolMessages.Initialize, cancellationToken).ConfigureAwait(false);
        await WriteLineAsync(CodexProtocolMessages.Initialized(), cancellationToken).ConfigureAwait(false);
        _initialized = true;
        await _events.Writer.WriteAsync(new AgentStatus(
            "Codex App Server connected with the existing ChatGPT sign-in.",
            DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> StartOrResumeThreadAsync(
        string sourceRoot,
        string? threadId,
        AgentSandbox sandbox,
        CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Workspace source root does not exist: {root}");
        }
        if (!_sandboxAuthorizer(root, sandbox))
        {
            throw new UnauthorizedAccessException(
                $"The capability broker has not authorized {sandbox} access for {root}.");
        }

        var response = string.IsNullOrWhiteSpace(threadId)
            ? await RequestAsync(
                id => CodexProtocolMessages.StartThread(id, root, sandbox),
                cancellationToken).ConfigureAwait(false)
            : await RequestAsync(
                id => CodexProtocolMessages.ResumeThread(id, threadId, root, sandbox),
                cancellationToken).ConfigureAwait(false);
        _threadId = ReadNestedString(response, "result", "thread", "id");
        if (string.IsNullOrWhiteSpace(_threadId))
        {
            throw new InvalidOperationException("Codex did not return a thread id.");
        }

        await _events.Writer.WriteAsync(
            new AgentThreadStarted(_threadId, DateTimeOffset.UtcNow),
            cancellationToken).ConfigureAwait(false);
        return _threadId;
    }

    public async Task<string> StartTurnAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var threadId = _threadId
            ?? throw new InvalidOperationException("Start or resume a Codex thread first.");
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("A turn prompt is required.", nameof(prompt));
        }

        var response = await RequestAsync(
            id => CodexProtocolMessages.StartTurn(id, threadId, prompt.Trim()),
            cancellationToken).ConfigureAwait(false);
        _turnId = ReadNestedString(response, "result", "turn", "id");
        if (string.IsNullOrWhiteSpace(_turnId))
        {
            throw new InvalidOperationException("Codex did not return a turn id.");
        }
        return _turnId;
    }

    public async Task SteerAsync(
        string instruction,
        CancellationToken cancellationToken = default)
    {
        var (threadId, turnId) = ActiveTurn();
        await RequestAsync(
            id => CodexProtocolMessages.SteerTurn(id, threadId, turnId, instruction),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task InterruptAsync(CancellationToken cancellationToken = default)
    {
        var (threadId, turnId) = ActiveTurn();
        await RequestAsync(
            id => CodexProtocolMessages.InterruptTurn(id, threadId, turnId),
            cancellationToken).ConfigureAwait(false);
    }

    public Task RespondToApprovalAsync(
        string requestId,
        AgentApprovalDecision decision,
        CancellationToken cancellationToken = default) =>
        WriteLineAsync(CodexProtocolMessages.ApprovalResponse(requestId, decision), cancellationToken);

    public async IAsyncEnumerable<AgentEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonElement> RequestAsync(
        Func<long, string> createMessage,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestSequence);
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(key, completion))
        {
            throw new InvalidOperationException($"Duplicate Codex request id: {key}");
        }

        using var registration = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));
        try
        {
            await WriteLineAsync(createMessage(id), cancellationToken).ConfigureAwait(false);
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        var input = _input ?? throw new InvalidOperationException("Codex App Server is not running.");
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await input.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(StreamReader output, CancellationToken cancellationToken)
    {
        try
        {
            while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (TryCompleteRequest(line))
                {
                    continue;
                }

                var translated = _translator.Translate(line);
                if (translated is not null)
                {
                    await _events.Writer.WriteAsync(translated, cancellationToken).ConfigureAwait(false);
                    if (translated is AgentTurnCompleted completed)
                    {
                        _turnId = null;
                        if (completed.Status == "failed" && completed.Error?.Contains(
                            "unauthorized",
                            StringComparison.OrdinalIgnoreCase) == true)
                        {
                            await _events.Writer.WriteAsync(new AgentAuthenticationRequired(
                                completed.Error,
                                DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal app shutdown.
        }
        catch (Exception exception)
        {
            _events.Writer.TryWrite(new AgentProtocolFailure(
                CodexMessageTranslator.Redact(exception.Message),
                DateTimeOffset.UtcNow));
        }
    }

    private bool TryCompleteRequest(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id)
                || (!root.TryGetProperty("result", out _)
                    && !root.TryGetProperty("error", out _)))
            {
                return false;
            }

            var key = id.ValueKind == JsonValueKind.String
                ? id.GetString() ?? string.Empty
                : id.GetRawText();
            if (!_pending.TryGetValue(key, out var completion))
            {
                return true;
            }

            if (root.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : error.GetRawText();
                completion.TrySetException(new InvalidOperationException(
                    CodexMessageTranslator.Redact(message ?? "Codex request failed.")));
            }
            else
            {
                completion.TrySetResult(root.Clone());
            }
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ReadErrorAsync(StreamReader error, CancellationToken cancellationToken)
    {
        try
        {
            while (await error.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (_standardError.Length < 16_384)
                {
                    _standardError.AppendLine(CodexMessageTranslator.Redact(line));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal app shutdown.
        }
    }

    private async Task ObserveExitAsync(Process process)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (_disposed)
        {
            return;
        }

        var exited = _translator.TranslateExit(process.ExitCode, _standardError.ToString());
        _events.Writer.TryWrite(exited);
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(new InvalidOperationException(exited.Message));
        }
    }

    private static async Task<bool> IsLoggedInAsync(
        ResolvedWindowsCommand codexCommand,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo(
            codexCommand,
            useShellExecute: false,
            redirectStandardOutput: true,
            redirectStandardError: true,
            createNoWindow: true);
        startInfo.ArgumentList.Add("login");
        startInfo.ArgumentList.Add("status");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not check Codex login status.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var status = (await standardOutput.ConfigureAwait(false))
            + (await standardError.ConfigureAwait(false));
        return process.ExitCode == 0
            && status.Contains("Logged in", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LoginInteractivelyAsync(
        ResolvedWindowsCommand codexCommand,
        CancellationToken cancellationToken)
    {
        var startInfo = CreateProcessStartInfo(codexCommand, useShellExecute: true);
        startInfo.ArgumentList.Add("login");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not open Codex login.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Codex login was cancelled or failed.");
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        ResolvedWindowsCommand command,
        bool useShellExecute,
        bool redirectStandardInput = false,
        bool redirectStandardOutput = false,
        bool redirectStandardError = false,
        bool createNoWindow = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            UseShellExecute = useShellExecute,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = redirectStandardOutput,
            RedirectStandardError = redirectStandardError,
            CreateNoWindow = createNoWindow,
        };
        foreach (var prefixArgument in command.PrefixArguments)
        {
            startInfo.ArgumentList.Add(prefixArgument);
        }
        return startInfo;
    }

    private (string ThreadId, string TurnId) ActiveTurn() =>
        (_threadId ?? throw new InvalidOperationException("No active Codex thread."),
         _turnId ?? throw new InvalidOperationException("No active Codex turn."));

    private static string ReadNestedString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
            {
                return string.Empty;
            }
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetimeCancellation.Cancel();
        _events.Writer.TryComplete();
        try
        {
            _input?.Close();
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
            if (_process is not null)
            {
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _process?.Dispose();
            _lifetimeCancellation.Dispose();
            _writeGate.Dispose();
        }
    }
}
