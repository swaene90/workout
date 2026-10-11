using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Workout.Api;

public class BuddyConnection
{
    public Guid FirstId { get; set; }
    public Guid SecondId { get; set; }
    public DateTimeOffset Created { get; set; }
}

public class BuddyInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InviterId { get; set; }
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset Expires { get; set; }
    public string Status { get; set; } = "pending";
    public Guid? AcceptedBy { get; set; }
}

public record InviteEmail(string Email);
public record InviteToken(string Token);

public static class Buddies
{
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static IQueryable<Guid> Visible(WorkoutDb db, Guid user) => db.Members.Where(m => m.Id == user ||
        db.BuddyConnections.Any(c => c.FirstId == user && c.SecondId == m.Id || c.SecondId == user && c.FirstId == m.Id)).Select(m => m.Id);
    public static BuddyConnection Connection(Guid a, Guid b, DateTimeOffset now) => new()
        { FirstId = a.CompareTo(b) < 0 ? a : b, SecondId = a.CompareTo(b) < 0 ? b : a, Created = now };
    public static async Task<BuddyInvitation?> Find(WorkoutDb db, string? token) => string.IsNullOrEmpty(token) || token.Length > 128 ? null :
        await db.BuddyInvitations.SingleOrDefaultAsync(i => i.TokenHash == Hash(token));
    public static bool Pending(BuddyInvitation i, TimeProvider clock) => i.Status == "pending" && i.Expires > clock.GetUtcNow();
    static object View(BuddyInvitation i, TimeProvider clock) => new { i.Id, i.Email, i.Expires, status = i.Status == "pending" && !Pending(i, clock) ? "expired" : i.Status };
    static object Link(BuddyInvitation i, string token, HttpContext h, IConfiguration config)
    {
        var origin = config["PublicOrigin"]?.TrimEnd('/') ?? $"{h.Request.Scheme}://{h.Request.Host}";
        return new { i.Id, i.Email, i.Expires, link = $"{origin}/invite/{token}" };
    }
    static string Renew(BuddyInvitation i, TimeProvider clock)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        i.TokenHash = Hash(token); i.Created = clock.GetUtcNow(); i.Expires = i.Created.AddDays(7); i.Status = "pending";
        return token;
    }
    public static void MapBuddies(this RouteGroupBuilder api, WebApplication app)
    {
        api.MapGet("/buddies", async (HttpContext h, WorkoutDb db, TimeProvider clock) =>
        {
            var user = WorkoutAuth.UserId(h.User); var visible = Visible(db, user);
            var buddies = await db.Members.Where(m => m.Id != user && visible.Contains(m.Id)).OrderBy(m => m.Name)
                .Select(m => new { m.Id, m.Name, m.ProfilePictureUrl }).ToListAsync();
            var invitations = await db.BuddyInvitations.Where(i => i.InviterId == user && i.Status == "pending").OrderByDescending(i => i.Created).ToListAsync();
            return Results.Ok(new { buddies, invitations = invitations.Select(i => View(i, clock)) });
        });
        api.MapPost("/buddy-invitations", async (InviteEmail input, HttpContext h, WorkoutDb db, TimeProvider clock, IConfiguration config) =>
        {
            var email = input.Email?.Trim().ToLowerInvariant() ?? "";
            if (email.Length > 320 || !System.Net.Mail.MailAddress.TryCreate(email, out var address) || address.Address != email)
                return Results.Problem("Enter a valid email address.", statusCode: 400);
            var user = WorkoutAuth.UserId(h.User);
            if (await db.Members.AnyAsync(m => m.Id == user && m.Email == email)) return Results.Problem("You cannot invite yourself.", statusCode: 400);
            if (await db.Members.AnyAsync(m => m.Email == email && Visible(db, user).Contains(m.Id))) return Results.Problem("You are already buddies.", statusCode: 409);
            var old = await db.BuddyInvitations.Where(i => i.InviterId == user && i.Email == email && i.Status == "pending").ToListAsync();
            if (old.Any(i => Pending(i, clock))) return Results.Problem("An invitation already exists. Replace its link in Settings.", statusCode: 409);
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
            foreach (var i in old) i.Status = "expired";
            // Release the filtered unique key before inserting a replacement for an expired invite.
            if (old.Count > 0) await db.SaveChangesAsync();
            var invite = new BuddyInvitation { InviterId = user, Email = email }; var token = Renew(invite, clock);
            db.BuddyInvitations.Add(invite);
            try { await db.SaveChangesAsync(); if (transaction is not null) await transaction.CommitAsync(); }
            catch (DbUpdateException) { return Results.Conflict(); }
            return Results.Ok(Link(invite, token, h, config));
        });
        api.MapDelete("/buddy-invitations/{id:guid}", async (Guid id, HttpContext h, WorkoutDb db) =>
        {
            var i = await db.BuddyInvitations.SingleOrDefaultAsync(i => i.Id == id && i.InviterId == WorkoutAuth.UserId(h.User));
            if (i is null) return Results.NotFound();
            if (i.Status == "pending")
            {
                i.Status = "canceled";
                try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
            }
            return Results.NoContent();
        });
        api.MapPost("/buddy-invitations/{id:guid}/replace", async (Guid id, HttpContext h, WorkoutDb db, TimeProvider clock, IConfiguration config) =>
        {
            var i = await db.BuddyInvitations.SingleOrDefaultAsync(i => i.Id == id && i.InviterId == WorkoutAuth.UserId(h.User));
            if (i is null) return Results.NotFound();
            if (i.Status != "pending") return Results.Conflict();
            var token = Renew(i, clock);
            try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
            return Results.Ok(Link(i, token, h, config));
        });
        api.MapDelete("/buddies/{memberId:guid}", async (Guid memberId, HttpContext h, WorkoutDb db) =>
        {
            var pair = Connection(WorkoutAuth.UserId(h.User), memberId, default);
            var c = await db.BuddyConnections.FindAsync(pair.FirstId, pair.SecondId);
            if (c is not null)
            {
                db.BuddyConnections.Remove(c);
                try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { /* Already removed by another request. */ }
            }
            return Results.NoContent();
        });
        // These endpoints are outside the Member group: prospective members have no member claim.
        app.MapGet("/api/buddy-invitations/preview", async (string token, HttpContext h, WorkoutDb db, TimeProvider clock, IAntiforgery csrf) =>
        {
            var i = await Find(db, token);
            if (i is null) return Results.NotFound();
            var restricted = await h.AuthenticateAsync("Invitation");
            var identity = h.User.Identity?.IsAuthenticated == true ? h.User : restricted.Principal;
            var matches = identity?.FindFirstValue("email")?.Equals(i.Email, StringComparison.OrdinalIgnoreCase) == true;
            var name = await db.Members.Where(m => m.Id == i.InviterId).Select(m => m.Name).SingleAsync();
            return Results.Ok(new { inviterName = name, status = Pending(i, clock) ? "pending" : i.Status == "pending" ? "expired" : i.Status,
                matchesEmail = matches, authenticated = identity?.Identity?.IsAuthenticated == true, csrfToken = csrf.GetAndStoreTokens(h).RequestToken });
        });
        app.MapPost("/api/buddy-invitations/accept", async (InviteToken input, HttpContext h, WorkoutDb db, TimeProvider clock, IAntiforgery csrf) =>
        {
            try { await csrf.ValidateRequestAsync(h); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            var restricted = await h.AuthenticateAsync("Invitation");
            var principal = h.User.HasClaim(c => c.Type == WorkoutAuth.UserClaim) ? h.User : restricted.Principal;
            if (principal?.Identity?.IsAuthenticated != true) return Results.Unauthorized();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable) : null;
            var i = await Find(db, input.Token);
            var email = principal.FindFirstValue("email"); var subject = principal.FindFirstValue("sub");
            if (i is null) return Results.NotFound();
            if (!i.Email.Equals(email, StringComparison.OrdinalIgnoreCase)) return Results.Problem("Sign in with the invited Google email.", statusCode: 403);
            var m = await db.Members.SingleOrDefaultAsync(m => m.Email == i.Email);
            if (i.Status == "accepted" && m is not null && i.AcceptedBy == m.Id) return Results.Ok(new { accepted = true });
            if (!Pending(i, clock)) return Results.Problem("This invitation is no longer available.", statusCode: 409);
            if (m is null)
            {
                if (string.IsNullOrEmpty(subject) || !string.Equals(principal.FindFirstValue("email_verified"), "true", StringComparison.OrdinalIgnoreCase)) return Results.Forbid();
                var name = principal.FindFirstValue("name") ?? "Workout buddy";
                m = new Member { Id = Guid.NewGuid(), Email = i.Email, Name = name[..Math.Min(name.Length, 100)], GoogleSubject = subject,
                    ProfilePictureUrl = WorkoutAuth.GooglePicture(principal.FindFirstValue("picture")) };
                db.Members.Add(m); db.WeeklyGoals.Add(new WeeklyGoal { UserId = m.Id, EffectiveWeek = Streaks.Monday(Streaks.Today(clock)), Days = 3 });
            }
            else if (!principal.HasClaim(WorkoutAuth.UserClaim, m.Id.ToString()) && m.GoogleSubject != subject) return Results.Forbid();
            if (m.Id == i.InviterId) return Results.BadRequest();
            var pair = Connection(m.Id, i.InviterId, clock.GetUtcNow());
            if (!await db.BuddyConnections.AnyAsync(c => c.FirstId == pair.FirstId && c.SecondId == pair.SecondId)) db.BuddyConnections.Add(pair);
            i.Status = "accepted"; i.AcceptedBy = m.Id;
            try { await db.SaveChangesAsync(); if (transaction is not null) await transaction.CommitAsync(); }
            catch (DbUpdateException) { return Results.Problem("The invitation changed. Refresh and try again.", statusCode: 409); }
            catch (Npgsql.PostgresException e) when (e.SqlState == "40001") { return Results.Problem("The invitation changed. Refresh and try again.", statusCode: 409); }
            if (!h.User.HasClaim(c => c.Type == WorkoutAuth.UserClaim))
            {
                var claims = principal.Claims.Where(c => c.Type != WorkoutAuth.UserClaim).ToList(); claims.Add(new Claim(WorkoutAuth.UserClaim, m.Id.ToString()));
                await h.SignInAsync("Cookies", new ClaimsPrincipal(new ClaimsIdentity(claims, "Google")));
                await h.SignOutAsync("Invitation");
            }
            return Results.Ok(new { accepted = true });
        });
        app.MapPost("/auth/invitation/logout", async (bool? member, HttpContext h, IAntiforgery csrf) =>
        {
            try { await csrf.ValidateRequestAsync(h); } catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await h.SignOutAsync("Invitation"); if (member == true) await h.SignOutAsync("Cookies"); return Results.NoContent();
        });
    }
}
