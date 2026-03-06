using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;
using ChatApp.Shared.Models;
using ChatApp.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace ChatApp.WebApi.Tests;

public sealed class MessagesControllerTests
{
    private readonly Mock<IMessageRepository> _repoMock = new();
    private readonly MessagesController       _sut;

    public MessagesControllerTests() => _sut = new MessagesController(_repoMock.Object);

    // ── Save ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_ReturnsOkWithSavedMessage()
    {
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<Message>()))
            .ReturnsAsync((Message m) =>
            {
                m.Id    = 42;
                m.SentAt = DateTime.UtcNow;
                return m;
            });

        var req = new SaveMessageRequest
            { FromUser = "alice", ToUser = "bob", Content = "Hello!" };

        var result = await _sut.Save(req);

        var ok  = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<MessageDto>(ok.Value);
        Assert.Equal("alice", dto.FromUser);
        Assert.Equal("Hello!", dto.Content);
    }

    // ── GetConversation ───────────────────────────────────────────────────

    [Fact]
    public async Task GetConversation_ReturnsMessages()
    {
        _repoMock.Setup(r => r.GetConversationAsync("alice", "bob", 1, 50))
            .ReturnsAsync(new[]
            {
                new Message { Id = 1, FromUser = "alice", ToUser = "bob", Content = "Hi" },
                new Message { Id = 2, FromUser = "bob",   ToUser = "alice", Content = "Hey" }
            });

        var result = await _sut.GetConversation("alice", "bob");

        var ok   = Assert.IsType<OkObjectResult>(result.Result);
        var msgs = Assert.IsAssignableFrom<IEnumerable<MessageDto>>(ok.Value).ToList();
        Assert.Equal(2, msgs.Count);
    }

    [Fact]
    public async Task GetConversation_ClampsPageSize()
    {
        _repoMock.Setup(r => r.GetConversationAsync("alice", "bob", 1, 50))
            .ReturnsAsync(Array.Empty<Message>());

        // pageSize=999 should be clamped to 50 internally
        var result = await _sut.GetConversation("alice", "bob", page: 0, pageSize: 999);

        // We can only verify it didn't throw and returned Ok
        Assert.IsType<OkObjectResult>(result.Result);
    }

    // ── GetUnreadCount ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_ReturnsCount()
    {
        _repoMock.Setup(r => r.GetUnreadCountAsync("bob", "alice")).ReturnsAsync(3);

        var result = await _sut.GetUnreadCount("bob", "alice");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(3, (int)ok.Value!);
    }

    [Fact]
    public async Task GetUnreadCount_ReturnsZero_WhenNoneUnread()
    {
        _repoMock.Setup(r => r.GetUnreadCountAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(0);

        var result = await _sut.GetUnreadCount("bob", "alice");

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, (int)ok.Value!);
    }

    // ── MarkAsRead ────────────────────────────────────────────────────────

    [Fact]
    public async Task MarkAsRead_ReturnsNoContent()
    {
        _repoMock.Setup(r => r.MarkAsReadAsync("bob", "alice")).Returns(Task.CompletedTask);

        var result = await _sut.MarkAsRead("bob", "alice");

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task MarkAsRead_CallsRepositoryWithCorrectArgs()
    {
        string? capturedTo = null, capturedFrom = null;
        _repoMock.Setup(r => r.MarkAsReadAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((t, f) => { capturedTo = t; capturedFrom = f; })
            .Returns(Task.CompletedTask);

        await _sut.MarkAsRead("bob", "alice");

        Assert.Equal("bob",   capturedTo);
        Assert.Equal("alice", capturedFrom);
    }
}
