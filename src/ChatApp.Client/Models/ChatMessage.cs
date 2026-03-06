using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatApp.Client.Models;

/// <summary>Represents a single chat message in the UI</summary>
public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _fromUser = string.Empty;

    [ObservableProperty]
    private string _toUser = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private DateTime _timestamp = DateTime.Now;

    [ObservableProperty]
    private bool _isRead;

    /// <summary>True if this message was sent by the local user</summary>
    public bool IsMine { get; init; }

    public string TimeDisplay => Timestamp.ToString("HH:mm");
    public string DateTimeDisplay => Timestamp.ToString("MMM dd, HH:mm");
}
