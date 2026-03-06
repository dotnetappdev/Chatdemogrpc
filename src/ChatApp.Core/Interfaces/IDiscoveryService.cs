using ChatApp.Core.Services;

namespace ChatApp.Core.Interfaces;

/// <summary>
/// Broadcasts presence over UDP so peers on the same LAN discover each other
/// automatically — no server or manual configuration needed.
/// </summary>
public interface IDiscoveryService
{
    event EventHandler<DiscoveredPeer>? PeerDiscovered;
    event EventHandler<string>?         PeerLeft;        // userName

    Task StartAsync(string userName, string displayName, int grpcPort);
    Task StopAsync();
}

/// <summary>A peer announced via UDP broadcast.</summary>
public record DiscoveredPeer(
    string UserName,
    string DisplayName,
    string IpAddress,
    int    GrpcPort);
