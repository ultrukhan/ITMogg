using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;
using RoomieSync.Services.Expenses;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public class SplitCalculatorTests
{
    [Fact]
    public void Equal_split_distributes_remainder_cents()
    {
        var shares = SplitCalculator.Calculate(
            RemainderSplitTotal,
            SplitMethod.Equally,
            CreateThreeEqualShares());

        Assert.Equal(ExpectedThreeWayHundredShares, shares.Select(s => s.Amount));
    }

    [Fact]
    public void Equal_split_sums_to_original_total()
    {
        var shares = SplitCalculator.Calculate(
            RemainderSplitTotal,
            SplitMethod.Equally,
            CreateThreeEqualShares());

        Assert.Equal(RemainderSplitTotal, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Mockup_example_64_65_between_three()
    {
        var shares = SplitCalculator.Calculate(
            MockupTotalAmount,
            SplitMethod.Equally,
            CreateThreeEqualShares());

        Assert.True(shares.All(s => s.Amount == MockupEachShareAmount));
    }

    [Fact]
    public void Percentage_split_sums_to_total()
    {
        var shares = SplitCalculator.Calculate(
            OddReceiptTotal,
            SplitMethod.ByPercentage,
            CreateSamplePercentageShares());

        Assert.Equal(OddReceiptTotal, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void Percentage_split_preserves_share_percentage()
    {
        var shares = SplitCalculator.Calculate(
            OddReceiptTotal,
            SplitMethod.ByPercentage,
            CreateSamplePercentageShares());

        Assert.Equal(PrimaryPercentageShare, shares[0].Percentage);
    }

    [Fact]
    public void Exact_split_keeps_values()
    {
        var shares = SplitCalculator.Calculate(
            ExactSplitTotal,
            SplitMethod.ByExactAmount,
            [new ShareInput(Guid.NewGuid(), FirstExactShare), new ShareInput(Guid.NewGuid(), SecondExactShare)]);

        Assert.Equal(ExpectedExactThirtyShares, shares.Select(s => s.Amount));
    }
}
