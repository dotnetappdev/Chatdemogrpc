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
    // ─── Services ────────────────────────────────────────────────────────

    private readonly GrpcHostService     _grpcHost        = new();
    private readonly PeerClientService   _peerClient      = new();
    private readonly UdpDiscoveryService _discovery       = new();
    private readonly LocalHistoryService _localHistory    = new();
    private ApiService?                  _api;              // optional REST API

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

    /// <summary>
    /// Optional REST API base URL.  Leave blank to run in full P2P / local mode.
    /// </summary>
    [ObservableProperty]
    private string _apiUrl = string.Empty;

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

    [ObservableProperty]
    private bool _apiConnected;

    public bool IsLoggedIn     => !string.IsNullOrEmpty(CurrentUserName);
    public bool HasSelectedPeer => SelectedPeer is not null;

    public ObservableCollection<PeerUser>    OnlineUsers { get; } = [];
    public ObservableCollection<ChatMessage> Messages    { get; } = [];

    // ─── Constructor ─────────────────────────────────────────────────────

    public MainViewModel()
    {
        _grpcHost.ChatService.MessageReceived         += OnGrpcMessageReceived;
        _grpcHost.ChatService.TypingIndicatorReceived += OnTypingIndicatorReceived;
        _discovery.PeerDiscovered                     += OnPeerDiscovered;
        _discovery.PeerLeft                           += OnPeerLeft;
    }

    // ─── Commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginUserName)) return;

        IsBusy     = true;
        StatusText = "Starting…";

        try
        {
            // Pick a free port for our embedded gRPC server
            LocalGrpcPort = FindFreePort();

            // Start embedded gRPC server (peers connect to us)
            await _grpcHost.StartAsync(LocalGrpcPort);

            CurrentUserName    = LoginUserName.Trim();
            CurrentDisplayName = string.IsNullOrWhiteSpace(LoginDisplayName)
                ? CurrentUserName
                : LoginDisplayName.Trim();

            LocalUserContext.CurrentUserName    = CurrentUserName;
            LocalUserContext.CurrentDisplayName = CurrentDisplayName;

            // ── Optional REST API ───────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(ApiUrl))
            {
                _api = new ApiService(ApiUrl.Trim());

                var reg = new RegisterUserRequest
                {
                    UserName    = CurrentUserName,
                    DisplayName = CurrentDisplayName,
                    IpAddress   = GetLocalIpAddress(),
                    GrpcPort    = LocalGrpcPort
                };

                var user = await _api.RegisterAsync(reg);
                ApiConnected = user is not null;

                if (!ApiConnected)
                    StatusText = $"⚠ API not reachable — running in P2P/local mode";
            }

            // ── UDP peer discovery (always on) ──────────────────────────
            await _discovery.StartAsync(CurrentUserName, CurrentDisplayName, LocalGrpcPort);

            StatusText = ApiConnected
                ? $"Online as {CurrentDisplayName} | gRPC :{LocalGrpcPort} | API connected"
                : $"Online as {CurrentDisplayName} | gRPC :{LocalGrpcPort} | P2P mode";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        _cts.Cancel();

        await _discovery.StopAsync();
        await _grpcHost.StopAsync();

        if (_api is not null && ApiConnected)
            await _api.UnregisterAsync(CurrentUserName);

        _api?.Dispose();
        _api = null;

        CurrentUserName    = string.Empty;
        CurrentDisplayName = string.Empty;
        LocalUserContext.CurrentUserName    = string.Empty;
        LocalUserContext.CurrentDisplayName = string.Empty;

        ApiConnected = false;
        OnlineUsers.Clear();
        Messages.Clear();
        SelectedPeer = null;
        StatusText   = "Not connected";
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (SelectedPeer is null || string.IsNullOrWhiteSpace(MessageInput)) return;

        var content      = MessageInput.Trim();
        MessageInput     = string.Empty;

        var proto = new ChatMessageProto
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = CurrentUserName,
            ToUser    = SelectedPeer.UserName,
            Content   = content,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type      = MessageType.Text
        };

        // Show immediately in the UI (optimistic)
        AddMessageToUi(proto.Id, proto.FromUser, proto.ToUser, content,
            DateTime.Now, isMine: true);

        // Fire-and-forget: send via gRPC, persist locally, and optionally to API
        _ = Task.Run(async () =>
        {
            // 1. Deliver via gRPC P2P
            var sent = await _peerClient.SendMessageAsync(
                SelectedPeer.IpAddress, SelectedPeer.GrpcPort, proto);

            if (!sent)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"⚠ Could not reach {SelectedPeer.DisplayName}");
            }

            // 2. Persist locally (always)
            await _localHistory.SaveAsync(CurrentUserName, SelectedPeer.UserName, content);

            // 3. Persist to REST API (optional)
            if (_api is not null && ApiConnected)
            {
                await _api.SaveMessageAsync(new SaveMessageRequest
                {
                    FromUser = CurrentUserName,
                    ToUser   = SelectedPeer.UserName,
                    Content  = content
                });
            }
        });
    }

    [RelayCommand]
    private async Task SelectPeerAsync(PeerUser peer)
    {
        SelectedPeer  = peer;
        IsPeerTyping  = false;
        Messages.Clear();

        // Load history — prefer local SQLite, fall back to API if needed
        var local = await _localHistory.GetConversationAsync(CurrentUserName, peer.UserName);

        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var m in local)
            {
                Messages.Add(new ChatMessage
                {
                    Id        = m.Id.ToString(),
                    FromUser  = m.FromUser,
                    ToUser    = m.ToUser,
                    Content   = m.Content,
                    Timestamp = m.SentAt,
                    IsMine    = m.FromUser == CurrentUserName
                });
            }
        });

        // If API is connected and we have no local history, fetch from API
        if (local.Count == 0 && _api is not null && ApiConnected)
        {
            var apiHistory = await _api.GetConversationAsync(CurrentUserName, peer.UserName);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Messages.Clear();
                foreach (var m in apiHistory)
                {
                    Messages.Add(new ChatMessage
                    {
                        Id        = m.Id.ToString(),
                        FromUser  = m.FromUser,
                        ToUser    = m.ToUser,
                        Content   = m.Content,
                        Timestamp = m.SentAt,
                        IsRead    = m.IsRead,
                        IsMine    = m.FromUser == CurrentUserName
                    });
                }
            });

            await _api.MarkAsReadAsync(CurrentUserName, peer.UserName);
        }

        peer.UnreadCount = 0;
        StatusText = $"Chatting with {peer.DisplayName}";
    }

    /// <summary>Manually add a peer by IP and gRPC port (for cross-subnet scenarios).</summary>
    [RelayCommand]
    private async Task AddPeerManuallyAsync()
    {
        if (string.IsNullOrWhiteSpace(PeerIpInput) || string.IsNullOrWhiteSpace(PeerPortInput))
            return;

        if (!int.TryParse(PeerPortInput, out var port)) return;

        StatusText = $"Pinging {PeerIpInput}:{port}…";
        var ping = await _peerClient.PingAsync(PeerIpInput, port, CurrentUserName);
        if (ping is null)
        {
            StatusText = $"⚠ Peer at {PeerIpInput}:{port} is not reachable";
            return;
        }

        UpsertPeer(ping.UserName, ping.DisplayName, PeerIpInput, port);
        StatusText    = $"✓ Connected to {ping.DisplayName}";
        PeerIpInput   = string.Empty;
        PeerPortInput = string.Empty;
    }

    // ─── Discovery Event Handlers ─────────────────────────────────────────

    private void OnPeerDiscovered(object? sender, DiscoveredPeer peer)
    {
        // Run a quick gRPC Ping to confirm the peer is actually reachable
        // then add/update the contacts list
        _ = Task.Run(async () =>
        {
            var ping = await _peerClient.PingAsync(peer.IpAddress, peer.GrpcPort, CurrentUserName);
            if (ping is null) return;

            Application.Current.Dispatcher.Invoke(() =>
                UpsertPeer(peer.UserName, peer.DisplayName, peer.IpAddress, peer.GrpcPort));
        });
    }

    private void OnPeerLeft(object? sender, string userName)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = OnlineUsers.FirstOrDefault(u => u.UserName == userName);
            if (existing is not null)
            {
                existing.Status = UserStatus.Offline;
                OnlineUsers.Remove(existing);
            }

            if (SelectedPeer?.UserName == userName)
                StatusText = $"⚠ {userName} went offline";
        });
    }

    // ─── gRPC Event Handlers ──────────────────────────────────────────────

    private void OnGrpcMessageReceived(object? sender, ChatMessageProto msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedPeer?.UserName == msg.FromUser)
            {
                AddMessageToUi(msg.Id, msg.FromUser, msg.ToUser, msg.Content,
                    DateTime.TryParse(msg.Timestamp, out var ts) ? ts : DateTime.Now,
                    isMine: false);
            }
            else
            {
                // Increment unread badge for the sender
                var peer = OnlineUsers.FirstOrDefault(u => u.UserName == msg.FromUser);
                if (peer is not null)
                    peer.UnreadCount++;
            }
        });

        // Persist locally + to API
        _ = Task.Run(async () =>
        {
            await _localHistory.SaveAsync(msg.FromUser, msg.ToUser, msg.Content);

            if (_api is not null && ApiConnected)
            {
                await _api.SaveMessageAsync(new SaveMessageRequest
                {
                    FromUser = msg.FromUser,
                    ToUser   = msg.ToUser,
                    Content  = msg.Content
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

    // ─── Helpers ─────────────────────────────────────────────────────────

    private void UpsertPeer(string userName, string displayName, string ip, int port)
    {
        var existing = OnlineUsers.FirstOrDefault(u => u.UserName == userName);
        if (existing is null)
        {
            OnlineUsers.Add(new PeerUser
            {
                UserName    = userName,
                DisplayName = displayName,
                IpAddress   = ip,
                GrpcPort    = port,
                Status      = UserStatus.Online
            });
        }
        else
        {
            existing.IpAddress   = ip;
            existing.GrpcPort    = port;
            existing.DisplayName = displayName;
            existing.Status      = UserStatus.Online;
        }
    }

    private void AddMessageToUi(
        string id, string from, string to, string content,
        DateTime timestamp, bool isMine)
    {
        Messages.Add(new ChatMessage
        {
            Id        = id,
            FromUser  = from,
            ToUser    = to,
            Content   = content,
            Timestamp = timestamp,
            IsMine    = isMine
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
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 65530);
            return (s.LocalEndPoint as IPEndPoint)?.Address.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }
}

