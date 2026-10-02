using System.Text.Json.Serialization;
namespace Workout.Api;

public class WorkoutMutationReceipt
{
    public Guid UserId { get; set; }
    public Guid MutationId { get; set; }
    public string RequestJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
}
public class NotificationPreference
{
    public Guid UserId { get; set; }
    public bool Reminders { get; set; } = true;
    public bool Partner { get; set; } = true;
    public bool Celebration { get; set; } = true;
    public bool Nudge { get; set; } = true;
    public string ReminderTime { get; set; } = "18:00";
    public string ReminderDays { get; set; } = "0,1,2,3,4,5,6";
    public string QuietStart { get; set; } = "21:00";
    public string QuietEnd { get; set; } = "08:00";
}
public class DeviceSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [JsonIgnore] public string Endpoint { get; set; } = "";
    [JsonIgnore] public string P256dh { get; set; } = "";
    [JsonIgnore] public string Auth { get; set; } = "";
}
public class NotificationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DedupKey { get; set; } = "";
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly Week { get; set; }
    public DateTimeOffset Due { get; set; }
    public DateTimeOffset Expires { get; set; }
    public bool Finished { get; set; }
}
public class NotificationDelivery
{
    public Guid JobId { get; set; }
    public Guid SubscriptionId { get; set; }
    public DateTimeOffset Due { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public int Attempts { get; set; }
    public bool Finished { get; set; }
    public bool Sent { get; set; }
}
public record WorkoutMutation(Guid MutationId, string Operation, Guid WorkoutId, long? ExpectedRevision, WorkoutInput? Workout);
public record SubscriptionKeys(string P256dh, string Auth);
public record SubscriptionInput(string Endpoint, SubscriptionKeys Keys);
public record SubscriptionDelete(string Endpoint);
