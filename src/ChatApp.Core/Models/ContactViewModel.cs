using CommunityToolkit.Mvvm.ComponentModel;
using ChatApp.Core.Interfaces;

namespace ChatApp.Core.Models;

/// <summary>UI-observable view of a contact entry for WPF binding.</summary>
public partial class ContactViewModel : ObservableObject
{
    [ObservableProperty] private string        _userName    = string.Empty;
    [ObservableProperty] private string        _displayName = string.Empty;
    [ObservableProperty] private ContactStatus _status      = ContactStatus.Pending;
    [ObservableProperty] private bool          _isIncoming;

    public bool IsPending  => Status == ContactStatus.Pending;
    public bool IsAccepted => Status == ContactStatus.Accepted;
    public bool IsBlocked  => Status == ContactStatus.Blocked;

    public string StatusIcon => Status switch
    {
        ContactStatus.Accepted => "✅",
        ContactStatus.Pending  => IsIncoming ? "📩" : "⏳",
        ContactStatus.Blocked  => "🚫",
        ContactStatus.Declined => "❌",
        _                      => ""
    };

    public string StatusLabel => Status switch
    {
        ContactStatus.Accepted => "Contact",
        ContactStatus.Pending  => IsIncoming ? "Wants to add you" : "Request sent",
        ContactStatus.Blocked  => "Blocked",
        ContactStatus.Declined => "Declined",
        _                      => ""
    };

    public static ContactViewModel FromEntry(ContactEntry e) => new()
    {
        UserName    = e.UserName,
        DisplayName = e.DisplayName,
        Status      = e.Status,
        IsIncoming  = e.IsIncoming
    };
}
