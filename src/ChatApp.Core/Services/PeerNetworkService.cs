using ChatApp.Core.Interfaces;
using ChatApp.Core.Models;
using ChatApp.Shared.Grpc;
using ChatApp.Shared.Models;

namespace ChatApp.Core.Services;

/// <summary>
/// Manages all outbound gRPC peer connections.
///
/// Decentralised gossip flow
/// ─────────────────────────
///  1. A new peer is discovered (UDP) or added manually.
///  2. We Ping the peer to confirm reachability.
///  3. We persist them in <see cref="IKnownPeersStore"/>.
///  4. We call GetKnownPeers on them (one-hop gossip).
///  5. For each peer they return that we don't yet know, we attempt to
///     Ping + persist them too — without further gossip (depth limit = 1).
///
/// This creates a self-healing mesh: every node learns about every other
/// node after at most one introduction, with no central coordinator.
/// </summary>
public sealed class PeerNetworkService : IPeerNetworkService
{
    private readonly IPeerChannelFactory _factory;
    private readonly IKnownPeersStore    _store;

    // Set by the ViewModel after login
    public string LocalUserName { get; set; } = string.Empty;

    public event EventHandler<PeerUser>? PeerConnected;

    public PeerNetworkService(IPeerChannelFactory factory, IKnownPeersStore store)
    {
        _factory = factory;
        _store   = store;
    }

    // ── Message delivery ─────────────────────────────────────────────────

    public async Task<bool> SendMessageAsync(PeerUser peer, ChatMessageProto message,
        CancellationToken ct = default)
    {
        var ch = _factory.Get(peer.IpAddress, peer.GrpcPort);
        return await ch.SendMessageAsync(message, ct);
    }

    public async Task<PingResponse?> PingAsync(string host, int port, string fromUser,
        CancellationToken ct = default)
    {
        var ch = _factory.Get(host, port);
        return await ch.PingAsync(fromUser, ct);
    }

    public async Task SendTypingAsync(PeerUser peer, string fromUser, bool isTyping,
        CancellationToken ct = default)
    {
        var ch = _factory.Get(peer.IpAddress, peer.GrpcPort);
        await ch.SendTypingAsync(fromUser, peer.UserName, isTyping, ct);
    }

    // ── Decentralised peer exchange ───────────────────────────────────────

    /// <summary>
    /// Ping + persist a peer, then gossip one hop to discover their network.
    /// </summary>
    public async Task ConnectAndExchangeAsync(string host, int port,
        CancellationToken ct = default)
    {
        var ch   = _factory.Get(host, port);
        var ping = await ch.PingAsync(LocalUserName, ct);
        if (ping is null) return;

        var peer = new PeerUser
        {
            UserName    = ping.UserName,
            DisplayName = ping.DisplayName,
            IpAddress   = host,
            GrpcPort    = port,
            Status      = UserStatus.Online
        };

        // Persist so we can reconnect next launch
        await _store.UpsertAsync(new KnownPeerEntry(
            ping.UserName, ping.DisplayName, host, port, DateTime.UtcNow));

        PeerConnected?.Invoke(this, peer);

        // One-hop gossip: ask the peer who they know
        var gossip = await ch.GetKnownPeersAsync(LocalUserName, ct);
        if (gossip is null) return;

        foreach (var info in gossip.Peers)
        {
            if (info.UserName == LocalUserName) continue;

            // Attempt to connect to each peer they know (depth = 1 only)
            _ = Task.Run(async () =>
            {
                var theirCh   = _factory.Get(info.IpAddress, info.GrpcPort);
                var theirPing = await theirCh.PingAsync(LocalUserName, ct);
                if (theirPing is null) return;

                await _store.UpsertAsync(new KnownPeerEntry(
                    theirPing.UserName, theirPing.DisplayName,
                    info.IpAddress, info.GrpcPort, DateTime.UtcNow));

                PeerConnected?.Invoke(this, new PeerUser
                {
                    UserName    = theirPing.UserName,
                    DisplayName = theirPing.DisplayName,
                    IpAddress   = info.IpAddress,
                    GrpcPort    = info.GrpcPort,
                    Status      = UserStatus.Online
                });
            }, ct);
        }
    }

    public void Dispose() => (_factory as IDisposable)?.Dispose();
}
