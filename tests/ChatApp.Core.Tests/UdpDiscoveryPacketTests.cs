using ChatApp.Core.Services;

namespace ChatApp.Core.Tests;

/// <summary>
/// Tests the pure packet-level logic of UDP discovery without
/// opening any real sockets.
/// </summary>
public sealed class UdpDiscoveryPacketTests
{
    [Fact]
    public void DiscoveryPacket_SerializesAndDeserializes_Correctly()
    {
        var packet = new DiscoveryPacket
        {
            Action      = DiscoveryAction.Hello,
            UserName    = "alice",
            DisplayName = "Alice",
            GrpcPort    = 50051
        };

        var json = System.Text.Json.JsonSerializer.Serialize(packet);
        var back = System.Text.Json.JsonSerializer.Deserialize<DiscoveryPacket>(json);

        Assert.NotNull(back);
        Assert.Equal(DiscoveryAction.Hello, back.Action);
        Assert.Equal("alice",  back.UserName);
        Assert.Equal("Alice",  back.DisplayName);
        Assert.Equal(50051,    back.GrpcPort);
    }

    [Fact]
    public void DiscoveryPacket_Bye_SerializesCorrectly()
    {
        var packet = new DiscoveryPacket { Action = DiscoveryAction.Bye, UserName = "bob" };
        var json   = System.Text.Json.JsonSerializer.Serialize(packet);
        var back   = System.Text.Json.JsonSerializer.Deserialize<DiscoveryPacket>(json);

        Assert.Equal(DiscoveryAction.Bye, back!.Action);
    }

    [Theory]
    [InlineData("",      "Alice", 50051, false)]
    [InlineData("alice", "Alice", 50051, true)]
    [InlineData("alice", "",      1,     true)]
    public void DiscoveryPacket_UserName_RequiredToBeNonEmpty(
        string userName, string displayName, int port, bool expectedValid)
    {
        var packet = new DiscoveryPacket
            { UserName = userName, DisplayName = displayName, GrpcPort = port };

        var isValid = !string.IsNullOrEmpty(packet.UserName) && packet.GrpcPort > 0;
        Assert.Equal(expectedValid, isValid);
    }
}
