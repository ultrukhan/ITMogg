using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Chores;

public sealed class ChoreService(
    IChoreRepository chores,
    IMemberRepository members,
    INotificationService notifications,
    IClock clock) : IChoreService
{
    public Task<IReadOnlyList<Chore>> GetChoresAsync(Guid householdId, CancellationToken ct = default)
        => chores.GetByHouseholdAsync(householdId, ct);

    public async Task<Chore> CreateChoreAsync(Guid householdId, NewChoreRequest request, CancellationToken ct = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        if (title.Length == 0) throw new DomainException("Task name is required.");
        var chore = new Chore
        {
            HouseholdId = householdId, Title = title, AssigneeId = request.AssigneeId,
            Deadline = request.Deadline, Rotation = request.Rotation, CreatedAt = clock.Now
        };
        await chores.AddAsync(chore, ct);
        await NotifyAssignedAsync(chore, ct);
        return chore;
    }

    public async Task<Chore> UpdateChoreAsync(Chore chore, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chore.Title)) throw new DomainException("Task name is required.");
        await chores.UpdateAsync(chore, ct);
        return chore;
    }

    public async Task<Chore> AssignAsync(Guid choreId, Guid? assigneeId, CancellationToken ct = default)
    {
        var chore = await chores.GetByIdAsync(choreId, ct) ?? throw new DomainException("Task not found.");
        chore.AssigneeId = assigneeId;
        await chores.UpdateAsync(chore, ct);
        await NotifyAssignedAsync(chore, ct);
        return chore;
    }

    public async Task<Chore?> ChangeStatusAsync(Guid choreId, ChoreStatus status, CancellationToken ct = default)
    {
        var chore = await chores.GetByIdAsync(choreId, ct) ?? throw new DomainException("Task not found.");
        if (chore.Status == status) return null;

        var wasDone = chore.Status == ChoreStatus.Done;
        chore.Status = status;
        chore.CompletedAt = status == ChoreStatus.Done ? clock.Now : null;
        await chores.UpdateAsync(chore, ct);

        if (status != ChoreStatus.Done || wasDone || chore.Rotation == ChoreRotation.None) return null;

        var list = await members.GetByHouseholdAsync(chore.HouseholdId, false, ct);
        var next = new Chore
        {
            HouseholdId = chore.HouseholdId,
            Title = chore.Title,
            Rotation = chore.Rotation,
            AssigneeId = GetNextAssignee(list, chore.AssigneeId),
            Deadline = NextDeadline(chore.Deadline ?? clock.Now, chore.Rotation),
            CreatedAt = clock.Now
        };
        await chores.AddAsync(next, ct);
        await NotifyAssignedAsync(next, ct);
        return next;
    }

    public Task DeleteChoreAsync(Guid choreId, CancellationToken ct = default) => chores.DeleteAsync(choreId, ct);

    public Guid? GetNextAssignee(IReadOnlyList<Member> memberList, Guid? currentAssignee)
    {
        var active = memberList.Where(m => m.IsActive).OrderBy(m => m.JoinedAt).ToList();
        if (active.Count == 0) return null;
        var i = active.FindIndex(m => m.Id == currentAssignee);
        return active[(i + 1) % active.Count].Id; 
    }

    internal DateTime NextDeadline(DateTime from, ChoreRotation rotation)
    {
        var next = rotation switch
        {
            ChoreRotation.Daily => from.AddDays(1),
            ChoreRotation.Weekly => from.AddDays(7),
            ChoreRotation.Monthly => from.AddMonths(1),
            _ => from
        };
        while (next < clock.Now && rotation != ChoreRotation.None)
            next = NextDeadline(next, rotation);
        return next;
    }

    private async Task NotifyAssignedAsync(Chore chore, CancellationToken ct)
    {
        if (chore.AssigneeId is { } id)
            await notifications.NotifyMemberAsync(chore.HouseholdId, id, NotificationType.ChoreAssigned,
                $"You were assigned \"{chore.Title}\"" + (chore.Deadline is { } d ? $" — due {d:MMM d, HH:mm}" : ""), ct);
    }
}
