using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Data.Seed;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Correct_password_signs_in()
    {
        using var sp = TestHost.Build();
        var r = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        Assert.True(r.Status == LoginStatus.Success && r.Member!.DisplayName == "Maya Torres" && r.Member.IsAdmin);
    }

    [Fact]
    public async Task Three_failures_lock_for_60_seconds()
    {
        var clock = new FakeClock(DefaultTestNow);
        using var sp = TestHost.Build(clock);
        var auth = sp.GetRequiredService<IAuthService>();

        var firstFail = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x");
        var secondFail = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x");
        var thirdFail = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x");

        clock.Advance(LockoutWait59Sec);
        var locked = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);

        clock.Advance(LockoutWait2Sec);
        var unlocked = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);

        Assert.True(
            firstFail.AttemptsLeft == FirstFailureAttemptsLeft
            && secondFail.AttemptsLeft == SecondFailureAttemptsLeft
            && thirdFail.Status == LoginStatus.LockedOut
            && locked.Status == LoginStatus.LockedOut
            && locked.LockoutRemaining <= OneSecondSpan
            && unlocked.Status == LoginStatus.Success);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        using var sp = TestHost.Build();
        var acc = await sp.GetRequiredService<Core.Abstractions.Repositories.IUserAccountRepository>().GetByEmailAsync(DemoDataSeeder.DemoAdminEmail);
        Assert.True(
            acc is not null
            && !acc.PasswordHash.Contains(DemoDataSeeder.DemoPassword)
            && !string.IsNullOrEmpty(acc.PasswordSalt));
    }
}

public class BalanceAndExpenseTests
{
    private static async Task<(ServiceProvider sp, Guid household, IReadOnlyList<Member> members)> Setup()
    {
        var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var members = await sp.GetRequiredService<IHouseholdService>().GetMembersAsync(login.Household!.Id);
        return (sp, login.Household.Id, members);
    }

    [Fact]
    public async Task Seed_balances_match_mockup_values_and_sum_to_zero()
    {
        var (sp, h, members) = await Setup();
        using var _ = sp;
        var balances = await sp.GetRequiredService<IBalanceService>().GetBalancesAsync(h);
        var maya = members.Single(m => m.DisplayName == "Maya Torres");
        Assert.True(
            balances.Sum(b => b.Balance) == 0m
            && balances.Single(b => b.MemberId == maya.Id).Balance == MayaSeedBalance);
    }

    [Fact]
    public async Task Settling_the_whole_plan_zeroes_all_balances()
    {
        var (sp, h, _) = await Setup();
        using var __ = sp;
        var svc = sp.GetRequiredService<IBalanceService>();
        var plan = await svc.GetSettlementPlanAsync(h);
        var wasUnsettled = !plan.IsSettled;
        foreach (var t in plan.Transfers) await svc.RecordSettlementAsync(h, t);
        var after = await svc.GetSettlementPlanAsync(h);
        Assert.True(wasUnsettled && after.IsSettled && after.Balances.All(b => b.Balance == 0m));
    }

    [Fact]
    public async Task New_expense_updates_balances()
    {
        var (sp, h, members) = await Setup();
        using var __ = sp;
        var balances = sp.GetRequiredService<IBalanceService>();
        var before = (await balances.GetBalancesAsync(h)).ToDictionary(b => b.MemberId, b => b.Balance);
        var payer = members[1];
        await sp.GetRequiredService<IExpenseService>().AddExpenseAsync(h, new NewExpenseRequest(
            "Pizza", PizzaExpenseAmount, DefaultTestNow.Date, ExpenseCategory.Entertainment, payer.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0m)).ToList()));
        var after = (await balances.GetBalancesAsync(h)).ToDictionary(b => b.MemberId, b => b.Balance);
        Assert.True(
            after[payer.Id] == before[payer.Id] + ExpectedPayerDelta
            && after[members[0].Id] == before[members[0].Id] + ExpectedPeerDelta);
    }

    [Fact]
    public async Task Validation_rejects_bad_input()
    {
        var (sp, _, members) = await Setup();
        using var __ = sp;
        var svc = sp.GetRequiredService<IExpenseService>();
        var bad = svc.Validate(new NewExpenseRequest("", InvalidNegativeAmount, DateTime.Today, ExpenseCategory.Other, members[0].Id, SplitMethod.Equally, Array.Empty<ShareInput>()));
        var pct = svc.Validate(new NewExpenseRequest("X", ChainDebtAmount, DefaultTestNow.Date, ExpenseCategory.Other, members[0].Id, SplitMethod.ByPercentage,
            [new ShareInput(members[0].Id, IncompletePctFirst), new ShareInput(members[1].Id, IncompletePctSecond)]));
        Assert.True(!bad.IsValid && pct.Errors.Any(e => e.Contains("100%")));
    }

    [Fact]
    public async Task Member_with_debt_cannot_be_deleted()
    {
        var (sp, _, members) = await Setup();
        using var __ = sp;
        var leo = members.Single(m => m.DisplayName == "Leo Novak");
        await Assert.ThrowsAsync<DomainException>(() => sp.GetRequiredService<IHouseholdService>().DeleteMemberAsync(leo.Id));
    }
}

public class ChoreServiceTests
{
    [Fact]
    public async Task Completing_rotating_chore_creates_next_for_next_member()
    {
        using var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var h = login.Household!.Id;
        var svc = sp.GetRequiredService<IChoreService>();
        var members = await sp.GetRequiredService<IHouseholdService>().GetMembersAsync(h);

        var chore = (await svc.GetChoresAsync(h)).First(c => c.Title == "Clean Kitchen");
        var next = await svc.ChangeStatusAsync(chore.Id, ChoreStatus.Done);
        var idx = members.ToList().FindIndex(m => m.Id == chore.AssigneeId);

        Assert.True(
            next is not null
            && next.Status == ChoreStatus.ToDo
            && next.AssigneeId == members[(idx + 1) % members.Count].Id
            && next.Deadline == chore.Deadline!.Value.AddDays(WeeklyRotationDays));
    }

    [Fact]
    public void Next_assignee_wraps_around()
    {
        using var sp = TestHost.Build(seed: false);
        var svc = sp.GetRequiredService<IChoreService>();
        var m = Enumerable.Range(0, InitialHouseholdSize).Select(i => new Member { DisplayName = $"M{i}", JoinedAt = BaseJoinDate.AddDays(i) }).ToList();
        Assert.True(
            svc.GetNextAssignee(m, m[0].Id) == m[1].Id
            && svc.GetNextAssignee(m, m[^1].Id) == m[0].Id
            && svc.GetNextAssignee(m, null) == m[0].Id);
    }
}

public class HouseholdServiceTests
{
    [Fact]
    public async Task Create_household_then_login_as_admin()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        var existedBefore = await hs.AnyHouseholdExistsAsync();
        var (h, admin) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Cool Flat", "Ann", "ann@x.com", "secret1", ["Bob", "Cid", ""]));
        var count = (await hs.GetMembersAsync(h.Id)).Count;
        var r = await sp.GetRequiredService<IAuthService>().LoginAsync("ANN@x.com", "secret1");
        Assert.True(!existedBefore && count == InitialHouseholdSize && r.Status == LoginStatus.Success && r.Member!.Id == admin.Id);
    }

    [Fact]
    public async Task More_than_six_members_is_rejected()
    {
        using var sp = TestHost.Build(seed: false);
        await Assert.ThrowsAsync<DomainException>(() => sp.GetRequiredService<IHouseholdService>().CreateHouseholdAsync(
            new NewHouseholdRequest("Big", "A", "a@x.com", "secret1", ["B", "C", "D", "E", "F", "G"])));
    }
}

public class ReceiptTests
{
    [Fact]
    public async Task Mock_parser_returns_valid_receipt()
    {
        using var sp = TestHost.Build(seed: false);
        var receipt = await sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(DummyImageBytes, "image/png", "receipt.png");
        var validation = sp.GetRequiredService<IValidationService>().ValidateReceipt(receipt);
        Assert.True(receipt.Total == receipt.Items.Sum(i => i.Price) && !validation.HasWarnings);
    }

    [Fact]
    public async Task Mock_parser_simulates_failure()
    {
        using var sp = TestHost.Build(seed: false);
        await Assert.ThrowsAsync<ReceiptParseException>(() =>
            sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(DummyImageBytes, "image/png", "fail.png"));
    }

    [Fact]
    public async Task Validation_warns_on_total_mismatch()
    {
        using var sp = TestHost.Build(seed: false);
        var receipt = await sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(DummyImageBytes, "image/png", "warn.png");
        Assert.True(sp.GetRequiredService<IValidationService>().ValidateReceipt(receipt).HasWarnings);
    }

    [Fact]
    public void Gemini_json_is_parsed()
    {
        var r = Services.Receipts.GeminiReceiptParser.ParseJson(SampleGeminiJson);
        Assert.True(
            r.Merchant == "Shop"
            && r.Date == SampleDateMay20
            && r.Category == ExpenseCategory.Groceries
            && r.Items.Count == ExpectedParsedItemCount
            && r.Total == SampleParsedTotal
            && r.Items[0].Price == SampleParsedMilkPrice);
    }
}

public class ExportTests
{
    [Fact]
    public async Task Pdf_and_csv_are_written()
    {
        using var sp = TestHost.Build(seed: false);
        var svc = sp.GetRequiredService<IExportService>();
        var rows = Enumerable.Range(0, ExportSampleRowCount)
            .Select(i => new ReportRow(SampleDateMay1.AddDays(i % ExportDateStepMod), $"Item, \"{i}\"", "Other", "Maya", ExportBaseRowAmount + i, ExportRowYourShare))
            .ToList();
        var pdf = Path.GetTempFileName();
        var csv = Path.GetTempFileName();
        try
        {
            await svc.ExportExpensesPdfAsync(pdf, "All Expenses", rows);
            await svc.ExportExpensesCsvAsync(csv, rows);
            var bytes = await File.ReadAllBytesAsync(pdf);
            var header = System.Text.Encoding.ASCII.GetString(bytes, 0, PdfMagicHeaderLength);
            var pdfText = System.Text.Encoding.Latin1.GetString(bytes);
            var lines = await File.ReadAllLinesAsync(csv);
            Assert.True(
                header.StartsWith("%PDF-1.4")
                && System.Text.RegularExpressions.Regex.IsMatch(pdfText, @"/Count [2-9]")
                && lines.Length == ExpectedCsvLineCount
                && lines[1].Contains("\"Item, \"\"0\"\"\""));
        }
        finally
        {
            if (File.Exists(pdf)) File.Delete(pdf);
            if (File.Exists(csv)) File.Delete(csv);
        }
    }
}

public class BackendUpgradesTests
{
    [Fact]
    public async Task Updating_expense_recalculates_balances_immediately()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        var (h, admin) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Test Home", "Uliana", "uliana@lnu.edu.ua", "pass123", ["Nazar"]));
        var members = await hs.GetMembersAsync(h.Id);
        var nazar = members.Single(m => m.DisplayName == "Nazar");

        var expSvc = sp.GetRequiredService<IExpenseService>();
        var balSvc = sp.GetRequiredService<IBalanceService>();

        var created = await expSvc.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Internet", InternetBaseAmount, SampleDateMay20, ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0m)).ToList()));

        var bal1 = (await balSvc.GetBalancesAsync(h.Id)).ToDictionary(b => b.MemberId, b => b.Balance);

        await expSvc.UpdateExpenseAsync(created.Id, new NewExpenseRequest(
            "Internet Fibre", InternetUpgradedAmount, SampleDateMay20, ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0m)).ToList()));

        var bal2 = (await balSvc.GetBalancesAsync(h.Id)).ToDictionary(b => b.MemberId, b => b.Balance);
        Assert.True(
            bal1[admin.Id] == ExpectedInitialShareBalance
            && bal1[nazar.Id] == -ExpectedInitialShareBalance
            && bal2[admin.Id] == ExpectedUpdatedShareBalance
            && bal2[nazar.Id] == -ExpectedUpdatedShareBalance);
    }

    [Fact]
    public async Task Member_with_open_chore_cannot_be_deleted()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        var (h, _) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Chore Home", "Uliana", "u@x.com", "pass123", ["Nazar", "Anna"]));
        var members = await hs.GetMembersAsync(h.Id);
        var nazar = members.Single(m => m.DisplayName == "Nazar");

        var choreSvc = sp.GetRequiredService<IChoreService>();
        var chore = await choreSvc.CreateChoreAsync(h.Id, new NewChoreRequest("Wash dishes", nazar.Id, DateTime.Today.AddDays(TomorrowOffsetDays), ChoreRotation.None));

        var canDeleteWithOpen = await hs.CanDeleteMemberAsync(nazar.Id);
        await choreSvc.ChangeStatusAsync(chore.Id, ChoreStatus.Done);
        var canDeleteAfterDone = await hs.CanDeleteMemberAsync(nazar.Id);
        await hs.DeleteMemberAsync(nazar.Id);
        var activeMembers = await hs.GetMembersAsync(h.Id);

        Assert.True(!canDeleteWithOpen && canDeleteAfterDone && activeMembers.All(m => m.Id != nazar.Id));
    }

    [Fact]
    public async Task Request_settlement_creates_notification_for_debtor()
    {
        using var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var h = login.Household!.Id;
        var balSvc = sp.GetRequiredService<IBalanceService>();
        var plan = await balSvc.GetSettlementPlanAsync(h);
        var transfer = plan.Transfers.First();

        await balSvc.RequestSettlementAsync(h, transfer);

        var notes = await sp.GetRequiredService<INotificationService>().GetNotificationsAsync(transfer.FromMemberId);
        Assert.Contains(notes, n => n.Type == NotificationType.SettlementRequest);
    }

    [Fact]
    public async Task Report_service_aggregates_period_summary()
    {
        using var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var reportSvc = sp.GetRequiredService<IReportService>();
        var summary = await reportSvc.GetPeriodSummaryAsync(login.Household!.Id, PeriodStart, PeriodEnd);

        Assert.True(
            summary.TotalSpent > 0m
            && summary.ByCategory.Count > 0
            && summary.ByMember.Count > 0
            && summary.TopCategory == ExpenseCategory.Rent);
    }

    [Fact]
    public async Task Backup_and_restore_completes_under_5_seconds()
    {
        using var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var backupSvc = sp.GetRequiredService<IBackupService>();
        var tempFile = Path.GetTempFileName();
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await backupSvc.ExportBackupAsync(login.Household!.Id, tempFile);
            var restored = await backupSvc.RestoreBackupAsync(tempFile);
            sw.Stop();

            Assert.True(
                sw.Elapsed.TotalSeconds < MaxBackupRestoreSeconds
                && restored.Household.Name == login.Household.Name
                && restored.Expenses.Count > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
