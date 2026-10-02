using Microsoft.EntityFrameworkCore;
namespace Workout.Api;

public static class NotificationRules
{
    public static bool Quiet(NotificationPreference p, TimeOnly time)
    {
        var start = TimeOnly.Parse(p.QuietStart); var end = TimeOnly.Parse(p.QuietEnd);
        return start < end ? time >= start && time < end : start > end && (time >= start || time < end);
    }
    public static DateTimeOffset LocalInstant(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        while (Streaks.Zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Streaks.Zone), TimeSpan.Zero);
    }
    public static DateTimeOffset AfterQuiet(NotificationPreference p, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Streaks.Zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        if (!Quiet(p, time)) return now;
        var date = DateOnly.FromDateTime(local.DateTime);
        if (time >= TimeOnly.Parse(p.QuietEnd)) date = date.AddDays(1);
        return LocalInstant(date, TimeOnly.Parse(p.QuietEnd));
    }
    public static bool Enabled(NotificationPreference p, string kind) => kind switch
    { "reminder" => p.Reminders, "partner" => p.Partner, "celebration" => p.Celebration, "nudge" => p.Nudge, _ => false };
    public static async Task Add(WorkoutDb db, Guid user, string kind, DateOnly date, DateTimeOffset now, NotificationPreference p)
    {
        if (!Enabled(p, kind) || !await db.Subscriptions.AnyAsync(x => x.UserId == user)) return;
        var week = Streaks.Monday(date);
        var key = $"{kind}:{user}:{(kind == "celebration" ? week : date):yyyy-MM-dd}";
        if (db.NotificationJobs.Local.Any(x => x.DedupKey == key) || await db.NotificationJobs.AnyAsync(x => x.DedupKey == key)) return;
        var due = kind is "partner" or "celebration" ? AfterQuiet(p, now) : now;
        if (kind is "reminder" or "nudge" && Quiet(p, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Streaks.Zone).DateTime))) return;
        var job = new NotificationJob { UserId = user, Kind = kind, Date = date, Week = week, DedupKey = key, Due = due, Expires = kind is "reminder" or "nudge" ? now.AddMinutes(15) : now.AddHours(24) };
        db.NotificationJobs.Add(job);
        foreach (var subscription in await db.Subscriptions.Where(x => x.UserId == user).ToListAsync())
            db.NotificationDeliveries.Add(new NotificationDelivery { JobId = job.Id, SubscriptionId = subscription.Id, Due = due });
    }
    public static async Task WorkoutChanged(WorkoutDb db, Guid actor, DateOnly date, List<DateOnly> before, List<DateOnly> after, TimeProvider clock)
    {
        var today = Streaks.Today(clock); var week = Streaks.Monday(today); var now = clock.GetUtcNow();
        if (date == today && !before.Contains(date))
            foreach (var p in await db.NotificationPreferences.Where(x => x.UserId != actor).ToListAsync())
                await Add(db, p.UserId, "partner", date, now, p);
        if (date < week || date > today) return;
        var goals = await db.WeeklyGoals.Where(x => x.UserId == actor).ToListAsync();
        var previous = Streaks.Calculate(before, goals, today);
        var summary = Streaks.Calculate(after, goals, today);
        var pref = await db.NotificationPreferences.FindAsync(actor);
        if (pref is not null && previous.WorkoutDaysThisWeek < previous.WeeklyGoal && summary.WorkoutDaysThisWeek >= summary.WeeklyGoal)
            await Add(db, actor, "celebration", today, now, pref);
    }
    public static async Task Schedule(WorkoutDb db, TimeProvider clock)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8472391)");
        var now = clock.GetUtcNow(); var local = TimeZoneInfo.ConvertTime(now, Streaks.Zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        foreach (var p in await db.NotificationPreferences.ToListAsync())
        {
            var dates = await db.Workouts.Where(x => x.UserId == p.UserId && x.Completed).Select(x => x.Date).ToListAsync();
            var goals = await db.WeeklyGoals.Where(x => x.UserId == p.UserId).ToListAsync();
            var summary = Streaks.Calculate(dates, goals, today);
            if (summary.WorkoutDaysThisWeek >= summary.WeeklyGoal) continue;
            var nudgeAt = LocalInstant(today, new TimeOnly(17, 0));
            if (today.DayOfWeek == DayOfWeek.Sunday && now >= nudgeAt && now < nudgeAt.AddMinutes(15))
                await Add(db, p.UserId, "nudge", today, now, p);
            var reminderAt = LocalInstant(today, TimeOnly.Parse(p.ReminderTime));
            if (!dates.Contains(today) && p.ReminderDays.Split(',').Contains(((int)today.DayOfWeek).ToString()) && now >= reminderAt && now < reminderAt.AddMinutes(15))
            {
                var nudgeKey = $"nudge:{p.UserId}:{today:yyyy-MM-dd}";
                var suppressed = await (from job in db.NotificationJobs join delivery in db.NotificationDeliveries on job.Id equals delivery.JobId where job.DedupKey == nudgeKey && delivery.Sent select delivery).AnyAsync();
                if (!suppressed) await Add(db, p.UserId, "reminder", today, now, p);
            }
        }
        await db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
    }
}
