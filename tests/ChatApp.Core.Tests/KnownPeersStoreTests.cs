using ChatApp.Core.Interfaces;
using ChatApp.Core.Services;

namespace ChatApp.Core.Tests;

public sealed class KnownPeersStoreTests : LocalDbTestBase
{
    private readonly KnownPeersStore _sut;

    public KnownPeersStoreTests() => _sut = new KnownPeersStore(Db);

    [Fact]
    public async Task Upsert_PersistsNewPeer()
    {
        var entry = new KnownPeerEntry("alice", "Alice", "192.168.1.1", 50051, DateTime.UtcNow);
        await _sut.UpsertAsync(entry);

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("alice",       all[0].UserName);
        Assert.Equal("Alice",       all[0].DisplayName);
        Assert.Equal("192.168.1.1", all[0].IpAddress);
        Assert.Equal(50051,         all[0].GrpcPort);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingPeer()
    {
        var original = new KnownPeerEntry("alice", "Alice", "192.168.1.1", 50051, DateTime.UtcNow);
        await _sut.UpsertAsync(original);

        var updated = new KnownPeerEntry("alice", "Alice W", "10.0.0.5", 9090, DateTime.UtcNow);
        await _sut.UpsertAsync(updated);

        var all = await _sut.GetAllAsync();
        Assert.Single(all);           // still only one row
        Assert.Equal("Alice W",  all[0].DisplayName);
        Assert.Equal("10.0.0.5", all[0].IpAddress);
        Assert.Equal(9090,        all[0].GrpcPort);
    }

    [Fact]
    public async Task GetAll_ReturnsAllPersistedPeers()
    {
        await _sut.UpsertAsync(new KnownPeerEntry("alice", "Alice", "10.0.0.1", 1, DateTime.UtcNow));
        await _sut.UpsertAsync(new KnownPeerEntry("bob",   "Bob",   "10.0.0.2", 2, DateTime.UtcNow));
        await _sut.UpsertAsync(new KnownPeerEntry("carol", "Carol", "10.0.0.3", 3, DateTime.UtcNow));

        var all = await _sut.GetAllAsync();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task GetAll_ReturnsEmptyWhenNoPeers()
    {
        var all = await _sut.GetAllAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task Remove_DeletesPeer()
    {
        await _sut.UpsertAsync(new KnownPeerEntry("alice", "Alice", "10.0.0.1", 1, DateTime.UtcNow));
        await _sut.UpsertAsync(new KnownPeerEntry("bob",   "Bob",   "10.0.0.2", 2, DateTime.UtcNow));

        await _sut.RemoveAsync("alice");

        var all = await _sut.GetAllAsync();
        Assert.Single(all);
        Assert.Equal("bob", all[0].UserName);
    }

    [Fact]
    public async Task Remove_IsIdempotent_WhenUserDoesNotExist()
    {
        // Should not throw
        await _sut.RemoveAsync("ghost");
    }
}
