using RoomieSync.Core.Models;

namespace RoomieSync.Core.Abstractions.Repositories;

public interface IHouseholdRepository
{
    Task<Household?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Household?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<Household>> GetAllAsync(CancellationToken ct = default);
    Task<bool> AnyAsync(CancellationToken ct = default);
    Task AddAsync(Household household, CancellationToken ct = default);
    Task UpdateAsync(Household household, CancellationToken ct = default);
}

public interface IMemberRepository
{
    Task<IReadOnlyList<Member>> GetByHouseholdAsync(Guid householdId, bool includeInactive = false, CancellationToken ct = default);
    Task<Member?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Member member, CancellationToken ct = default);
    Task UpdateAsync(Member member, CancellationToken ct = default);
}

public interface IUserAccountRepository
{
    Task<UserAccount?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<UserAccount>> GetAllByEmailAsync(string email, CancellationToken ct = default);
    Task<UserAccount?> GetByMemberIdAsync(Guid memberId, CancellationToken ct = default);
    Task AddAsync(UserAccount account, CancellationToken ct = default);
    Task UpdateAsync(UserAccount account, CancellationToken ct = default);
}

public interface IExpenseRepository
{
    Task<IReadOnlyList<Expense>> GetByHouseholdAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<Expense?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Expense expense, CancellationToken ct = default);
    Task UpdateAsync(Expense expense, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface ISettlementRepository
{
    Task<IReadOnlyList<Settlement>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default);
    Task AddAsync(Settlement settlement, CancellationToken ct = default);
}

public interface IChoreRepository
{
    Task<IReadOnlyList<Chore>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default);
    Task<Chore?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Chore chore, CancellationToken ct = default);
    Task UpdateAsync(Chore chore, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}

public interface INotificationRepository
{
    Task<IReadOnlyList<Notification>> GetForMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid memberId, CancellationToken ct = default);
}

public interface IReportRepository
{
    Task<IReadOnlyList<CategorySpendSummary>> GetCategorySpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlySpendSummary>> GetMonthlySpendingAsync(Guid householdId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<MemberSpendSummary>> GetMemberSpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
}
