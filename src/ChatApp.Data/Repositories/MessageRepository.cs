using ChatApp.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Data.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly ChatDbContext _db;

    public MessageRepository(ChatDbContext db) => _db = db;

    public async Task<Message> SaveAsync(Message message)
    {
        message.SentAt = DateTime.UtcNow;
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        return message;
    }

    public async Task<IEnumerable<Message>> GetConversationAsync(
        string user1, string user2, int page, int pageSize)
    {
        return await _db.Messages
            .Where(m =>
                (m.FromUser == user1 && m.ToUser == user2) ||
                (m.FromUser == user2 && m.ToUser == user1))
            .OrderByDescending(m => m.SentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .OrderBy(m => m.SentAt)
            .ToListAsync();
    }

    public async Task<int> GetUnreadCountAsync(string toUser, string fromUser) =>
        await _db.Messages.CountAsync(m =>
            m.ToUser == toUser && m.FromUser == fromUser && !m.IsRead);

    public async Task MarkAsReadAsync(string toUser, string fromUser)
    {
        var unread = await _db.Messages
            .Where(m => m.ToUser == toUser && m.FromUser == fromUser && !m.IsRead)
            .ToListAsync();

        foreach (var msg in unread)
            msg.IsRead = true;

        await _db.SaveChangesAsync();
    }
}
