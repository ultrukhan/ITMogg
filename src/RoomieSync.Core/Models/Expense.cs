namespace RoomieSync.Core.Models;

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Merchant { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public ExpenseCategory Category { get; set; } = ExpenseCategory.Other;
    public Guid PaidByMemberId { get; set; }
    public SplitMethod SplitMethod { get; set; } = SplitMethod.Equally;
    public string? ReceiptImagePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<ExpenseShare> Shares { get; set; } = new();

    public HashSet<Guid> DeleteApprovedBy { get; set; } = new();

    public decimal ShareOf(Guid memberId) => Shares.Where(s => s.MemberId == memberId).Sum(s => s.Amount);

    public IReadOnlyCollection<Guid> RequiredApprovers =>
        Shares.Select(s => s.MemberId).Append(PaidByMemberId).Distinct().ToList();
}

public sealed class ExpenseShare
{
    public Guid MemberId { get; set; }
    public decimal Amount { get; set; }
    public decimal? Percentage { get; set; }
}
