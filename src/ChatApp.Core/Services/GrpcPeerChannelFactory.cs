using System.Net.Http;
using ChatApp.Shared.Grpc;
using Grpc.Net.Client;

namespace ChatApp.Core.Services;

/// <summary>
/// Production implementation: wraps a real gRPC channel.
/// One channel is kept per host:port to reuse HTTP/2 connections.
/// </summary>
public sealed class GrpcPeerChannel : IPeerChannel, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly ChatService.ChatServiceClient _client;

    public GrpcPeerChannel(string host, int port)
    {
        _channel = GrpcChannel.ForAddress($"http://{host}:{port}",
            new GrpcChannelOptions { HttpHandler = new HttpClientHandler() });
        _client  = new ChatService.ChatServiceClient(_channel);
    }

    public async Task<bool> SendMessageAsync(ChatMessageProto message, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.SendMessageAsync(
                new SendMessageRequest { Message = message }, cancellationToken: ct);
            return resp.Success;
        }
        catch { return false; }
    }

    public async Task<PingResponse?> PingAsync(string fromUser, CancellationToken ct = default)
    {
        try
        {
            return await _client.PingAsync(
                new PingRequest { FromUser = fromUser },
                deadline: DateTime.UtcNow.AddSeconds(3),
                cancellationToken: ct);
        }
        catch { return null; }
    }

    public async Task<GetKnownPeersResponse?> GetKnownPeersAsync(
        string requestingUser, CancellationToken ct = default)
    {
        try
        {
            return await _client.GetKnownPeersAsync(
                new GetKnownPeersRequest { RequestingUser = requestingUser },
                deadline: DateTime.UtcNow.AddSeconds(5),
                cancellationToken: ct);
        }
        catch { return null; }
    }

    public async Task SendTypingAsync(string fromUser, string toUser, bool isTyping,
        CancellationToken ct = default)
    {
        try
        {
            await _client.SendTypingIndicatorAsync(
                new TypingRequest { FromUser = fromUser, ToUser = toUser, IsTyping = isTyping },
                deadline: DateTime.UtcNow.AddSeconds(2),
                cancellationToken: ct);
        }
        catch { /* best-effort */ }
    }

    public void Dispose() => _channel.Dispose();
}

/// <summary>
/// Production factory: pools channels by host:port.
/// </summary>
public sealed class GrpcPeerChannelFactory : IPeerChannelFactory, IDisposable
{
    private readonly Dictionary<string, GrpcPeerChannel> _pool = [];
    private readonly object _lock = new();

    public IPeerChannel Get(string host, int port)
    {
        var key = $"{host}:{port}";
        lock (_lock)
        {
            if (!_pool.TryGetValue(key, out var ch))
            {
                ch = new GrpcPeerChannel(host, port);
                _pool[key] = ch;
            }
            return ch;
        }
    }

    public void Remove(string host, int port)
    {
        var key = $"{host}:{port}";
        lock (_lock)
        {
            if (_pool.Remove(key, out var ch))
                ch.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var ch in _pool.Values) ch.Dispose();
            _pool.Clear();
        }
    }
}
