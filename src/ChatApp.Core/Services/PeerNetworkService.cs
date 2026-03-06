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
///  6. Any offline-queued messages for that peer are drained.
///
/// This creates a self-healing mesh: every node learns about every other
/// node after at most one introduction, with no central coordinator.
/// </summary>
public sealed class PeerNetworkService : IPeerNetworkService
{
    private readonly IPeerChannelFactory  _factory;
    private readonly IKnownPeersStore     _store;
    private readonly IOfflineQueueService _offlineQueue;

    // Set by the ViewModel after login
    public string LocalUserName { get; set; } = string.Empty;

    public event EventHandler<PeerUser>?                     PeerConnected;
    public event EventHandler<(string ToUser, int Count)>?   PendingMessagesDelivered;

    public PeerNetworkService(
        IPeerChannelFactory  factory,
        IKnownPeersStore     store,
        IOfflineQueueService offlineQueue)
    {
        _factory      = factory;
        _store        = store;
        _offlineQueue = offlineQueue;
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
    /// Ping + persist a peer, drain the offline queue, then gossip one hop.
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

        // ── Drain offline queue ───────────────────────────────────────────
        await DrainOfflineQueueAsync(peer, ch, ct);

        // ── One-hop gossip ────────────────────────────────────────────────
        var gossip = await ch.GetKnownPeersAsync(LocalUserName, ct);
        if (gossip is null) return;

        foreach (var info in gossip.Peers)
        {
            if (info.UserName == LocalUserName) continue;

            _ = Task.Run(async () =>
            {
                var theirCh   = _factory.Get(info.IpAddress, info.GrpcPort);
                var theirPing = await theirCh.PingAsync(LocalUserName, ct);
                if (theirPing is null) return;

                await _store.UpsertAsync(new KnownPeerEntry(
                    theirPing.UserName, theirPing.DisplayName,
                    info.IpAddress, info.GrpcPort, DateTime.UtcNow));

                var theirPeer = new PeerUser
                {
                    UserName    = theirPing.UserName,
                    DisplayName = theirPing.DisplayName,
                    IpAddress   = info.IpAddress,
                    GrpcPort    = info.GrpcPort,
                    Status      = UserStatus.Online
                };
                PeerConnected?.Invoke(this, theirPeer);

                await DrainOfflineQueueAsync(theirPeer, theirCh, ct);
            }, ct);
        }
    }

    // ── Offline queue drain ───────────────────────────────────────────────

    private async Task DrainOfflineQueueAsync(PeerUser peer, IPeerChannel ch,
        CancellationToken ct)
    {
        var pending = await _offlineQueue.GetPendingWithIdsAsync(peer.UserName);
        if (pending.Count == 0) return;

        var delivered = 0;
        foreach (var (rowId, msg) in pending)
        {
            var ok = await ch.SendMessageAsync(msg, ct);
            if (ok)
            {
                await _offlineQueue.RemoveAsync(rowId);
                delivered++;
            }
        }

        if (delivered > 0)
            PendingMessagesDelivered?.Invoke(this, (peer.UserName, delivered));
    }

    public void Dispose() => (_factory as IDisposable)?.Dispose();
}
