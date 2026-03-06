using ChatApp.Core.Interfaces;
using ChatApp.Shared.Grpc;
using Grpc.Core;

namespace ChatApp.Core.Services;

/// <summary>
/// gRPC service implementation hosted inside every WPF process.
/// Peers connect directly to this endpoint — no central server.
/// </summary>
public sealed class PeerChatService : ChatService.ChatServiceBase
{
    private readonly IKnownPeersStore  _knownPeers;
    private readonly IContactService   _contacts;
    private readonly List<IServerStreamWriter<ChatMessageProto>> _subscribers = [];
    private readonly object _lock = new();

    public event EventHandler<ChatMessageProto>?                 MessageReceived;
    public event EventHandler<(string FromUser, bool IsTyping)>? TypingIndicatorReceived;

    public PeerChatService(IKnownPeersStore knownPeers, IContactService contacts)
    {
        _knownPeers = knownPeers;
        _contacts   = contacts;
    }

    // ── Unary: receive a message ──────────────────────────────────────────

    public override async Task<SendMessageResponse> SendMessage(
        SendMessageRequest request, ServerCallContext context)
    {
        var msg = request.Message;
        if (msg is null)
            return new SendMessageResponse { Success = false, Error = "Empty message" };

        // Silently discard messages from blocked users (shadow-block: sender
        // gets Success=true so they don't know they are blocked).
        if (await _contacts.IsBlockedAsync(msg.FromUser))
            return new SendMessageResponse { Success = true, MessageId = msg.Id };

        PushToSubscribers(msg);
        MessageReceived?.Invoke(this, msg);

        return new SendMessageResponse { Success = true, MessageId = msg.Id };
    }

    // ── Server-streaming: push messages to a connected subscriber ─────────

    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<ChatMessageProto> responseStream,
        ServerCallContext context)
    {
        lock (_lock) _subscribers.Add(responseStream);
        try   { await Task.Delay(Timeout.Infinite, context.CancellationToken); }
        catch (OperationCanceledException) { }
        finally { lock (_lock) _subscribers.Remove(responseStream); }
    }

    // ── Unary: health-check / identity ───────────────────────────────────

    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context) =>
        Task.FromResult(new PingResponse
        {
            Online      = true,
            UserName    = LocalUserContext.CurrentUserName,
            DisplayName = LocalUserContext.CurrentDisplayName
        });

    // ── Unary: typing indicator ───────────────────────────────────────────

    public override Task<TypingResponse> SendTypingIndicator(
        TypingRequest request, ServerCallContext context)
    {
        TypingIndicatorReceived?.Invoke(this, (request.FromUser, request.IsTyping));
        return Task.FromResult(new TypingResponse { Acknowledged = true });
    }

    // ── Unary: gossip / peer exchange ─────────────────────────────────────

    public override async Task<GetKnownPeersResponse> GetKnownPeers(
        GetKnownPeersRequest request, ServerCallContext context)
    {
        var all      = await _knownPeers.GetAllAsync();
        var response = new GetKnownPeersResponse();

        foreach (var p in all)
        {
            if (p.UserName == request.RequestingUser) continue;

            response.Peers.Add(new PeerInfo
            {
                UserName        = p.UserName,
                DisplayName     = p.DisplayName,
                IpAddress       = p.IpAddress,
                GrpcPort        = p.GrpcPort,
                LastSeenUnixUtc = new DateTimeOffset(p.LastSeenUtc).ToUnixTimeSeconds()
            });
        }

        return response;
    }

    // ── Internal broadcast ────────────────────────────────────────────────

    private void PushToSubscribers(ChatMessageProto message)
    {
        List<IServerStreamWriter<ChatMessageProto>> snapshot;
        lock (_lock) snapshot = [.. _subscribers];

        foreach (var sub in snapshot)
            _ = Task.Run(async () =>
            {
                try { await sub.WriteAsync(message); }
                catch { /* subscriber disconnected */ }
            });
    }
}
