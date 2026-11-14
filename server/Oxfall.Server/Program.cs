using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oxfall.Core.Net;
using Oxfall.Server;

// Oxfall game server. Listens on PORT (default 7777): GET / says it is alive, /ws is the game.
// The island seed comes from OXFALL_SEED; changing it is the weekly wipe.
var port = Environment.GetEnvironmentVariable("PORT") ?? "7777";
int seed = int.TryParse(Environment.GetEnvironmentVariable("OXFALL_SEED"), out var s) ? s : 20261113;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

var game = new GameServer(seed, app.Logger);
var stopping = app.Lifetime.ApplicationStopping;

app.MapGet("/", (Func<string>)(() => $"Oxfall server · protocol {Protocol.Version} · seed {game.Seed} · {game.PlayerCount} online"));
app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    string address = context.Request.Headers["X-Forwarded-For"].ToString();
    if (string.IsNullOrEmpty(address)) address = context.Connection.RemoteIpAddress?.ToString() ?? "?";
    await game.Serve(socket, address, stopping);
});

_ = Task.Run(async () =>
{
    try { await game.RunTicks(stopping); }
    catch (OperationCanceledException) { }
});

app.Logger.LogInformation("Oxfall server on port {Port}, seed {Seed}", port, seed);
app.Run("http://0.0.0.0:" + port);
