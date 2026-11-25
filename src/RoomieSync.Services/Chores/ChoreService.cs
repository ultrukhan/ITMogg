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
    {
        InputValidator.RequireNonEmptyGuid(householdId, "Household ID");
        return chores.GetByHouseholdAsync(householdId, ct);
    }

    public async Task<Chore> CreateChoreAsync(Guid householdId, NewChoreRequest request, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(householdId, "Household ID");
        if (request is null)
            throw new DomainException("Chore request is required.");

        var title = InputValidator.ValidateChoreTitle(request.Title);
        if (!Enum.IsDefined(request.Rotation))
            throw new DomainException("Invalid chore rotation.");

        if (request.Deadline is { } deadline
            && (deadline < clock.Now.AddMinutes(-1) || deadline > clock.Now.AddYears(InputValidator.MaxFutureChoreYears)))
        {
            throw new DomainException("Chore deadline must not be in the past or more than 5 years ahead.");
        }

        await EnsureValidAssigneeAsync(householdId, request.AssigneeId, ct);

        var chore = new Chore
        {
            HouseholdId = householdId,
            Title = title,
            AssigneeId = request.AssigneeId,
            Deadline = request.Deadline,
            Rotation = request.Rotation,
            CreatedAt = clock.Now
        };
        await chores.AddAsync(chore, ct);
        await NotifyAssignedAsync(chore, ct);
        return chore;
    }

    public async Task<Chore> UpdateChoreAsync(Chore chore, CancellationToken ct = default)
    {
        if (chore is null)
            throw new DomainException("Chore is required.");

        InputValidator.RequireNonEmptyGuid(chore.Id, "Chore ID");
        InputValidator.RequireNonEmptyGuid(chore.HouseholdId, "Household ID");
        chore.Title = InputValidator.ValidateChoreTitle(chore.Title);

        if (!Enum.IsDefined(chore.Status))
            throw new DomainException("Invalid chore status.");
        if (!Enum.IsDefined(chore.Rotation))
            throw new DomainException("Invalid chore rotation.");

        if (chore.Deadline is { } deadline
            && (deadline < InputValidator.MinAllowedDate || deadline > clock.Now.AddYears(InputValidator.MaxFutureChoreYears)))
        {
            throw new DomainException("Chore deadline is out of the allowed date range.");
        }

        await EnsureValidAssigneeAsync(chore.HouseholdId, chore.AssigneeId, ct);
        await chores.UpdateAsync(chore, ct);
        return chore;
    }

    public async Task<Chore> AssignAsync(Guid choreId, Guid? assigneeId, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(choreId, "Chore ID");
        var chore = await chores.GetByIdAsync(choreId, ct) ?? throw new DomainException("Task not found.");
        await EnsureValidAssigneeAsync(chore.HouseholdId, assigneeId, ct);

        chore.AssigneeId = assigneeId;
        await chores.UpdateAsync(chore, ct);
        await NotifyAssignedAsync(chore, ct);
        return chore;
    }

    public async Task<Chore?> ChangeStatusAsync(Guid choreId, ChoreStatus status, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(choreId, "Chore ID");
        if (!Enum.IsDefined(status))
            throw new DomainException("Invalid chore status.");

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

    public Task DeleteChoreAsync(Guid choreId, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(choreId, "Chore ID");
        return chores.DeleteAsync(choreId, ct);
    }

    public Guid? GetNextAssignee(IReadOnlyList<Member> memberList, Guid? currentAssignee)
    {
        if (memberList is null)
            throw new DomainException("Member list is required.");

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

    private async Task EnsureValidAssigneeAsync(Guid householdId, Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is not { } id) return;

        InputValidator.RequireNonEmptyGuid(id, "Assignee ID");
        var assignee = await members.GetByIdAsync(id, ct) ?? throw new DomainException("Selected assignee not found.");
        if (!assignee.IsActive || assignee.HouseholdId != householdId)
            throw new DomainException("Selected assignee must be an active member of this household.");
    }

    private async Task NotifyAssignedAsync(Chore chore, CancellationToken ct)
    {
        if (chore.AssigneeId is { } id)
            await notifications.NotifyMemberAsync(chore.HouseholdId, id, NotificationType.ChoreAssigned,
                $"You were assigned \"{chore.Title}\"" + (chore.Deadline is { } d ? $" — due {d:MMM d, HH:mm}" : ""), ct);
    }
}
