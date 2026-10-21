namespace RoomieSync.Core.Models;

public sealed class Settlement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public Guid FromMemberId { get; set; }
    public Guid ToMemberId { get; set; }
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Now;
}

public sealed record Transfer(Guid FromMemberId, Guid ToMemberId, decimal Amount);

public sealed record MemberBalance(Guid MemberId, decimal Balance);

public sealed record SettlementPlan(
    IReadOnlyList<Transfer> Transfers,
    IReadOnlyList<MemberBalance> Balances,
    int UnoptimizedTransferCount)
{
    public decimal TotalAmount => Transfers.Sum(t => t.Amount);
    public int TransfersSaved => Math.Max(0, UnoptimizedTransferCount - Transfers.Count);
    public bool IsSettled => Transfers.Count == 0;
}
