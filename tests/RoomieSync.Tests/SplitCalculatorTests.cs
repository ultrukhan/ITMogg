using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;
using RoomieSync.Services.Expenses;

namespace RoomieSync.Tests;

public class SplitCalculatorTests
{
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

    [Fact]
    public void Equal_split_distributes_remainder_cents()
    {
        var shares = SplitCalculator.Calculate(100m, SplitMethod.Equally, new[] { new ShareInput(A, 0), new ShareInput(B, 0), new ShareInput(C, 0) });
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, shares.Select(s => s.Amount));
        Assert.Equal(100m, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Mockup_example_64_65_between_three()
    {
        var shares = SplitCalculator.Calculate(64.65m, SplitMethod.Equally, new[] { new ShareInput(A, 0), new ShareInput(B, 0), new ShareInput(C, 0) });
        Assert.All(shares, s => Assert.Equal(21.55m, s.Amount));
    }

    [Fact]
    public void Percentage_split_sums_to_total()
    {
        var shares = SplitCalculator.Calculate(99.99m, SplitMethod.ByPercentage, new[] { new ShareInput(A, 50), new ShareInput(B, 25), new ShareInput(C, 25) });
        Assert.Equal(99.99m, shares.Sum(s => s.Amount));
        Assert.Equal(50m, shares[0].Percentage);
    }

    [Fact]
    public void Exact_split_keeps_values()
    {
        var shares = SplitCalculator.Calculate(30m, SplitMethod.ByExactAmount, new[] { new ShareInput(A, 10), new ShareInput(B, 20) });
        Assert.Equal(new[] { 10m, 20m }, shares.Select(s => s.Amount));
    }
}
