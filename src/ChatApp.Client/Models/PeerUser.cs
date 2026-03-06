using CommunityToolkit.Mvvm.ComponentModel;
using ChatApp.Shared.Models;

namespace ChatApp.Client.Models;

/// <summary>Represents a peer user shown in the contacts list</summary>
public partial class PeerUser : ObservableObject
{
    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _ipAddress = string.Empty;

    [ObservableProperty]
    private int _grpcPort;

    [ObservableProperty]
    private UserStatus _status = UserStatus.Offline;

    [ObservableProperty]
    private int _unreadCount;

    [ObservableProperty]
    private bool _isTyping;

    public bool IsOnline => Status == UserStatus.Online;

    public string StatusIcon => Status switch
    {
        UserStatus.Online => "🟢",
        UserStatus.Away => "🟡",
        UserStatus.Busy => "🔴",
        _ => "⚫"
    };

    public string Endpoint => $"{IpAddress}:{GrpcPort}";

    public static PeerUser FromDto(UserDto dto) => new()
    {
        UserName = dto.UserName,
        DisplayName = dto.DisplayName,
        IpAddress = dto.IpAddress,
        GrpcPort = dto.GrpcPort,
        Status = dto.Status
    };
}
