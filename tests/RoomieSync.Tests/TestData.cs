using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Tests;

public static class TestData
{
    public const int DashboardExpenseCount = 1_000;
    public const int BalancePerfExpenseCount = 500;
    public const int HighVolumeExpenseCount = 10_000;
    public const int HighVolumeChoreCount = 5_000;
    public const int LargeSimplifyMemberCount = 500;

    public const long MaxDashboardLoadMs = 1_000L;
    public const long MaxBalanceCalcMs = 500L;
    public const long MaxFallbackMs = 1_000L;
    public const long MaxHighVolumeQueryMs = 500L;
    public const double MaxBackupRestoreSeconds = 5.0;

    public const long TwoGigabytes = 2L * 1024L * 1024L * 1024L;
    public const long TinyStorageLimitBytes = 2L;

    public const int OverMaxTitleLength = 205;
    public const int LongRowTitleLength = 45;
    public const int InvalidEnumValue = 999;

    public const int FirstFailureAttemptsLeft = 2;
    public const int SecondFailureAttemptsLeft = 1;
    public const int InitialHouseholdSize = 3;
    public const int MinHouseholdSize = 2;
    public const int MaxTransfersForSixMembers = 5;
    public const int ExpectedCentTransfersCount = 2;
    public const int WeeklyRotationDays = 7;
    public const int ExportSampleRowCount = 80;
    public const int ExpectedCsvLineCount = 81;
    public const int PdfMagicHeaderLength = 8;
    public const int MinValidationErrorsCount = 4;
    public const int MinReceiptWarningsCount = 3;
    public const int ExpectedFutureWarningsCount = 2;
    public const int ExpectedNotificationEventCount = 4;
    public const int ExpectedParsedItemCount = 2;
    public const int RequiredDeleteApprovals = 2;

    public const decimal EqualShareWeight = 1m;
    public const decimal InvalidNegativeAmount = -5m;
    public const decimal RemainderSplitTotal = 100m;
    public const decimal ShareThirdFirst = 33.34m;
    public const decimal ShareThirdRest = 33.33m;

    public const decimal MockupTotalAmount = 64.65m;
    public const decimal MockupEachShareAmount = 21.55m;
    public const decimal OddReceiptTotal = 99.99m;

    public const decimal PrimaryPercentageShare = 50m;
    public const decimal SecondaryPercentageShare = 25m;
    public const decimal IncompletePctFirst = 60m;
    public const decimal IncompletePctSecond = 30m;
    public const decimal Non100PctShare = 40m;
    public const decimal ExpectedNon100PctSum = 80m;

    public const decimal ExactSplitTotal = 30m;
    public const decimal FirstExactShare = 10m;
    public const decimal SecondExactShare = 20m;

    public const decimal DirectDebtAmount = 50m;
    public const decimal ChainDebtAmount = 10m;
    public const decimal OneCentBalance = 0.01m;
    public const decimal TwoCentsBalance = 0.02m;
    public const decimal NegThreeCentsBalance = -0.03m;

    public const decimal MayaSeedBalance = 124.50m;
    public const decimal MemberBalanceB = 42.30m;
    public const decimal MemberBalanceC = 10.01m;
    public const decimal MemberBalanceD = -54.55m;
    public const decimal MemberBalanceE = -112.25m;
    public const decimal MemberBalanceF = -10.01m;

    public const decimal PizzaExpenseAmount = 40m;
    public const decimal ExpectedPayerDelta = 30m;
    public const decimal ExpectedPeerDelta = -10m;

    public const decimal SampleParsedTotal = 12.5m;
    public const decimal SampleParsedMilkPrice = 2.5m;
    public const decimal ExportBaseRowAmount = 10m;
    public const decimal ExportRowYourShare = 5m;

    public const decimal InternetBaseAmount = 100m;
    public const decimal InternetUpgradedAmount = 200m;
    public const decimal ExpectedInitialShareBalance = 50m;
    public const decimal ExpectedUpdatedShareBalance = 100m;

    public const decimal BasePerfExpenseAmount = 150m;
    public const decimal BalancePerfExpenseAmount = 300m;
    public const decimal RentMonthlyAmount = 800m;
    public const decimal GroceriesSampleAmount = 200m;
    public const decimal TotalAnalyticsSpentAmount = 1_000m;
    public const double ExpectedCategorySharePct = 80.0;
    public const decimal ExpectedNetAfterRentAndGroceries = 450m;
    public const decimal PartialSettlementAmount = 150m;
    public const decimal ExpectedNetAfterPartialSettlement = 300m;

    public const decimal UnroundedMoney = 12.345m;
    public const decimal RoundedMoney = 12.35m;
    public const long RoundedMoneyCents = 1235L;
    public const decimal FormattedSampleAmount = 125.50m;
    public const decimal LargeFormattedAmount = 2_000_000m;
    public const decimal ShareLookupAmount = 15.5m;
    public const decimal SmallExpenseAmount = 20m;
    public const decimal MediumExpenseAmount = 50m;
    public const decimal DinnerExpenseAmount = 60m;
    public const decimal SettlementTransferAmount = 25m;
    public const decimal EditedExpenseAmount = 225m;
    public const decimal ExpectedSolomiiaBalanceAfterEdit = 374m;
    public const decimal SeedExpenseAmount = 500m;

    public const int SampleReportYear = 2026;
    public const int SampleReportMonth = 5;

    public const int GeminiTimeoutSec = 5;
    public const int SimulatedDelaySec = 10;
    public const int ShortClientTimeoutMs = 150;
    public const int ClockToleranceSec = 5;

    public const int TomorrowOffsetDays = 1;
    public const int FutureReceiptOffsetDays = 5;
    public const int OverdueShortOffsetDays = -2;
    public const int OverdueMediumOffsetDays = -5;
    public const int OverdueLongOffsetDays = -10;
    public const int MonthlyQueryOffsetDays = -30;
    public const int PreviousMonthOffset = -1;
    public const int TwoMonthsAgoOffset = -2;

    public const int ExpenseAmountStepMod = 50;
    public const int ExpenseDayStepMod = 25;
    public const int ExpenseCategoryMod = 6;
    public const int DaysInYearMod = 365;
    public const int ExportDateStepMod = 20;

    public const int RandomSeed = 1;
    public const int RandomMinCents = -10000;
    public const int RandomMaxCents = 10000;
    public const decimal CentsDivisor = 100m;

    public static readonly DateTime DefaultTestNow = new(2026, 5, 24, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime OctTestNow = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime PeriodStart = new(2026, 1, 1);
    public static readonly DateTime PeriodEnd = new(2026, 12, 31);
    public static readonly DateTime SampleDateMay20 = new(2026, 5, 20);
    public static readonly DateTime SampleDateMay1 = new(2026, 5, 1);
    public static readonly DateTime BaseJoinDate = new(2024, 1, 1);

    public static readonly TimeSpan LockoutWait59Sec = TimeSpan.FromSeconds(59);
    public static readonly TimeSpan LockoutWait2Sec = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LockoutWait61Sec = TimeSpan.FromSeconds(61);
    public static readonly TimeSpan OneSecondSpan = TimeSpan.FromSeconds(1);

    public static readonly byte[] DummyImageBytes = [1, 2, 3];
    public static readonly byte[] JpegHeaderBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    public static readonly decimal[] ExpectedThreeWayHundredShares = [ShareThirdFirst, ShareThirdRest, ShareThirdRest];
    public static readonly decimal[] ExpectedExactThirtyShares = [FirstExactShare, SecondExactShare];

    public const string SampleGeminiJson = "{\"merchant\":\"Shop\",\"date\":\"2026-05-20\",\"total\":12.5,\"category\":\"groceries\",\"items\":[{\"name\":\"Milk\",\"price\":2.5},{\"name\":\"Bread\",\"price\":10}]}";

    public static IReadOnlyList<ShareInput> CreateThreeEqualShares() =>
        [new ShareInput(Guid.NewGuid(), 0m), new ShareInput(Guid.NewGuid(), 0m), new ShareInput(Guid.NewGuid(), 0m)];

    public static IReadOnlyList<ShareInput> CreateSamplePercentageShares() =>
        [new ShareInput(Guid.NewGuid(), PrimaryPercentageShare), new ShareInput(Guid.NewGuid(), SecondaryPercentageShare), new ShareInput(Guid.NewGuid(), SecondaryPercentageShare)];

    public static IReadOnlyList<MemberBalance> CreateSixMemberBalances() =>
    [
        new MemberBalance(Guid.NewGuid(), MayaSeedBalance),
        new MemberBalance(Guid.NewGuid(), MemberBalanceB),
        new MemberBalance(Guid.NewGuid(), MemberBalanceC),
        new MemberBalance(Guid.NewGuid(), MemberBalanceD),
        new MemberBalance(Guid.NewGuid(), MemberBalanceE),
        new MemberBalance(Guid.NewGuid(), MemberBalanceF)
    ];

    public static bool TransfersSettleAllBalances(IEnumerable<MemberBalance> balances, IReadOnlyList<Transfer> transfers)
    {
        var after = balances.ToDictionary(b => b.MemberId, b => b.Balance);
        foreach (var t in transfers)
        {
            if (t.Amount <= 0m || t.FromMemberId == t.ToMemberId)
                return false;
            after[t.FromMemberId] += t.Amount;
            after[t.ToMemberId] -= t.Amount;
        }
        return after.Values.All(v => v == 0m);
    }
}
