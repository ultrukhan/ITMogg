using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Data.Seed;

public sealed class DemoDataSeeder(
    IHouseholdRepository households,
    IMemberRepository members,
    IUserAccountRepository accounts,
    IExpenseRepository expenses,
    ISettlementRepository settlements,
    IChoreRepository chores,
    INotificationRepository notifications,
    IPasswordHasher hasher,
    IClock clock)
{
    public const string DemoPassword = "demo123";
    public const string DemoAdminEmail = "maya@roomiesync.app";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await households.AnyAsync(ct)) return;

        var now = clock.Now;
        var today = now.Date;

        var home = new Household { Name = "Home Sweet Home", CreatedAt = new DateTime(2023, 9, 1) };
        await households.AddAsync(home, ct);

        var maya = NewMember(home, "Maya Torres", MemberRole.Admin, "#E07A5F", 0);
        var jake = NewMember(home, "Jake Miller", MemberRole.Member, "#3B82F6", 1);
        var sam = NewMember(home, "Sam Carter", MemberRole.Member, "#2E9E5B", 2);
        var leo = NewMember(home, "Leo Novak", MemberRole.Member, "#8B5CF6", 3);
        var all = new[] { maya, jake, sam, leo };
        foreach (var m in all)
        {
            await members.AddAsync(m, ct);
            var (hash, salt) = hasher.Hash(DemoPassword);
            await accounts.AddAsync(new UserAccount
            {
                MemberId = m.Id,
                Email = $"{m.FirstName.ToLowerInvariant()}@roomiesync.app",
                PasswordHash = hash,
                PasswordSalt = salt
            }, ct);
        }

        var rnd = new Random(42);
        var list = new List<Expense>();

        void Add(string title, decimal amount, DateTime date, ExpenseCategory cat, Member payer, string? merchant = null, Member[]? among = null)
        {
            if (date > today) return;
            list.Add(Equal(home, title, amount, date, cat, payer, among ?? all, merchant));
        }

        for (var m = 3; m >= 0; m--)
        {
            var monthStart = new DateTime(today.Year, today.Month, 1).AddMonths(-m);
            var monthName = monthStart.ToString("MMMM", System.Globalization.CultureInfo.InvariantCulture);
            Add($"Rent — {monthName}", 1800m, monthStart, ExpenseCategory.Rent, all[m % 4]);
            Add("Internet — Comcast", 89.99m, monthStart.AddDays(9), ExpenseCategory.Utilities, jake, "Comcast");
            Add("Utilities — Water", 38m + rnd.Next(0, 1200) / 100m, monthStart.AddDays(12), ExpenseCategory.Utilities, leo);
            Add("Streaming Subscription", 15.99m, monthStart.AddDays(19), ExpenseCategory.Entertainment, sam, "Netflix");

            var groceryPayers = new[] { jake, maya, sam, leo };
            var g = 0;
            for (var d = 2; d < 28; d += 7, g++)
                Add("Groceries — Whole Foods", 60m + rnd.Next(0, 16000) / 100m, monthStart.AddDays(d), ExpenseCategory.Groceries, groceryPayers[(g + m) % 4], "Whole Foods");

            Add("Dinner Out — The Grill", 70m + rnd.Next(0, 4000) / 100m, monthStart.AddDays(16), ExpenseCategory.Entertainment, leo, "The Grill");
            Add("Cleaning Supplies", 18m + rnd.Next(0, 1500) / 100m, monthStart.AddDays(6), ExpenseCategory.Other, sam, among: new[] { maya, sam, leo });
        }

        list.RemoveAll(e => e.Date > today.AddDays(-3) && e.Category != ExpenseCategory.Rent);
        Add("Streaming Subscription", 15.99m, today.AddDays(-2), ExpenseCategory.Entertainment, sam, "Netflix");
        Add("Electricity Bill", 142.00m, today.AddDays(-1), ExpenseCategory.Utilities, maya);
        Add("Weekly Groceries", 86.20m, today, ExpenseCategory.Groceries, jake, "Trader Joe's");

        var party = new Expense
        {
            HouseholdId = home.Id, Title = "House Party Supplies", Amount = 120m, Date = today.AddDays(-12),
            Category = ExpenseCategory.Entertainment, PaidByMemberId = maya.Id, SplitMethod = SplitMethod.ByPercentage,
            Shares =
            {
                new ExpenseShare { MemberId = maya.Id, Percentage = 40, Amount = 48m },
                new ExpenseShare { MemberId = jake.Id, Percentage = 20, Amount = 24m },
                new ExpenseShare { MemberId = sam.Id, Percentage = 20, Amount = 24m },
                new ExpenseShare { MemberId = leo.Id, Percentage = 20, Amount = 24m },
            }
        };
        list.Add(party);

        foreach (var e in list.OrderBy(e => e.Date))
        {
            e.CreatedAt = e.Date == today ? now.AddMinutes(-30) : e.Date.AddHours(12);
            await expenses.AddAsync(e, ct);
        }

        var target = new Dictionary<Guid, decimal>
        {
            [maya.Id] = 124.50m, [sam.Id] = 42.30m, [jake.Id] = -54.55m, [leo.Id] = -112.25m
        };
        var raw = all.ToDictionary(m => m.Id, m => list.Where(e => e.PaidByMemberId == m.Id).Sum(e => e.Amount) - list.Sum(e => e.ShareOf(m.Id)));
        var diff = all.ToDictionary(m => m.Id, m => Money.ToCents(target[m.Id] - raw[m.Id]));
        var payers = diff.Where(d => d.Value > 0).Select(d => (d.Key, d.Value)).ToList();
        var receivers = diff.Where(d => d.Value < 0).Select(d => (d.Key, Value: -d.Value)).ToList();
        int pi = 0, ri = 0;
        while (pi < payers.Count && ri < receivers.Count)
        {
            var x = Math.Min(payers[pi].Value, receivers[ri].Value);
            await settlements.AddAsync(new Settlement
            {
                HouseholdId = home.Id, FromMemberId = payers[pi].Key, ToMemberId = receivers[ri].Key,
                Amount = Money.FromCents(x), Date = today.AddDays(-20)
            }, ct);
            payers[pi] = (payers[pi].Key, payers[pi].Value - x);
            receivers[ri] = (receivers[ri].Key, receivers[ri].Value - x);
            if (payers[pi].Value == 0) pi++;
            if (receivers[ri].Value == 0) ri++;
        }
        
        var choreSeed = new (string Title, Member Who, ChoreStatus Status, DateTime Deadline)[]
        {
            ("Take out the trash", jake, ChoreStatus.ToDo, today.AddHours(18)),
            ("Clean Kitchen", jake, ChoreStatus.ToDo, today.AddDays(2).AddHours(20)),
            ("Take Out Recycling", leo, ChoreStatus.ToDo, today.AddDays(-1).AddHours(9)),
            ("Vacuum Living Room", maya, ChoreStatus.InProgress, today.AddDays(1).AddHours(20)),
            ("Water the Plants", sam, ChoreStatus.InProgress, today.AddDays(3).AddHours(12)),
            ("Wash the Dishes", sam, ChoreStatus.Done, today.AddDays(-1).AddHours(21)),
            ("Fold & Put Away Laundry", leo, ChoreStatus.Done, today.AddDays(-2).AddHours(19)),
        };
        var i = 0;
        foreach (var (title, who, status, deadline) in choreSeed)
        {
            await chores.AddAsync(new Chore
            {
                HouseholdId = home.Id, Title = title, AssigneeId = who.Id, Status = status, Deadline = deadline,
                Rotation = ChoreRotation.Weekly, CreatedAt = now.AddDays(-7).AddMinutes(i++),
                CompletedAt = status == ChoreStatus.Done ? deadline.AddHours(-1) : null
            }, ct);
        }

        await notifications.AddRangeAsync(new[]
        {
            Note(home, maya, NotificationType.NewExpense, "Jake added \"Weekly Groceries\" — $86.20", now.AddMinutes(-30)),
            Note(home, maya, NotificationType.OverdueChore, "Chore \"Take Out Recycling\" assigned to Leo is overdue", now.AddHours(-3)),
            Note(home, maya, NotificationType.SettlementRequest, "Sam asked to settle up: Jake owes Sam $42.30", now.AddDays(-1), read: true),
            Note(home, maya, NotificationType.NewExpense, "Sam added \"Streaming Subscription\" — $15.99", now.AddDays(-2), read: true),
        }, ct);
    }

    private static Member NewMember(Household h, string name, MemberRole role, string color, int order) => new()
    {
        HouseholdId = h.Id, DisplayName = name, Role = role, AvatarColor = color,
        JoinedAt = h.CreatedAt.AddMinutes(order)
    };

    private static Expense Equal(Household h, string title, decimal amount, DateTime date, ExpenseCategory cat, Member payer, IReadOnlyList<Member> among, string? merchant)
    {
        var cents = Money.ToCents(amount);
        var baseShare = cents / among.Count;
        var remainder = cents % among.Count;
        var e = new Expense
        {
            HouseholdId = h.Id, Title = title, Amount = amount, Date = date, Category = cat,
            PaidByMemberId = payer.Id, SplitMethod = SplitMethod.Equally, Merchant = merchant
        };
        for (var i = 0; i < among.Count; i++)
            e.Shares.Add(new ExpenseShare { MemberId = among[i].Id, Amount = Money.FromCents(baseShare + (i < remainder ? 1 : 0)) });
        return e;
    }

    private static Notification Note(Household h, Member to, NotificationType type, string text, DateTime at, bool read = false) => new()
    {
        HouseholdId = h.Id, RecipientMemberId = to.Id, Type = type, Message = text, CreatedAt = at, IsRead = read
    };
}
