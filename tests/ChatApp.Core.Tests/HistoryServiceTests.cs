using ChatApp.Core.Services;

namespace ChatApp.Core.Tests;

public sealed class HistoryServiceTests : LocalDbTestBase
{
    private readonly HistoryService _sut;

    public HistoryServiceTests() => _sut = new HistoryService(Db);

    [Fact]
    public async Task Save_PersistsMessageToDatabase()
    {
        await _sut.SaveAsync("alice", "bob", "Hello Bob!");

        var rows = Db.Messages.ToList();
        Assert.Single(rows);
        Assert.Equal("alice", rows[0].FromUser);
        Assert.Equal("bob",   rows[0].ToUser);
        Assert.Equal("Hello Bob!", rows[0].Content);
    }

    [Fact]
    public async Task GetConversation_ReturnsBothDirections()
    {
        await _sut.SaveAsync("alice", "bob", "Hi from Alice");
        await _sut.SaveAsync("bob",   "alice", "Hi from Bob");
        await _sut.SaveAsync("alice", "carol", "This is a different conversation");

        var conv = await _sut.GetConversationAsync("alice", "bob");

        Assert.Equal(2, conv.Count);
        Assert.Contains(conv, m => m.Content == "Hi from Alice");
        Assert.Contains(conv, m => m.Content == "Hi from Bob");
    }

    [Fact]
    public async Task GetConversation_SetsIsMineCorrectly()
    {
        await _sut.SaveAsync("alice", "bob", "sent");
        await _sut.SaveAsync("bob",   "alice", "received");

        var conv = await _sut.GetConversationAsync("alice", "bob", localUser: "alice");

        var sent     = conv.First(m => m.Content == "sent");
        var received = conv.First(m => m.Content == "received");

        Assert.True(sent.IsMine);
        Assert.False(received.IsMine);
    }

    [Fact]
    public async Task GetConversation_ReturnsEmptyForUnknownUsers()
    {
        var conv = await _sut.GetConversationAsync("nobody", "nobody_else");
        Assert.Empty(conv);
    }

    [Fact]
    public async Task GetConversation_RespectsCountLimit()
    {
        for (var i = 0; i < 10; i++)
            await _sut.SaveAsync("alice", "bob", $"msg {i}");

        var conv = await _sut.GetConversationAsync("alice", "bob", count: 3);

        Assert.Equal(3, conv.Count);
    }

    [Fact]
    public async Task GetConversation_OrdersChronologically()
    {
        await _sut.SaveAsync("alice", "bob", "first");
        await Task.Delay(10); // ensure different timestamps
        await _sut.SaveAsync("alice", "bob", "second");

        var conv = await _sut.GetConversationAsync("alice", "bob");

        Assert.Equal("first",  conv[0].Content);
        Assert.Equal("second", conv[1].Content);
    }
}
