using ChatApp.Core.Models;

namespace ChatApp.Core.Interfaces;

/// <summary>
/// Persists known peers to a local SQLite store so the app can reconnect to
/// previously-seen peers on startup — even across subnets, without any server.
/// </summary>
public interface IKnownPeersStore
{
    Task                 UpsertAsync(KnownPeerEntry peer);
    Task<List<KnownPeerEntry>> GetAllAsync();
    Task                 RemoveAsync(string userName);
}

public sealed record KnownPeerEntry(
    string   UserName,
    string   DisplayName,
    string   IpAddress,
    int      GrpcPort,
    DateTime LastSeenUtc);
