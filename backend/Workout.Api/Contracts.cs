namespace Workout.Api;

public record SetInput(int Reps, decimal WeightLb);
public record ExerciseInput(string Name, string Kind, List<SetInput> Sets, decimal? DurationMinutes, decimal? DistanceMiles);
public record WorkoutInput(DateOnly Date, string Title, string Type, bool Completed,
    decimal? DurationMinutes, string Notes, List<ExerciseInput> Exercises);
public record TemplateInput(string Name, string Type, string Notes, List<ExerciseInput> Exercises);
public record GoalInput(int Days);
public record AppearanceInput(string Theme, string Mode);

public static class Inputs
{
    public static readonly string[] Types = ["Strength", "Cardio", "Mixed", "Other"];
    public static Dictionary<string, string[]> Validate(WorkoutInput input, DateOnly today)
    {
        var errors = ValidateExercises(input.Exercises);
        if (input.Date < new DateOnly(1970, 1, 5)) errors["date"] = ["Choose a date on or after January 5, 1970."];
        if (input.Completed && input.Date > today) errors["date"] = ["Completed workouts cannot be in the future."];
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 120) errors["title"] = ["Enter a title of 1–120 characters."];
        if (!Types.Contains(input.Type)) errors["type"] = ["Choose Strength, Cardio, Mixed, or Other."];
        if (input.DurationMinutes is <= 0 or > 1440) errors["durationMinutes"] = ["Duration must be greater than 0 and at most 1440 minutes."];
        if (input.Notes is null || input.Notes.Length > 4000) errors["notes"] = ["Notes must be at most 4000 characters."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(TemplateInput input)
    {
        var errors = ValidateExercises(input.Exercises);
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) errors["name"] = ["Enter a name of 1–120 characters."];
        if (!Types.Contains(input.Type)) errors["type"] = ["Choose Strength, Cardio, Mixed, or Other."];
        if (input.Notes is null || input.Notes.Length > 4000) errors["notes"] = ["Notes must be at most 4000 characters."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidateExercises(List<ExerciseInput>? exercises)
    {
        var errors = new Dictionary<string, string[]>();
        if (exercises is null || exercises.Count > 50) { errors["exercises"] = ["Provide at most 50 exercises."]; return errors; }
        for (int i = 0; i < exercises.Count; i++)
        {
            var e = exercises[i];
            if (e is null || string.IsNullOrWhiteSpace(e.Name) || e.Name.Length > 100 || !new[] { "Strength", "Cardio" }.Contains(e.Kind))
            { errors[$"exercises[{i}]"] = ["Enter an exercise name and choose Strength or Cardio."]; continue; }
            if (e.Sets is null || e.Sets.Count > 100 || e.Sets.Any(s => s is null || s.Reps < 1 || s.Reps > 1000 || s.WeightLb < 0 || s.WeightLb > 10000))
                errors[$"exercises[{i}].sets"] = ["Sets require 1–1000 reps and 0–10000 lb; at most 100 sets per exercise."];
            if (e.DurationMinutes is <= 0 or > 1440 || e.DistanceMiles is < 0 or > 1000)
                errors[$"exercises[{i}].cardio"] = ["Enter valid duration and distance."];
            if (e.Kind == "Cardio" && (e.Sets?.Count > 0 || e.DurationMinutes is null))
                errors[$"exercises[{i}]"] = ["Cardio requires duration and cannot contain strength sets."];
            if (e.Kind == "Strength" && (e.DurationMinutes is not null || e.DistanceMiles is not null))
                errors[$"exercises[{i}]"] = ["Strength exercises use sets, reps, and weight."];
        }
        return errors;
    }

    public static List<ExerciseEntry> Entities(List<ExerciseInput> exercises) => exercises.Select((e, i) => new ExerciseEntry
    {
        Name = e.Name.Trim(),
        Kind = e.Kind,
        Position = i,
        DurationMinutes = e.DurationMinutes,
        DistanceMiles = e.DistanceMiles,
        Sets = e.Sets.Select((s, j) => new StrengthSet { Reps = s.Reps, WeightLb = s.WeightLb, Position = j }).ToList()
    }).ToList();
}
