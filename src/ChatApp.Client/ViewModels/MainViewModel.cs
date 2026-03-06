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
    // ── Core services ─────────────────────────────────────────────────────
    private readonly LocalDb              _db;
    private readonly IHistoryService      _history;
    private readonly IKnownPeersStore     _peerStore;
    private readonly IDiscoveryService    _discovery;
    private readonly IPeerNetworkService  _network;
    private readonly GrpcHostService      _grpcHost;
    private readonly IContactService      _contacts;
    private readonly IOfflineQueueService _offlineQueue;
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

    [ObservableProperty] private string _messageInput   = string.Empty;
    [ObservableProperty] private string _statusText     = "Not connected";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _peerIpInput    = string.Empty;
    [ObservableProperty] private string _peerPortInput  = string.Empty;
    [ObservableProperty] private int    _localGrpcPort;
    [ObservableProperty] private bool   _isPeerTyping;
    [ObservableProperty] private bool   _apiConnected;

    /// <summary>Add-contact textbox on the Contacts panel.</summary>
    [ObservableProperty] private string _addContactUserName = string.Empty;

    /// <summary>Shown as an in-app notification banner (empty = hidden).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotification))]
    private string _notificationText = string.Empty;

    public bool HasNotification => !string.IsNullOrEmpty(NotificationText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NetworkSummary))]
    private int _knownPeerCount;

    public bool IsLoggedIn      => !string.IsNullOrEmpty(CurrentUserName);
    public bool HasSelectedPeer => SelectedPeer is not null;
    public string NetworkSummary =>
        $"{OnlineUsers.Count} online  ·  {KnownPeerCount} ever seen";

    public ObservableCollection<PeerUser>         OnlineUsers      { get; } = [];
    public ObservableCollection<ChatMessage>       Messages         { get; } = [];
    /// <summary>All saved contacts (accepted + pending + blocked).</summary>
    public ObservableCollection<ContactViewModel>  Contacts         { get; } = [];
    /// <summary>Incoming pending contact requests — shown as a notification list.</summary>
    public ObservableCollection<ContactViewModel>  PendingRequests  { get; } = [];

    // ── Constructor ──────────────────────────────────────────────────────

    public MainViewModel()
    {
        _db           = LocalDb.CreateForProduction();
        _peerStore    = new KnownPeersStore(_db);
        _history      = new HistoryService(_db);
        _contacts     = new ContactService(_db);
        _offlineQueue = new OfflineQueueService(_db);

        var channelFactory = new GrpcPeerChannelFactory();
        _network           = new PeerNetworkService(channelFactory, _peerStore, _offlineQueue);
        _discovery         = new UdpDiscoveryService();

        var chatSvc = new PeerChatService(_peerStore, _contacts);
        _grpcHost   = new GrpcHostService(chatSvc);

        chatSvc.MessageReceived                  += OnGrpcMessageReceived;
        chatSvc.TypingIndicatorReceived          += OnTypingIndicatorReceived;
        _network.PeerConnected                   += OnPeerConnected;
        _network.PendingMessagesDelivered        += OnPendingMessagesDelivered;
        _discovery.PeerDiscovered                += OnPeerDiscovered;
        _discovery.PeerLeft                      += OnPeerLeft;
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

            // ── Load saved contacts ───────────────────────────────────────
            await RefreshContactsAsync();

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

        ApiConnected    = false;
        NotificationText = string.Empty;
        OnlineUsers.Clear();
        Messages.Clear();
        Contacts.Clear();
        PendingRequests.Clear();
        SelectedPeer    = null;
        StatusText      = "Not connected";
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (SelectedPeer is null || string.IsNullOrWhiteSpace(MessageInput)) return;

        var content  = MessageInput.Trim();
        MessageInput = string.Empty;

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
            {
                // Peer is offline — queue for later delivery
                await _offlineQueue.EnqueueAsync(proto);
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"📭 {SelectedPeer.DisplayName} is offline — message queued");
            }

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

    // ── Contact commands ──────────────────────────────────────────────────

    /// <summary>
    /// Send a contact request to a peer that is currently online.
    /// The request is delivered via the standard gRPC SendMessage RPC using
    /// message type CONTACT_REQUEST. If the peer is offline the request is
    /// queued and delivered automatically when they come back online.
    /// </summary>
    [RelayCommand]
    private async Task SendContactRequestAsync()
    {
        var target = AddContactUserName.Trim();
        if (string.IsNullOrEmpty(target) || target == CurrentUserName) return;

        // Check we haven't already sent/have a relationship with this user
        var existing = await _contacts.GetAsync(target);
        if (existing is not null)
        {
            StatusText = $"Already have a contact relationship with {target}";
            return;
        }

        var proto = new ChatMessageProto
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = CurrentUserName,
            ToUser    = target,
            Content   = CurrentDisplayName, // display name carried as content
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type      = MessageType.ContactRequest
        };

        // Save outgoing pending record locally
        await _contacts.UpsertAsync(new ContactEntry(
            target, target, ContactStatus.Pending, IsIncoming: false, DateTime.UtcNow));
        await RefreshContactsAsync();

        // Deliver if online, queue if offline
        var peer = OnlineUsers.FirstOrDefault(u => u.UserName == target);
        if (peer is not null)
        {
            await _network.SendMessageAsync(peer, proto);
            StatusText = $"📨 Contact request sent to {target}";
        }
        else
        {
            await _offlineQueue.EnqueueAsync(proto);
            StatusText = $"📭 Contact request queued — {target} is offline";
        }

        AddContactUserName = string.Empty;
    }

    [RelayCommand]
    private async Task AcceptContactAsync(ContactViewModel contact)
    {
        await _contacts.UpsertAsync(new ContactEntry(
            contact.UserName, contact.DisplayName,
            ContactStatus.Accepted, IsIncoming: true, DateTime.UtcNow));

        // Send acceptance back to the requester
        var proto = new ChatMessageProto
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = CurrentUserName,
            ToUser    = contact.UserName,
            Content   = CurrentDisplayName,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type      = MessageType.ContactAccepted
        };

        var peer = OnlineUsers.FirstOrDefault(u => u.UserName == contact.UserName);
        if (peer is not null)
            await _network.SendMessageAsync(peer, proto);
        else
            await _offlineQueue.EnqueueAsync(proto);

        await RefreshContactsAsync();
        StatusText = $"✅ You are now contacts with {contact.DisplayName}";
    }

    [RelayCommand]
    private async Task DeclineContactAsync(ContactViewModel contact)
    {
        // Send decline notification if the requester is online
        var proto = new ChatMessageProto
        {
            Id        = Guid.NewGuid().ToString(),
            FromUser  = CurrentUserName,
            ToUser    = contact.UserName,
            Content   = string.Empty,
            Timestamp = DateTime.UtcNow.ToString("o"),
            Type      = MessageType.ContactDeclined
        };

        var peer = OnlineUsers.FirstOrDefault(u => u.UserName == contact.UserName);
        if (peer is not null)
            await _network.SendMessageAsync(peer, proto);
        else
            await _offlineQueue.EnqueueAsync(proto);

        // Store as declined so we don't prompt again
        await _contacts.UpsertAsync(new ContactEntry(
            contact.UserName, contact.DisplayName,
            ContactStatus.Declined, IsIncoming: true, DateTime.UtcNow));

        await RefreshContactsAsync();
        StatusText = $"❌ Declined contact request from {contact.DisplayName}";
    }

    [RelayCommand]
    private async Task BlockContactAsync(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return;

        var existing = await _contacts.GetAsync(userName);
        await _contacts.UpsertAsync(new ContactEntry(
            userName,
            existing?.DisplayName ?? userName,
            ContactStatus.Blocked,
            existing?.IsIncoming ?? false,
            DateTime.UtcNow));

        await RefreshContactsAsync();
        StatusText = $"🚫 {userName} blocked";
    }

    [RelayCommand]
    private async Task UnblockContactAsync(string userName)
    {
        await _contacts.RemoveAsync(userName);
        await RefreshContactsAsync();
        StatusText = $"✅ {userName} unblocked";
    }

    /// <summary>Dismiss the in-app notification banner.</summary>
    [RelayCommand]
    private void DismissNotification() => NotificationText = string.Empty;

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

    private void OnPendingMessagesDelivered(object? sender, (string ToUser, int Count) e)
    {
        // This fires on a background thread — marshal to UI thread for notification
        Application.Current.Dispatcher.Invoke(() =>
        {
            var msg = e.Count == 1
                ? $"📬 1 queued message delivered to {e.ToUser}"
                : $"📬 {e.Count} queued messages delivered to {e.ToUser}";
            ShowNotification(msg);
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
        Application.Current.Dispatcher.Invoke(() => HandleIncomingMessage(msg));

        _ = Task.Run(async () =>
        {
            // Only persist TEXT messages to history
            if (msg.Type == MessageType.Text)
            {
                await _history.SaveAsync(msg.FromUser, msg.ToUser, msg.Content);
                if (_api is not null && ApiConnected)
                    await _api.SaveMessageAsync(new SaveMessageRequest
                        { FromUser = msg.FromUser, ToUser = msg.ToUser, Content = msg.Content });
            }
        });
    }

    private void HandleIncomingMessage(ChatMessageProto msg)
    {
        switch (msg.Type)
        {
            case MessageType.Text:
                if (SelectedPeer?.UserName == msg.FromUser)
                    AddMessageToUi(msg.Id, msg.FromUser, msg.ToUser, msg.Content,
                        DateTime.TryParse(msg.Timestamp, out var ts) ? ts : DateTime.Now,
                        isMine: false);
                else
                {
                    var peer = OnlineUsers.FirstOrDefault(u => u.UserName == msg.FromUser);
                    if (peer is not null) peer.UnreadCount++;
                }
                break;

            case MessageType.ContactRequest:
                _ = Task.Run(() => HandleContactRequestAsync(msg));
                break;

            case MessageType.ContactAccepted:
                _ = Task.Run(() => HandleContactAcceptedAsync(msg));
                break;

            case MessageType.ContactDeclined:
                _ = Task.Run(() => HandleContactDeclinedAsync(msg));
                break;
        }
    }

    private async Task HandleContactRequestAsync(ChatMessageProto msg)
    {
        // Save incoming pending request (content = sender's display name)
        var displayName = string.IsNullOrWhiteSpace(msg.Content) ? msg.FromUser : msg.Content;
        await _contacts.UpsertAsync(new ContactEntry(
            msg.FromUser, displayName,
            ContactStatus.Pending, IsIncoming: true, DateTime.UtcNow));

        Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshContactsAsync().ConfigureAwait(false);
            ShowNotification($"📩 {displayName} wants to add you as a contact");
        });
    }

    private async Task HandleContactAcceptedAsync(ChatMessageProto msg)
    {
        var displayName = string.IsNullOrWhiteSpace(msg.Content) ? msg.FromUser : msg.Content;
        await _contacts.UpsertAsync(new ContactEntry(
            msg.FromUser, displayName,
            ContactStatus.Accepted, IsIncoming: false, DateTime.UtcNow));

        Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshContactsAsync().ConfigureAwait(false);
            ShowNotification($"✅ {displayName} accepted your contact request");
        });
    }

    private async Task HandleContactDeclinedAsync(ChatMessageProto msg)
    {
        var existing = await _contacts.GetAsync(msg.FromUser);
        await _contacts.UpsertAsync(new ContactEntry(
            msg.FromUser,
            existing?.DisplayName ?? msg.FromUser,
            ContactStatus.Declined, IsIncoming: false, DateTime.UtcNow));

        Application.Current.Dispatcher.Invoke(() =>
        {
            RefreshContactsAsync().ConfigureAwait(false);
            ShowNotification($"❌ {msg.FromUser} declined your contact request");
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

    private async Task RefreshContactsAsync()
    {
        var all     = await _contacts.GetByStatusAsync(
            ContactStatus.Pending, ContactStatus.Accepted, ContactStatus.Blocked);
        var pending = all.Where(c => c.IsIncoming && c.Status == ContactStatus.Pending).ToList();

        Application.Current.Dispatcher.Invoke(() =>
        {
            Contacts.Clear();
            foreach (var c in all) Contacts.Add(ContactViewModel.FromEntry(c));

            PendingRequests.Clear();
            foreach (var c in pending) PendingRequests.Add(ContactViewModel.FromEntry(c));

            OnPropertyChanged(nameof(PendingRequests));
        });
    }

    private void ShowNotification(string text)
    {
        NotificationText = text;
        // Auto-dismiss after 8 seconds
        _ = Task.Run(async () =>
        {
            await Task.Delay(8000);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (NotificationText == text) // only if still showing this notification
                    NotificationText = string.Empty;
            });
        });
    }

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
