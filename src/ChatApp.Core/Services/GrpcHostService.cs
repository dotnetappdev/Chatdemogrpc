using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ChatApp.Core.Services;

/// <summary>
/// Embeds a Kestrel gRPC server inside the WPF process.
/// Each running instance is a fully independent peer node.
/// </summary>
public sealed class GrpcHostService
{
    private WebApplication? _app;

    public PeerChatService ChatService { get; }

    public GrpcHostService(PeerChatService chatService)
        => ChatService = chatService;

    public async Task StartAsync(int port)
    {
        var builder = WebApplication.CreateBuilder();

        builder.Logging.ClearProviders();

        builder.WebHost.ConfigureKestrel(k =>
            k.ListenLocalhost(port, o => o.Protocols = HttpProtocols.Http2));

        builder.Services.AddGrpc(o => o.EnableDetailedErrors = true);
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
