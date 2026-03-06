using ChatApp.Core.Interfaces;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Shared.Grpc;
using Moq;

namespace ChatApp.Core.Tests;

public sealed class PeerNetworkServiceTests : LocalDbTestBase
{
    private readonly Mock<IPeerChannelFactory>  _factoryMock = new();
    private readonly Mock<IPeerChannel>         _channelMock = new();
    private readonly Mock<IOfflineQueueService> _queueMock   = new();
    private readonly KnownPeersStore            _store;
    private readonly PeerNetworkService         _sut;

    public PeerNetworkServiceTests()
    {
        _store = new KnownPeersStore(Db);

        _factoryMock
            .Setup(f => f.Get(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(_channelMock.Object);

        // Default: queue is always empty so drain is a no-op
        _queueMock
            .Setup(q => q.GetPendingWithIdsAsync(It.IsAny<string>()))
            .ReturnsAsync([]);

        _sut = new PeerNetworkService(_factoryMock.Object, _store, _queueMock.Object)
        {
            LocalUserName = "localuser"
        };
    }

    // ── ConnectAndExchangeAsync ───────────────────────────────────────────

    [Fact]
    public async Task ConnectAndExchange_DoesNothing_WhenPingFails()
    {
        _channelMock
            .Setup(c => c.PingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PingResponse?)null);

        PeerUser? connected = null;
        _sut.PeerConnected += (_, p) => connected = p;

        await _sut.ConnectAndExchangeAsync("1.2.3.4", 5000);

        Assert.Null(connected);
        Assert.Empty(await _store.GetAllAsync());
    }

    [Fact]
    public async Task ConnectAndExchange_PersistsPeer_WhenPingSucceeds()
    {
        _channelMock
            .Setup(c => c.PingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PingResponse { Online = true, UserName = "alice", DisplayName = "Alice" });

        _channelMock
            .Setup(c => c.GetKnownPeersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetKnownPeersResponse()); // no additional peers

        await _sut.ConnectAndExchangeAsync("192.168.1.1", 50051);

        var saved = await _store.GetAllAsync();
        Assert.Single(saved);
        Assert.Equal("alice",       saved[0].UserName);
        Assert.Equal("192.168.1.1", saved[0].IpAddress);
        Assert.Equal(50051,         saved[0].GrpcPort);
    }

    [Fact]
    public async Task ConnectAndExchange_RaisesPeerConnected_WhenPingSucceeds()
    {
        _channelMock
            .Setup(c => c.PingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PingResponse { Online = true, UserName = "alice", DisplayName = "Alice" });

        _channelMock
            .Setup(c => c.GetKnownPeersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetKnownPeersResponse());

        PeerUser? raised = null;
        _sut.PeerConnected += (_, p) => raised = p;

        await _sut.ConnectAndExchangeAsync("192.168.1.1", 50051);

        Assert.NotNull(raised);
        Assert.Equal("alice", raised.UserName);
    }

    [Fact]
    public async Task ConnectAndExchange_DrainsPendingQueue_WhenPingSucceeds()
    {
        var msg = new ChatMessageProto { Id = "1", FromUser = "localuser", ToUser = "alice", Content = "queued" };

        _channelMock
            .Setup(c => c.PingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PingResponse { Online = true, UserName = "alice", DisplayName = "Alice" });
        _channelMock
            .Setup(c => c.GetKnownPeersAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetKnownPeersResponse());
        _channelMock
            .Setup(c => c.SendMessageAsync(msg, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _queueMock
            .Setup(q => q.GetPendingWithIdsAsync("alice"))
            .ReturnsAsync([(42, msg)]);

        (string ToUser, int Count)? delivered = null;
        _sut.PendingMessagesDelivered += (_, e) => delivered = e;

        await _sut.ConnectAndExchangeAsync("192.168.1.1", 50051);

        Assert.NotNull(delivered);
        Assert.Equal("alice", delivered!.Value.ToUser);
        Assert.Equal(1,       delivered.Value.Count);

        _queueMock.Verify(q => q.RemoveAsync(42), Times.Once);
    }

    // ── SendMessageAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_ReturnsFalse_WhenChannelFails()
    {
        _channelMock
            .Setup(c => c.SendMessageAsync(It.IsAny<ChatMessageProto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var peer   = new PeerUser { IpAddress = "1.2.3.4", GrpcPort = 5000 };
        var result = await _sut.SendMessageAsync(peer, new ChatMessageProto { Content = "test" });

        Assert.False(result);
    }

    [Fact]
    public async Task SendMessage_ReturnsTrue_WhenChannelSucceeds()
    {
        _channelMock
            .Setup(c => c.SendMessageAsync(It.IsAny<ChatMessageProto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var peer   = new PeerUser { IpAddress = "1.2.3.4", GrpcPort = 5000 };
        var result = await _sut.SendMessageAsync(peer, new ChatMessageProto { Content = "hello" });

        Assert.True(result);
    }
}
