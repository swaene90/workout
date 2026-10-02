using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Workout.Api;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
builder.Services.AddDbContext<WorkoutDb>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Workout") ?? "Host=192.168.0.48;Port=5432;Database=workout;Username=workout_app"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPushSender>(services => new PushSender(PushConfiguration.Client(), services.GetRequiredService<IConfiguration>()));
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddProblemDetails();
builder.Services.AddWorkoutAuth(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "workout.csrf";
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
var protection = builder.Services.AddDataProtection().SetApplicationName("Workout");
if (builder.Configuration["DataProtection:Path"] is { Length: > 0 } keyPath)
    protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Parse(builder.Configuration["Proxy:TrustedIp"] ?? "172.30.0.3"));
    options.ForwardLimit = 1;
});

if (args.Contains("--generate-vapid"))
{
    var keys = PushConfiguration.GenerateKeys();
    Console.WriteLine($"Notifications__PublicKey={keys.PublicKey}");
    Console.WriteLine($"Notifications__PrivateKey={keys.PrivateKey}");
    return;
}
var app = builder.Build();
if (args.Contains("--migrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<WorkoutDb>().Database.MigrateAsync();
    return;
}
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), branch => branch.UseHttpsRedirection());
}
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https://*.googleusercontent.com; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth") || context.Request.Path.StartsWithSegments("/signin-google"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider(new Dictionary<string, string>(new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider().Mappings) { [".webmanifest"] = "application/manifest+json" }),
    OnPrepareResponse = context =>
    {
        if (context.File.Name is "sw.js" or "index.html" or "manifest.webmanifest") context.Context.Response.Headers.CacheControl = "no-cache";
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    var expected = context.Request.Headers["X-Workout-Account"].FirstOrDefault();
    if (context.Request.Path.StartsWithSegments("/api") && context.Request.Path != "/api/me" && expected is not null &&
        context.User.HasClaim(c => c.Type == WorkoutAuth.UserClaim) && expected != WorkoutAuth.UserId(context.User).ToString())
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { detail = "Your account changed. Sign in to the same account before continuing." });
        return;
    }
    await next();
});

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (WorkoutDb db) =>
{
    try { return await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503); }
    catch { return Results.StatusCode(503); }
});
app.MapGet("/auth/login", (IConfiguration config) =>
    string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"]) || string.IsNullOrWhiteSpace(config["Authentication:Google:ClientSecret"])
        ? Results.Problem("Google login has not been configured.", statusCode: 503)
        : Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, ["Google"]));

var api = app.MapGroup("/api").RequireAuthorization("Member");
api.AddEndpointFilter(async (context, next) =>
{
    var http = context.HttpContext;
    if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method))
    {
        try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
        catch (AntiforgeryValidationException) { return Results.Problem("Refresh the page and try again.", statusCode: 400, title: "Invalid CSRF token"); }
    }
    return await next(context);
});
api.MapGet("/me", async (HttpContext http, WorkoutDb db, IAntiforgery csrf, TimeProvider clock) =>
{
    var member = await db.Members.AsNoTracking().SingleAsync(x => x.Id == WorkoutAuth.UserId(http.User));
    return Results.Ok(new { user = member, csrfToken = csrf.GetAndStoreTokens(http).RequestToken, today = Streaks.Today(clock), timeZone = "America/New_York" });
});
api.MapPost("/logout", async (HttpContext http) => { await http.SignOutAsync("Cookies"); return Results.NoContent(); });
api.MapPut("/preferences", async (AppearanceInput input, HttpContext http, WorkoutDb db) =>
{
    var errors = new Dictionary<string, string[]>();
    if (!new[] { "green", "red", "blue", "beige", "purple" }.Contains(input.Theme))
        errors["theme"] = ["Choose green, red, blue, beige, or purple."];
    if (!new[] { "light", "dark" }.Contains(input.Mode))
        errors["mode"] = ["Choose light or dark mode."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var member = await db.Members.SingleAsync(x => x.Id == WorkoutAuth.UserId(http.User));
    member.Theme = input.Theme;
    member.Mode = input.Mode;
    await db.SaveChangesAsync();
    return Results.Ok(new { member.Theme, member.Mode });
});
api.MapGet("/dashboard", async (WorkoutDb db, TimeProvider clock) =>
{
    var today = Streaks.Today(clock);
    var members = await db.Members.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
    var goals = await db.WeeklyGoals.AsNoTracking().ToListAsync();
    var sessions = await db.Workouts.AsNoTracking().Where(x => x.Completed).Select(x => new { x.UserId, x.Date }).ToListAsync();
    return Results.Ok(members.Select(m => new { user = m, summary = Streaks.Calculate(sessions.Where(x => x.UserId == m.Id).Select(x => x.Date), goals.Where(x => x.UserId == m.Id), today) }));
});
api.MapGet("/offline-snapshot", async (HttpContext http, WorkoutDb db, TimeProvider clock) =>
{
    var user = WorkoutAuth.UserId(http.User); var today = Streaks.Today(clock); var week = Streaks.Monday(today);
    var workouts = await db.Workouts.AsNoTracking().Where(w => w.UserId == user && w.Date >= week && w.Date <= today)
        .Include(w => w.Exercises.OrderBy(e => e.Position)).ThenInclude(e => e.Sets.OrderBy(s => s.Position)).ToListAsync();
    return Results.Ok(new { week, workouts });
});
api.MapGet("/workouts", async (WorkoutDb db, Guid? userId, DateOnly? from, DateOnly? to, int? page) =>
{
    var query = db.Workouts.AsNoTracking().AsQueryable();
    if (userId.HasValue) query = query.Where(x => x.UserId == userId);
    if (from.HasValue) query = query.Where(x => x.Date >= from);
    if (to.HasValue) query = query.Where(x => x.Date <= to);
    int currentPage = Math.Max(1, page ?? 1);
    var total = await query.CountAsync();
    var items = await query.OrderByDescending(x => x.Date).ThenBy(x => x.Id).Skip((currentPage - 1) * 30).Take(30)
        .Include(x => x.Exercises.OrderBy(e => e.Position)).ThenInclude(x => x.Sets.OrderBy(s => s.Position)).ToListAsync();
    return Results.Ok(new { items, total, page = currentPage });
});
api.MapGet("/workouts/{id:guid}", async (Guid id, WorkoutDb db) =>
{
    var workout = await db.Workouts.AsNoTracking().Include(x => x.Exercises.OrderBy(e => e.Position)).ThenInclude(x => x.Sets.OrderBy(s => s.Position)).SingleOrDefaultAsync(x => x.Id == id);
    return workout is null ? Results.NotFound() : Results.Ok(workout);
});
api.MapNotifications();
api.MapPost("/workout-mutations", (WorkoutMutation input, HttpContext http, WorkoutDb db, TimeProvider clock) =>
    WorkoutMutations.Apply(input, WorkoutAuth.UserId(http.User), db, clock));
api.MapPost("/workouts", (WorkoutInput input, HttpContext http, WorkoutDb db, TimeProvider clock) =>
    WorkoutMutations.Apply(new WorkoutMutation(Guid.NewGuid(), "create", Guid.NewGuid(), null, input), WorkoutAuth.UserId(http.User), db, clock));
api.MapPut("/workouts/{id:guid}", (Guid id, WorkoutInput input, HttpContext http, WorkoutDb db, TimeProvider clock) =>
    long.TryParse(http.Request.Headers["If-Match"].FirstOrDefault()?.Trim('"'), out var revision)
        ? WorkoutMutations.Apply(new WorkoutMutation(Guid.NewGuid(), "update", id, revision, input), WorkoutAuth.UserId(http.User), db, clock)
        : Task.FromResult(Results.Problem("Supply the workout revision in If-Match.", statusCode: 428)));
api.MapDelete("/workouts/{id:guid}", async (Guid id, HttpContext http, WorkoutDb db, TimeProvider clock) =>
{
    if (!long.TryParse(http.Request.Headers["If-Match"].FirstOrDefault()?.Trim('"'), out var revision))
        return Results.Problem("Supply the workout revision in If-Match.", statusCode: 428);
    var result = await WorkoutMutations.Apply(new WorkoutMutation(Guid.NewGuid(), "delete", id, revision, null), WorkoutAuth.UserId(http.User), db, clock);
    return result is Microsoft.AspNetCore.Http.HttpResults.ContentHttpResult { StatusCode: 200 } ? Results.NoContent() : result;
});
api.MapGet("/goals", async (HttpContext http, WorkoutDb db) => Results.Ok(await db.WeeklyGoals.AsNoTracking()
    .Where(x => x.UserId == WorkoutAuth.UserId(http.User)).OrderBy(x => x.EffectiveWeek).ToListAsync()));
api.MapPut("/goals", async (GoalInput input, HttpContext http, WorkoutDb db, TimeProvider clock) =>
{
    if (input.Days is < 1 or > 7) return Results.ValidationProblem(new Dictionary<string, string[]> { ["days"] = ["Choose 1–7 days."] });
    var userId = WorkoutAuth.UserId(http.User);
    var week = Streaks.Monday(Streaks.Today(clock)).AddDays(7);
    var goal = await db.WeeklyGoals.FindAsync(userId, week);
    if (goal is null) db.WeeklyGoals.Add(goal = new WeeklyGoal { UserId = userId, EffectiveWeek = week });
    goal.Days = input.Days;
    await db.SaveChangesAsync(); return Results.Ok(goal);
});
api.MapGet("/templates", async (HttpContext http, WorkoutDb db) =>
{
    var templates = await db.Templates.AsNoTracking().Where(x => x.UserId == WorkoutAuth.UserId(http.User)).OrderBy(x => x.Name).ToListAsync();
    return Results.Ok(templates.Select(TemplateView));
});
api.MapPost("/templates", async (TemplateInput input, HttpContext http, WorkoutDb db) =>
{
    var errors = Inputs.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    var template = new WorkoutTemplate { UserId = WorkoutAuth.UserId(http.User), Name = input.Name.Trim(), Type = input.Type, Notes = input.Notes, ExercisesJson = JsonSerializer.Serialize(input.Exercises, JsonSerializerOptions.Web) };
    db.Templates.Add(template); await db.SaveChangesAsync(); return Results.Created($"/api/templates/{template.Id}", TemplateView(template));
});
api.MapPut("/templates/{id:guid}", async (Guid id, TemplateInput input, HttpContext http, WorkoutDb db) =>
{
    var template = await db.Templates.FindAsync(id);
    if (template is null) return Results.NotFound();
    if (template.UserId != WorkoutAuth.UserId(http.User)) return Results.Forbid();
    var errors = Inputs.Validate(input);
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    template.Name = input.Name.Trim(); template.Type = input.Type; template.Notes = input.Notes;
    template.ExercisesJson = JsonSerializer.Serialize(input.Exercises, JsonSerializerOptions.Web);
    await db.SaveChangesAsync(); return Results.Ok(TemplateView(template));
});
api.MapDelete("/templates/{id:guid}", async (Guid id, HttpContext http, WorkoutDb db) =>
{
    var template = await db.Templates.FindAsync(id);
    if (template is null) return Results.NotFound();
    if (template.UserId != WorkoutAuth.UserId(http.User)) return Results.Forbid();
    db.Templates.Remove(template); await db.SaveChangesAsync(); return Results.NoContent();
});
api.MapGet("/progress", async (WorkoutDb db, Guid userId, string exercise) =>
{
    if (string.IsNullOrWhiteSpace(exercise)) return Results.BadRequest();
    var normalized = exercise.Trim().ToLower();
    var sessions = await db.Workouts.AsNoTracking().Where(x => x.Completed && x.UserId == userId && x.Exercises.Any(e => e.Name.ToLower() == normalized))
        .Include(x => x.Exercises).ThenInclude(x => x.Sets).OrderBy(x => x.Date).ToListAsync();
    return Results.Ok(sessions.SelectMany(s => s.Exercises.Where(e => e.Name.Trim().Equals(exercise.Trim(), StringComparison.OrdinalIgnoreCase)).Select(e => new
    {
        s.Id,
        s.Date,
        e.Kind,
        e.DurationMinutes,
        e.DistanceMiles,
        maxWeightLb = e.Sets.Count > 0 ? e.Sets.Max(x => x.WeightLb) : (decimal?)null,
        volumeLb = e.Sets.Sum(x => x.WeightLb * x.Reps),
        sets = e.Sets.OrderBy(x => x.Position)
    })));
});
app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/auth")) { context.Response.StatusCode = 404; return; }
    var file = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "index.html");
    if (File.Exists(file)) { context.Response.ContentType = "text/html"; await context.Response.SendFileAsync(file); }
    else { context.Response.StatusCode = 404; }
});
app.Run();

static object TemplateView(WorkoutTemplate template) => new
{
    template.Id,
    template.UserId,
    template.Name,
    template.Type,
    template.Notes,
    exercises = JsonSerializer.Deserialize<List<ExerciseInput>>(template.ExercisesJson, JsonSerializerOptions.Web)
};

public partial class Program;
