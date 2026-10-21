using RoomieSync.Core.Models;

namespace RoomieSync.Data.InMemory;

public sealed class InMemoryDataStore
{
    internal readonly object Sync = new();

    internal List<Household> Households { get; } = new();
    internal List<Member> Members { get; } = new();
    internal List<UserAccount> Accounts { get; } = new();
    internal List<Expense> Expenses { get; } = new();
    internal List<Settlement> Settlements { get; } = new();
    internal List<Chore> Chores { get; } = new();
    internal List<Notification> Notifications { get; } = new();

    internal static Household Clone(Household h) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Currency = h.Currency,
        CreatedAt = h.CreatedAt
    };

    internal static Member Clone(Member m) => new()
    {
        Id = m.Id,
        HouseholdId = m.HouseholdId,
        DisplayName = m.DisplayName,
        Role = m.Role,
        AvatarColor = m.AvatarColor,
        JoinedAt = m.JoinedAt,
        IsActive = m.IsActive,
        IsEmailConfirmed = m.IsEmailConfirmed
    };

    internal static UserAccount Clone(UserAccount a) => new()
    {
        MemberId = a.MemberId,
        Email = a.Email,
        PasswordHash = a.PasswordHash,
        PasswordSalt = a.PasswordSalt,
        FailedAttempts = a.FailedAttempts,
        LockedUntil = a.LockedUntil
    };

    internal static Expense Clone(Expense e) => new()
    {
        Id = e.Id,
        HouseholdId = e.HouseholdId,
        Title = e.Title,
        Merchant = e.Merchant,
        Amount = e.Amount,
        Date = e.Date,
        Category = e.Category,
        PaidByMemberId = e.PaidByMemberId,
        SplitMethod = e.SplitMethod,
        ReceiptImagePath = e.ReceiptImagePath,
        CreatedAt = e.CreatedAt,
        DeleteApprovedBy = new HashSet<Guid>(e.DeleteApprovedBy),
        Shares = e.Shares.Select(s => new ExpenseShare { MemberId = s.MemberId, Amount = s.Amount, Percentage = s.Percentage }).ToList()
    };

    internal static Settlement Clone(Settlement s) => new()
    {
        Id = s.Id,
        HouseholdId = s.HouseholdId,
        FromMemberId = s.FromMemberId,
        ToMemberId = s.ToMemberId,
        Amount = s.Amount,
        Date = s.Date
    };

    internal static Chore Clone(Chore c) => new()
    {
        Id = c.Id,
        HouseholdId = c.HouseholdId,
        Title = c.Title,
        AssigneeId = c.AssigneeId,
        Deadline = c.Deadline,
        Status = c.Status,
        Rotation = c.Rotation,
        CreatedAt = c.CreatedAt,
        CompletedAt = c.CompletedAt
    };

    internal static Notification Clone(Notification n) => new()
    {
        Id = n.Id,
        HouseholdId = n.HouseholdId,
        RecipientMemberId = n.RecipientMemberId,
        Type = n.Type,
        Message = n.Message,
        CreatedAt = n.CreatedAt,
        IsRead = n.IsRead
    };
}
