using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workout.Api;
using Xunit;

namespace Workout.Tests;

public class BuddyTests
{
    static async Task Csrf(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", me.GetProperty("csrfToken").GetString());
    }
    static async Task<(Guid Id, string Token)> Invite(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail(email));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (result.GetProperty("id").GetGuid(), new Uri(result.GetProperty("link").GetString()!).Segments.Last());
    }
    static void InvitationCookie(ApiFactory app, HttpClient client, string email, string subject = "new-google-subject")
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", email), new Claim("email_verified", "true"),
            new Claim("sub", subject), new Claim("name", "New buddy") }, "Google"));
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) }, "Invitation");
        var cookie = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Invitation").TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie", $"workout.invitation={cookie}");
    }
    static async Task PreviewCsrf(HttpClient client, string token)
    {
        var preview = await client.GetFromJsonAsync<JsonElement>($"/api/buddy-invitations/preview?token={token}");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", preview.GetProperty("csrfToken").GetString());
    }
    [Fact]
    public async Task NewMemberAcceptsExplicitlyAndCannotReadBeforeAcceptance()
    {
        await using var app = new ApiFactory(); using var owner = app.Client(); await Csrf(owner);
        var invite = await Invite(owner, "  Friend@Example.com  ");
        using var friend = app.Client(null); InvitationCookie(app, friend, "friend@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await friend.GetAsync("/api/dashboard")).StatusCode);
        await PreviewCsrf(friend, invite.Token);
        Assert.Equal(HttpStatusCode.OK, (await friend.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(invite.Token))).StatusCode);
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
        var member = await db.Members.SingleAsync(m => m.Email == "friend@example.com");
        Assert.Equal("new-google-subject", member.GoogleSubject);
        Assert.Equal(3, (await db.WeeklyGoals.SingleAsync(g => g.UserId == member.Id)).Days);
        Assert.Equal(2, await db.BuddyConnections.CountAsync());
        Assert.Equal("accepted", (await db.BuddyInvitations.FindAsync(invite.Id))!.Status);
        Assert.DoesNotContain(invite.Token, (await db.BuddyInvitations.FindAsync(invite.Id))!.TokenHash);
    }
    [Fact]
    public async Task ConnectionsDoNotExposeOtherBuddiesAndRemovalPreservesAccounts()
    {
        await using var app = new ApiFactory(); using var owner = app.Client(); await Csrf(owner);
        var id = Guid.NewGuid(); var workoutId = Guid.NewGuid();
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
            db.Members.Add(new Member { Id = id, Email = "friend@example.com", Name = "Friend" });
            db.BuddyConnections.Add(Buddies.Connection(Members.YourId, id, DateTimeOffset.UnixEpoch));
            db.Workouts.Add(new WorkoutSession { Id = workoutId, UserId = id, Date = new(2026, 10, 1), Completed = true });
            await db.SaveChangesAsync();
        }
        using var friend = app.Client("friend@example.com"); using var original = app.Client("swaene15@gmail.com"); await Csrf(friend);
        Assert.Equal(3, (await owner.GetFromJsonAsync<JsonElement[]>("/api/dashboard"))!.Length);
        Assert.Equal(2, (await friend.GetFromJsonAsync<JsonElement[]>("/api/dashboard"))!.Length);
        Assert.Equal(HttpStatusCode.NotFound, (await original.GetAsync($"/api/workouts/{workoutId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await original.GetAsync($"/api/workouts?userId={id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await original.GetAsync($"/api/progress?userId={id}&exercise=Bench")).StatusCode);
        Assert.Equal(0, (await original.GetFromJsonAsync<JsonElement>("/api/workouts")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await friend.DeleteAsync($"/api/buddies/{Members.YourId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await friend.DeleteAsync($"/api/buddies/{Members.YourId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/workouts/{workoutId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await friend.GetAsync($"/api/workouts/{workoutId}")).StatusCode);
        Assert.Single((await friend.GetFromJsonAsync<JsonElement[]>("/api/dashboard"))!);
    }
    [Fact]
    public async Task WrongEmailCanceledExpiredAndReplacedLinksCannotBeAccepted()
    {
        await using var app = new ApiFactory(); using var owner = app.Client(); await Csrf(owner);
        var invite = await Invite(owner, "friend@example.com");
        using var wrong = app.Client(null); InvitationCookie(app, wrong, "other@example.com"); await PreviewCsrf(wrong, invite.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await wrong.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(invite.Token))).StatusCode);
        var replacement = await owner.PostAsync($"/api/buddy-invitations/{invite.Id}/replace", null); replacement.EnsureSuccessStatusCode();
        var result = await replacement.Content.ReadFromJsonAsync<JsonElement>(); var token = new Uri(result.GetProperty("link").GetString()!).Segments.Last();
        Assert.Equal(HttpStatusCode.NotFound, (await wrong.GetAsync($"/api/buddy-invitations/preview?token={invite.Token}")).StatusCode);
        using var friend = app.Client(null); InvitationCookie(app, friend, "friend@example.com"); await PreviewCsrf(friend, token);
        await owner.DeleteAsync($"/api/buddy-invitations/{invite.Id}");
        Assert.Equal(HttpStatusCode.Conflict, (await friend.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(token))).StatusCode);
        var expired = await Invite(owner, "expired@example.com");
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
            (await db.BuddyInvitations.FindAsync(expired.Id))!.Expires = DateTimeOffset.UnixEpoch; await db.SaveChangesAsync();
        }
        using var late = app.Client(null); InvitationCookie(app, late, "expired@example.com"); await PreviewCsrf(late, expired.Token);
        Assert.Equal(HttpStatusCode.Conflict, (await late.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(expired.Token))).StatusCode);
    }
    [Fact]
    public async Task InvitationsArePersonalAndRequireCsrf()
    {
        await using var app = new ApiFactory(); using var owner = app.Client(); using var original = app.Client("swaene15@gmail.com");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail("friend@example.com"))).StatusCode);
        await Csrf(owner); await Csrf(original);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail("not-an-email"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail("swaene1@gmail.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail("swaene15@gmail.com"))).StatusCode);
        var invite = await Invite(owner, "friend@example.com");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/buddy-invitations", new InviteEmail("friend@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await original.DeleteAsync($"/api/buddy-invitations/{invite.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await original.PostAsync($"/api/buddy-invitations/{invite.Id}/replace", null)).StatusCode);
        var other = await Invite(original, "friend@example.com"); Assert.NotEqual(invite.Id, other.Id);
    }
    [Fact]
    public async Task ExistingAccountAcceptanceIsIdempotentAndReconnectRequiresNewInvitation()
    {
        await using var app = new ApiFactory(); using var owner = app.Client(); using var original = app.Client("swaene15@gmail.com");
        await Csrf(owner); await Csrf(original);
        await owner.DeleteAsync($"/api/buddies/{Members.BrittId}");
        var invite = await Invite(owner, "swaene15@gmail.com");
        for (var n = 0; n < 2; n++) Assert.Equal(HttpStatusCode.OK, (await original.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(invite.Token))).StatusCode);
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement[]>("/api/dashboard"))!.Length);
        await owner.DeleteAsync($"/api/buddies/{Members.BrittId}");
        await original.PostAsJsonAsync("/api/buddy-invitations/accept", new InviteToken(invite.Token));
        Assert.Single((await owner.GetFromJsonAsync<JsonElement[]>("/api/dashboard"))!);
    }
    [Fact]
    public async Task PartnerJobsAreOnlyCreatedForDirectBuddies()
    {
        await using var app = new ApiFactory(); using var owner = app.Client();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
        var stranger = Guid.NewGuid();
        db.Members.Add(new Member { Id = stranger, Name = "Stranger", Email = "stranger@example.com" });
        foreach (var id in new[] { Members.BrittId, stranger })
        {
            db.NotificationPreferences.Add(new NotificationPreference { UserId = id });
            db.Subscriptions.Add(new DeviceSubscription { UserId = id, Endpoint = $"https://web.push.apple.com/{id}" });
        }
        await db.SaveChangesAsync();
        await NotificationRules.WorkoutChanged(db, Members.YourId, new(2026, 10, 1), [], [new(2026, 10, 1)], scope.ServiceProvider.GetRequiredService<TimeProvider>());
        await db.SaveChangesAsync();
        var job = Assert.Single(await db.NotificationJobs.ToListAsync()); Assert.Equal(Members.BrittId, job.UserId);
    }
}
