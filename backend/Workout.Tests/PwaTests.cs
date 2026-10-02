using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workout.Api;
using Xunit;

namespace Workout.Tests;
public class PwaTests
{
    static WorkoutInput Input(string date = "2026-10-01", bool completed = true) => new(DateOnly.Parse(date), "Workout", "Other", completed, null, "", []);
    static async Task Csrf(HttpClient c)
    {
        var me = await c.GetFromJsonAsync<JsonElement>("/api/me");
        c.DefaultRequestHeaders.Remove("X-CSRF-TOKEN"); c.DefaultRequestHeaders.Add("X-CSRF-TOKEN", me.GetProperty("csrfToken").GetString());
    }
    [Fact]
    public async Task MutationsAreIdempotentAndRejectStaleRevisionsOrChangedReceipts()
    {
        await using var app = new ApiFactory(); using var client = app.Client(); await Csrf(client);
        var create = new WorkoutMutation(Guid.NewGuid(), "create", Guid.NewGuid(), null, Input());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/workout-mutations", create)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/workout-mutations", create)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/workout-mutations", create with { Workout = Input() with { Notes = "Different" } })).StatusCode);
        var update = create with { MutationId = Guid.NewGuid(), Operation = "update", ExpectedRevision = 1, Workout = Input() with { Notes = "Updated" } };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/workout-mutations", update)).StatusCode);
        var stale = await client.PostAsJsonAsync("/api/workout-mutations", update with { MutationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var conflict = await stale.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(2, conflict.GetProperty("current").GetProperty("revision").GetInt64());
        var delete = create with { MutationId = Guid.NewGuid(), Operation = "delete", ExpectedRevision = 2, Workout = null };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/workout-mutations", delete)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/workout-mutations", delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/workouts/{create.WorkoutId}")).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
        Assert.Equal(3, await db.MutationReceipts.CountAsync());
    }
    [Fact]
    public async Task MutationsRequireCsrfAndOwnershipAndLegacyWritesRequireRevision()
    {
        await using var app = new ApiFactory(); using var you = app.Client(); using var partner = app.Client("swaene15@gmail.com");
        var create = new WorkoutMutation(Guid.NewGuid(), "create", Guid.NewGuid(), null, Input());
        Assert.Equal(HttpStatusCode.BadRequest, (await you.PostAsJsonAsync("/api/workout-mutations", create)).StatusCode);
        await Csrf(you); await Csrf(partner); await you.PostAsJsonAsync("/api/workout-mutations", create);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.PostAsJsonAsync("/api/workout-mutations", create with { MutationId = Guid.NewGuid(), Operation = "delete", ExpectedRevision = 1, Workout = null })).StatusCode);
        Assert.Equal(428, (int)(await you.PutAsJsonAsync($"/api/workouts/{create.WorkoutId}", Input())).StatusCode);
        Assert.Equal(428, (int)(await you.DeleteAsync($"/api/workouts/{create.WorkoutId}")).StatusCode);
    }
    [Fact]
    public async Task AccountSwitchCannotReadOrWriteThroughAnotherAccountsOpenTab()
    {
        await using var app = new ApiFactory(); using var partner = app.Client("swaene15@gmail.com"); await Csrf(partner);
        partner.DefaultRequestHeaders.Add("X-Workout-Account", Members.YourId.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await partner.GetAsync("/api/templates")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await partner.PutAsJsonAsync("/api/preferences", new AppearanceInput("red", "dark"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await partner.GetAsync("/api/me")).StatusCode);
        partner.DefaultRequestHeaders.Remove("X-Workout-Account");
        var actual = await partner.GetFromJsonAsync<JsonElement>("/api/me");
        Assert.Equal("green", actual.GetProperty("user").GetProperty("theme").GetString());
    }
    [Fact]
    public async Task NotificationsAreOptionalAndPreferencesBelongToAuthenticatedMember()
    {
        await using var app = new ApiFactory(); using var you = app.Client(); using var partner = app.Client("swaene15@gmail.com"); await Csrf(you); await Csrf(partner);
        var config = await you.GetFromJsonAsync<JsonElement>("/api/notifications/config"); Assert.False(config.GetProperty("available").GetBoolean());
        var p = new NotificationPreference { UserId = Members.BrittId, ReminderTime = "19:00", Partner = false, ReminderDays = "" };
        Assert.Equal(HttpStatusCode.OK, (await you.PutAsJsonAsync("/api/notifications/preferences", p)).StatusCode);
        var own = await you.GetFromJsonAsync<NotificationPreference>("/api/notifications/preferences");
        Assert.Equal(Members.YourId, own!.UserId); Assert.False(own.Partner);
        var other = await partner.GetFromJsonAsync<NotificationPreference>("/api/notifications/preferences"); Assert.True(other!.Partner); Assert.Equal("18:00", other.ReminderTime);
        p.ReminderTime = "invalid"; Assert.Equal(HttpStatusCode.BadRequest, (await you.PutAsJsonAsync("/api/notifications/preferences", p)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await you.PostAsJsonAsync("/api/notifications/subscriptions", new SubscriptionInput("https://web.push.apple.com/example", new("bad", "bad")))).StatusCode);
    }
    [Fact]
    public async Task CheckInsAndGoalsEnqueueOnceButDraftsAndExtraSessionsDoNot()
    {
        await using var app = new ApiFactory(); using var c = app.Client(); await Csrf(c);
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
            foreach (var user in new[] { Members.YourId, Members.BrittId }) { db.NotificationPreferences.Add(new() { UserId = user }); db.Subscriptions.Add(new() { UserId = user, Endpoint = $"https://web.push.apple.com/{user}" }); }
            await db.SaveChangesAsync();
        }
        await c.PostAsJsonAsync("/api/workouts", Input("2026-09-28")); await c.PostAsJsonAsync("/api/workouts", Input("2026-09-29"));
        var draft = await (await c.PostAsJsonAsync("/api/workouts", Input(completed: false))).Content.ReadFromJsonAsync<JsonElement>();
        using (var scope = app.Services.CreateScope()) Assert.Empty(await scope.ServiceProvider.GetRequiredService<WorkoutDb>().NotificationJobs.ToListAsync());
        c.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "1");
        Assert.Equal(HttpStatusCode.OK, (await c.PutAsJsonAsync($"/api/workouts/{draft.GetProperty("id").GetString()}", Input())).StatusCode);
        await c.PostAsJsonAsync("/api/workouts", Input()); await c.PostAsJsonAsync("/api/workouts", Input("2026-09-20"));
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>(); var jobs = await db.NotificationJobs.ToListAsync();
            Assert.Equal(2, jobs.Count); Assert.Single(jobs, j => j.Kind == "partner"); Assert.Single(jobs, j => j.Kind == "celebration"); Assert.Equal(2, await db.NotificationDeliveries.CountAsync());
        }
    }
    [Fact]
    public async Task MovingCompletedWorkoutDoesNotInventAdditionalWorkoutDay()
    {
        await using var app = new ApiFactory(); using var c = app.Client(); await Csrf(c);
        using (var scope = app.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>(); db.NotificationPreferences.Add(new() { UserId = Members.YourId }); db.Subscriptions.Add(new() { UserId = Members.YourId, Endpoint = "https://web.push.apple.com/you" }); await db.SaveChangesAsync(); }
        await c.PostAsJsonAsync("/api/workouts", Input("2026-09-28"));
        var second = await (await c.PostAsJsonAsync("/api/workouts", Input("2026-09-29"))).Content.ReadFromJsonAsync<JsonElement>();
        c.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "1");
        await c.PutAsJsonAsync($"/api/workouts/{second.GetProperty("id").GetString()}", Input());
        using var scope2 = app.Services.CreateScope(); Assert.Empty(await scope2.ServiceProvider.GetRequiredService<WorkoutDb>().NotificationJobs.ToListAsync());
    }
    [Fact]
    public async Task SundayNudgeSuppressesLaterReminderAndSchedulingIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new WorkoutDb(options); await db.Database.EnsureCreatedAsync();
        db.NotificationPreferences.Add(new() { UserId = Members.YourId }); db.Subscriptions.Add(new() { UserId = Members.YourId, Endpoint = "https://web.push.apple.com/a" }); await db.SaveChangesAsync();
        await NotificationRules.Schedule(db, new FixedClock(DateTimeOffset.Parse("2026-10-04T21:00:00Z")));
        await NotificationRules.Schedule(db, new FixedClock(DateTimeOffset.Parse("2026-10-04T21:00:30Z")));
        (await db.NotificationDeliveries.SingleAsync()).Sent = true;
        (await db.NotificationDeliveries.SingleAsync()).Finished = true;
        await db.SaveChangesAsync();
        await NotificationRules.Schedule(db, new FixedClock(DateTimeOffset.Parse("2026-10-04T22:00:00Z")));
        Assert.Single(await db.NotificationJobs.ToListAsync()); Assert.Equal("nudge", (await db.NotificationJobs.SingleAsync()).Kind);
    }
    [Theory]
    [InlineData("2026-10-02T02:00:00Z", "2026-10-02T12:00:00Z")]
    [InlineData("2026-10-02T10:00:00Z", "2026-10-02T12:00:00Z")]
    [InlineData("2026-11-01T03:00:00Z", "2026-11-01T13:00:00Z")]
    public void QuietHoursRespectMidnightAndDst(string now, string due) => Assert.Equal(DateTimeOffset.Parse(due), NotificationRules.AfterQuiet(new(), DateTimeOffset.Parse(now)));
    [Fact]
    public void InvalidDstLocalTimeMovesToFirstValidMinute()
    {
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:00:00Z"), NotificationRules.LocalInstant(new(2026,3,8), new(2,30)));
        Assert.False(NotificationRules.Quiet(new() { QuietStart = "08:00", QuietEnd = "08:00" }, new(8,0)));
    }
    [Theory]
    [InlineData("https://web.push.apple.com/path", true)]
    [InlineData("https://fcm.googleapis.com/fcm/send/id", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/id", true)]
    [InlineData("http://web.push.apple.com/path", false)]
    [InlineData("https://web.push.apple.com.evil.example/path", false)]
    [InlineData("https://127.0.0.1/path", false)]
    [InlineData("https://web.push.apple.com:8080/path", false)]
    [InlineData("https://user@web.push.apple.com/path", false)]
    public void PushEndpointsAreLimitedToTrustedHttpsProviders(string endpoint, bool allowed) => Assert.Equal(allowed, PushConfiguration.Endpoint(endpoint));
    [Theory]
    [InlineData("127.0.0.1", false)] [InlineData("192.168.0.48", false)] [InlineData("10.0.0.1", false)] [InlineData("172.21.0.3", false)]
    [InlineData("169.254.169.254", false)] [InlineData("::1", false)] [InlineData("fc00::1", false)] [InlineData("::ffff:127.0.0.1", false)] [InlineData("1.1.1.1", true)]
    public void PushConnectionRejectsPrivateAddresses(string address, bool allowed) => Assert.Equal(allowed, PushConfiguration.PublicAddress(IPAddress.Parse(address)));
    [Fact]
    public async Task PushPayloadUsesModernEncryption()
    {
        var keys = PushConfiguration.GenerateKeys();
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["Notifications:PublicKey"] = keys.PublicKey, ["Notifications:PrivateKey"] = keys.PrivateKey, ["Notifications:Subject"] = "mailto:test@example.com"
        }).Build();
        using var handler = new CapturePush(); using var http = new HttpClient(handler); using var sender = new PushSender(http, config);
        await sender.Send(new DeviceSubscription { Endpoint = "https://web.push.apple.com/a", P256dh = keys.PublicKey, Auth = Convert.ToBase64String(new byte[16]).TrimEnd('=') }, "payload", 3600, CancellationToken.None);
        Assert.Equal("aes128gcm", handler.Encoding);
        Assert.Equal("vapid", handler.Scheme);
        Assert.NotEmpty(handler.Body!);
    }
    private sealed class CapturePush : HttpMessageHandler
    {
        public string? Encoding; public string? Scheme; public byte[]? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Encoding = request.Content!.Headers.ContentEncoding.Single(); Scheme = request.Headers.Authorization?.Scheme;
            Body = await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
