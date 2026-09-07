using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Workspace.Host.Persistence;

namespace Workspace.Host.Protocol;

public sealed class WorkspaceProtocolServer(
    CommandDispatcher dispatcher,
    AtomicWorkspaceStore workspaceStore)
{
    private const int ReceiveBufferSize = 16 * 1024;
    private const int MaximumMessageSize = 1024 * 1024;

    public async Task RunConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(socket);
        var snapshotSent = false;

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var json = await ReceiveMessageAsync(socket, cancellationToken);
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
                await SendAsync(
                    socket,
                    ProtocolEnvelope.Error(
                        TryReadCorrelationId(json),
                        "unsupported_protocol",
                        exception.Message),
                    cancellationToken);
                await socket.CloseAsync(
                    WebSocketCloseStatus.PolicyViolation,
                    exception.Message,
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
