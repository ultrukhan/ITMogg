using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Balances;


public sealed class DebtSimplifier : IDebtSimplifier
{
    public IReadOnlyList<Transfer> Simplify(IEnumerable<MemberBalance> balances)
    {
        var creditors = new List<(Guid Id, long Cents)>();
        var debtors = new List<(Guid Id, long Cents)>();
        foreach (var b in balances)
        {
            var c = Money.ToCents(b.Balance);
            if (c > 0) creditors.Add((b.MemberId, c));
            else if (c < 0) debtors.Add((b.MemberId, -c));
        }

        var result = new List<Transfer>();
        while (creditors.Count > 0 && debtors.Count > 0)
        {
            creditors.Sort((a, b) => b.Cents.CompareTo(a.Cents));
            debtors.Sort((a, b) => b.Cents.CompareTo(a.Cents));
            var (cId, cAmt) = creditors[0];
            var (dId, dAmt) = debtors[0];
            var x = Math.Min(cAmt, dAmt);
            result.Add(new Transfer(dId, cId, Money.FromCents(x)));

            if (cAmt == x) creditors.RemoveAt(0); else creditors[0] = (cId, cAmt - x);
            if (dAmt == x) debtors.RemoveAt(0); else debtors[0] = (dId, dAmt - x);
        }
        return result;
    }
}
