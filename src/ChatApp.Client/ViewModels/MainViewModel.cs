using ChatApp.Core;
using ChatApp.Core.Interfaces;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Shared.Grpc;
using ChatApp.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;

namespace ChatApp.Client.ViewModels;

/// <summary>
/// Thin coordinator — delegates all business logic to ChatApp.Core services.
/// Owns UI state only.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // ── Core services (injected / created at login) ───────────────────────
    private readonly LocalDb              _db;
    private readonly IHistoryService      _history;
    private readonly IKnownPeersStore     _peerStore;
    private readonly IDiscoveryService    _discovery;
    private readonly IPeerNetworkService  _network;
    private readonly GrpcHostService      _grpcHost;
    private          ApiService?          _api;

    // ── Observable state ─────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn))]
    private string _currentUserName = string.Empty;

    [ObservableProperty] private string _currentDisplayName = string.Empty;
    [ObservableProperty] private string _loginUserName      = string.Empty;
    [ObservableProperty] private string _loginDisplayName   = string.Empty;

    /// <summary>Optional REST API URL. Blank = pure P2P / local mode.</summary>
    [ObservableProperty] private string _apiUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPeer))]
    private PeerUser? _selectedPeer;

    [ObservableProperty] private string _messageInput  = string.Empty;
    [ObservableProperty] private string _statusText    = "Not connected";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _peerIpInput   = string.Empty;
    [ObservableProperty] private string _peerPortInput = string.Empty;
    [ObservableProperty] private int    _localGrpcPort;
    [ObservableProperty] private bool   _isPeerTyping;
    [ObservableProperty] private bool   _apiConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkSummary))]
    private int _knownPeerCount;

    public bool IsLoggedIn      => !string.IsNullOrEmpty(CurrentUserName);
    public bool HasSelectedPeer => SelectedPeer is not null;
    public string NetworkSummary =>
        $"{OnlineUsers.Count} online  ·  {KnownPeerCount} ever seen";

    public ObservableCollection<PeerUser>    OnlineUsers { get; } = [];
    public ObservableCollection<ChatMessage> Messages    { get; } = [];

    // ── Constructor ──────────────────────────────────────────────────────

    public MainViewModel()
    {
        _db        = LocalDb.CreateForProduction();
        _peerStore = new KnownPeersStore(_db);
        _history   = new HistoryService(_db);

        var channelFactory = new GrpcPeerChannelFactory();
        _network           = new PeerNetworkService(channelFactory, _peerStore);
        _discovery         = new UdpDiscoveryService();

        var chatSvc = new PeerChatService(_peerStore);
        _grpcHost   = new GrpcHostService(chatSvc);

        chatSvc.MessageReceived         += OnGrpcMessageReceived;
        chatSvc.TypingIndicatorReceived += OnTypingIndicatorReceived;
        _network.PeerConnected          += OnPeerConnected;
        _discovery.PeerDiscovered       += OnPeerDiscovered;
        _discovery.PeerLeft             += OnPeerLeft;
    }

    // ── Commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(LoginUserName)) return;
        IsBusy = true; StatusText = "Starting…";

        try
        {
            LocalGrpcPort = FindFreePort();

            CurrentUserName    = LoginUserName.Trim();
            CurrentDisplayName = string.IsNullOrWhiteSpace(LoginDisplayName)
                ? CurrentUserName : LoginDisplayName.Trim();

            LocalUserContext.CurrentUserName    = CurrentUserName;
            LocalUserContext.CurrentDisplayName = CurrentDisplayName;

            ((PeerNetworkService)_network).LocalUserName = CurrentUserName;

            // Start embedded gRPC server — this node is now reachable by peers
            await _grpcHost.StartAsync(LocalGrpcPort);

            // ── Optional REST API ─────────────────────────────────────────
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
            }

            // ── Bootstrap: reconnect to every previously-known peer ───────
            var saved = await _peerStore.GetAllAsync();
            KnownPeerCount = saved.Count;
            foreach (var p in saved)
                _ = Task.Run(() => _network.ConnectAndExchangeAsync(p.IpAddress, p.GrpcPort));

            // ── LAN broadcast discovery ──────────────────────────────────
            await _discovery.StartAsync(CurrentUserName, CurrentDisplayName, LocalGrpcPort);

            StatusText = ApiConnected
                ? $"🌐 {CurrentDisplayName} | gRPC :{LocalGrpcPort} | API connected"
                : $"📡 {CurrentDisplayName} | gRPC :{LocalGrpcPort} | P2P mesh";
        }
        catch (Exception ex) { StatusText = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        await _discovery.StopAsync();
        await _grpcHost.StopAsync();

        if (_api is not null && ApiConnected)
            await _api.UnregisterAsync(CurrentUserName);
        _api?.Dispose();
        _api = null;

        CurrentUserName = string.Empty;
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

        var content   = MessageInput.Trim();
        MessageInput  = string.Empty;

        var proto = new ChatMessageProto
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = CurrentUserName,
            ToUser    = SelectedPeer.UserName,
            Content   = content,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type      = MessageType.Text
        };

        // Optimistic UI update
        AddMessageToUi(proto.Id, CurrentUserName, SelectedPeer.UserName,
            content, DateTime.Now, isMine: true);

        _ = Task.Run(async () =>
        {
            var sent = await _network.SendMessageAsync(SelectedPeer, proto);

            if (!sent)
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"⚠ Could not reach {SelectedPeer.DisplayName}");

            await _history.SaveAsync(CurrentUserName, SelectedPeer.UserName, content);

            if (_api is not null && ApiConnected)
                await _api.SaveMessageAsync(new SaveMessageRequest
                    { FromUser = CurrentUserName, ToUser = SelectedPeer.UserName, Content = content });
        });
    }

    [RelayCommand]
    private async Task SelectPeerAsync(PeerUser peer)
    {
        SelectedPeer = peer;
        IsPeerTyping = false;
        Messages.Clear();

        var history = await _history.GetConversationAsync(
            CurrentUserName, peer.UserName, localUser: CurrentUserName);

        // Fall back to API history if local is empty
        if (history.Count == 0 && _api is not null && ApiConnected)
        {
            var apiMsgs = await _api.GetConversationAsync(CurrentUserName, peer.UserName);
            history = apiMsgs.Select(m => new ChatMessage(
                m.Id.ToString(), m.FromUser, m.ToUser, m.Content, m.SentAt,
                IsMine: m.FromUser == CurrentUserName)).ToList();
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            foreach (var m in history) Messages.Add(m);
        });

        if (_api is not null && ApiConnected)
            await _api.MarkAsReadAsync(CurrentUserName, peer.UserName);

        peer.UnreadCount = 0;
        StatusText = $"Chatting with {peer.DisplayName}";
    }

    /// <summary>Manually add a peer by IP:Port — useful across subnets or VPNs.</summary>
    [RelayCommand]
    private async Task AddPeerManuallyAsync()
    {
        if (!int.TryParse(PeerPortInput, out var port)) return;
        if (string.IsNullOrWhiteSpace(PeerIpInput)) return;

        StatusText = $"Connecting to {PeerIpInput}:{port}…";
        await _network.ConnectAndExchangeAsync(PeerIpInput, port);
        PeerIpInput = PeerPortInput = string.Empty;
    }

    // ── Event handlers from Core ─────────────────────────────────────────

    private void OnPeerConnected(object? sender, PeerUser peer)
    {
        Application.Current.Dispatcher.Invoke(() => UpsertOnlineUser(peer));
        _ = Task.Run(async () =>
        {
            var all = await _peerStore.GetAllAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                KnownPeerCount = all.Count;
                OnPropertyChanged(nameof(NetworkSummary));
            });
        });
    }

    private void OnPeerDiscovered(object? sender, DiscoveredPeer peer)
        => _ = Task.Run(() => _network.ConnectAndExchangeAsync(peer.IpAddress, peer.GrpcPort));

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

    private void OnGrpcMessageReceived(object? sender, ChatMessageProto msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedPeer?.UserName == msg.FromUser)
                AddMessageToUi(msg.Id, msg.FromUser, msg.ToUser, msg.Content,
                    DateTime.TryParse(msg.Timestamp, out var ts) ? ts : DateTime.Now,
                    isMine: false);
            else
            {
                var peer = OnlineUsers.FirstOrDefault(u => u.UserName == msg.FromUser);
                if (peer is not null) peer.UnreadCount++;
            }
        });

        _ = Task.Run(async () =>
        {
            await _history.SaveAsync(msg.FromUser, msg.ToUser, msg.Content);
            if (_api is not null && ApiConnected)
                await _api.SaveMessageAsync(new SaveMessageRequest
                    { FromUser = msg.FromUser, ToUser = msg.ToUser, Content = msg.Content });
        });
    }

    private void OnTypingIndicatorReceived(object? sender, (string FromUser, bool IsTyping) e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (SelectedPeer?.UserName == e.FromUser)
                IsPeerTyping = e.IsTyping;
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void UpsertOnlineUser(PeerUser peer)
    {
        var existing = OnlineUsers.FirstOrDefault(u => u.UserName == peer.UserName);
        if (existing is null) OnlineUsers.Add(peer);
        else
        {
            existing.IpAddress   = peer.IpAddress;
            existing.GrpcPort    = peer.GrpcPort;
            existing.DisplayName = peer.DisplayName;
            existing.Status      = UserStatus.Online;
        }
        OnPropertyChanged(nameof(NetworkSummary));
    }

    private void AddMessageToUi(string id, string from, string to, string content,
        DateTime ts, bool isMine)
        => Messages.Add(new ChatMessage(id, from, to, content, ts, isMine));

    private static int FindFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
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


