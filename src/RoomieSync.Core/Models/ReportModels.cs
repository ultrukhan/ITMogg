namespace RoomieSync.Core.Models;

public sealed record CategorySpendSummary(ExpenseCategory Category, decimal TotalAmount, int ExpenseCount, double Percentage);

public sealed record MonthlySpendSummary(int Year, int Month, decimal TotalAmount, int ExpenseCount);

public sealed record MemberSpendSummary(Guid MemberId, string DisplayName, decimal TotalPaid, decimal TotalShare)
{
    public decimal NetBalance => TotalPaid - TotalShare;
}

public sealed record HouseholdReportSummary(
    decimal TotalSpent,
    decimal MonthlyAverage,
    int ExpenseCount,
    ExpenseCategory? TopCategory,
    IReadOnlyList<CategorySpendSummary> ByCategory,
    IReadOnlyList<MonthlySpendSummary> ByMonth,
    IReadOnlyList<MemberSpendSummary> ByMember);

public sealed class HouseholdBackupPackage
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Household Household { get; set; } = new();
    public List<Member> Members { get; set; } = new();
    public List<UserAccount> Accounts { get; set; } = new();
    public List<Expense> Expenses { get; set; } = new();
    public List<Settlement> Settlements { get; set; } = new();
    public List<Chore> Chores { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();
}
