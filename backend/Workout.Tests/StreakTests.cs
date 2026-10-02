using Workout.Api;
using Xunit;

namespace Workout.Tests;

public class StreakTests
{
    private static readonly WeeklyGoal[] Default = [new() { Days = 3, EffectiveWeek = new(1970, 1, 5) }];
    private static DateOnly D(string date) => DateOnly.Parse(date);
    private static StreakSummary Calculate(string today, params string[] dates) => Streaks.Calculate(dates.Select(D), Default, D(today));

    [Fact] public void NoWorkoutsHasNoStreak() => Assert.Equal(0, Calculate("2026-10-01").CurrentStreak);
    [Fact] public void RepeatedSessionsCountOneDay()
    {
        var result = Calculate("2026-10-01", "2026-09-28", "2026-09-28", "2026-09-28");
        Assert.Equal(1, result.WorkoutDaysThisWeek); Assert.Equal(0, result.CurrentStreak);
    }
    [Fact] public void IncompleteCurrentWeekPreservesPreviousStreak()
    {
        var result = Calculate("2026-10-01", "2026-09-21", "2026-09-23", "2026-09-25");
        Assert.Equal(1, result.CurrentStreak); Assert.Equal(1, result.LongestStreak);
    }
    [Fact] public void MeetingCurrentGoalImmediatelyExtendsStreak()
    {
        var result = Calculate("2026-10-01", "2026-09-21", "2026-09-23", "2026-09-25", "2026-09-28", "2026-09-29", "2026-10-01");
        Assert.Equal(2, result.CurrentStreak); Assert.Equal(2, result.LongestStreak);
    }
    [Fact] public void MissingPastWeekBreaksCurrentButPreservesLongest()
    {
        var result = Calculate("2026-10-05", "2026-09-21", "2026-09-23", "2026-09-25");
        Assert.Equal(0, result.CurrentStreak); Assert.Equal(1, result.LongestStreak);
    }
    [Fact] public void SundayRemainsCurrentUntilMonday()
    {
        var dates = new[] { "2026-09-21", "2026-09-23", "2026-09-25" };
        Assert.Equal(1, Calculate("2026-10-04", dates).CurrentStreak);
        Assert.Equal(0, Calculate("2026-10-05", dates).CurrentStreak);
    }
    [Fact] public void BackdatedInsertAndDeletionRecalculateStreak()
    {
        var dates = new[] { "2026-09-21", "2026-09-23", "2026-09-25", "2026-09-28", "2026-09-29" };
        Assert.Equal(0, Calculate("2026-10-05", dates).CurrentStreak);
        Assert.Equal(2, Calculate("2026-10-05", dates.Append("2026-10-01").ToArray()).CurrentStreak);
        Assert.Equal(0, Calculate("2026-10-05", dates).CurrentStreak);
    }
    [Fact] public void GoalChangeUsesHistoricalGoalsAndShowsPendingGoal()
    {
        var goals = new[] { Default[0], new WeeklyGoal { EffectiveWeek = D("2026-09-28"), Days = 2 }, new WeeklyGoal { EffectiveWeek = D("2026-10-05"), Days = 5 } };
        var result = Streaks.Calculate(new[] { "2026-09-21", "2026-09-23", "2026-09-25", "2026-09-28", "2026-09-29" }.Select(D), goals, D("2026-10-01"));
        Assert.Equal(2, result.CurrentStreak); Assert.Equal(2, result.WeeklyGoal); Assert.Equal(5, result.NextWeeklyGoal);
    }
    [Fact] public void FutureDatesCannotInflateStats()
    {
        var result = Calculate("2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04");
        Assert.Equal(0, result.CurrentStreak); Assert.Equal(0, result.WorkoutDaysThisWeek);
    }
    [Theory]
    [InlineData("2026-10-05T03:59:59Z", "2026-10-04")]
    [InlineData("2026-10-05T04:00:00Z", "2026-10-05")]
    [InlineData("2026-03-08T06:59:00Z", "2026-03-08")]
    [InlineData("2026-03-08T07:01:00Z", "2026-03-08")]
    [InlineData("2026-11-01T05:59:00Z", "2026-11-01")]
    [InlineData("2026-11-01T06:01:00Z", "2026-11-01")]
    public void NewYorkDateHandlesMidnightAndDst(string utc, string expected) => Assert.Equal(D(expected), Streaks.Today(new FixedClock(DateTimeOffset.Parse(utc))));
}
public class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

public class AuthenticationTests
{
    [Theory]
    [InlineData("swaene1@gmail.com", "true", "123", true)]
    [InlineData("swaene15@gmail.com", "true", "456", true)]
    [InlineData("other@gmail.com", "true", "789", false)]
    [InlineData("swaene1@gmail.com", "false", "123", false)]
    [InlineData("swaene1@gmail.com", null, "123", false)]
    [InlineData("swaene1@gmail.com", "true", "", false)]
    public void OnlyVerifiedAllowedGoogleIdentitiesAreEligible(string email, string? verified, string sub, bool allowed) => Assert.Equal(allowed, WorkoutAuth.Eligible(email, verified, sub));
}
