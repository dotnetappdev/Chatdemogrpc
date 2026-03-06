using ChatApp.Shared.Grpc;
using Grpc.Core;

namespace ChatApp.Client.Services;

/// <summary>
/// P2P gRPC service implementation - each WPF instance hosts this.
/// Peers connect directly to this endpoint to exchange messages.
/// </summary>
public class PeerChatService : ChatService.ChatServiceBase
{
    private readonly List<IServerStreamWriter<ChatMessageProto>> _subscribers = [];
    private readonly object _lock = new();

    /// <summary>Raised when a message is received from a peer</summary>
    public event EventHandler<ChatMessageProto>? MessageReceived;

    /// <summary>Raised when a peer starts/stops typing</summary>
    public event EventHandler<(string FromUser, bool IsTyping)>? TypingIndicatorReceived;

    // A peer calls this to send us a message directly
    public override Task<SendMessageResponse> SendMessage(
        SendMessageRequest request, ServerCallContext context)
    {
        var msg = request.Message;
        if (msg is null)
            return Task.FromResult(new SendMessageResponse { Success = false, Error = "Empty message" });

        // Broadcast to all local subscribers (our UI)
        BroadcastToSubscribers(msg);
        MessageReceived?.Invoke(this, msg);

        return Task.FromResult(new SendMessageResponse
        {
            Success = true,
            MessageId = msg.Id
        });
    }

    // A peer subscribes to our stream to receive pushed messages
    public override async Task Subscribe(
        SubscribeRequest request,
        IServerStreamWriter<ChatMessageProto> responseStream,
        ServerCallContext context)
    {
        lock (_lock)
            _subscribers.Add(responseStream);

        try
        {
            // Keep the stream open until the client disconnects
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (_lock)
                _subscribers.Remove(responseStream);
        }
    }

    // Ping - let a peer check if we are alive
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        return Task.FromResult(new PingResponse
        {
            Online = true,
            UserName = LocalUserContext.CurrentUserName,
            DisplayName = LocalUserContext.CurrentDisplayName
        });
    }

    // Receive a typing indicator from a peer
    public override Task<TypingResponse> SendTypingIndicator(
        TypingRequest request, ServerCallContext context)
    {
        TypingIndicatorReceived?.Invoke(this, (request.FromUser, request.IsTyping));
        return Task.FromResult(new TypingResponse { Acknowledged = true });
    }

    private void BroadcastToSubscribers(ChatMessageProto message)
    {
        List<IServerStreamWriter<ChatMessageProto>> snapshot;
        lock (_lock)
            snapshot = [.. _subscribers];

        foreach (var sub in snapshot)
        {
            _ = Task.Run(async () =>
            {
                try { await sub.WriteAsync(message); }
                catch { /* subscriber disconnected */ }
            });
        }
    }
}

/// <summary>Simple static context holding the current user's identity</summary>
public static class LocalUserContext
{
    public static string CurrentUserName { get; set; } = string.Empty;
    public static string CurrentDisplayName { get; set; } = string.Empty;
}
