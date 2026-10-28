using System.Collections.Concurrent;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Balances;

public sealed class BalanceService(
    IExpenseRepository expenses,
    ISettlementRepository settlements,
    IMemberRepository members,
    IDebtSimplifier simplifier,
    INotificationService notifications,
    IClock clock) : IBalanceService
{
    private readonly ConcurrentDictionary<(Guid HouseholdId, Guid FromId, Guid ToId), Guid> _pendingConfirmations = new();

    public async Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(Guid householdId, CancellationToken ct = default)
    {
        var (balances, _) = await ComputeAsync(householdId, ct);
        return balances;
    }

    public async Task<SettlementPlan> GetSettlementPlanAsync(Guid householdId, CancellationToken ct = default)
    {
        var (balances, pairwise) = await ComputeAsync(householdId, ct);
        var transfers = simplifier.Simplify(balances);
        return new SettlementPlan(transfers, balances, pairwise);
    }

    public bool HasPendingConfirmation(Guid householdId, Transfer transfer, out Guid confirmedByMemberId)
        => _pendingConfirmations.TryGetValue((householdId, transfer.FromMemberId, transfer.ToMemberId), out confirmedByMemberId);

    public async Task<SettlementActionResult> InitiateOrConfirmSettlementAsync(Guid householdId, Transfer transfer, Guid actorMemberId, CancellationToken ct = default)
    {
        if (transfer.Amount <= 0) throw new DomainException("Settlement amount must be positive.");

        var from = await members.GetByIdAsync(transfer.FromMemberId, ct) ?? throw new DomainException("Debtor not found.");
        var to = await members.GetByIdAsync(transfer.ToMemberId, ct) ?? throw new DomainException("Creditor not found.");

        if (actorMemberId != transfer.FromMemberId && actorMemberId != transfer.ToMemberId)
            throw new DomainException($"Only {from.FirstName} (debtor) or {to.FirstName} (creditor) can mark this debt as paid.");

        var key = (householdId, transfer.FromMemberId, transfer.ToMemberId);
        if (_pendingConfirmations.TryGetValue(key, out var firstConfirmerId))
        {
            if (firstConfirmerId == actorMemberId)
            {
                var otherName = actorMemberId == from.Id ? to.FirstName : from.FirstName;
                return new SettlementActionResult(false, $"Already marked by you. Waiting for {otherName} to confirm.");
            }

            _pendingConfirmations.TryRemove(key, out _);
            await RecordSettlementAsync(householdId, transfer, ct);
            return new SettlementActionResult(true, $"Payment between {from.FirstName} and {to.FirstName} confirmed and recorded!");
        }

        _pendingConfirmations[key] = actorMemberId;
        var otherParty = actorMemberId == from.Id ? to : from;
        var actorName = actorMemberId == from.Id ? from.FirstName : to.FirstName;

        await notifications.NotifyMemberAsync(
            householdId,
            otherParty.Id,
            NotificationType.SettlementRequest,
            $"{actorName} marked {MoneyFormat.Format(transfer.Amount)} as paid. Open Settlements and click 'Settle Up' to confirm.",
            ct);

        return new SettlementActionResult(false, $"Confirmation request sent to {otherParty.FirstName}. Balance will update once {otherParty.FirstName} confirms.");
    }

    public async Task<Settlement> RecordSettlementAsync(Guid householdId, Transfer transfer, CancellationToken ct = default)
    {
        if (transfer.Amount <= 0) throw new DomainException("Settlement amount must be positive.");
        var s = new Settlement
        {
            HouseholdId = householdId,
            FromMemberId = transfer.FromMemberId,
            ToMemberId = transfer.ToMemberId,
            Amount = Money.Round(transfer.Amount),
            Date = clock.Now
        };
        await settlements.AddAsync(s, ct);

        var from = await members.GetByIdAsync(transfer.FromMemberId, ct);
        var to = await members.GetByIdAsync(transfer.ToMemberId, ct);
        await notifications.NotifyHouseholdAsync(householdId, NotificationType.SettlementRecorded,
            $"{from?.FirstName} paid {to?.FirstName} {MoneyFormat.Format(s.Amount)}", null, ct);
        return s;
    }

    public async Task RequestSettlementAsync(Guid householdId, Transfer transfer, CancellationToken ct = default)
    {
        if (transfer.Amount <= 0) throw new DomainException("Settlement request amount must be positive.");
        var debtor = await members.GetByIdAsync(transfer.FromMemberId, ct) ?? throw new DomainException("Debtor not found.");
        var creditor = await members.GetByIdAsync(transfer.ToMemberId, ct) ?? throw new DomainException("Creditor not found.");

        await notifications.NotifyMemberAsync(
            householdId,
            debtor.Id,
            NotificationType.SettlementRequest,
            $"{creditor.FirstName} asked to settle up: {debtor.FirstName} owes {creditor.FirstName} {MoneyFormat.Format(Money.Round(transfer.Amount))}",
            ct);
    }

    public Task<IReadOnlyList<Settlement>> GetSettlementsAsync(Guid householdId, CancellationToken ct = default)
        => settlements.GetByHouseholdAsync(householdId, ct);

    private async Task<(IReadOnlyList<MemberBalance>, int)> ComputeAsync(Guid householdId, CancellationToken ct)
    {
        var memberList = await members.GetByHouseholdAsync(householdId, includeInactive: true, ct);
        var cents = memberList.ToDictionary(m => m.Id, _ => 0L);
        var pair = new Dictionary<(Guid From, Guid To), long>();

        void AddPair(Guid debtor, Guid creditor, long amount)
        {
            if (debtor == creditor || amount == 0) return;
            var key = debtor.CompareTo(creditor) < 0 ? (debtor, creditor) : (creditor, debtor);
            var sign = key.Item1 == debtor ? 1 : -1;
            pair[key] = pair.GetValueOrDefault(key) + sign * amount;
        }

        foreach (var e in await expenses.GetByHouseholdAsync(householdId, ct: ct))
        {
            cents.TryAdd(e.PaidByMemberId, 0);
            cents[e.PaidByMemberId] += Money.ToCents(e.Amount);
            foreach (var s in e.Shares)
            {
                var c = Money.ToCents(s.Amount);
                cents.TryAdd(s.MemberId, 0);
                cents[s.MemberId] -= c;
                AddPair(s.MemberId, e.PaidByMemberId, c);
            }
        }

        foreach (var s in await settlements.GetByHouseholdAsync(householdId, ct))
        {
            var c = Money.ToCents(s.Amount);
            cents.TryAdd(s.FromMemberId, 0);
            cents.TryAdd(s.ToMemberId, 0);
            cents[s.FromMemberId] += c;
            cents[s.ToMemberId] -= c;
            AddPair(s.ToMemberId, s.FromMemberId, c);
        }

        var balances = cents.Select(kv => new MemberBalance(kv.Key, Money.FromCents(kv.Value))).ToList();
        return (balances, pair.Values.Count(v => v != 0));
    }
}
