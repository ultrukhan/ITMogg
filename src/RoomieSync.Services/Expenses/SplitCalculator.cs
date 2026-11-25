using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Expenses;

public static class SplitCalculator
{
    public static IReadOnlyList<ExpenseShare> Calculate(decimal amount, SplitMethod method, IReadOnlyList<ShareInput> inputs)
    {
        if (inputs is null || inputs.Count == 0) return Array.Empty<ExpenseShare>();
        if (amount < 0 || !InputValidator.HasValidScale(amount))
            throw new DomainException("Expense amount for split calculation must be non-negative and have at most 2 decimal places.");
        if (inputs.Any(s => s.MemberId == Guid.Empty || s.Value < 0 || !InputValidator.HasValidScale(s.Value)))
            throw new DomainException("Split inputs must contain valid member IDs, non-negative values, and at most 2 decimal places.");

        var totalCents = Money.ToCents(amount);

        switch (method)
        {
            case SplitMethod.Equally:
            {
                var baseShare = totalCents / inputs.Count;
                var remainder = totalCents % inputs.Count;
                return inputs.Select((s, i) => new ExpenseShare
                {
                    MemberId = s.MemberId,
                    Amount = Money.FromCents(baseShare + (i < remainder ? 1 : 0))
                }).ToList();
            }
            case SplitMethod.ByPercentage:
            {
                var cents = inputs.Select(s => (long)Math.Floor(totalCents * s.Value / 100m)).ToArray();
                var percentSum = inputs.Sum(s => s.Value);
                if (Math.Abs(percentSum - 100m) < 0.001m)
                {
                    var diff = totalCents - cents.Sum();
                    for (var i = 0; diff > 0; i = (i + 1) % cents.Length, diff--) cents[i]++;
                }
                return inputs.Select((s, i) => new ExpenseShare
                {
                    MemberId = s.MemberId, Amount = Money.FromCents(cents[i]), Percentage = s.Value
                }).ToList();
            }
            case SplitMethod.ByExactAmount:
                return inputs.Select(s => new ExpenseShare { MemberId = s.MemberId, Amount = Money.Round(s.Value) }).ToList();
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }
    }
}
