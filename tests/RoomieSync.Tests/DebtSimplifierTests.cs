using RoomieSync.Core.Models;
using RoomieSync.Services.Balances;

namespace RoomieSync.Tests;

public class DebtSimplifierTests
{
    private readonly DebtSimplifier _sut = new();
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid(), E = Guid.NewGuid(), F = Guid.NewGuid();

    private static void AssertSettlesAll(IEnumerable<MemberBalance> balances, IReadOnlyList<Transfer> transfers)
    {
        var after = balances.ToDictionary(b => b.MemberId, b => b.Balance);
        foreach (var t in transfers)
        {
            Assert.True(t.Amount > 0);
            Assert.NotEqual(t.FromMemberId, t.ToMemberId);
            after[t.FromMemberId] += t.Amount;
            after[t.ToMemberId] -= t.Amount;
        }
        Assert.All(after.Values, v => Assert.Equal(0m, v));
    }

    [Fact]
    public void Empty_input_gives_no_transfers() => Assert.Empty(_sut.Simplify(Array.Empty<MemberBalance>()));

    [Fact]
    public void All_zero_balances_give_no_transfers()
        => Assert.Empty(_sut.Simplify(new[] { new MemberBalance(A, 0), new MemberBalance(B, 0) }));

    [Fact]
    public void Two_people_one_transfer()
    {
        var t = Assert.Single(_sut.Simplify(new[] { new MemberBalance(A, 50), new MemberBalance(B, -50) }));
        Assert.Equal(new Transfer(B, A, 50), t);
    }

    [Fact]
    public void Chain_debt_is_collapsed()
    {
        // A винен B 10, B винен C 10 → лише A платить C.
        var balances = new[] { new MemberBalance(A, -10), new MemberBalance(B, 0), new MemberBalance(C, 10) };
        var t = Assert.Single(_sut.Simplify(balances));
        Assert.Equal(new Transfer(A, C, 10), t);
    }

    [Fact]
    public void Six_members_need_at_most_five_transfers()
    {
        var balances = new[]
        {
            new MemberBalance(A, 124.50m), new MemberBalance(B, 42.30m), new MemberBalance(C, 10.01m),
            new MemberBalance(D, -54.55m), new MemberBalance(E, -112.25m), new MemberBalance(F, -10.01m)
        };
        var transfers = _sut.Simplify(balances);
        Assert.True(transfers.Count <= 5);
        AssertSettlesAll(balances, transfers);
    }

    [Fact]
    public void Cents_are_not_lost_to_rounding()
    {
        var balances = new[] { new MemberBalance(A, 0.01m), new MemberBalance(B, 0.02m), new MemberBalance(C, -0.03m) };
        var transfers = _sut.Simplify(balances);
        Assert.Equal(2, transfers.Count);
        AssertSettlesAll(balances, transfers);
    }

    [Fact]
    public void Runs_well_under_500ms_for_large_input()
    {
        var rnd = new Random(1);
        var list = Enumerable.Range(0, 500).Select(_ => new MemberBalance(Guid.NewGuid(), rnd.Next(-10000, 10000) / 100m)).ToList();
        var fix = -list.Sum(b => b.Balance);
        list.Add(new MemberBalance(Guid.NewGuid(), fix));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var transfers = _sut.Simplify(list);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
        AssertSettlesAll(list, transfers);
    }
}
