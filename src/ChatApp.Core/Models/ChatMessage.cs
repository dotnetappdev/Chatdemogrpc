namespace ChatApp.Core.Models;

/// <summary>Immutable record for a single chat message (sent or received).</summary>
public sealed record ChatMessage(
    string   Id,
    string   FromUser,
    string   ToUser,
    string   Content,
    DateTime Timestamp,
    bool     IsMine)
{
    public string TimeDisplay     => Timestamp.ToLocalTime().ToString("HH:mm");
    public string DateTimeDisplay => Timestamp.ToLocalTime().ToString("MMM dd, HH:mm");
}
