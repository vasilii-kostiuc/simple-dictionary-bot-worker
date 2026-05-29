using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace BotWorker.Worker.Infrastructure;

public class WsConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ClientWebSocket _socket = new();

    public WebSocketState State => _socket.State;

    public async Task ConnectAsync(Uri uri, CancellationToken ct)
    {
        await _socket.ConnectAsync(uri, ct);
    }

    public async Task SendAsync(WsMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct
        );
    }

    public async Task<WsMessage?> ReceiveAsync(CancellationToken ct)
    {
        var buffer = new List<byte>(4096);
        var chunk = new byte[4096];

        while (true)
        {
            var result = await _socket.ReceiveAsync(new ArraySegment<byte>(chunk), ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            buffer.AddRange(chunk[..result.Count]);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        return JsonSerializer.Deserialize<WsMessage>(json, JsonOptions);
    }

    public async Task CloseAsync(CancellationToken ct)
    {
        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _socket.Dispose();
        await ValueTask.CompletedTask;
    }
}
