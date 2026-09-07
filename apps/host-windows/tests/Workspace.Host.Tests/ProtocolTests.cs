using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;

namespace Workspace.Host.Tests;

public sealed class ProtocolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"workspace-protocol-tests-{Guid.NewGuid():N}");

    public ProtocolTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void UnsupportedProtocolVersionFailsExplicitly()
    {
        var exception = Assert.Throws<UnsupportedProtocolVersionException>(() =>
            ProtocolEnvelope.Parse("""{"protocol":99,"type":"command","id":"bad-version","operation":"application.list"}"""));

        Assert.Equal(99, exception.ProtocolVersion);
        Assert.Contains("protocol", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://example.com", false)]
    [InlineData("null", false)]
    public void BrowserOriginsMustResolveToLoopback(string? origin, bool expected)
    {
        Assert.Equal(expected, LoopbackOriginPolicy.IsTrusted(origin));
    }

    [Fact]
    public async Task EveryCommandGetsOneCorrelatedResultOrError()
    {
        var dispatcher = CreateDispatcher();
        var command = ProtocolEnvelope.Command("command-7", "not.supported");

        var outcome = await dispatcher.DispatchAsync(command, CancellationToken.None);

        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("command-7", outcome.Response.Id);
        Assert.Equal("unsupported_operation", outcome.Response.Code);
        Assert.Empty(outcome.Events);
    }

    [Fact]
    public async Task ApplicationListReturnsCatalogEntriesWithoutRuntimeProcessIdentity()
    {
        var dispatcher = CreateDispatcher();

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("list-1", "application.list"),
            CancellationToken.None);

        Assert.Equal("result", outcome.Response.Type);
        Assert.Equal("list-1", outcome.Response.Id);
        Assert.True(outcome.Response.Success);
        var application = Assert.Single(outcome.Response.Payload!.Value.EnumerateArray());
        Assert.Equal("pc.application:notepad", application.GetProperty("id").GetString());
        Assert.DoesNotContain("process", outcome.Response.Payload.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplicationLaunchReturnsDurableIdentityWithoutRuntimeProcessIdentity()
    {
        var dispatcher = CreateDispatcher();

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("launch-1", "application.launch", "Notepad"),
            CancellationToken.None);

        Assert.Equal("result", outcome.Response.Type);
        Assert.Equal("launch-1", outcome.Response.Id);
        Assert.Equal("pc.application:notepad", outcome.Response.Payload!.Value.GetProperty("applicationId").GetString());
        Assert.DoesNotContain("process", outcome.Response.Payload.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("APPLICATION_LAUNCHED", Assert.Single(outcome.Events).Event);
    }

    [Fact]
    public async Task MissingApplicationReturnsCorrelatedError()
    {
        var dispatcher = CreateDispatcher();

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("launch-missing", "application.launch", "Missing App"),
            CancellationToken.None);

        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("launch-missing", outcome.Response.Id);
        Assert.Equal("application_not_found", outcome.Response.Code);
        Assert.Empty(outcome.Events);
    }

    [Fact]
    public async Task PresentationIsDurableBeforeUpdateEventIsReturned()
    {
        var store = CreateStore();
        var original = WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad");
        await store.SaveAsync(new WorkspaceDocument(1, [original]), CancellationToken.None);
        var dispatcher = CreateDispatcher(store);
        var presentation = PresentationState.Default with { Position = new Vec3(2, 1, -3) };

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "move-1",
                "entity.setPresentation",
                original.Id,
                JsonSerializer.SerializeToElement(presentation)),
            CancellationToken.None);

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(presentation, Assert.Single(persisted.Entities).Presentation);
        Assert.Equal("result", outcome.Response.Type);
        var update = Assert.Single(outcome.Events);
        Assert.Equal("PRESENTATION_UPDATED", update.Event);
        Assert.Equal(original.Id, update.Payload!.Value.GetProperty("entityId").GetString());
    }

    [Fact]
    public async Task ConcurrentPresentationMutationsAreSerialized()
    {
        var original = WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad");
        var store = new ConcurrentObservationWorkspaceStore(
            new WorkspaceDocument(1, [original]));
        var dispatcher = CreateDispatcher(store);

        await Task.WhenAll(
            dispatcher.DispatchAsync(
                ProtocolEnvelope.Command(
                    "move-a",
                    "entity.setPresentation",
                    original.Id,
                    JsonSerializer.SerializeToElement(
                        PresentationState.Default with { Position = new Vec3(1, 0, 0) })),
                CancellationToken.None),
            dispatcher.DispatchAsync(
                ProtocolEnvelope.Command(
                    "move-b",
                    "entity.setPresentation",
                    original.Id,
                    JsonSerializer.SerializeToElement(
                        PresentationState.Default with { Position = new Vec3(2, 0, 0) })),
                CancellationToken.None));

        Assert.Equal(1, store.MaximumConcurrentTransactions);
    }

    [Fact]
    public async Task WindowFocusUsesSemanticEntityIdentity()
    {
        var focus = new RecordingWindowFocusService();
        var dispatcher = CreateDispatcher(windowFocus: focus);

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("focus-1", "window.focus", "pc.window:pc.application:notepad"),
            CancellationToken.None);

        Assert.Equal("result", outcome.Response.Type);
        Assert.Equal("pc.window:pc.application:notepad", focus.LastEntityId);
    }

    [Fact]
    public async Task ServerSendsSnapshotOnlyAfterSupportedVersionIsReceived()
    {
        var store = CreateStore();
        await store.SaveAsync(
            new WorkspaceDocument(1, [WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad")]),
            CancellationToken.None);
        var server = new WorkspaceProtocolServer(CreateDispatcher(store), store);
        var command = JsonSerializer.Serialize(
            ProtocolEnvelope.Command("list-through-server", "application.list"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var socket = new ScriptedWebSocket(command);

        await server.RunConnectionAsync(socket, CancellationToken.None);

        Assert.Collection(
            socket.SentMessages,
            snapshot => Assert.Equal("snapshot", JsonDocument.Parse(snapshot).RootElement.GetProperty("type").GetString()),
            result =>
            {
                var envelope = JsonDocument.Parse(result).RootElement;
                Assert.Equal("result", envelope.GetProperty("type").GetString());
                Assert.Equal("list-through-server", envelope.GetProperty("id").GetString());
            });
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    [Fact]
    public async Task ServerRejectsUnsupportedVersionWithoutSendingSnapshot()
    {
        var store = CreateStore();
        var server = new WorkspaceProtocolServer(CreateDispatcher(store), store);
        using var socket = new ScriptedWebSocket(
            """{"protocol":99,"type":"command","id":"bad-version","operation":"application.list"}""");

        await server.RunConnectionAsync(socket, CancellationToken.None);

        var message = Assert.Single(socket.SentMessages);
        var envelope = JsonDocument.Parse(message).RootElement;
        Assert.Equal("error", envelope.GetProperty("type").GetString());
        Assert.Equal("bad-version", envelope.GetProperty("id").GetString());
        Assert.Equal("unsupported_protocol", envelope.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ServerRejectsBinaryMessagesWithProtocolErrorAndCleanClose()
    {
        var store = CreateStore();
        var server = new WorkspaceProtocolServer(CreateDispatcher(store), store);
        using var socket = new ScriptedWebSocket("not-json", WebSocketMessageType.Binary);

        await server.RunConnectionAsync(socket, CancellationToken.None);

        var message = Assert.Single(socket.SentMessages);
        var envelope = JsonDocument.Parse(message).RootElement;
        Assert.Equal("error", envelope.GetProperty("type").GetString());
        Assert.Equal("invalid_message", envelope.GetProperty("code").GetString());
        Assert.Equal(WebSocketState.Closed, socket.State);
    }

    [Fact]
    public async Task ServerExplicitlyRejectsASecondConcurrentClient()
    {
        var store = CreateStore();
        var server = new WorkspaceProtocolServer(CreateDispatcher(store), store);
        var command = JsonSerializer.Serialize(
            ProtocolEnvelope.Command("first-client", "application.list"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var first = new ScriptedWebSocket(
            command,
            WebSocketMessageType.Text,
            blockAfterMessage: true);
        var firstConnection = server.RunConnectionAsync(first, CancellationToken.None);
        await first.WaitingForNextReceive;

        using var second = new ScriptedWebSocket(command);
        await server.RunConnectionAsync(second, CancellationToken.None);

        var rejection = Assert.Single(second.SentMessages);
        var envelope = JsonDocument.Parse(rejection).RootElement;
        Assert.Equal("connection_in_use", envelope.GetProperty("code").GetString());
        Assert.Equal(WebSocketState.Closed, second.State);

        first.ReleaseReceive();
        await firstConnection;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private AtomicWorkspaceStore CreateStore() =>
        new(Path.Combine(_tempDir, "workspace.json"));

    private CommandDispatcher CreateDispatcher(
        IWorkspaceStore? store = null,
        IWindowFocusService? windowFocus = null)
    {
        var catalog = new InMemoryApplicationCatalog(
        [
            new ApplicationDescriptor("pc.application:notepad", "Notepad", @"C:\Windows\notepad.exe", null),
        ]);

        return new CommandDispatcher(
            catalog,
            new ApplicationLauncher(new FixedProcessLauncher(4242)),
            store ?? CreateStore(),
            windowFocus ?? new RecordingWindowFocusService());
    }

    private sealed class FixedProcessLauncher(int processId) : IProcessLauncher
    {
        public Task<int> LaunchAsync(string executablePath, string? arguments, CancellationToken cancellationToken) =>
            Task.FromResult(processId);
    }

    private sealed class RecordingWindowFocusService : IWindowFocusService
    {
        public string? LastEntityId { get; private set; }

        public Task FocusAsync(string entityId, CancellationToken cancellationToken)
        {
            LastEntityId = entityId;
            return Task.CompletedTask;
        }
    }

    private sealed class ConcurrentObservationWorkspaceStore(WorkspaceDocument document) : IWorkspaceStore
    {
        private WorkspaceDocument _document = document;
        private int _activeTransactions;
        private int _maximumConcurrentTransactions;

        public int MaximumConcurrentTransactions => _maximumConcurrentTransactions;

        public async Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeTransactions);
            InterlockedExtensions.Max(ref _maximumConcurrentTransactions, active);
            await Task.Delay(25, cancellationToken);

            lock (this)
            {
                return new WorkspaceDocument(_document.SchemaVersion, [.. _document.Entities]);
            }
        }

        public async Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(25, cancellationToken);
                lock (this)
                {
                    _document = document;
                }
            }
            finally
            {
                Interlocked.Decrement(ref _activeTransactions);
            }
        }
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class ScriptedWebSocket(
        string message,
        WebSocketMessageType messageType = WebSocketMessageType.Text,
        bool blockAfterMessage = false) : WebSocket
    {
        private readonly byte[] _message = Encoding.UTF8.GetBytes(message);
        private bool _messageReceived;
        private WebSocketState _state = WebSocketState.Open;
        private WebSocketCloseStatus? _closeStatus;
        private string? _closeStatusDescription;
        private readonly TaskCompletionSource _waitingForNextReceive = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseReceive = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> SentMessages { get; } = [];

        public Task WaitingForNextReceive => _waitingForNextReceive.Task;

        public void ReleaseReceive() => _releaseReceive.TrySetResult();

        public override WebSocketCloseStatus? CloseStatus => _closeStatus;

        public override string? CloseStatusDescription => _closeStatusDescription;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken) =>
            CloseAsync(closeStatus, statusDescription, cancellationToken);

        public override void Dispose() => _state = WebSocketState.Closed;

        public override async Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_messageReceived)
            {
                _waitingForNextReceive.TrySetResult();
                if (blockAfterMessage)
                {
                    await _releaseReceive.Task.WaitAsync(cancellationToken);
                }

                return new WebSocketReceiveResult(
                    0,
                    WebSocketMessageType.Close,
                    endOfMessage: true);
            }

            _messageReceived = true;
            _message.AsSpan().CopyTo(buffer.AsSpan());
            return new WebSocketReceiveResult(
                _message.Length,
                messageType,
                endOfMessage: true);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SentMessages.Add(Encoding.UTF8.GetString(buffer));
            return Task.CompletedTask;
        }
    }
}
