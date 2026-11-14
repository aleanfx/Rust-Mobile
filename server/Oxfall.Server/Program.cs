using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oxfall.Core.Net;
using Oxfall.Core.World;
using Oxfall.Server;

// Oxfall game server. Listens on PORT (default 7777): GET / says it is alive, /ws is the game.
// The island seed comes from OXFALL_SEED: a number, or "weekly" for a new island every Thursday at
// 19:00 UTC (the server stops at wipe time and its host starts it again on the new island).
// OXFALL_SAVE is the save file (default oxfall-world.sav); OXFALL_DEV=1 is for automated tests
// only: clients may teleport and everyone wakes up on the same beach.
var port = Environment.GetEnvironmentVariable("PORT") ?? "7777";
string seedSetting = Environment.GetEnvironmentVariable("OXFALL_SEED") ?? "";
bool weekly = seedSetting == "weekly";
var wipe = WipeSchedule.LastWipe(DateTime.UtcNow);
int seed = weekly ? WipeSchedule.SeedFor(wipe) : int.TryParse(seedSetting, out var s) ? s : 20261113;
bool dev = Environment.GetEnvironmentVariable("OXFALL_DEV") == "1";
string savePath = Environment.GetEnvironmentVariable("OXFALL_SAVE") ?? "oxfall-world.sav";
if (savePath == "none") savePath = null;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });

var game = new GameServer(seed, dev, savePath, app.Logger);
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

// Saved once more on the way out; and on a weekly server, stopped at wipe time.
app.Lifetime.ApplicationStopping.Register(() =>
{
    game.SaveNow();
    app.Logger.LogInformation("Island saved");
});
if (weekly)
{
    var next = WipeSchedule.NextWipe(DateTime.UtcNow);
    app.Logger.LogInformation("Weekly island {Seed}, next wipe {Next:u}", seed, next);
    _ = Task.Run(async () =>
    {
        try
        {
            while (DateTime.UtcNow < next) await Task.Delay(TimeSpan.FromSeconds(30), stopping);
            app.Logger.LogInformation("Wipe time: stopping so the new island starts");
            app.Lifetime.StopApplication();
        }
        catch (OperationCanceledException) { }
    });
}

app.Logger.LogInformation("Oxfall server on port {Port}, seed {Seed}", port, seed);
app.Run("http://0.0.0.0:" + port);
