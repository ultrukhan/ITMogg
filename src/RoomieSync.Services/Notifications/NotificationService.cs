using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Notifications;

/// <summary>Внутрішні сповіщення (фронтенд 1 показує їх у дзвіночку в шапці).</summary>
public sealed class NotificationService(
    INotificationRepository notifications,
    IMemberRepository members,
    IChoreRepository chores,
    IClock clock) : INotificationService
{
    public event EventHandler? NotificationsChanged;

    public Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid memberId, CancellationToken ct = default)
        => notifications.GetForMemberAsync(memberId, 50, ct);

    public async Task NotifyHouseholdAsync(Guid householdId, NotificationType type, string message, Guid? exceptMemberId = null, CancellationToken ct = default)
    {
        var recipients = (await members.GetByHouseholdAsync(householdId, false, ct)).Where(m => m.Id != exceptMemberId);
        await notifications.AddRangeAsync(recipients.Select(m => new Notification
        {
            HouseholdId = householdId, RecipientMemberId = m.Id, Type = type, Message = message, CreatedAt = clock.Now
        }).ToList(), ct);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task NotifyMemberAsync(Guid householdId, Guid memberId, NotificationType type, string message, CancellationToken ct = default)
    {
        await notifications.AddRangeAsync(new[]
        {
            new Notification { HouseholdId = householdId, RecipientMemberId = memberId, Type = type, Message = message, CreatedAt = clock.Now }
        }, ct);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Створює сповіщення про протерміновані завдання (один раз на завдання для кожного учасника).</summary>
    public async Task CheckOverdueChoresAsync(Guid householdId, CancellationToken ct = default)
    {
        var now = clock.Now;
        var overdue = (await chores.GetByHouseholdAsync(householdId, ct)).Where(c => c.IsOverdue(now)).ToList();
        if (overdue.Count == 0) return;

        var memberList = await members.GetByHouseholdAsync(householdId, false, ct);
        var created = new List<Notification>();
        foreach (var m in memberList)
        {
            var existing = await notifications.GetForMemberAsync(m.Id, 200, ct);
            foreach (var c in overdue)
            {
                var who = memberList.FirstOrDefault(x => x.Id == c.AssigneeId)?.FirstName ?? "nobody";
                var text = $"Chore \"{c.Title}\" assigned to {who} is overdue";
                if (existing.Any(n => n.Type == NotificationType.OverdueChore && n.Message == text)) continue;
                created.Add(new Notification { HouseholdId = householdId, RecipientMemberId = m.Id, Type = NotificationType.OverdueChore, Message = text, CreatedAt = now });
            }
        }
        if (created.Count == 0) return;
        await notifications.AddRangeAsync(created, ct);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkAllReadAsync(Guid memberId, CancellationToken ct = default)
    {
        await notifications.MarkAllReadAsync(memberId, ct);
        NotificationsChanged?.Invoke(this, EventArgs.Empty);
    }
}
