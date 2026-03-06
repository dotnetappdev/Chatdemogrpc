using ChatApp.Core.Interfaces;
using ChatApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Services;

/// <inheritdoc cref="IHistoryService"/>
public sealed class HistoryService : IHistoryService, IDisposable
{
    private readonly LocalDb _db;

    public HistoryService(LocalDb db) => _db = db;

    public async Task SaveAsync(string fromUser, string toUser, string content)
    {
        _db.Messages.Add(new LocalMessageRow
        {
            FromUser = fromUser,
            ToUser   = toUser,
            Content  = content,
            SentAt   = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChatMessage>> GetConversationAsync(
        string user1, string user2, int count = 100, string localUser = "")
    {
        var rows = await _db.Messages
            .Where(m => (m.FromUser == user1 && m.ToUser == user2)
                     || (m.FromUser == user2 && m.ToUser == user1))
            .OrderByDescending(m => m.SentAt)
            .Take(count)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return rows.Select(r => new ChatMessage(
            r.Id.ToString(),
            r.FromUser,
            r.ToUser,
            r.Content,
            r.SentAt,
            IsMine: r.FromUser == localUser)).ToList();
    }

    public void Dispose() => _db.Dispose();
}
