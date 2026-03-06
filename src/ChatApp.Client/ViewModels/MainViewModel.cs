using ChatApp.Client.Models;
using ChatApp.Client.Services;
using ChatApp.Shared.Grpc;
using ChatApp.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace ChatApp.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GrpcHostService _grpcHost;
    private readonly PeerClientService _peerClient;
    private readonly ApiService _apiService;

    private Timer? _refreshTimer;
    private CancellationTokenSource _cts = new();

    // ─── Observable State ────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string _currentUserName = string.Empty;

    [ObservableProperty]
    private string _currentDisplayName = string.Empty;

    [ObservableProperty]
    private string _loginUserName = string.Empty;

    [ObservableProperty]
    private string _loginDisplayName = string.Empty;

    [ObservableProperty]
    private string _apiUrl = "http://localhost:5000";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPeer))]
    private PeerUser? _selectedPeer;

    [ObservableProperty]
    private string _messageInput = string.Empty;

    [ObservableProperty]
    private string _statusText = "Not connected";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _peerIpInput = string.Empty;

    [ObservableProperty]
    private string _peerPortInput = string.Empty;

    [ObservableProperty]
    private int _localGrpcPort;

    [ObservableProperty]
    private bool _isPeerTyping;

    public bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUserName);
    public bool HasSelectedPeer => SelectedPeer is not null;

    public ObservableCollection<PeerUser> OnlineUsers { get; } = [];
    public ObservableCollection<ChatMessage> Messages { get; } = [];

    // ─── Constructor ─────────────────────────────────────────────────────

    public MainViewModel()
    {
        _grpcHost = new GrpcHostService();
        _peerClient = new PeerClientService();
        _apiService = new ApiService();

        // Wire up gRPC message events
        _grpcHost.ChatService.MessageReceived += OnMessageReceived;
        _grpcHost.ChatService.TypingIndicatorReceived += OnTypingIndicatorReceived;
    }

    // ─── Commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginUserName)) return;

        IsBusy = true;
        StatusText = "Connecting…";

        try
        {
            // Find a free port for our gRPC server
            LocalGrpcPort = FindFreePort();

            // Start embedded gRPC server
            await _grpcHost.StartAsync(LocalGrpcPort);

            // Get local IP
            var localIp = GetLocalIpAddress();

            // Update API URL from input
            _apiService.Dispose();
            var api = new ApiService(ApiUrl);

            // Register with the REST API
            var reg = new RegisterUserRequest
            {
                UserName = LoginUserName.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(LoginDisplayName)
                    ? LoginUserName.Trim()
                    : LoginDisplayName.Trim(),
                IpAddress = localIp,
                GrpcPort = LocalGrpcPort
            };

            var user = await api.RegisterAsync(reg);
            if (user is null)
            {
                StatusText = "Could not register with API. Check API URL.";
                IsBusy = false;
                return;
            }

            CurrentUserName = user.UserName;
            CurrentDisplayName = user.DisplayName;
            LocalUserContext.CurrentUserName = CurrentUserName;
            LocalUserContext.CurrentDisplayName = CurrentDisplayName;

            // Swap to registered api
            _apiService.Dispose();
            // Use a field instead
            _registeredApi = api;

            StatusText = $"Online as {CurrentDisplayName} | gRPC port {LocalGrpcPort}";

            // Start polling for online users
            _refreshTimer = new Timer(
                async _ => await RefreshUsersAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            StatusText = $"Login error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _refreshTimer?.Dispose();
        _cts.Cancel();

        await _grpcHost.StopAsync();

        if (_registeredApi is not null)
            await _registeredApi.UnregisterAsync(CurrentUserName);

        CurrentUserName = string.Empty;
        CurrentDisplayName = string.Empty;
        LocalUserContext.CurrentUserName = string.Empty;
        OnlineUsers.Clear();
        Messages.Clear();
        SelectedPeer = null;
        StatusText = "Not connected";
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (SelectedPeer is null || string.IsNullOrWhiteSpace(MessageInput)) return;

        var content = MessageInput.Trim();
        MessageInput = string.Empty;

        var proto = new ChatMessageProto
        {
            Id = Guid.NewGuid().ToString(),
            FromUser = CurrentUserName,
            ToUser = SelectedPeer.UserName,
            Content = content,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type = MessageType.Text
        };

        // Show in UI immediately
        var uiMsg = new ChatMessage
        {
            Id = proto.Id,
            FromUser = proto.FromUser,
            ToUser = proto.ToUser,
            Content = proto.Content,
            Timestamp = DateTime.Now,
            IsMine = true
        };

        Application.Current.Dispatcher.Invoke(() => Messages.Add(uiMsg));

        // Send via gRPC P2P
        var sent = await _peerClient.SendMessageAsync(
            SelectedPeer.IpAddress, SelectedPeer.GrpcPort, proto);

        // Persist to history via REST API
        if (_registeredApi is not null)
        {
            await _registeredApi.SaveMessageAsync(new SaveMessageRequest
            {
                FromUser = CurrentUserName,
                ToUser = SelectedPeer.UserName,
                Content = content
            });
        }

        if (!sent)
            StatusText = $"⚠ Could not deliver to {SelectedPeer.DisplayName} (peer may be offline)";
    }

    [RelayCommand]
    private async Task SelectPeerAsync(PeerUser peer)
    {
        SelectedPeer = peer;
        Messages.Clear();
        IsPeerTyping = false;

        // Load conversation history from REST API
        if (_registeredApi is not null)
        {
            var history = await _registeredApi.GetConversationAsync(
                CurrentUserName, peer.UserName);

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var msg in history)
                {
                    Messages.Add(new ChatMessage
                    {
                        Id = msg.Id.ToString(),
                        FromUser = msg.FromUser,
                        ToUser = msg.ToUser,
                        Content = msg.Content,
                        Timestamp = msg.SentAt,
                        IsRead = msg.IsRead,
                        IsMine = msg.FromUser == CurrentUserName
                    });
                }
            });

            // Mark messages as read
            await _registeredApi.MarkAsReadAsync(CurrentUserName, peer.UserName);
            peer.UnreadCount = 0;
        }

        StatusText = $"Chatting with {peer.DisplayName}";
    }

    [RelayCommand]
    private async Task AddPeerManuallyAsync()
    {
        if (string.IsNullOrWhiteSpace(PeerIpInput) || string.IsNullOrWhiteSpace(PeerPortInput))
            return;

        if (!int.TryParse(PeerPortInput, out var port)) return;

        var ping = await _peerClient.PingAsync(PeerIpInput, port, CurrentUserName);
        if (ping is null)
        {
            StatusText = $"⚠ Peer at {PeerIpInput}:{port} is not reachable";
            return;
        }

        var peer = new PeerUser
        {
            UserName = ping.UserName,
            DisplayName = ping.DisplayName,
            IpAddress = PeerIpInput,
            GrpcPort = port,
            Status = UserStatus.Online
        };

        if (!OnlineUsers.Any(u => u.UserName == peer.UserName))
            OnlineUsers.Add(peer);

        StatusText = $"✓ Connected to {peer.DisplayName}";
        PeerIpInput = string.Empty;
        PeerPortInput = string.Empty;
    }

    [RelayCommand]
    private async Task RefreshUsersManuallyAsync()
    {
        await RefreshUsersAsync();
    }

    // ─── Private Helpers ─────────────────────────────────────────────────

    private ApiService? _registeredApi;

    private async Task RefreshUsersAsync()
    {
        if (_registeredApi is null) return;

        try
        {
            var users = await _registeredApi.GetOnlineUsersAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                // Add new users, update existing
                foreach (var dto in users)
                {
                    if (dto.UserName == CurrentUserName) continue;

                    var existing = OnlineUsers.FirstOrDefault(u => u.UserName == dto.UserName);
                    if (existing is null)
                        OnlineUsers.Add(PeerUser.FromDto(dto));
                    else
                    {
                        existing.Status = dto.Status;
                        existing.IpAddress = dto.IpAddress;
                        existing.GrpcPort = dto.GrpcPort;
                    }
                }

                // Remove users that are no longer online
                var toRemove = OnlineUsers
                    .Where(u => !users.Any(d => d.UserName == u.UserName))
                    .ToList();
                foreach (var u in toRemove)
                    OnlineUsers.Remove(u);
            });
        }
        catch { /* API may be offline */ }
    }

    private void OnMessageReceived(object? sender, ChatMessageProto msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Only show if this is the active conversation
            if (SelectedPeer?.UserName == msg.FromUser)
            {
                Messages.Add(new ChatMessage
                {
                    Id = msg.Id,
                    FromUser = msg.FromUser,
                    ToUser = msg.ToUser,
                    Content = msg.Content,
                    Timestamp = DateTime.TryParse(msg.Timestamp, out var ts) ? ts : DateTime.Now,
                    IsMine = false
                });
            }
            else
            {
                // Increment unread badge
                var peer = OnlineUsers.FirstOrDefault(u => u.UserName == msg.FromUser);
                if (peer is not null) peer.UnreadCount++;
            }
        });

        // Persist to history
        _ = Task.Run(async () =>
        {
            if (_registeredApi is not null)
            {
                await _registeredApi.SaveMessageAsync(new SaveMessageRequest
                {
                    FromUser = msg.FromUser,
                    ToUser = msg.ToUser,
                    Content = msg.Content
                });
            }
        });
    }

    private void OnTypingIndicatorReceived(object? sender, (string FromUser, bool IsTyping) args)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedPeer?.UserName == args.FromUser)
                IsPeerTyping = args.IsTyping;
        });
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
