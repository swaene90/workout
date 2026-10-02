using Microsoft.EntityFrameworkCore;
using Workout.Api;
using Xunit;

namespace Workout.Tests;

public class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WORKOUT_TEST_POSTGRES"))) Skip = "Set WORKOUT_TEST_POSTGRES to an isolated PostgreSQL database."; }
}
public class PostgresTests
{
    [PostgresFact] public async Task MigrationsPersistNestedExercisesTemplatesAndCascadeDeletes()
    {
        var options = new DbContextOptionsBuilder<WorkoutDb>().UseNpgsql(Environment.GetEnvironmentVariable("WORKOUT_TEST_POSTGRES")).Options;
        var id = Guid.NewGuid(); var templateId = Guid.NewGuid();
        await using (var db = new WorkoutDb(options))
        {
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            db.Workouts.Add(new WorkoutSession { Id = id, UserId = Members.YourId, Date = new(2026, 10, 1), Completed = true,
                Exercises = Inputs.Entities([new("Bench", "Strength", [new(8, 135)], null, null), new("Running", "Cardio", [], 30, 3.1m)]) });
            db.Templates.Add(new WorkoutTemplate { Id = templateId, UserId = Members.YourId, Name = "Test", ExercisesJson = "[]" });
            await db.SaveChangesAsync();
        }
        await using (var db = new WorkoutDb(options))
        {
            var workout = await db.Workouts.Include(w => w.Exercises).ThenInclude(e => e.Sets).SingleAsync(w => w.Id == id);
            Assert.Equal(2, workout.Exercises.Count); Assert.Equal(135, workout.Exercises.Single(e => e.Kind == "Strength").Sets.Single().WeightLb);
            Assert.Equal(3.1m, workout.Exercises.Single(e => e.Kind == "Cardio").DistanceMiles);
            Assert.NotNull(await db.Templates.FindAsync(templateId));
            db.Workouts.Remove(workout); db.Templates.Remove((await db.Templates.FindAsync(templateId))!); await db.SaveChangesAsync();
        }
        await using (var db = new WorkoutDb(options)) Assert.False(await db.Set<ExerciseEntry>().AnyAsync(e => e.WorkoutSessionId == id));
    }
}
