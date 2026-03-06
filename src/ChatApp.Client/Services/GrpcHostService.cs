using Grpc.AspNetCore.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatApp.Client.Services;

/// <summary>
/// Hosts the gRPC server within the WPF process so peers can connect directly.
/// Each running instance listens on a unique local port.
/// </summary>
public class GrpcHostService
{
    private WebApplication? _app;
    private int _port;

    public PeerChatService ChatService { get; } = new PeerChatService();
    public int Port => _port;

    public async Task StartAsync(int port)
    {
        _port = port;

        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders(); // Keep WPF output clean

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenLocalhost(port, listenOptions =>
            {
                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = true;
        });

        // Register our singleton so DI can inject it
        builder.Services.AddSingleton(ChatService);

        _app = builder.Build();
        _app.MapGrpcService<PeerChatService>();

        await _app.StartAsync();
    }

    public async Task StopAsync()
    {
        if (_app is not null)
            await _app.StopAsync();
    }
}
