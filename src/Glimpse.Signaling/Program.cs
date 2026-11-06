using System.Threading.RateLimiting;
using Glimpse.RoomProtocol;
using Glimpse.Signaling;
using Microsoft.AspNetCore.RateLimiting;

var app = SignalingApplication.Build(args);
await app.RunAsync();

public static class SignalingApplication
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 150000);
        // Room codes are invitation secrets: do not log request paths or bodies.
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<IceConfiguration>();
        builder.Services.AddSingleton<RoomStore>();
        builder.Services.AddHostedService<RoomCleanup>();
        builder.Services.AddRateLimiter(options => {
            options.RejectionStatusCode = 429;
            options.AddFixedWindowLimiter("admission", limiter => { limiter.PermitLimit = 120; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; });
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("all",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10000, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        configure?.Invoke(builder);
        var app = builder.Build();
        // Validate relay configuration at startup, before accepting any room.
        var ice = app.Services.GetRequiredService<IceConfiguration>();
        app.Use(async (context, next) => {
            context.Response.Headers.CacheControl = "no-store";
            try { await next(context); }
            catch (RoomFailure ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new RoomError(ex.Message)); }
        });
        app.UseRateLimiter();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", relayConfigured = ice.RelayConfigured }));
        app.MapPost("/v1/rooms", (RoomStore store) => store.Create()).RequireRateLimiting("admission");
        app.MapPost("/v1/rooms/{code}/join", (string code, RoomStore store) => store.Join(code)).RequireRateLimiting("admission");
        app.MapGet("/v1/rooms/{code}", (string code, HttpContext context, RoomStore store) => store.Read(code, Token(context)));
        app.MapPut("/v1/rooms/{code}/{kind}", (string code, string kind, SignalMessage message, HttpContext context, RoomStore store) => {
            store.Publish(code, Token(context), kind, message.Sdp); return Results.NoContent();
        });
        app.MapDelete("/v1/rooms/{code}", (string code, HttpContext context, RoomStore store) => { store.Close(code, Token(context)); return Results.NoContent(); });
        return app;
    }
    private static string Token(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) throw new RoomFailure(401, "Acesso à sala inválido.");
        return header[7..];
    }
}

public sealed class RoomCleanup(RoomStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) store.Prune(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
