using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Workout.Api;

public static class WorkoutAuth
{
    public const string UserClaim = "workout_user_id";
    public static Guid UserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(UserClaim)!);

    public static bool Eligible(string? email, string? verified, string? subject) =>
        !string.IsNullOrWhiteSpace(email) && string.Equals(verified, "true", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(subject);

    public static string? GooglePicture(string? value) =>
        value is { Length: <= 2048 } && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.Port == 443 && uri.UserInfo.Length == 0 &&
        uri.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            ? uri.AbsoluteUri : null;

    public static void AddWorkoutAuth(this IServiceCollection services, IConfiguration configuration, bool development)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "Cookies";
        }).AddCookie("Cookies", options =>
        {
            options.Cookie.Name = "workout.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; },
                OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; },
                OnValidatePrincipal = async context =>
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<WorkoutDb>();
                    var id = context.Principal?.FindFirstValue(UserClaim);
                    if (!Guid.TryParse(id, out var userId)) { context.RejectPrincipal(); return; }
                    var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId);
                    if (member is null || member.GoogleSubject != context.Principal?.FindFirstValue("sub"))
                        context.RejectPrincipal();
                }
            };
        }).AddCookie("Invitation", options =>
        {
            options.Cookie.Name = "workout.invitation";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
        }).AddOpenIdConnect("Google", options =>
        {
            options.Authority = "https://accounts.google.com";
            options.ClientId = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientId"]) ? "not-configured" : configuration["Authentication:Google:ClientId"]!;
            options.ClientSecret = string.IsNullOrWhiteSpace(configuration["Authentication:Google:ClientSecret"]) ? "not-configured" : configuration["Authentication:Google:ClientSecret"]!;
            options.CallbackPath = "/signin-google";
            options.ResponseType = OpenIdConnectResponseType.Code;
            options.UsePkce = true;
            options.MapInboundClaims = false;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.ClaimActions.MapUniqueJsonKey("picture", "picture");
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("email");
            options.Scope.Add("profile");
            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = async context =>
                {
                    var email = context.Principal?.FindFirstValue("email");
                    var verified = context.Principal?.FindFirstValue("email_verified");
                    var subject = context.Principal?.FindFirstValue("sub");
                    if (!Eligible(email, verified, subject)) { context.Fail("This Google account is not allowed."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<WorkoutDb>();
                    var normalized = email!.ToLowerInvariant();
                    var member = await db.Members.SingleOrDefaultAsync(x => x.Email == normalized);
                    if (member is null)
                    {
                        context.Properties!.Items.TryGetValue("invite", out var inviteToken);
                        var invitation = await Buddies.Find(db, inviteToken);
                        var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
                        if (invitation is null || !Buddies.Pending(invitation, clock) || invitation.Email != normalized)
                            context.Fail("This Google account has no valid invitation.");
                        return;
                    }
                    if (member.GoogleSubject is not null && member.GoogleSubject != subject)
                    { context.Fail("Account identity does not match."); return; }
                    member.GoogleSubject = subject;
                    await db.SaveChangesAsync();
                    ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim(UserClaim, member.Id.ToString()));
                    ((ClaimsIdentity)context.Principal.Identity!).AddClaim(new Claim(ClaimTypes.NameIdentifier, member.Id.ToString()));
                },
                OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    var redirect = context.Properties?.RedirectUri;
                    context.Response.Redirect(redirect?.StartsWith("/invite/", StringComparison.Ordinal) == true ? redirect + "?authError=1" : "/?authError=1");
                    return Task.CompletedTask;
                },
                OnTicketReceived = async context =>
                {
                    if (!context.Principal!.HasClaim(c => c.Type == UserClaim))
                    {
                        await context.HttpContext.SignOutAsync("Cookies");
                        await context.HttpContext.SignInAsync("Invitation", context.Principal,
                            new AuthenticationProperties { IsPersistent = false });
                        context.HandleResponse();
                        context.Response.Redirect(context.ReturnUri ?? "/");
                        return;
                    }
                    var db = context.HttpContext.RequestServices.GetRequiredService<WorkoutDb>();
                    var member = await db.Members.SingleAsync(x => x.Id == UserId(context.Principal!));
                    member.ProfilePictureUrl = GooglePicture(context.Principal?.FindFirstValue("picture"));
                    await db.SaveChangesAsync();
                    await context.HttpContext.SignOutAsync("Invitation");
                }
            };
        });
        services.AddAuthorization(options => options.AddPolicy("Member", policy => policy
            .RequireAuthenticatedUser().RequireClaim(UserClaim).RequireAssertion(async context =>
            {
                if (!Guid.TryParse(context.User.FindFirstValue(UserClaim), out var id) || context.Resource is not HttpContext http) return false;
                return await http.RequestServices.GetRequiredService<WorkoutDb>().Members.AnyAsync(m => m.Id == id);
            })));
    }
}
