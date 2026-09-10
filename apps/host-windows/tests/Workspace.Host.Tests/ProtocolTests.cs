using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Workspace.Host.Applications;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Protocol;
using Workspace.Host.Windows;

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

    [Fact]
    public void Application_open_payload_uses_literal_json_and_rejects_unexpected_fields()
    {
        var accepted = ApplicationControlRequestParser.ParseOpen(
            JsonDocument.Parse("""
            {"applicationId":"pc.application:notepad","launchPolicy":"reuseOrLaunch","surfaceEntityId":"spatial.surface:right","replaceOccupied":false}
            """).RootElement);

        Assert.Equal("pc.application:notepad", accepted.ApplicationId);
        Assert.Equal(ApplicationLaunchPolicy.ReuseOrLaunch, accepted.LaunchPolicy);

        Assert.Throws<JsonException>(() => ApplicationControlRequestParser.ParseOpen(
            JsonDocument.Parse("""
            {"applicationId":"pc.application:notepad","arbitraryCommand":"cmd.exe /c whoami"}
            """).RootElement));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://workspace.local", true)]
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
    public async Task InvalidPresentationIsRejectedWithoutChangingDurableState()
    {
        var store = CreateStore();
        var original = WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad");
        await store.SaveAsync(new WorkspaceDocument(1, [original]), CancellationToken.None);
        var dispatcher = CreateDispatcher(store);
        var invalid = PresentationState.Default with { Size = new Vec3(0, 1, 1) };

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "invalid-presentation",
                "entity.setPresentation",
                original.Id,
                JsonSerializer.SerializeToElement(invalid)),
            CancellationToken.None);

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("invalid_payload", outcome.Response.Code);
        Assert.Equal(original.Presentation, Assert.Single(persisted.Entities).Presentation);
        Assert.Empty(outcome.Events);
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
    public async Task WindowFocusBlockedByWindowsReturnsExplicitProtocolError()
    {
        var dispatcher = CreateDispatcher(windowFocus: new RejectedWindowFocusService());

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("focus-blocked", "window.focus", "pc.window:pc.application:notepad"),
            CancellationToken.None);

        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("INPUT_TARGET_NOT_PERMITTED", outcome.Response.Code);
    }

    [Fact]
    public async Task LaunchCreatesDurableWindowEntityWithoutPersistingRuntimeStreamId()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler);

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("launch-window", "application.launch", "Notepad"),
            CancellationToken.None);

        var created = Assert.Single(outcome.Events, envelope =>
            envelope.Event == "ENTITY_CREATED"
            && envelope.Payload!.Value.GetProperty("id").GetString()
                == "pc.window:pc.application:notepad");
        Assert.Equal(EntityKinds.Window, created.Payload!.Value.GetProperty("kind").GetString());
        Assert.Equal(
            "pc.window:pc.application:notepad",
            created.Payload!.Value.GetProperty("id").GetString());

        var persisted = await store.LoadAsync(CancellationToken.None);
        Assert.Contains(persisted.Entities, entity => entity.Id == "pc.application:notepad");
        Assert.Contains(persisted.Entities, entity => entity.Id == "pc.window:pc.application:notepad");
        var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("surface-runtime", json, StringComparison.Ordinal);
        Assert.DoesNotContain("424242", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SurfaceCommandsReturnFramesThroughTransientStreamIdentity()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler);
        await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("launch-window", "application.launch", "Notepad"),
            CancellationToken.None);

        var opened = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "open-surface",
                "surface.open",
                "pc.window:pc.application:notepad"),
            CancellationToken.None);
        var streamId = opened.Response.Payload!.Value.GetProperty("streamId").GetString();
        Assert.Equal("surface-runtime-1", streamId);

        var framed = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "read-frame",
                "surface.frame",
                streamId,
                JsonSerializer.SerializeToElement(new { afterSequence = -1 })),
            CancellationToken.None);
        var framePayload = framed.Response.Payload!.Value;
        Assert.True(framePayload.GetProperty("available").GetBoolean());
        Assert.Equal(
            Convert.ToBase64String([1, 2, 3, 4]),
            framePayload.GetProperty("frame").GetProperty("dataBase64").GetString());

        await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command("close-surface", "surface.close", streamId),
            CancellationToken.None);
        Assert.Empty(capture.ActiveStreamIds);
    }

    [Fact]
    public async Task WindowInputResolvesTheCurrentRuntimeWindowBySemanticIdentity()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        var input = new RecordingInputRouter();
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler, input);

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "input-window",
                "window.input",
                "pc.window:pc.application:notepad",
                JsonSerializer.SerializeToElement(new
                {
                    kind = "pointer",
                    phase = "down",
                    x = 0.5,
                    y = 0.5,
                    button = "primary",
                })),
            CancellationToken.None);

        Assert.Equal("result", outcome.Response.Type);
        Assert.Equal((nint)424242, input.LastWindow?.Hwnd);
        Assert.Equal(new WindowBounds(10, 20, 800, 600), input.LastWindow?.Bounds);
        Assert.Equal("pointer", input.LastIntent?.Kind);
        Assert.Equal(0.5, input.LastIntent?.X);
        Assert.Equal(0.5, input.LastIntent?.Y);
    }

    [Fact]
    public async Task InputBlockedByWindowsIntegrityReturnsExplicitProtocolError()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        var input = new RecordingInputRouter(reject: true);
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler, input);

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "input-blocked",
                "window.input",
                "pc.window:pc.application:notepad",
                JsonSerializer.SerializeToElement(new
                {
                    kind = "text",
                    text = "blocked",
                })),
            CancellationToken.None);

        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("INPUT_TARGET_NOT_PERMITTED", outcome.Response.Code);
    }

    [Fact]
    public async Task WindowInputRejectsOversizedTextBeforeNativeDispatch()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        var input = new RecordingInputRouter();
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler, input);

        var outcome = await dispatcher.DispatchAsync(
            ProtocolEnvelope.Command(
                "input-too-large",
                "window.input",
                "pc.window:pc.application:notepad",
                JsonSerializer.SerializeToElement(new
                {
                    kind = "text",
                    text = new string('x', WindowInputLimits.MaximumTextLength + 1),
                })),
            CancellationToken.None);

        Assert.Equal("error", outcome.Response.Type);
        Assert.Equal("invalid_payload", outcome.Response.Code);
        Assert.Null(input.LastIntent);
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
    public async Task ServerReleasesHeldInputWhenTheSpatialClientDisconnects()
    {
        var store = CreateStore();
        var capture = new FrameWindowCapture();
        var input = new RecordingInputRouter();
        await using var reconciler = new WindowReconciler(capture);
        var dispatcher = CreateSurfaceDispatcher(store, reconciler, input);
        var server = new WorkspaceProtocolServer(dispatcher, store);
        var command = JsonSerializer.Serialize(
            ProtocolEnvelope.Command("list-before-close", "application.list"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var socket = new ScriptedWebSocket(command);

        await server.RunConnectionAsync(socket, CancellationToken.None);

        Assert.Equal(1, input.ReleaseCalls);
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
        Assert.Equal(1, socket.CloseAsyncCalls);
        Assert.Equal(0, socket.CloseOutputAsyncCalls);
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

    private CommandDispatcher CreateSurfaceDispatcher(
        IWorkspaceStore store,
        WindowReconciler reconciler,
        IInputRouter? inputRouter = null)
    {
        var catalog = new InMemoryApplicationCatalog(
        [
            new ApplicationDescriptor("pc.application:notepad", "Notepad", @"C:\Windows\notepad.exe", null),
        ]);
        IWindowCatalog windows = new FixedWindowCatalog(
        [
            new WindowSnapshot(
                (nint)424242,
                4242,
                "Untitled - Notepad",
                new WindowBounds(10, 20, 800, 600),
                true,
                false,
                "pc.application:notepad"),
        ]);

        return new CommandDispatcher(
            catalog,
            new ApplicationLauncher(new FixedProcessLauncher(4242)),
            store,
            new RecordingWindowFocusService(),
            windows,
            reconciler,
            inputRouter);
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

    private sealed class RejectedWindowFocusService : IWindowFocusService
    {
        public Task FocusAsync(string entityId, CancellationToken cancellationToken) =>
            throw new InputTargetNotPermittedException("Windows rejected focus.");
    }

    private sealed class FixedWindowCatalog(IReadOnlyList<WindowSnapshot> windows) : IWindowCatalog
    {
        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(windows);
        }
    }

    private sealed class FrameWindowCapture : IWindowCapture
    {
        private readonly HashSet<string> _active = [];
        private int _nextStreamId;

        public IReadOnlyCollection<string> ActiveStreamIds => _active;

        public Task<SurfaceStreamHandle> StartAsync(nint hwnd, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = new SurfaceStreamHandle($"surface-runtime-{++_nextStreamId}", 800, 600);
            _active.Add(handle.StreamId);
            return Task.FromResult(handle);
        }

        public ValueTask<SurfaceFrame?> ReadLatestFrameAsync(
            string streamId,
            long afterSequence,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SurfaceFrame? frame = _active.Contains(streamId) && afterSequence < 1
                ? new SurfaceFrame(streamId, 1, 800, 600, "image/png", [1, 2, 3, 4])
                : null;
            return ValueTask.FromResult(frame);
        }

        public Task StopAsync(string streamId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _active.Remove(streamId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _active.Clear();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingInputRouter(bool reject = false) : IInputRouter
    {
        public WindowSnapshot? LastWindow { get; private set; }

        public WindowInputIntent? LastIntent { get; private set; }

        public int ReleaseCalls { get; private set; }

        public Task RouteAsync(
            WindowSnapshot window,
            WindowInputIntent intent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reject)
            {
                throw new InputTargetNotPermittedException("Blocked by Windows integrity level.");
            }
            LastWindow = window;
            LastIntent = intent;
            return Task.CompletedTask;
        }

        public Task ReleaseAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReleaseCalls++;
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

        public int CloseAsyncCalls { get; private set; }

        public int CloseOutputAsyncCalls { get; private set; }

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
            CloseAsyncCalls++;
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            CloseOutputAsyncCalls++;
            _closeStatus = closeStatus;
            _closeStatusDescription = statusDescription;
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

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
