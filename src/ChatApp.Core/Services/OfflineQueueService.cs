using ChatApp.Core.Interfaces;
using ChatApp.Shared.Grpc;
using Microsoft.EntityFrameworkCore;

namespace ChatApp.Core.Services;

/// <inheritdoc cref="IOfflineQueueService"/>
public sealed class OfflineQueueService : IOfflineQueueService
{
    private readonly LocalDb _db;

    public OfflineQueueService(LocalDb db) => _db = db;

    public async Task EnqueueAsync(ChatMessageProto message)
    {
        _db.PendingMessages.Add(new PendingMessageRow
        {
            MessageId   = message.Id,
            ToUser      = message.ToUser,
            FromUser    = message.FromUser,
            Content     = message.Content,
            MessageType = (int)message.Type,
            QueuedAt    = DateTime.UtcNow,
            Attempts    = 0
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChatMessageProto>> GetPendingForUserAsync(string toUser)
    {
        var rows = await _db.PendingMessages
            .Where(p => p.ToUser == toUser)
            .OrderBy(p => p.QueuedAt)
            .ToListAsync();

        return rows.Select(ToProto).ToList();
    }

    public async Task<List<(int RowId, ChatMessageProto Message)>> GetPendingWithIdsAsync(string toUser)
    {
        var rows = await _db.PendingMessages
            .Where(p => p.ToUser == toUser)
            .OrderBy(p => p.QueuedAt)
            .ToListAsync();

        return rows.Select(r => (r.Id, ToProto(r))).ToList();
    }

    public async Task RemoveAsync(int rowId)
    {
        var row = await _db.PendingMessages.FindAsync(rowId);
        if (row is not null)
        {
            _db.PendingMessages.Remove(row);
            await _db.SaveChangesAsync();
        }
    }

    public async Task RemoveAllForUserAsync(string toUser)
    {
        var rows = await _db.PendingMessages
            .Where(p => p.ToUser == toUser)
            .ToListAsync();

        if (rows.Count == 0) return;
        _db.PendingMessages.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    private static ChatMessageProto ToProto(PendingMessageRow r) => new()
    {
        Id        = r.MessageId,
        ToUser    = r.ToUser,
        FromUser  = r.FromUser,
        Content   = r.Content,
        Type      = (MessageType)r.MessageType,
        Timestamp = r.QueuedAt.ToString("o")
    };
}
