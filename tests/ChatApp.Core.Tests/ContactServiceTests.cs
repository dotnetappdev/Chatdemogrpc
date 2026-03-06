using ChatApp.Core.Interfaces;
using ChatApp.Core.Services;

namespace ChatApp.Core.Tests;

public sealed class ContactServiceTests : LocalDbTestBase
{
    private readonly ContactService _sut;

    public ContactServiceTests() => _sut = new ContactService(Db);

    // ── Upsert ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upsert_InsertsNewContact()
    {
        var entry = new ContactEntry("alice", "Alice", ContactStatus.Accepted,
            IsIncoming: false, DateTime.UtcNow);
        await _sut.UpsertAsync(entry);

        var got = await _sut.GetAsync("alice");
        Assert.NotNull(got);
        Assert.Equal(ContactStatus.Accepted, got.Status);
        Assert.False(got.IsIncoming);
    }

    [Fact]
    public async Task Upsert_UpdatesExistingContact()
    {
        var initial = new ContactEntry("alice", "Alice", ContactStatus.Pending,
            IsIncoming: true, DateTime.UtcNow);
        await _sut.UpsertAsync(initial);

        var updated = new ContactEntry("alice", "Alice W", ContactStatus.Accepted,
            IsIncoming: true, DateTime.UtcNow);
        await _sut.UpsertAsync(updated);

        var got = await _sut.GetAsync("alice");
        Assert.Equal(ContactStatus.Accepted, got!.Status);
        Assert.Equal("Alice W", got.DisplayName);
    }

    // ── GetByStatus ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByStatus_ReturnsOnlyMatchingContacts()
    {
        await _sut.UpsertAsync(new ContactEntry("alice", "Alice", ContactStatus.Accepted,  false, DateTime.UtcNow));
        await _sut.UpsertAsync(new ContactEntry("bob",   "Bob",   ContactStatus.Pending,   true,  DateTime.UtcNow));
        await _sut.UpsertAsync(new ContactEntry("carol", "Carol", ContactStatus.Blocked,   false, DateTime.UtcNow));

        var accepted = await _sut.GetByStatusAsync(ContactStatus.Accepted);
        Assert.Single(accepted);
        Assert.Equal("alice", accepted[0].UserName);
    }

    [Fact]
    public async Task GetByStatus_SupportsMultipleStatuses()
    {
        await _sut.UpsertAsync(new ContactEntry("alice", "Alice", ContactStatus.Accepted,  false, DateTime.UtcNow));
        await _sut.UpsertAsync(new ContactEntry("bob",   "Bob",   ContactStatus.Pending,   true,  DateTime.UtcNow));
        await _sut.UpsertAsync(new ContactEntry("carol", "Carol", ContactStatus.Blocked,   false, DateTime.UtcNow));

        var visible = await _sut.GetByStatusAsync(ContactStatus.Accepted, ContactStatus.Pending);
        Assert.Equal(2, visible.Count);
    }

    // ── IsBlocked ─────────────────────────────────────────────────────────

    [Fact]
    public async Task IsBlocked_ReturnsTrue_WhenUserIsBlocked()
    {
        await _sut.UpsertAsync(new ContactEntry("eve", "Eve", ContactStatus.Blocked, false, DateTime.UtcNow));
        Assert.True(await _sut.IsBlockedAsync("eve"));
    }

    [Fact]
    public async Task IsBlocked_ReturnsFalse_WhenUserIsNotBlocked()
    {
        await _sut.UpsertAsync(new ContactEntry("alice", "Alice", ContactStatus.Accepted, false, DateTime.UtcNow));
        Assert.False(await _sut.IsBlockedAsync("alice"));
    }

    [Fact]
    public async Task IsBlocked_ReturnsFalse_WhenUserNotFound()
    {
        Assert.False(await _sut.IsBlockedAsync("nobody"));
    }

    // ── Remove ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_DeletesContact()
    {
        await _sut.UpsertAsync(new ContactEntry("alice", "Alice", ContactStatus.Accepted, false, DateTime.UtcNow));
        await _sut.RemoveAsync("alice");

        Assert.Null(await _sut.GetAsync("alice"));
    }

    [Fact]
    public async Task Remove_IsIdempotent_WhenUserNotFound()
    {
        await _sut.RemoveAsync("ghost"); // should not throw
    }
}
