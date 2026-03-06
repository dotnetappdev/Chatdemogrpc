using ChatApp.Data.Entities;
using ChatApp.Data.Repositories;
using ChatApp.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MessagesController : ControllerBase
{
    private readonly IMessageRepository _messages;

    public MessagesController(IMessageRepository messages) => _messages = messages;

    /// <summary>Save a message to history</summary>
    [HttpPost]
    public async Task<ActionResult<MessageDto>> Save([FromBody] SaveMessageRequest request)
    {
        var msg = new Message
        {
            FromUser = request.FromUser,
            ToUser = request.ToUser,
            Content = request.Content
        };

        var saved = await _messages.SaveAsync(msg);
        return Ok(Map(saved));
    }

    /// <summary>Get conversation history between two users</summary>
    [HttpGet("{user1}/{user2}")]
    public async Task<ActionResult<IEnumerable<MessageDto>>> GetConversation(
        string user1,
        string user2,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;

        var messages = await _messages.GetConversationAsync(user1, user2, page, pageSize);
        return Ok(messages.Select(Map));
    }

    /// <summary>Get unread message count</summary>
    [HttpGet("unread/{toUser}/{fromUser}")]
    public async Task<ActionResult<int>> GetUnreadCount(string toUser, string fromUser)
    {
        var count = await _messages.GetUnreadCountAsync(toUser, fromUser);
        return Ok(count);
    }

    /// <summary>Mark messages as read</summary>
    [HttpPost("markread/{toUser}/{fromUser}")]
    public async Task<IActionResult> MarkAsRead(string toUser, string fromUser)
    {
        await _messages.MarkAsReadAsync(toUser, fromUser);
        return NoContent();
    }

    private static MessageDto Map(Message m) => new()
    {
        Id = m.Id,
        FromUser = m.FromUser,
        ToUser = m.ToUser,
        Content = m.Content,
        SentAt = m.SentAt,
        IsRead = m.IsRead
    };
}
