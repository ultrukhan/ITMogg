namespace RoomieSync.Core.Models;

public sealed class Chore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? AssigneeId { get; set; }
    public DateTime? Deadline { get; set; }
    public ChoreStatus Status { get; set; } = ChoreStatus.ToDo;
    public ChoreRotation Rotation { get; set; } = ChoreRotation.Weekly;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public bool IsOverdue(DateTime now) => Status != ChoreStatus.Done && Deadline is { } d && d < now;
}
