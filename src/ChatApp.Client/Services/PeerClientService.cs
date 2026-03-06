using ChatApp.Shared.Grpc;
using Grpc.Net.Client;

namespace ChatApp.Client.Services;

/// <summary>
/// Manages outbound gRPC connections to other peers.
/// Each peer connection is keyed by the peer's endpoint (host:port).
/// </summary>
public class PeerClientService : IDisposable
{
    private readonly Dictionary<string, (GrpcChannel Channel, ChatService.ChatServiceClient Client)> _connections = [];
    private bool _disposed;

    public ChatService.ChatServiceClient GetOrCreateClient(string host, int port)
    {
        var key = $"{host}:{port}";
        if (!_connections.TryGetValue(key, out var conn))
        {
            var channel = GrpcChannel.ForAddress($"http://{host}:{port}", new GrpcChannelOptions
            {
                // Reuse HTTP/2 connection for speed
                HttpHandler = new HttpClientHandler()
            });
            var client = new ChatService.ChatServiceClient(channel);
            conn = (channel, client);
            _connections[key] = conn;
        }
        return conn.Client;
    }

    /// <summary>Send a message directly to a peer via gRPC</summary>
    public async Task<bool> SendMessageAsync(
        string host, int port, ChatMessageProto message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(host, port);
            var response = await client.SendMessageAsync(
                new SendMessageRequest { Message = message },
                cancellationToken: cancellationToken);
            return response.Success;
        }
        catch
        {
            RemoveConnection(host, port);
            return false;
        }
    }

    /// <summary>Check if a peer is online (Ping RPC)</summary>
    public async Task<PingResponse?> PingAsync(
        string host, int port,
        string fromUser,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(host, port);
            return await client.PingAsync(
                new PingRequest { FromUser = fromUser },
                deadline: DateTime.UtcNow.AddSeconds(3),
                cancellationToken: cancellationToken);
        }
        catch
        {
            RemoveConnection(host, port);
            return null;
        }
    }

    /// <summary>Send typing indicator to a peer</summary>
    public async Task SendTypingAsync(
        string host, int port,
        string fromUser, string toUser, bool isTyping,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetOrCreateClient(host, port);
            await client.SendTypingIndicatorAsync(
                new TypingRequest { FromUser = fromUser, ToUser = toUser, IsTyping = isTyping },
                deadline: DateTime.UtcNow.AddSeconds(2),
                cancellationToken: cancellationToken);
        }
        catch { /* best-effort */ }
    }

    private void RemoveConnection(string host, int port)
    {
        var key = $"{host}:{port}";
        if (_connections.Remove(key, out var conn))
            conn.Channel.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var (_, conn) in _connections)
            conn.Channel.Dispose();
        _connections.Clear();
    }
}
