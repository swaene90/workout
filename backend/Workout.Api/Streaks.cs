namespace Workout.Api;

public record ActivityDay(DateOnly Date, bool WorkedOut);
public record WeekSummary(DateOnly Start, int Days, int Goal, bool Met);
public record StreakSummary(int CurrentStreak, int LongestStreak, int WorkoutDaysThisWeek,
    int WeeklyGoal, int? NextWeeklyGoal, DateOnly WeekStart, List<ActivityDay> Calendar, List<WeekSummary> Weeks);

public static class Streaks
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);
    public static DateOnly Monday(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static StreakSummary Calculate(IEnumerable<DateOnly> workoutDates, IEnumerable<WeeklyGoal> goals, DateOnly today)
    {
        var dates = workoutDates.Where(d => d <= today).ToHashSet();
        var history = goals.OrderBy(g => g.EffectiveWeek).ToList();
        int Goal(DateOnly week) => history.LastOrDefault(g => g.EffectiveWeek <= week)?.Days ?? 3;
        var currentWeek = Monday(today);
        var firstWeek = dates.Count > 0 ? Monday(dates.Min()) : currentWeek;
        var weeks = new List<WeekSummary>();
        int consecutive = 0, longest = 0, current = 0;
        for (var week = firstWeek; week <= currentWeek; week = week.AddDays(7))
        {
            var days = Enumerable.Range(0, 7).Count(i => dates.Contains(week.AddDays(i)));
            var goal = Goal(week);
            bool met = days >= goal;
            weeks.Add(new(week, days, goal, met));
            if (met) consecutive++;
            else if (week < currentWeek) consecutive = 0;
            longest = Math.Max(longest, consecutive);
            current = consecutive;
        }
        var calendar = Enumerable.Range(0, 7).Select(i => new ActivityDay(currentWeek.AddDays(i), dates.Contains(currentWeek.AddDays(i)))).ToList();
        return new(current, longest, calendar.Count(d => d.WorkedOut), Goal(currentWeek),
            history.FirstOrDefault(g => g.EffectiveWeek == currentWeek.AddDays(7))?.Days,
            currentWeek, calendar, weeks.TakeLast(12).ToList());
    }
}
