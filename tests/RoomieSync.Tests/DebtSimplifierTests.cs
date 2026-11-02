using RoomieSync.Core.Models;
using RoomieSync.Services.Balances;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public class DebtSimplifierTests
{
    [Fact]
    public void Empty_input_gives_no_transfers()
    {
        var sut = new DebtSimplifier();
        Assert.Empty(sut.Simplify(Array.Empty<MemberBalance>()));
    }

    [Fact]
    public void All_zero_balances_give_no_transfers()
    {
        var sut = new DebtSimplifier();
        var balances = new[]
        {
            new MemberBalance(Guid.NewGuid(), 0m),
            new MemberBalance(Guid.NewGuid(), 0m)
        };
        Assert.Empty(sut.Simplify(balances));
    }

    [Fact]
    public void Two_people_one_transfer()
    {
        var sut = new DebtSimplifier();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var transfers = sut.Simplify([new MemberBalance(a, DirectDebtAmount), new MemberBalance(b, -DirectDebtAmount)]);
        Assert.Equal([new Transfer(b, a, DirectDebtAmount)], transfers);
    }

    [Fact]
    public void Chain_debt_is_collapsed()
    {
        var sut = new DebtSimplifier();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var balances = new[]
        {
            new MemberBalance(a, -ChainDebtAmount),
            new MemberBalance(b, 0m),
            new MemberBalance(c, ChainDebtAmount)
        };
        var transfers = sut.Simplify(balances);
        Assert.Equal([new Transfer(a, c, ChainDebtAmount)], transfers);
    }

    [Fact]
    public void Six_members_need_at_most_five_transfers()
    {
        var sut = new DebtSimplifier();
        var balances = CreateSixMemberBalances();
        var transfers = sut.Simplify(balances);
        Assert.True(transfers.Count <= MaxTransfersForSixMembers && TransfersSettleAllBalances(balances, transfers));
    }

    [Fact]
    public void Cents_are_not_lost_to_rounding()
    {
        var sut = new DebtSimplifier();
        var balances = new[]
        {
            new MemberBalance(Guid.NewGuid(), OneCentBalance),
            new MemberBalance(Guid.NewGuid(), TwoCentsBalance),
            new MemberBalance(Guid.NewGuid(), NegThreeCentsBalance)
        };
        var transfers = sut.Simplify(balances);
        Assert.True(transfers.Count == ExpectedCentTransfersCount && TransfersSettleAllBalances(balances, transfers));
    }

    [Fact]
    public void Runs_well_under_500ms_for_large_input()
    {
        var sut = new DebtSimplifier();
        var rnd = new Random(RandomSeed);
        var list = Enumerable.Range(0, LargeSimplifyMemberCount)
            .Select(_ => new MemberBalance(Guid.NewGuid(), rnd.Next(RandomMinCents, RandomMaxCents) / CentsDivisor))
            .ToList();
        var fix = -list.Sum(b => b.Balance);
        list.Add(new MemberBalance(Guid.NewGuid(), fix));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var transfers = sut.Simplify(list);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < MaxBalanceCalcMs && TransfersSettleAllBalances(list, transfers));
    }
}
