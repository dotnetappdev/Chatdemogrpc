using ChatApp.Core.Models;
using ChatApp.Shared.Grpc;

namespace ChatApp.Core.Interfaces;

/// <summary>
/// Manages all outbound gRPC connections to remote peers.
/// Handles message delivery, liveness checks, typing indicators,
/// and the gossip peer-exchange that makes the network fully decentralised.
/// </summary>
public interface IPeerNetworkService : IDisposable
{
    /// <summary>Fired (on any thread) when a peer is confirmed reachable.</summary>
    event EventHandler<PeerUser>? PeerConnected;

    /// <summary>
    /// Fired when offline-queued messages are flushed to a peer that just came online.
    /// The int is the number of messages delivered.
    /// </summary>
    event EventHandler<(string ToUser, int Count)>? PendingMessagesDelivered;

    /// <summary>Send a chat message directly to a peer via gRPC.</summary>
    Task<bool> SendMessageAsync(PeerUser peer, ChatMessageProto message,
        CancellationToken ct = default);

    /// <summary>Confirm a peer is alive and get their identity.</summary>
    Task<PingResponse?> PingAsync(string host, int port, string fromUser,
        CancellationToken ct = default);

    /// <summary>Send a typing indicator to a peer.</summary>
    Task SendTypingAsync(PeerUser peer, string fromUser, bool isTyping,
        CancellationToken ct = default);

    /// <summary>
    /// Ping a peer, persist them, then exchange known-peers lists (gossip).
    /// Used both on UDP discovery and on manual peer add.
    /// </summary>
    Task ConnectAndExchangeAsync(string host, int port, CancellationToken ct = default);
}
