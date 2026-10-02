using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Workout.Api;

public static class Members
{
    public static readonly Guid YourId = Guid.Parse("e1111111-1111-4111-8111-111111111111");
    public static readonly Guid BrittId = Guid.Parse("b2222222-2222-4222-8222-222222222222");
    public static bool Allowed(string? email) => email is not null &&
        (email.Equals("swaene1@gmail.com", StringComparison.OrdinalIgnoreCase) ||
         email.Equals("swaene15@gmail.com", StringComparison.OrdinalIgnoreCase));
}

public class Member
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Theme { get; set; } = "green";
    public string Mode { get; set; } = "light";
    public string? ProfilePictureUrl { get; set; }
    [JsonIgnore] public string? GoogleSubject { get; set; }
}

public class WeeklyGoal
{
    public Guid UserId { get; set; }
    public DateOnly EffectiveWeek { get; set; }
    public int Days { get; set; }
}

public class WorkoutSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public string Title { get; set; } = "Workout";
    public string Type { get; set; } = "Mixed";
    public bool Completed { get; set; }
    public decimal? DurationMinutes { get; set; }
    public string Notes { get; set; } = "";
    public List<ExerciseEntry> Exercises { get; set; } = [];
}

public class ExerciseEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore] public Guid WorkoutSessionId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Strength";
    public int Position { get; set; }
    public decimal? DurationMinutes { get; set; }
    public decimal? DistanceMiles { get; set; }
    public List<StrengthSet> Sets { get; set; } = [];
}

public class StrengthSet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [JsonIgnore] public Guid ExerciseEntryId { get; set; }
    public int Position { get; set; }
    public int Reps { get; set; }
    public decimal WeightLb { get; set; }
}

public class WorkoutTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "Mixed";
    public string Notes { get; set; } = "";
    public string ExercisesJson { get; set; } = "[]";
}

public class WorkoutDb(DbContextOptions<WorkoutDb> options) : DbContext(options)
{
    public DbSet<Member> Members => Set<Member>();
    public DbSet<WeeklyGoal> WeeklyGoals => Set<WeeklyGoal>();
    public DbSet<WorkoutSession> Workouts => Set<WorkoutSession>();
    public DbSet<WorkoutTemplate> Templates => Set<WorkoutTemplate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Member>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Member>().HasIndex(x => x.GoogleSubject).IsUnique();
        b.Entity<Member>().Property(x => x.Name).HasMaxLength(100);
        b.Entity<Member>().Property(x => x.Email).HasMaxLength(320);
        b.Entity<Member>().Property(x => x.Theme).HasMaxLength(12).HasDefaultValue("green");
        b.Entity<Member>().Property(x => x.Mode).HasMaxLength(8).HasDefaultValue("light");
        b.Entity<Member>().Property(x => x.ProfilePictureUrl).HasMaxLength(2048);
        b.Entity<Member>().HasData(
            new Member { Id = Workout.Api.Members.YourId, Name = "You", Email = "swaene1@gmail.com" },
            new Member { Id = Workout.Api.Members.BrittId, Name = "Britt", Email = "swaene15@gmail.com" });
        b.Entity<WeeklyGoal>().HasKey(x => new { x.UserId, x.EffectiveWeek });
        b.Entity<WeeklyGoal>().HasOne<Member>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<WeeklyGoal>().HasData(
            new WeeklyGoal { UserId = Workout.Api.Members.YourId, EffectiveWeek = new DateOnly(1970, 1, 5), Days = 3 },
            new WeeklyGoal { UserId = Workout.Api.Members.BrittId, EffectiveWeek = new DateOnly(1970, 1, 5), Days = 3 });
        b.Entity<WorkoutSession>().HasOne<Member>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<WorkoutSession>().HasIndex(x => new { x.UserId, x.Date });
        b.Entity<WorkoutSession>().HasMany(x => x.Exercises).WithOne().HasForeignKey(x => x.WorkoutSessionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ExerciseEntry>().HasMany(x => x.Sets).WithOne().HasForeignKey(x => x.ExerciseEntryId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<WorkoutTemplate>().HasOne<Member>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<WorkoutTemplate>().Property(x => x.ExercisesJson).HasColumnType("jsonb");
        b.Entity<StrengthSet>().Property(x => x.WeightLb).HasPrecision(10, 2);
        b.Entity<ExerciseEntry>().Property(x => x.DurationMinutes).HasPrecision(10, 2);
        b.Entity<ExerciseEntry>().Property(x => x.DistanceMiles).HasPrecision(10, 3);
        b.Entity<WorkoutSession>().Property(x => x.DurationMinutes).HasPrecision(10, 2);
    }
}
