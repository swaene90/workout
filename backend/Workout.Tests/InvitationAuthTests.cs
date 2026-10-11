using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Workout.Api;
using Xunit;

namespace Workout.Tests;

public class InvitationAuthTests
{
    [Theory]
    [InlineData("friend@example.com", "true", true, true)]
    [InlineData("friend@example.com", "true", false, false)]
    [InlineData("wrong@example.com", "true", true, false)]
    [InlineData("friend@example.com", "false", true, false)]
    public async Task GoogleCallbackOnlyAdmitsVerifiedInvitedEmail(string email, string verified, bool includeInvite, bool succeeds)
    {
        await using var app = new ApiFactory(); using var client = app.Client();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
        db.BuddyInvitations.Add(new BuddyInvitation { InviterId = Members.YourId, Email = "friend@example.com", TokenHash = Buddies.Hash("token"),
            Created = DateTimeOffset.Parse("2026-10-01T16:00:00Z"), Expires = DateTimeOffset.Parse("2026-10-08T16:00:00Z") }); await db.SaveChangesAsync();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Google");
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", email), new Claim("email_verified", verified), new Claim("sub", "subject") }, "Google"));
        var properties = new AuthenticationProperties(); if (includeInvite) properties.Items["invite"] = "token";
        var context = new TokenValidatedContext(http, new AuthenticationScheme("Google", null, typeof(OpenIdConnectHandler)), options, principal, properties);
        await options.Events.TokenValidated(context);
        Assert.Equal(succeeds, context.Result?.Failure is null);
        Assert.False(principal.HasClaim(c => c.Type == WorkoutAuth.UserClaim));
        Assert.False(await db.Members.AnyAsync(m => m.Email == "friend@example.com"));
    }
    [Fact]
    public async Task ExistingAccountGoogleSubjectCannotBeRebound()
    {
        await using var app = new ApiFactory(); using var client = app.Client();
        using var scope = app.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<WorkoutDb>();
        (await db.Members.FindAsync(Members.YourId))!.GoogleSubject = "original"; await db.SaveChangesAsync();
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get("Google");
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", "swaene1@gmail.com"), new Claim("email_verified", "true"), new Claim("sub", "replacement") }, "Google"));
        var context = new TokenValidatedContext(http, new AuthenticationScheme("Google", null, typeof(OpenIdConnectHandler)), options, principal, new AuthenticationProperties());
        await options.Events.TokenValidated(context);
        Assert.NotNull(context.Result?.Failure);
        Assert.Equal("original", (await db.Members.FindAsync(Members.YourId))!.GoogleSubject);
    }
}
