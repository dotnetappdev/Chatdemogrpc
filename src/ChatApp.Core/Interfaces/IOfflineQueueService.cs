using ChatApp.Shared.Grpc;

namespace ChatApp.Core.Interfaces;

/// <summary>
/// Queues messages for offline peers and delivers them when the peer reconnects.
///
/// Flow:
///   Sender:   message → send fails → <see cref="EnqueueAsync"/> → retry later
///   Delivery: peer comes online → <see cref="GetPendingForUserAsync"/> → send each → <see cref="RemoveAsync"/>
/// </summary>
public interface IOfflineQueueService
{
    /// <summary>Store a message that could not be delivered because the peer was offline.</summary>
    Task EnqueueAsync(ChatMessageProto message);

    /// <summary>Retrieve all queued messages for a given recipient, oldest first.</summary>
    Task<List<ChatMessageProto>> GetPendingForUserAsync(string toUser);

    /// <summary>Remove a single queued message after successful delivery.</summary>
    Task RemoveAsync(int rowId);

    /// <summary>Remove all queued messages for a given recipient.</summary>
    Task RemoveAllForUserAsync(string toUser);

    /// <summary>Returns row IDs alongside messages so callers can call <see cref="RemoveAsync"/>.</summary>
    Task<List<(int RowId, ChatMessageProto Message)>> GetPendingWithIdsAsync(string toUser);
}
