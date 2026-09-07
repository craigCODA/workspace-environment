using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Workspace.Host.Persistence;

namespace Workspace.Host.Protocol;

public sealed class WorkspaceProtocolServer(
    CommandDispatcher dispatcher,
    IWorkspaceStore workspaceStore)
{
    private const int ReceiveBufferSize = 16 * 1024;
    private const int MaximumMessageSize = 1024 * 1024;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public async Task RunConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        if (!await _connectionGate.WaitAsync(0, cancellationToken))
        {
            await FailConnectionAsync(
                socket,
                ProtocolEnvelope.Error(
                    null,
                    "connection_in_use",
                    "Workspace Host V0 supports one spatial client at a time."),
                WebSocketCloseStatus.PolicyViolation,
                cancellationToken);
            return;
        }

        try
        {
            var snapshotSent = false;
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                string? json;
                try
                {
                    json = await ReceiveMessageAsync(socket, cancellationToken);
                }
                catch (InvalidProtocolEnvelopeException exception)
                {
                    await FailConnectionAsync(
                        socket,
                        ProtocolEnvelope.Error(null, "invalid_message", exception.Message),
                        WebSocketCloseStatus.InvalidMessageType,
                        cancellationToken);
                    return;
                }

                if (json is null)
                {
                    return;
                }

                ProtocolEnvelope command;
                try
                {
                    command = ProtocolEnvelope.Parse(json);
                }
                catch (UnsupportedProtocolVersionException exception)
                {
                    await FailConnectionAsync(
                        socket,
                        ProtocolEnvelope.Error(
                            TryReadCorrelationId(json),
                            "unsupported_protocol",
                            exception.Message),
                        WebSocketCloseStatus.PolicyViolation,
                        cancellationToken);
                    return;
                }
                catch (Exception exception) when (exception is JsonException or InvalidProtocolEnvelopeException)
                {
                    await SendAsync(
                        socket,
                        ProtocolEnvelope.Error(
                            TryReadCorrelationId(json),
                            "invalid_envelope",
                            exception.Message),
                        cancellationToken);
                    continue;
                }

                if (!snapshotSent)
                {
                    var document = await workspaceStore.LoadAsync(cancellationToken);
                    await SendAsync(
                        socket,
                        ProtocolEnvelope.Snapshot(document.Entities),
                        cancellationToken);
                    snapshotSent = true;
                }

                var outcome = await dispatcher.DispatchAsync(command, cancellationToken);
                await SendAsync(socket, outcome.Response, cancellationToken);
                foreach (var eventEnvelope in outcome.Events)
                {
                    await SendAsync(socket, eventEnvelope, cancellationToken);
                }
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private static async Task<string?> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(rented, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(
                        result.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        result.CloseStatusDescription,
                        cancellationToken);
                    return null;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidProtocolEnvelopeException("Workspace protocol accepts text messages only.");
                }

                message.Write(rented, 0, result.Count);
                if (message.Length > MaximumMessageSize)
                {
                    throw new InvalidProtocolEnvelopeException("Workspace protocol message exceeds the size limit.");
                }

                if (result.EndOfMessage)
                {
                    return Encoding.UTF8.GetString(message.GetBuffer(), 0, checked((int)message.Length));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        ProtocolEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope, ProtocolEnvelope.SerializerOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private static async Task FailConnectionAsync(
        WebSocket socket,
        ProtocolEnvelope error,
        WebSocketCloseStatus closeStatus,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        await SendAsync(socket, error, cancellationToken);
        await socket.CloseOutputAsync(closeStatus, error.Message, cancellationToken);
    }

    private static string? TryReadCorrelationId(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
