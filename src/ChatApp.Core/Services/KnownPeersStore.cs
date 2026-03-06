using ChatApp.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Services;

/// <inheritdoc cref="IKnownPeersStore"/>
public sealed class KnownPeersStore : IKnownPeersStore
{
    private readonly LocalDb _db;

    public KnownPeersStore(LocalDb db) => _db = db;

    public async Task UpsertAsync(KnownPeerEntry peer)
    {
        var existing = await _db.KnownPeers.FindAsync(peer.UserName);
        if (existing is null)
        {
            _db.KnownPeers.Add(new KnownPeerRow
            {
                UserName    = peer.UserName,
                DisplayName = peer.DisplayName,
                IpAddress   = peer.IpAddress,
                GrpcPort    = peer.GrpcPort,
                LastSeenUtc = peer.LastSeenUtc
            });
        }
        else
        {
            existing.DisplayName = peer.DisplayName;
            existing.IpAddress   = peer.IpAddress;
            existing.GrpcPort    = peer.GrpcPort;
            existing.LastSeenUtc = peer.LastSeenUtc;
        }
        await _db.SaveChangesAsync();
    }

    public async Task<List<KnownPeerEntry>> GetAllAsync()
    {
        var rows = await _db.KnownPeers.ToListAsync();
        return rows.Select(r => new KnownPeerEntry(
            r.UserName, r.DisplayName, r.IpAddress, r.GrpcPort, r.LastSeenUtc)).ToList();
    }

    public async Task RemoveAsync(string userName)
    {
        var row = await _db.KnownPeers.FindAsync(userName);
        if (row is not null)
        {
            _db.KnownPeers.Remove(row);
            await _db.SaveChangesAsync();
        }
    }
}
