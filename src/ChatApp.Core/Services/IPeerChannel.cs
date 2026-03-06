using ChatApp.Shared.Grpc;

namespace ChatApp.Core.Services;

/// <summary>
/// Abstracts a single outbound gRPC connection to one remote peer.
/// The interface makes <see cref="PeerNetworkService"/> fully unit-testable.
/// </summary>
public interface IPeerChannel
{
    Task<bool>             SendMessageAsync(ChatMessageProto message,      CancellationToken ct = default);
    Task<PingResponse?>    PingAsync(string fromUser,                      CancellationToken ct = default);
    Task<GetKnownPeersResponse?> GetKnownPeersAsync(string requestingUser, CancellationToken ct = default);
    Task                   SendTypingAsync(string fromUser, string toUser, bool isTyping, CancellationToken ct = default);
}

/// <summary>Creates <see cref="IPeerChannel"/> instances (injectable / mockable).</summary>
public interface IPeerChannelFactory
{
    IPeerChannel Get(string host, int port);
    void         Remove(string host, int port);
}
