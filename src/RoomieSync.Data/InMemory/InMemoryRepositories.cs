using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Models;
using static RoomieSync.Data.InMemory.InMemoryDataStore;

namespace RoomieSync.Data.InMemory;

public sealed class InMemoryHouseholdRepository(InMemoryDataStore store) : IHouseholdRepository
{
    public Task<Household?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Households.Where(h => h.Id == id).Select(Clone).FirstOrDefault());
    }

    public Task<Household?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        lock (store.Sync)
            return Task.FromResult(store.Households
                .Where(h => string.Equals(h.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(Clone).FirstOrDefault());
    }

    public Task<IReadOnlyList<Household>> GetAllAsync(CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Household> list = store.Households.Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<bool> AnyAsync(CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Households.Count > 0);
    }

    public Task AddAsync(Household household, CancellationToken ct = default)
    {
        lock (store.Sync) store.Households.Add(Clone(household));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Household household, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var i = store.Households.FindIndex(h => h.Id == household.Id);
            if (i >= 0) store.Households[i] = Clone(household);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryMemberRepository(InMemoryDataStore store) : IMemberRepository
{
    public Task<IReadOnlyList<Member>> GetByHouseholdAsync(Guid householdId, bool includeInactive = false, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Member> list = store.Members
                .Where(m => m.HouseholdId == householdId && (includeInactive || m.IsActive))
                .OrderBy(m => m.JoinedAt)
                .Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Member?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Members.Where(m => m.Id == id).Select(Clone).FirstOrDefault());
    }

    public Task AddAsync(Member member, CancellationToken ct = default)
    {
        lock (store.Sync) store.Members.Add(Clone(member));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Member member, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var i = store.Members.FindIndex(m => m.Id == member.Id);
            if (i >= 0) store.Members[i] = Clone(member);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryUserAccountRepository(InMemoryDataStore store) : IUserAccountRepository
{
    public Task<UserAccount?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        lock (store.Sync)
            return Task.FromResult(store.Accounts
                .Where(a => string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(Clone).FirstOrDefault());
    }

    public Task<IReadOnlyList<UserAccount>> GetAllByEmailAsync(string email, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<UserAccount> list = store.Accounts
                .Where(a => string.Equals(a.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
                .Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<UserAccount?> GetByMemberIdAsync(Guid memberId, CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Accounts.Where(a => a.MemberId == memberId).Select(Clone).FirstOrDefault());
    }

    public Task AddAsync(UserAccount account, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            store.Accounts.RemoveAll(a => a.MemberId == account.MemberId);
            store.Accounts.Add(Clone(account));
        }
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UserAccount account, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var i = store.Accounts.FindIndex(a => a.MemberId == account.MemberId);
            if (i >= 0) store.Accounts[i] = Clone(account);
        }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryExpenseRepository(InMemoryDataStore store) : IExpenseRepository
{
    public Task<IReadOnlyList<Expense>> GetByHouseholdAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Expense> list = store.Expenses
                .Where(e => e.HouseholdId == householdId
                            && (from == null || e.Date >= from)
                            && (to == null || e.Date <= to))
                .OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
                .Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Expense?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Expenses.Where(e => e.Id == id).Select(Clone).FirstOrDefault());
    }

    public Task AddAsync(Expense expense, CancellationToken ct = default)
    {
        lock (store.Sync) store.Expenses.Add(Clone(expense));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Expense expense, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var i = store.Expenses.FindIndex(e => e.Id == expense.Id);
            if (i >= 0) store.Expenses[i] = Clone(expense);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) store.Expenses.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class InMemorySettlementRepository(InMemoryDataStore store) : ISettlementRepository
{
    public Task<IReadOnlyList<Settlement>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Settlement> list = store.Settlements.Where(s => s.HouseholdId == householdId)
                .OrderByDescending(s => s.Date).Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task AddAsync(Settlement settlement, CancellationToken ct = default)
    {
        lock (store.Sync) store.Settlements.Add(Clone(settlement));
        return Task.CompletedTask;
    }
}

public sealed class InMemoryChoreRepository(InMemoryDataStore store) : IChoreRepository
{
    public Task<IReadOnlyList<Chore>> GetByHouseholdAsync(Guid householdId, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Chore> list = store.Chores.Where(c => c.HouseholdId == householdId)
                .OrderBy(c => c.CreatedAt).Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task<Chore?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) return Task.FromResult(store.Chores.Where(c => c.Id == id).Select(Clone).FirstOrDefault());
    }

    public Task AddAsync(Chore chore, CancellationToken ct = default)
    {
        lock (store.Sync) store.Chores.Add(Clone(chore));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Chore chore, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var i = store.Chores.FindIndex(c => c.Id == chore.Id);
            if (i >= 0) store.Chores[i] = Clone(chore);
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        lock (store.Sync) store.Chores.RemoveAll(c => c.Id == id);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryNotificationRepository(InMemoryDataStore store) : INotificationRepository
{
    public Task<IReadOnlyList<Notification>> GetForMemberAsync(Guid memberId, int take = 50, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            IReadOnlyList<Notification> list = store.Notifications.Where(n => n.RecipientMemberId == memberId)
                .OrderByDescending(n => n.CreatedAt).Take(take).Select(Clone).ToList();
            return Task.FromResult(list);
        }
    }

    public Task AddRangeAsync(IEnumerable<Notification> notifications, CancellationToken ct = default)
    {
        lock (store.Sync) store.Notifications.AddRange(notifications.Select(Clone));
        return Task.CompletedTask;
    }

    public Task MarkAllReadAsync(Guid memberId, CancellationToken ct = default)
    {
        lock (store.Sync)
            foreach (var n in store.Notifications.Where(n => n.RecipientMemberId == memberId))
                n.IsRead = true;
        return Task.CompletedTask;
    }
}

public sealed class InMemoryReportRepository(InMemoryDataStore store) : IReportRepository
{
    public Task<IReadOnlyList<CategorySpendSummary>> GetCategorySpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var filtered = store.Expenses
                .Where(e => e.HouseholdId == householdId && (from == null || e.Date >= from) && (to == null || e.Date <= to))
                .ToList();
            var grandTotal = filtered.Sum(e => e.Amount);
            IReadOnlyList<CategorySpendSummary> list = filtered
                .GroupBy(e => e.Category)
                .Select(g =>
                {
                    var sum = g.Sum(e => e.Amount);
                    var pct = grandTotal == 0 ? 0 : (double)(sum / grandTotal * 100m);
                    return new CategorySpendSummary(g.Key, sum, g.Count(), pct);
                })
                .OrderByDescending(x => x.TotalAmount)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<MonthlySpendSummary>> GetMonthlySpendingAsync(Guid householdId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var filtered = store.Expenses
                .Where(e => e.HouseholdId == householdId && e.Date >= from && e.Date <= to)
                .ToList();
            IReadOnlyList<MonthlySpendSummary> list = filtered
                .GroupBy(e => (e.Date.Year, e.Date.Month))
                .Select(g => new MonthlySpendSummary(g.Key.Year, g.Key.Month, g.Sum(e => e.Amount), g.Count()))
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<MemberSpendSummary>> GetMemberSpendingAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
    {
        lock (store.Sync)
        {
            var members = store.Members.Where(m => m.HouseholdId == householdId && m.IsActive).OrderBy(m => m.JoinedAt).ToList();
            var filtered = store.Expenses
                .Where(e => e.HouseholdId == householdId && (from == null || e.Date >= from) && (to == null || e.Date <= to))
                .ToList();
            IReadOnlyList<MemberSpendSummary> list = members
                .Select(m => new MemberSpendSummary(
                    m.Id,
                    m.DisplayName,
                    filtered.Where(e => e.PaidByMemberId == m.Id).Sum(e => e.Amount),
                    filtered.Sum(e => e.ShareOf(m.Id))))
                .ToList();
            return Task.FromResult(list);
        }
    }
}
