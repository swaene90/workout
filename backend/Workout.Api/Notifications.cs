using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using System.Security.Cryptography;
namespace Workout.Api;

public static class PushConfiguration
{
    public static (string PublicKey, string PrivateKey) GenerateKeys()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (Encode([4, .. p.Q.X!, .. p.Q.Y!]), Encode(p.D!));
    }
    public static bool Enabled(IConfiguration c) => c.GetValue<bool>("Notifications:Enabled") &&
        !string.IsNullOrWhiteSpace(c["Notifications:PublicKey"]) && !string.IsNullOrWhiteSpace(c["Notifications:PrivateKey"]) &&
        Key(c["Notifications:PublicKey"]!, 65) && Key(c["Notifications:PrivateKey"]!, 32) &&
        Uri.TryCreate(c["Notifications:Subject"], UriKind.Absolute, out var subject) && (subject.Scheme == "mailto" || subject.Scheme == "https");
    public static bool Endpoint(string value) => value.Length <= 4096 && Uri.TryCreate(value, UriKind.Absolute, out var u) &&
        u.Scheme == "https" && u.Port == 443 && u.UserInfo.Length == 0 && u.Fragment.Length == 0 &&
        (u.Host.EndsWith(".push.apple.com", StringComparison.OrdinalIgnoreCase) || u.Host == "fcm.googleapis.com" || u.Host == "updates.push.services.mozilla.com" || u.Host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase));
    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224 && !(b[0] == 169 && b[1] == 254) &&
                !(b[0] == 172 && b[1] >= 16 && b[1] <= 31) && !(b[0] == 192 && b[1] == 168) && !(b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        return (b[0] & 0xe0) == 0x20; // Only global IPv6 unicast.
    }
    public static HttpClient Client()
    {
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
        handler.ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !PublicAddress(a))) throw new HttpRequestException("Push endpoint did not resolve to public addresses.");
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try { await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
                catch { socket.Dispose(); }
            }
            throw new HttpRequestException("Push endpoint could not be reached.");
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
    }
    public static bool Key(string value, int bytes)
    {
        try { var s = value.Replace('-', '+').Replace('_', '/'); return Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '=')).Length == bytes; }
        catch { return false; }
    }
}

public static class NotificationEndpoints
{
    public static void MapNotifications(this RouteGroupBuilder api)
    {
        api.MapGet("/notifications/config", (IConfiguration c) => Results.Ok(new { available = PushConfiguration.Enabled(c), publicKey = PushConfiguration.Enabled(c) ? c["Notifications:PublicKey"] : null }));
        api.MapGet("/notifications/preferences", async (HttpContext h, WorkoutDb db) => Results.Ok(await db.NotificationPreferences.FindAsync(WorkoutAuth.UserId(h.User)) ?? new NotificationPreference { UserId = WorkoutAuth.UserId(h.User) }));
        api.MapPut("/notifications/preferences", async (NotificationPreference p, HttpContext h, WorkoutDb db) =>
        {
            if (!TimeOnly.TryParseExact(p.ReminderTime, "HH:mm", out _) || !TimeOnly.TryParseExact(p.QuietStart, "HH:mm", out _) || !TimeOnly.TryParseExact(p.QuietEnd, "HH:mm", out _) ||
                p.ReminderDays is null || p.ReminderDays.Length > 13 || p.ReminderDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(d => !int.TryParse(d, out var n) || n < 0 || n > 6))
                return Results.BadRequest(new { detail = "Choose valid times and reminder days." });
            var user = WorkoutAuth.UserId(h.User);
            var existing = await db.NotificationPreferences.FindAsync(user);
            p.UserId = user;
            if (existing is null) db.NotificationPreferences.Add(p); else db.Entry(existing).CurrentValues.SetValues(p);
            await db.SaveChangesAsync(); return Results.Ok(p);
        });
        api.MapPost("/notifications/subscriptions", async (SubscriptionInput input, HttpContext h, WorkoutDb db, IConfiguration c) =>
        {
            if (!PushConfiguration.Enabled(c)) return Results.Problem("Notifications are not configured.", statusCode: 503);
            if (input.Endpoint is null || input.Keys is null || input.Keys.P256dh is null || input.Keys.Auth is null || !PushConfiguration.Endpoint(input.Endpoint) || !PushConfiguration.Key(input.Keys.P256dh, 65) || !PushConfiguration.Key(input.Keys.Auth, 16))
                return Results.BadRequest(new { detail = "Invalid push subscription." });
            var user = WorkoutAuth.UserId(h.User);
            var sub = await db.Subscriptions.SingleOrDefaultAsync(x => x.Endpoint == input.Endpoint);
            if (sub is not null && sub.UserId != user) return Results.Conflict(new { detail = "This device is registered to another account. Sign out of that account first." });
            if (sub is null) db.Subscriptions.Add(sub = new DeviceSubscription { UserId = user, Endpoint = input.Endpoint });
            sub.P256dh = input.Keys.P256dh; sub.Auth = input.Keys.Auth;
            if (await db.NotificationPreferences.FindAsync(user) is null) db.NotificationPreferences.Add(new NotificationPreference { UserId = user });
            await db.SaveChangesAsync(); return Results.Ok(new { sub.Id });
        });
        api.MapDelete("/notifications/subscriptions", async ([Microsoft.AspNetCore.Mvc.FromBody] SubscriptionDelete input, HttpContext h, WorkoutDb db) =>
        {
            var user = WorkoutAuth.UserId(h.User);
            var sub = await db.Subscriptions.SingleOrDefaultAsync(x => x.UserId == user && x.Endpoint == input.Endpoint);
            if (sub is not null) { db.NotificationDeliveries.RemoveRange(db.NotificationDeliveries.Where(x => x.SubscriptionId == sub.Id)); db.Subscriptions.Remove(sub); await db.SaveChangesAsync(); }
            return Results.NoContent();
        });
    }
}

public interface IPushSender
{
    Task Send(DeviceSubscription subscription, string payload, int ttl, CancellationToken ct);
}
public sealed class PushSender(HttpClient http, IConfiguration config) : IPushSender, IDisposable
{
    private readonly PushServiceClient client = new(http) { AutoRetryAfter = false };
    public async Task Send(DeviceSubscription subscription, string payload, int ttl, CancellationToken ct)
    {
        if (!PushConfiguration.Endpoint(subscription.Endpoint)) throw new ArgumentException("Unsupported push provider.");
        using var auth = new VapidAuthentication(config["Notifications:PublicKey"], config["Notifications:PrivateKey"]) { Subject = config["Notifications:Subject"] };
        var target = new PushSubscription { Endpoint = subscription.Endpoint, Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth } };
        await client.RequestPushMessageDeliveryAsync(target, new PushMessage(payload) { TimeToLive = ttl }, auth, ct);
    }
    public void Dispose() => http.Dispose();
}

public class NotificationWorker(IServiceScopeFactory scopes, IConfiguration config, TimeProvider clock, ILogger<NotificationWorker> log, IPushSender sender) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (PushConfiguration.Enabled(config))
                {
                    using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
                    await NotificationRules.Schedule(db, clock);
                    await Deliver(db, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch { log.LogWarning("Notification worker failed; it will retry. No subscription details are logged."); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
    public async Task Deliver(WorkoutDb db, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var candidates = await db.NotificationDeliveries.AsNoTracking().Where(x => !x.Finished && x.Due <= now && (x.LeaseUntil == null || x.LeaseUntil < now)).OrderBy(x => x.Due).ThenBy(x => x.JobId).Take(50).ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            now = clock.GetUtcNow();
            var token = Guid.NewGuid(); var lease = now.AddMinutes(2);
            var claimed = await db.NotificationDeliveries.Where(x => x.JobId == candidate.JobId && x.SubscriptionId == candidate.SubscriptionId && !x.Finished && (x.LeaseUntil == null || x.LeaseUntil < now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, lease).SetProperty(x => x.LeaseToken, token), ct);
            if (claimed != 1) continue;
            db.ChangeTracker.Clear();
            var d = await db.NotificationDeliveries.FindAsync([candidate.JobId, candidate.SubscriptionId], ct);
            var job = await db.NotificationJobs.FindAsync([candidate.JobId], ct);
            var sub = await db.Subscriptions.FindAsync([candidate.SubscriptionId], ct);
            if (d is null || job is null || sub is null) continue;
            var p = await db.NotificationPreferences.FindAsync([job.UserId], ct);
            var today = Streaks.Today(clock);
            var skip = p is null || !NotificationRules.Enabled(p, job.Kind) || job.Expires <= now || sub.UserId != job.UserId;
            var member = await db.Members.FindAsync([job.UserId], ct);
            skip |= member is null || !Members.Allowed(member.Email);
            var dates = await db.Workouts.Where(x => x.UserId == job.UserId && x.Completed).Select(x => x.Date).ToListAsync(ct);
            var goals = await db.WeeklyGoals.Where(x => x.UserId == job.UserId).ToListAsync(ct);
            var summary = Streaks.Calculate(dates, goals, today);
            if (job.Kind is "reminder" or "nudge") skip |= job.Date != today || summary.WorkoutDaysThisWeek >= summary.WeeklyGoal || (job.Kind == "reminder" && dates.Contains(today));
            if (job.Kind == "celebration")
            {
                var celebrated = Streaks.Calculate(dates, goals, job.Week.AddDays(6));
                skip |= celebrated.WorkoutDaysThisWeek < celebrated.WeeklyGoal;
            }
            if (job.Kind == "partner") skip |= !await db.Workouts.AnyAsync(x => x.UserId != job.UserId && x.Completed && x.Date == job.Date, ct);
            if (!skip && NotificationRules.Quiet(p!, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Streaks.Zone).DateTime)))
            {
                if (job.Kind is "partner" or "celebration") { d.Due = NotificationRules.AfterQuiet(p!, now); d.LeaseUntil = null; await db.SaveChangesAsync(ct); continue; }
                skip = true;
            }
            if (skip) d.Finished = true;
            else
            {
                try
                {
                    if (!PushConfiguration.Endpoint(sub.Endpoint)) throw new ArgumentException("Unsupported endpoint.");
                    var body = job.Kind switch
                    {
                        "partner" => "Your workout partner checked in. Keep showing up together!",
                        "celebration" => $"You reached your workout goal for the week of {job.Week:MMM d}!",
                        "nudge" => $"You need {summary.WeeklyGoal - summary.WorkoutDaysThisWeek} more workout day(s) to reach this week's goal.",
                        _ => "Time for your workout. Check in when you're done."
                    };
                    var payload = JsonSerializer.Serialize(new { title = "Workout", body, tag = job.DedupKey, url = "/?page=dashboard" });
                    await sender.Send(sub, payload, Math.Max(0, (int)(job.Expires - now).TotalSeconds), ct);
                    d.Finished = true;
                    d.Sent = true;
                }
                catch (PushServiceClientException e) when (e.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    db.NotificationDeliveries.RemoveRange(db.NotificationDeliveries.Where(x => x.SubscriptionId == sub.Id)); db.Subscriptions.Remove(sub);
                    await db.SaveChangesAsync(ct); log.LogInformation("Removed expired push subscription."); continue;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch
                {
                    d.Attempts++; d.Finished = d.Attempts >= 6;
                    d.Due = now.AddMinutes(Math.Min(60, Math.Pow(2, d.Attempts)));
                    log.LogWarning("Push delivery failed on attempt {Attempt}.", d.Attempts);
                }
            }
            d.LeaseUntil = null;
            await db.SaveChangesAsync(ct);
            if (!await db.NotificationDeliveries.AnyAsync(x => x.JobId == job.Id && !x.Finished, ct)) { job.Finished = true; await db.SaveChangesAsync(ct); }
        }
        if (candidates.Count > 0) log.LogInformation("Notification queue checked: {DueCount} due deliveries.", candidates.Count);
    }
}
