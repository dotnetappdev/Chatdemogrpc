namespace ChatApp.Core.Interfaces;

/// <summary>Status of a contact relationship (stored as int in DB).</summary>
public enum ContactStatus
{
    Pending  = 0,
    Accepted = 1,
    Blocked  = 2,
    Declined = 3
}

/// <summary>Immutable view of a contact entry.</summary>
public sealed record ContactEntry(
    string        UserName,
    string        DisplayName,
    ContactStatus Status,
    bool          IsIncoming,
    DateTime      CreatedAt);

/// <summary>
/// Manages the local contact list: add requests, accepts, declines, blocks.
/// All data is stored locally — no server required.
/// </summary>
public interface IContactService
{
    /// <summary>Add or update a contact entry (upsert).</summary>
    Task UpsertAsync(ContactEntry contact);

    /// <summary>Get all contacts matching the given status(es).</summary>
    Task<List<ContactEntry>> GetByStatusAsync(params ContactStatus[] statuses);

    /// <summary>Get a single contact by username, or null if not found.</summary>
    Task<ContactEntry?> GetAsync(string userName);

    /// <summary>
    /// Returns true when the given username is blocked by the local user.
    /// Used by <see cref="ChatApp.Core.Services.PeerChatService"/> to discard messages.
    /// </summary>
    Task<bool> IsBlockedAsync(string userName);

    /// <summary>Remove a contact entry entirely (e.g., after declining).</summary>
    Task RemoveAsync(string userName);
}
