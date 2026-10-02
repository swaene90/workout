using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workout.Api;
using Xunit;
namespace Workout.Tests;

public class NotificationDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T16:00:00Z");
    private sealed class SqliteWorkoutDb(DbContextOptions<WorkoutDb> options) : WorkoutDb(options)
    {
        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);
            // SQLite cannot compare DateTimeOffset directly; tests use UTC ticks.
            foreach (var entity in b.Model.GetEntityTypes())
                foreach (var property in entity.GetProperties().Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                    b.Entity(entity.ClrType).Property(property.Name).HasConversion<DateTimeOffsetToBinaryConverter>();
        }
    }
    private sealed class Sender : IPushSender
    {
        public int Calls; public Exception? Failure;
        public TaskCompletionSource? Started; public TaskCompletionSource? Release;
        public async Task Send(DeviceSubscription s, string payload, int ttl, CancellationToken ct)
        {
            Calls++; Started?.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(ct);
            if (Failure is not null) throw Failure;
        }
    }
    private static NotificationWorker Worker(Sender sender, DateTimeOffset? now = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new(services.GetRequiredService<IServiceScopeFactory>(), new ConfigurationBuilder().Build(), new FixedClock(now ?? Now), NullLogger<NotificationWorker>.Instance, sender);
    }
    private static async Task Seed(WorkoutDb db)
    {
        await db.Database.EnsureCreatedAsync();
        var sub = new DeviceSubscription { UserId = Members.YourId, Endpoint = "https://web.push.apple.com/a" };
        var job = new NotificationJob { UserId = Members.YourId, DedupKey = Guid.NewGuid().ToString(), Kind = "reminder", Date = new(2026,10,1), Week = new(2026,9,28), Due = Now, Expires = Now.AddDays(1) };
        db.NotificationPreferences.Add(new() { UserId = Members.YourId }); db.Subscriptions.Add(sub); db.NotificationJobs.Add(job);
        db.NotificationDeliveries.Add(new() { JobId = job.Id, SubscriptionId = sub.Id, Due = Now }); await db.SaveChangesAsync();
    }
    [Fact]
    public async Task DeliveryIsClaimedOnceAcrossWorkers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        var sender = new Sender { Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var worker = Worker(sender); var delivery = worker.Deliver(db, CancellationToken.None); await sender.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var otherDb = new SqliteWorkoutDb(options); using var otherWorker = Worker(sender); await otherWorker.Deliver(otherDb, CancellationToken.None);
        Assert.Equal(1, sender.Calls); sender.Release.SetResult(); await delivery;
        Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync()).Finished);
        Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync()).Sent);
        Assert.True((await db.NotificationJobs.AsNoTracking().SingleAsync()).Finished);
    }
    [Fact]
    public async Task TransientFailureSurvivesRestartAndRetriesAfterBackoff()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        var sender = new Sender { Failure = new HttpRequestException("Unavailable") };
        using var worker = Worker(sender); await worker.Deliver(db, CancellationToken.None);
        var retry = await db.NotificationDeliveries.AsNoTracking().SingleAsync(); Assert.False(retry.Finished); Assert.Equal(1, retry.Attempts); Assert.True(retry.Due > Now); Assert.Null(retry.LeaseUntil);
        sender.Failure = null;
        await using var restartedDb = new SqliteWorkoutDb(options); using var restartedWorker = Worker(sender, Now.AddMinutes(3)); await restartedWorker.Deliver(restartedDb, CancellationToken.None);
        Assert.Equal(2, sender.Calls); Assert.True((await restartedDb.NotificationDeliveries.AsNoTracking().SingleAsync()).Finished);
    }
    [Fact]
    public async Task GoneSubscriptionIsRemovedWithoutFurtherRetries()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        var sender = new Sender { Failure = new Lib.Net.Http.WebPush.PushServiceClientException("Gone", HttpStatusCode.Gone) };
        using var worker = Worker(sender); await worker.Deliver(db, CancellationToken.None);
        Assert.Empty(await db.Subscriptions.ToListAsync()); Assert.Empty(await db.NotificationDeliveries.ToListAsync()); Assert.Equal(1,sender.Calls);
    }
    [Fact]
    public async Task DeliveryRechecksPreferencesAndCompletedWorkoutDays()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        db.Workouts.Add(new() { UserId = Members.YourId, Date = new(2026,10,1), Completed = true }); await db.SaveChangesAsync();
        var sender = new Sender(); using var worker = Worker(sender); await worker.Deliver(db, CancellationToken.None);
        Assert.Equal(0,sender.Calls); Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync()).Finished);
    }
    [Fact]
    public async Task AbandonedLeaseIsRecoveredAndExpiredJobIsDiscarded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        var d = await db.NotificationDeliveries.SingleAsync(); d.LeaseUntil = Now.AddMinutes(-1); d.LeaseToken = Guid.NewGuid(); await db.SaveChangesAsync();
        var sender = new Sender(); using var worker = Worker(sender); await worker.Deliver(db, CancellationToken.None); Assert.Equal(1,sender.Calls);
        d = await db.NotificationDeliveries.SingleAsync(); d.Finished = false; (await db.NotificationJobs.SingleAsync()).Expires = Now.AddSeconds(-1); await db.SaveChangesAsync();
        await worker.Deliver(db, CancellationToken.None); Assert.Equal(1,sender.Calls); Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync()).Finished);
    }
    [Fact]
    public async Task SundayCelebrationDeferredUntilMondayStillUsesItsOriginalWeek()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseSqlite(connection).Options;
        await using var db = new SqliteWorkoutDb(options); await Seed(db);
        var morning = DateTimeOffset.Parse("2026-10-05T12:00:00Z");
        var job = await db.NotificationJobs.SingleAsync(); job.Kind = "celebration"; job.Date = new(2026,10,4); job.Due = morning; job.Expires = morning.AddHours(12);
        (await db.NotificationDeliveries.SingleAsync()).Due = morning;
        foreach (var date in new[] { new DateOnly(2026,9,28), new DateOnly(2026,9,29), new DateOnly(2026,10,4) }) db.Workouts.Add(new() { UserId = Members.YourId, Date = date, Completed = true });
        await db.SaveChangesAsync();
        var sender = new Sender(); using var worker = Worker(sender, morning); await worker.Deliver(db, CancellationToken.None);
        Assert.Equal(1,sender.Calls); Assert.True((await db.NotificationDeliveries.AsNoTracking().SingleAsync()).Sent);
    }}
