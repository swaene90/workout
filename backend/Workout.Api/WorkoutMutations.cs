using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace Workout.Api;

public static class WorkoutMutations
{
    public static async Task<IResult> Apply(WorkoutMutation input, Guid userId, WorkoutDb db, TimeProvider clock)
    {
        if (input.MutationId == Guid.Empty || input.WorkoutId == Guid.Empty || !new[] { "create", "update", "delete" }.Contains(input.Operation))
            return Results.BadRequest(new { detail = "Invalid mutation identity or operation." });
        if (input.Operation != "delete")
        {
            if (input.Workout is null) return Results.BadRequest();
            var errors = Inputs.Validate(input.Workout, Streaks.Today(clock));
            if (errors.Count > 0) return Results.ValidationProblem(errors);
        }
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync() : null;
        // Serialize all workout writers and the scheduler, including separate app instances.
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(8472391)");
        var json = JsonSerializer.Serialize(input, JsonSerializerOptions.Web);
        var receipt = await db.MutationReceipts.FindAsync(userId, input.MutationId);
        if (receipt is not null)
            return receipt.RequestJson == json ? Results.Content(receipt.ResultJson, "application/json") : Results.Conflict(new { detail = "Mutation ID was already used for different changes." });
        var session = await db.Workouts.Include(x => x.Exercises).ThenInclude(x => x.Sets).SingleOrDefaultAsync(x => x.Id == input.WorkoutId);
        if (session is not null && session.UserId != userId) return Results.Forbid();
        if ((input.Operation == "create" && session is not null) || (input.Operation != "create" && (session is null || session.Revision != input.ExpectedRevision)))
            return Results.Conflict(new { detail = "This workout changed on another device.", current = session });
        var beforeDays = await db.Workouts.Where(x => x.UserId == userId && x.Completed).Select(x => x.Date).ToListAsync();
        var previousDate = session?.Date;
        var previousCompleted = session?.Completed ?? false;
        if (input.Operation == "delete") db.Workouts.Remove(session!);
        else
        {
            var w = input.Workout!;
            if (session is null)
            {
                session = new WorkoutSession { Id = input.WorkoutId, UserId = userId };
                db.Workouts.Add(session);
            }
            else
            {
                db.RemoveRange(session.Exercises);
                session.Revision++;
            }
            session.Date = w.Date; session.Title = w.Title.Trim(); session.Type = w.Type;
            session.Completed = w.Completed; session.DurationMinutes = w.DurationMinutes; session.Notes = w.Notes;
            session.Exercises = Inputs.Entities(w.Exercises);
            if (input.Operation == "update") db.AddRange(session.Exercises);
        }
        var resultJson = JsonSerializer.Serialize(input.Operation == "delete" ? new { deleted = true, id = input.WorkoutId } : (object)session!, JsonSerializerOptions.Web);
        db.MutationReceipts.Add(new WorkoutMutationReceipt { UserId = userId, MutationId = input.MutationId, RequestJson = json, ResultJson = resultJson });
        if (input.Operation != "delete" && session!.Completed && (!previousCompleted || previousDate != session.Date))
            {
            var afterDays = await db.Workouts.AsNoTracking().Where(x => x.UserId == userId && x.Completed && x.Id != session.Id).Select(x => x.Date).ToListAsync();
            afterDays.Add(session.Date);
            await NotificationRules.WorkoutChanged(db, userId, session.Date, beforeDays, afterDays, clock);
        }
        await db.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        return Results.Content(resultJson, "application/json", statusCode: input.Operation == "create" ? 201 : 200);
    }
}
