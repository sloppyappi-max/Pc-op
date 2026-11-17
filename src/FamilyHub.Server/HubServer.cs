using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace FamilyHub.Server;

/// <summary>
/// The web server behind the app. It listens on the whole home network so phones can reach it.
/// Requests from this PC are trusted; anything else must send the family PIN.
/// </summary>
public static class HubServer
{
    public const int DefaultPort = 5180;

    public static WebApplication Build(string dataDir, int port = DefaultPort)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = DataStore.JsonOptions.PropertyNamingPolicy;
        });
        builder.Services.AddSingleton(new DataStore(dataDir));

        var app = builder.Build();
        var store = app.Services.GetRequiredService<DataStore>();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // Phones should always get the latest UI after an update.
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
        });

        // PIN check for every API call that does not come from this PC.
        app.Use(async (ctx, next) =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api") && !IsLocal(ctx))
            {
                var pin = store.Read(d => d.Settings.Pin);
                var sent = ctx.Request.Headers["X-Pin"].ToString();
                if (sent != pin)
                {
                    await Task.Delay(500); // slows down guessing
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await ctx.Response.WriteAsJsonAsync(new { error = "PIN required" });
                    return;
                }
            }
            await next();
        });

        MapApi(app, store, port);
        return app;
    }

    private static bool IsLocal(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        return ip is not null && IPAddress.IsLoopback(ip);
    }

    private static void MapApi(WebApplication app, DataStore store, int port)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/whoami", (HttpContext ctx) => new { local = IsLocal(ctx) });

        // ---- Events ----
        api.MapGet("/events", () => store.Read(d => d.Events.OrderBy(e => e.Date).ToList()));

        api.MapPost("/events", (CalendarEvent e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Title)) return Results.BadRequest(new { error = "Title is required" });
            e.Id = Guid.NewGuid().ToString("N");
            store.Write(d => { d.Events.Add(e); return 0; });
            return Results.Ok(e);
        });

        api.MapPut("/events/{id}", (string id, CalendarEvent e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Title)) return Results.BadRequest(new { error = "Title is required" });
            e.Id = id;
            bool found = store.Write(d =>
            {
                int i = d.Events.FindIndex(x => x.Id == id);
                if (i < 0) return false;
                d.Events[i] = e;
                return true;
            });
            return found ? Results.Ok(e) : Results.NotFound();
        });

        api.MapDelete("/events/{id}", (string id) =>
            store.Write(d => d.Events.RemoveAll(x => x.Id == id)) > 0 ? Results.NoContent() : Results.NotFound());

        // ---- Jobs ----
        api.MapGet("/jobs", () => store.Read(d => d.Jobs.ToList()));

        api.MapPost("/jobs", (Job j) =>
        {
            if (string.IsNullOrWhiteSpace(j.Name)) return Results.BadRequest(new { error = "Job name is required" });
            j.Id = Guid.NewGuid().ToString("N");
            store.Write(d => { d.Jobs.Add(j); return 0; });
            return Results.Ok(j);
        });

        api.MapPut("/jobs/{id}", (string id, Job j) =>
        {
            if (string.IsNullOrWhiteSpace(j.Name)) return Results.BadRequest(new { error = "Job name is required" });
            j.Id = id;
            bool found = store.Write(d =>
            {
                int i = d.Jobs.FindIndex(x => x.Id == id);
                if (i < 0) return false;
                d.Jobs[i] = j;
                return true;
            });
            return found ? Results.Ok(j) : Results.NotFound();
        });

        api.MapDelete("/jobs/{id}", (string id) =>
            store.Write(d =>
            {
                // Days off that pointed at this job stay on the calendar, just unlinked.
                foreach (var e in d.Events.Where(e => e.JobId == id)) e.JobId = null;
                return d.Jobs.RemoveAll(x => x.Id == id);
            }) > 0 ? Results.NoContent() : Results.NotFound());

        // ---- Settings (the PIN is never sent back to phones) ----
        api.MapGet("/settings", (HttpContext ctx) => store.Read(d => new
        {
            d.Settings.FamilyName,
            d.Settings.Balance,
            d.Settings.BalanceAsOf,
        }));

        api.MapPut("/settings", (SettingsUpdate u) =>
        {
            store.Write(d =>
            {
                if (u.FamilyName is not null) d.Settings.FamilyName = u.FamilyName;
                if (u.Balance is not null)
                {
                    d.Settings.Balance = u.Balance.Value;
                    d.Settings.BalanceAsOf = u.BalanceAsOf ?? DateOnly.FromDateTime(DateTime.Today);
                }
                return 0;
            });
            return Results.NoContent();
        });

        // ---- Calendar + money ----
        api.MapGet("/calendar", (DateOnly from, DateOnly to) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 400)
                return Results.BadRequest(new { error = "Bad date range" });
            return Results.Ok(store.Read(d => Planner.Occurrences(d, from, to)));
        });

        api.MapGet("/summary", (int year, int month) =>
        {
            if (month is < 1 or > 12) return Results.BadRequest(new { error = "Bad month" });
            return Results.Ok(store.Read(d => Planner.Summarize(d, year, month)));
        });

        // ---- Phone connection info (only shown on the PC itself) ----
        api.MapGet("/connect", (HttpContext ctx) =>
        {
            if (!IsLocal(ctx)) return Results.Forbid();
            var urls = LanAddresses().Select(ip => $"http://{ip}:{port}").ToList();
            return Results.Ok(new { urls, pin = store.Read(d => d.Settings.Pin) });
        });

        api.MapPost("/connect/new-pin", (HttpContext ctx) =>
        {
            if (!IsLocal(ctx)) return Results.Forbid();
            var pin = store.Write(d => d.Settings.Pin = Random.Shared.Next(100000, 1000000).ToString());
            return Results.Ok(new { pin });
        });

        api.MapGet("/connect/qr.svg", (HttpContext ctx, string url) =>
        {
            if (!IsLocal(ctx)) return Results.Forbid();
            using var gen = new QRCodeGenerator();
            using var data = gen.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(6);
            return Results.Content(svg, "image/svg+xml");
        });
    }

    public record SettingsUpdate(string? FamilyName, decimal? Balance, DateOnly? BalanceAsOf);

    /// <summary>This PC's addresses on the home network (what a phone would type in).</summary>
    public static IEnumerable<string> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(a.Address)
                        && !a.Address.ToString().StartsWith("169.254."))
            .Select(a => a.Address.ToString())
            .Distinct();
}
