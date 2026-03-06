using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChatApp.Client.Services;

/// <summary>
/// UDP broadcast-based peer discovery.
///
/// How it works:
///   1. On start, broadcasts a "hello" packet to the subnet (255.255.255.255).
///   2. Sends a heartbeat "hello" every <see cref="HeartbeatInterval"/> seconds.
///   3. Listens for "hello" packets from other peers and raises <see cref="PeerDiscovered"/>.
///   4. Listens for "bye" packets and raises <see cref="PeerLeft"/>.
///   5. Evicts peers that have not been heard from for <see cref="StaleTimeout"/>.
///   6. On stop, broadcasts a "bye" packet.
///
/// No server, no configuration — works on any IPv4 LAN.
/// </summary>
public sealed class UdpDiscoveryService : IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────
    public const int DiscoveryPort   = 45678;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaleTimeout      = TimeSpan.FromSeconds(50);

    // ── Events ────────────────────────────────────────────────────────────
    public event EventHandler<DiscoveredPeer>? PeerDiscovered;
    public event EventHandler<string>?         PeerLeft;   // userName

    // ── State ─────────────────────────────────────────────────────────────
    private string _userName    = string.Empty;
    private string _displayName = string.Empty;
    private int    _grpcPort;

    private UdpClient?           _listener;
    private CancellationTokenSource _cts = new();

    /// <summary>Peers seen recently: userName → last-seen UTC</summary>
    private readonly Dictionary<string, DateTime> _lastSeen = [];
    private readonly object _lock = new();

    // ── Public API ────────────────────────────────────────────────────────

    public async Task StartAsync(string userName, string displayName, int grpcPort)
    {
        _userName    = userName;
        _displayName = displayName;
        _grpcPort    = grpcPort;
        _cts         = new CancellationTokenSource();

        // Bind listener
        _listener = new UdpClient();
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _listener.EnableBroadcast = true;

        // Announce ourselves
        await BroadcastAsync(DiscoveryAction.Hello);

        // Background: listen + heartbeat + stale-eviction
        _ = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        _ = Task.Run(() => HeartbeatLoopAsync(_cts.Token), _cts.Token);
        _ = Task.Run(() => StaleCheckLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task StopAsync()
    {
        await BroadcastAsync(DiscoveryAction.Bye);
        _cts.Cancel();
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
    }

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    // ── Private ───────────────────────────────────────────────────────────

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(ct);
                var json   = Encoding.UTF8.GetString(result.Buffer);
                var packet = JsonSerializer.Deserialize<DiscoveryPacket>(json);
                if (packet is null) continue;

                // Ignore our own broadcasts
                if (packet.UserName == _userName) continue;

                var senderIp = result.RemoteEndPoint.Address.ToString();

                if (packet.Action == DiscoveryAction.Bye)
                {
                    lock (_lock) _lastSeen.Remove(packet.UserName);
                    PeerLeft?.Invoke(this, packet.UserName);
                }
                else // Hello / Heartbeat
                {
                    bool isNew;
                    lock (_lock)
                    {
                        isNew = !_lastSeen.ContainsKey(packet.UserName);
                        _lastSeen[packet.UserName] = DateTime.UtcNow;
                    }

                    var peer = new DiscoveredPeer(
                        packet.UserName,
                        packet.DisplayName,
                        senderIp,
                        packet.GrpcPort);

                    PeerDiscovered?.Invoke(this, peer);

                    // If this is a fresh peer, reply so they discover us too
                    if (isNew)
                        await BroadcastAsync(DiscoveryAction.Hello);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* socket closed or parse error — keep going */ }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, ct);
                await BroadcastAsync(DiscoveryAction.Hello);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore */ }
        }
    }

    private async Task StaleCheckLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(StaleTimeout, ct);

                List<string> evicted;
                lock (_lock)
                {
                    var cutoff = DateTime.UtcNow - StaleTimeout;
                    evicted = _lastSeen
                        .Where(kv => kv.Value < cutoff)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var u in evicted) _lastSeen.Remove(u);
                }

                foreach (var u in evicted)
                    PeerLeft?.Invoke(this, u);
            }
            catch (OperationCanceledException) { break; }
            catch { /* ignore */ }
        }
    }

    private async Task BroadcastAsync(DiscoveryAction action)
    {
        try
        {
            var packet = new DiscoveryPacket
            {
                Action      = action,
                UserName    = _userName,
                DisplayName = _displayName,
                GrpcPort    = _grpcPort
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(packet);

            using var sender = new UdpClient();
            sender.EnableBroadcast = true;
            var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);
            await sender.SendAsync(bytes, bytes.Length, endpoint);
        }
        catch { /* best-effort */ }
    }
}

// ── Data classes ──────────────────────────────────────────────────────────

public enum DiscoveryAction { Hello, Bye }

public class DiscoveryPacket
{
    [JsonPropertyName("action")]
    public DiscoveryAction Action { get; set; }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("grpcPort")]
    public int GrpcPort { get; set; }
}

public record DiscoveredPeer(
    string UserName,
    string DisplayName,
    string IpAddress,
    int GrpcPort);
