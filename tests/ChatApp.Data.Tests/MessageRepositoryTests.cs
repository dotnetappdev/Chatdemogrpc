using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;

namespace ChatApp.Data.Tests;

public sealed class MessageRepositoryTests : DataTestBase
{
    private readonly MessageRepository _sut;

    public MessageRepositoryTests() => _sut = new MessageRepository(Db);

    [Fact]
    public async Task Save_PersistsMessage()
    {
        var msg = new Message { FromUser = "alice", ToUser = "bob", Content = "Hello!" };
        var saved = await _sut.SaveAsync(msg);

        Assert.True(saved.Id > 0);
        Assert.NotEqual(default, saved.SentAt);
    }

    [Fact]
    public async Task GetConversation_ReturnsBothDirections()
    {
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "A→B" });
        await _sut.SaveAsync(new Message { FromUser = "bob",   ToUser = "alice", Content = "B→A" });
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "carol", Content = "other" });

        var conv = (await _sut.GetConversationAsync("alice", "bob", 1, 100)).ToList();

        Assert.Equal(2, conv.Count);
        Assert.Contains(conv, m => m.Content == "A→B");
        Assert.Contains(conv, m => m.Content == "B→A");
    }

    [Fact]
    public async Task GetConversation_OrdersChronologically()
    {
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "first" });
        await Task.Delay(10);
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "second" });

        var conv = (await _sut.GetConversationAsync("alice", "bob", 1, 100)).ToList();

        Assert.Equal("first",  conv[0].Content);
        Assert.Equal("second", conv[1].Content);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsCorrectCount()
    {
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "1", IsRead = false });
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "2", IsRead = false });
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = "3", IsRead = true  });

        var count = await _sut.GetUnreadCountAsync("bob", "alice");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task MarkAsRead_MarksOnlyRelevantMessages()
    {
        await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob",   Content = "a→b", IsRead = false });
        await _sut.SaveAsync(new Message { FromUser = "carol", ToUser = "bob",   Content = "c→b", IsRead = false });

        await _sut.MarkAsReadAsync("bob", "alice");

        Assert.Equal(0, await _sut.GetUnreadCountAsync("bob", "alice"));
        Assert.Equal(1, await _sut.GetUnreadCountAsync("bob", "carol")); // untouched
    }

    [Fact]
    public async Task GetConversation_SupportsPagination()
    {
        for (var i = 1; i <= 5; i++)
            await _sut.SaveAsync(new Message { FromUser = "alice", ToUser = "bob", Content = $"msg{i}" });

        var page1 = (await _sut.GetConversationAsync("alice", "bob", 1, 2)).ToList();
        var page2 = (await _sut.GetConversationAsync("alice", "bob", 2, 2)).ToList();
        var page3 = (await _sut.GetConversationAsync("alice", "bob", 3, 2)).ToList();

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Single(page3);
    }
}
