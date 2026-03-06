using ChatApp.Core.Services;
using ChatApp.Shared.Grpc;

namespace ChatApp.Core.Tests;

public sealed class OfflineQueueServiceTests : LocalDbTestBase
{
    private readonly OfflineQueueService _sut;

    public OfflineQueueServiceTests() => _sut = new OfflineQueueService(Db);

    private static ChatMessageProto MakeMessage(string from, string to,
        string content = "hello", MessageType type = MessageType.Text)
        => new()
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = from,
            ToUser    = to,
            Content   = content,
            Type      = type,
            Timestamp = DateTime.UtcNow.ToString("o")
        };

    // ── Enqueue ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_StoresMessage()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "Hi Bob"));

        var pending = await _sut.GetPendingForUserAsync("bob");
        Assert.Single(pending);
        Assert.Equal("Hi Bob", pending[0].Content);
    }

    [Fact]
    public async Task Enqueue_SupportsMultipleMessagesForSameRecipient()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "msg 1"));
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "msg 2"));
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "msg 3"));

        var pending = await _sut.GetPendingForUserAsync("bob");
        Assert.Equal(3, pending.Count);
    }

    // ── GetPendingForUser ─────────────────────────────────────────────────

    [Fact]
    public async Task GetPending_ReturnsOldestFirst()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "first"));
        await Task.Delay(5);
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "second"));

        var pending = await _sut.GetPendingForUserAsync("bob");
        Assert.Equal("first",  pending[0].Content);
        Assert.Equal("second", pending[1].Content);
    }

    [Fact]
    public async Task GetPending_ReturnsEmpty_WhenNoMessages()
    {
        var pending = await _sut.GetPendingForUserAsync("ghost");
        Assert.Empty(pending);
    }

    [Fact]
    public async Task GetPending_OnlyReturnsMessagesForCorrectRecipient()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob",   "for bob"));
        await _sut.EnqueueAsync(MakeMessage("alice", "carol", "for carol"));

        var forBob = await _sut.GetPendingForUserAsync("bob");
        Assert.Single(forBob);
        Assert.Equal("for bob", forBob[0].Content);
    }

    // ── Remove ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_DeletesSingleMessage()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "keep"));
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "delete me"));

        var withIds = await _sut.GetPendingWithIdsAsync("bob");
        var toDelete = withIds.First(x => x.Message.Content == "delete me");

        await _sut.RemoveAsync(toDelete.RowId);

        var remaining = await _sut.GetPendingForUserAsync("bob");
        Assert.Single(remaining);
        Assert.Equal("keep", remaining[0].Content);
    }

    // ── RemoveAllForUser ──────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAllForUser_ClearsAllMessages()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "msg 1"));
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "msg 2"));

        await _sut.RemoveAllForUserAsync("bob");

        Assert.Empty(await _sut.GetPendingForUserAsync("bob"));
    }

    [Fact]
    public async Task RemoveAllForUser_DoesNotAffectOtherRecipients()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob",   "for bob"));
        await _sut.EnqueueAsync(MakeMessage("alice", "carol", "for carol"));

        await _sut.RemoveAllForUserAsync("bob");

        Assert.Empty(await _sut.GetPendingForUserAsync("bob"));
        Assert.Single(await _sut.GetPendingForUserAsync("carol"));
    }

    // ── Contact request message type ──────────────────────────────────────

    [Fact]
    public async Task Enqueue_PreservesMessageType()
    {
        await _sut.EnqueueAsync(MakeMessage("alice", "bob", "Alice",
            type: MessageType.ContactRequest));

        var pending = await _sut.GetPendingForUserAsync("bob");
        Assert.Equal(MessageType.ContactRequest, pending[0].Type);
    }
}
