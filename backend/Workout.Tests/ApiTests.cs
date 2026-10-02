using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Workout.Api;
using Xunit;

namespace Workout.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string database = Guid.NewGuid().ToString();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = ""
        }));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<DbContextOptions<WorkoutDb>>();
            services.RemoveAll<IDbContextOptionsConfiguration<WorkoutDb>>();
            services.AddDbContext<WorkoutDb>(o => o.UseInMemoryDatabase(database));
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(new FixedClock(DateTimeOffset.Parse("2026-10-01T16:00:00Z")));
            services.AddAuthentication(o => { o.DefaultScheme = "Test"; o.DefaultChallengeScheme = "Test"; o.DefaultForbidScheme = "Test"; }).AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { });
        });
    }
    public HttpClient Client(string? email = "swaene1@gmail.com")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var scope = Services.CreateScope(); scope.ServiceProvider.GetRequiredService<WorkoutDb>().Database.EnsureCreated();
        if (email is not null) client.DefaultRequestHeaders.Add("X-Test-Email", email);
        return client;
    }
}
public class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var email = Request.Headers["X-Test-Email"].FirstOrDefault();
        if (email is null) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim> { new("email", email), new(ClaimTypes.NameIdentifier, email) };
        if (Members.Allowed(email)) claims.Add(new(WorkoutAuth.UserClaim, email == "swaene1@gmail.com" ? Members.YourId.ToString() : Members.BrittId.ToString()));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
    }
}

public class ApiTests
{
    [Fact]
    public async Task AppearanceIsPersonalPersistsAndRequiresCsrf()
    {
        await using var app = new ApiFactory(); using var you = app.Client(); using var brother = app.Client("swaene15@gmail.com");
        Assert.Equal(HttpStatusCode.BadRequest, (await you.PutAsJsonAsync("/api/preferences", new AppearanceInput("purple", "dark"))).StatusCode);
        await Csrf(you); await Csrf(brother);
        Assert.Equal(HttpStatusCode.OK, (await you.PutAsJsonAsync("/api/preferences", new AppearanceInput("purple", "dark"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await brother.PutAsJsonAsync("/api/preferences", new AppearanceInput("blue", "light"))).StatusCode);
        using var freshYou = app.Client();
        var youProfile = (await freshYou.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("user");
        var brotherProfile = (await brother.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("user");
        Assert.Equal("purple", youProfile.GetProperty("theme").GetString());
        Assert.Equal("dark", youProfile.GetProperty("mode").GetString());
        Assert.Equal("blue", brotherProfile.GetProperty("theme").GetString());
        Assert.Equal("light", brotherProfile.GetProperty("mode").GetString());
    }
    [Theory]
    [InlineData("orange", "light")]
    [InlineData("green", "system")]
    [InlineData("GREEN", "dark")]
    public async Task InvalidAppearancesAreRejected(string theme, string mode)
    {
        await using var app = new ApiFactory(); using var client = app.Client(); await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/preferences", new AppearanceInput(theme, mode))).StatusCode);
        var profile = (await client.GetFromJsonAsync<JsonElement>("/api/me")).GetProperty("user");
        Assert.Equal("green", profile.GetProperty("theme").GetString());
        Assert.Equal("light", profile.GetProperty("mode").GetString());
    }
    [Fact]
    public async Task MissingGoogleCredentialsDoNotBreakHealthAndLoginExplainsSetup()
    {
        await using var app = new ApiFactory(); using var client = app.Client(null);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/auth/login")).StatusCode);
    }
    private static WorkoutInput Workout(string date = "2026-10-01", bool completed = true) => new(DateOnly.Parse(date), "Bench day", "Strength", completed, 45, "Felt good", [new("Bench press", "Strength", [new(8, 135)], null, null)]);
    private static async Task Csrf(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<JsonElement>("/api/me");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", me.GetProperty("csrfToken").GetString());
    }
    [Fact]
    public async Task UnauthenticatedAndUnknownAccountsCannotReadWorkouts()
    {
        await using var app = new ApiFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client(null).GetAsync("/api/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client("other@gmail.com").GetAsync("/api/workouts")).StatusCode);
    }
    [Fact]
    public async Task WritesRequireRealAntiforgeryToken()
    {
        await using var app = new ApiFactory(); using var client = app.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/workouts", Workout())).StatusCode);
        await Csrf(client); Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/workouts", Workout())).StatusCode);
    }
    [Fact]
    public async Task BrotherCanReadButCannotEditOrDeleteYourWorkoutOrTemplate()
    {
        await using var app = new ApiFactory(); using var you = app.Client(); using var brother = app.Client("swaene15@gmail.com");
        await Csrf(you); await Csrf(brother);
        var workout = await (await you.PostAsJsonAsync("/api/workouts", Workout())).Content.ReadFromJsonAsync<JsonElement>();
        var id = workout.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await brother.GetAsync($"/api/workouts/{id}")).StatusCode);
        brother.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await brother.PutAsJsonAsync($"/api/workouts/{id}", Workout())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await brother.DeleteAsync($"/api/workouts/{id}")).StatusCode);
        var input = new TemplateInput("Bench", "Strength", "", Workout().Exercises);
        var template = await (await you.PostAsJsonAsync("/api/templates", input)).Content.ReadFromJsonAsync<JsonElement>();
        var tid = template.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await brother.PutAsJsonAsync($"/api/templates/{tid}", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await brother.DeleteAsync($"/api/templates/{tid}")).StatusCode);
        Assert.Empty(await brother.GetFromJsonAsync<JsonElement[]>("/api/templates") ?? []);
    }
    [Fact]
    public async Task FutureCompletedWorkoutsFailButTemplatesAndDraftsDoNotCount()
    {
        await using var app = new ApiFactory(); using var client = app.Client(); await Csrf(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/workouts", Workout("2026-10-02"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/workouts", Workout("2026-10-02", false))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/templates", new TemplateInput("Bench", "Strength", "", Workout().Exercises))).StatusCode);
        var dashboard = await client.GetFromJsonAsync<JsonElement[]>("/api/dashboard");
        Assert.All(dashboard!, d => Assert.Equal(0, d.GetProperty("summary").GetProperty("workoutDaysThisWeek").GetInt32()));
    }
    [Fact]
    public async Task EditsReplaceExercisesAndGoalChangesStartNextMonday()
    {
        await using var app = new ApiFactory(); using var client = app.Client(); await Csrf(client);
        var created = await (await client.PostAsJsonAsync("/api/workouts", Workout())).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();
        var updated = Workout() with { Exercises = [new("Running", "Cardio", [], 30, 3)] };
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "1");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/workouts/{id}", updated)).StatusCode);
        var read = await client.GetFromJsonAsync<JsonElement>($"/api/workouts/{id}");
        Assert.Single(read.GetProperty("exercises").EnumerateArray());
        Assert.Equal("Running", read.GetProperty("exercises")[0].GetProperty("name").GetString());
        var goal = await (await client.PutAsJsonAsync("/api/goals", new GoalInput(5))).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-10-05", goal.GetProperty("effectiveWeek").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/goals", new GoalInput(8))).StatusCode);
        client.DefaultRequestHeaders.Remove("If-Match");
        client.DefaultRequestHeaders.TryAddWithoutValidation("If-Match", "2");
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/workouts/{id}")).StatusCode);
    }
}
