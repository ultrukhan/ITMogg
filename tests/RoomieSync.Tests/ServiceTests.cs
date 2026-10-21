using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Data.Seed;

namespace RoomieSync.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Correct_password_signs_in()
    {
        using var sp = TestHost.Build();
        var r = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        Assert.Equal(LoginStatus.Success, r.Status);
        Assert.Equal("Maya Torres", r.Member!.DisplayName);
        Assert.True(r.Member.IsAdmin);
    }

    [Fact]
    public async Task Three_failures_lock_for_60_seconds()
    {
        var clock = new FakeClock(new DateTime(2026, 5, 24, 12, 0, 0));
        using var sp = TestHost.Build(clock);
        var auth = sp.GetRequiredService<IAuthService>();

        Assert.Equal(2, (await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x")).AttemptsLeft);
        Assert.Equal(1, (await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x")).AttemptsLeft);
        Assert.Equal(LoginStatus.LockedOut, (await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, "x")).Status);

        // Навіть правильний пароль не пускає під час блокування
        clock.Advance(TimeSpan.FromSeconds(59));
        var locked = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        Assert.Equal(LoginStatus.LockedOut, locked.Status);
        Assert.True(locked.LockoutRemaining <= TimeSpan.FromSeconds(1));

        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(LoginStatus.Success, (await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword)).Status);
    }

    [Fact]
    public async Task Passwords_are_stored_hashed()
    {
        using var sp = TestHost.Build();
        var acc = await sp.GetRequiredService<Core.Abstractions.Repositories.IUserAccountRepository>().GetByEmailAsync(DemoDataSeeder.DemoAdminEmail);
        Assert.NotNull(acc);
        Assert.DoesNotContain(DemoDataSeeder.DemoPassword, acc!.PasswordHash);
        Assert.NotEmpty(acc.PasswordSalt);
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
        Assert.Equal(0m, balances.Sum(b => b.Balance));
        var maya = members.Single(m => m.DisplayName == "Maya Torres");
        Assert.Equal(124.50m, balances.Single(b => b.MemberId == maya.Id).Balance);
    }

    [Fact]
    public async Task Settling_the_whole_plan_zeroes_all_balances()
    {
        var (sp, h, _) = await Setup();
        using var __ = sp;
        var svc = sp.GetRequiredService<IBalanceService>();
        var plan = await svc.GetSettlementPlanAsync(h);
        Assert.False(plan.IsSettled);
        foreach (var t in plan.Transfers) await svc.RecordSettlementAsync(h, t);
        var after = await svc.GetSettlementPlanAsync(h);
        Assert.True(after.IsSettled);
        Assert.All(after.Balances, b => Assert.Equal(0m, b.Balance));
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
            "Pizza", 40m, new DateTime(2026, 5, 24), ExpenseCategory.Entertainment, payer.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0)).ToList()));
        var after = (await balances.GetBalancesAsync(h)).ToDictionary(b => b.MemberId, b => b.Balance);
        Assert.Equal(before[payer.Id] + 30m, after[payer.Id]);
        Assert.Equal(before[members[0].Id] - 10m, after[members[0].Id]);
    }

    [Fact]
    public async Task Validation_rejects_bad_input()
    {
        var (sp, _, members) = await Setup();
        using var __ = sp;
        var svc = sp.GetRequiredService<IExpenseService>();
        var bad = svc.Validate(new NewExpenseRequest("", -5m, DateTime.Today, ExpenseCategory.Other, members[0].Id, SplitMethod.Equally, Array.Empty<ShareInput>()));
        Assert.False(bad.IsValid);
        var pct = svc.Validate(new NewExpenseRequest("X", 10m, new DateTime(2026, 5, 24), ExpenseCategory.Other, members[0].Id, SplitMethod.ByPercentage,
            new[] { new ShareInput(members[0].Id, 60), new ShareInput(members[1].Id, 30) }));
        Assert.Contains(pct.Errors, e => e.Contains("100%"));
    }

    [Fact]
    public async Task Member_with_debt_cannot_be_deleted()
    {
        var (sp, _, members) = await Setup();
        using var __ = sp;
        var leo = members.Single(m => m.DisplayName == "Leo Novak");
        Assert.False(await sp.GetRequiredService<IHouseholdService>().CanDeleteMemberAsync(leo.Id));
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

        Assert.NotNull(next);
        Assert.Equal(ChoreStatus.ToDo, next!.Status);
        var idx = members.ToList().FindIndex(m => m.Id == chore.AssigneeId);
        Assert.Equal(members[(idx + 1) % members.Count].Id, next.AssigneeId);
        Assert.Equal(chore.Deadline!.Value.AddDays(7), next.Deadline);
    }

    [Fact]
    public void Next_assignee_wraps_around()
    {
        using var sp = TestHost.Build(seed: false);
        var svc = sp.GetRequiredService<IChoreService>();
        var m = Enumerable.Range(0, 3).Select(i => new Member { DisplayName = $"M{i}", JoinedAt = new DateTime(2024, 1, 1).AddDays(i) }).ToList();
        Assert.Equal(m[1].Id, svc.GetNextAssignee(m, m[0].Id));
        Assert.Equal(m[0].Id, svc.GetNextAssignee(m, m[2].Id));
        Assert.Equal(m[0].Id, svc.GetNextAssignee(m, null));
    }
}

public class HouseholdServiceTests
{
    [Fact]
    public async Task Create_household_then_login_as_admin()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        Assert.False(await hs.AnyHouseholdExistsAsync());
        var (h, admin) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Cool Flat", "Ann", "ann@x.com", "secret1", new[] { "Bob", "Cid", "" }));
        Assert.Equal(3, (await hs.GetMembersAsync(h.Id)).Count);
        var r = await sp.GetRequiredService<IAuthService>().LoginAsync("ANN@x.com", "secret1");
        Assert.Equal(LoginStatus.Success, r.Status);
        Assert.Equal(admin.Id, r.Member!.Id);
    }

    [Fact]
    public async Task More_than_six_members_is_rejected()
    {
        using var sp = TestHost.Build(seed: false);
        await Assert.ThrowsAsync<DomainException>(() => sp.GetRequiredService<IHouseholdService>().CreateHouseholdAsync(
            new NewHouseholdRequest("Big", "A", "a@x.com", "secret1", new[] { "B", "C", "D", "E", "F", "G" })));
    }
}

public class ReceiptTests
{
    [Fact]
    public async Task Mock_parser_returns_valid_receipt()
    {
        using var sp = TestHost.Build(seed: false);
        var receipt = await sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(new byte[] { 1 }, "image/png", "receipt.png");
        Assert.Equal(receipt.Items.Sum(i => i.Price), receipt.Total);
        Assert.False(sp.GetRequiredService<IValidationService>().ValidateReceipt(receipt).HasWarnings);
    }

    [Fact]
    public async Task Mock_parser_simulates_failure()
    {
        using var sp = TestHost.Build(seed: false);
        await Assert.ThrowsAsync<ReceiptParseException>(() =>
            sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(new byte[] { 1 }, "image/png", "fail.png"));
    }

    [Fact]
    public async Task Validation_warns_on_total_mismatch()
    {
        using var sp = TestHost.Build(seed: false);
        var receipt = await sp.GetRequiredService<IReceiptParser>().ParseReceiptAsync(new byte[] { 1 }, "image/png", "warn.png");
        Assert.True(sp.GetRequiredService<IValidationService>().ValidateReceipt(receipt).HasWarnings);
    }

    [Fact]
    public void Gemini_json_is_parsed()
    {
        var r = Services.Receipts.GeminiReceiptParser.ParseJson("""
            {"merchant":"Shop","date":"2026-05-20","total":12.5,"category":"groceries","items":[{"name":"Milk","price":2.5},{"name":"Bread","price":10}]}
            """);
        Assert.Equal("Shop", r.Merchant);
        Assert.Equal(new DateTime(2026, 5, 20), r.Date);
        Assert.Equal(ExpenseCategory.Groceries, r.Category);
        Assert.Equal(2, r.Items.Count);
    }
}

public class ExportTests
{
    [Fact]
    public async Task Pdf_and_csv_are_written()
    {
        using var sp = TestHost.Build(seed: false);
        var svc = sp.GetRequiredService<IExportService>();
        var rows = Enumerable.Range(0, 80).Select(i => new ReportRow(new DateTime(2026, 5, 1).AddDays(i % 20), $"Item, \"{i}\"", "Other", "Maya", 10 + i, 5)).ToList();
        var pdf = Path.GetTempFileName();
        var csv = Path.GetTempFileName();
        await svc.ExportExpensesPdfAsync(pdf, "All Expenses", rows);
        await svc.ExportExpensesCsvAsync(csv, rows);
        var bytes = await File.ReadAllBytesAsync(pdf);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Matches(@"/Count [2-9]", System.Text.Encoding.Latin1.GetString(bytes));
        var lines = await File.ReadAllLinesAsync(csv);
        Assert.Equal(81, lines.Length);
        Assert.Contains("\"Item, \"\"0\"\"\"", lines[1]);
    }
}

public class BackendUpgradesTests
{
    [Fact]
    public async Task Updating_expense_recalculates_balances_immediately()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        var (h, admin) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Test Home", "Uliana", "uliana@lnu.edu.ua", "pass123", new[] { "Nazar" }));
        var members = await hs.GetMembersAsync(h.Id);
        var nazar = members.Single(m => m.DisplayName == "Nazar");

        var expSvc = sp.GetRequiredService<IExpenseService>();
        var balSvc = sp.GetRequiredService<IBalanceService>();

        var created = await expSvc.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Internet", 100m, new DateTime(2026, 5, 20), ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0)).ToList()));

        var bal1 = (await balSvc.GetBalancesAsync(h.Id)).ToDictionary(b => b.MemberId, b => b.Balance);
        Assert.Equal(50m, bal1[admin.Id]);
        Assert.Equal(-50m, bal1[nazar.Id]);

        // Оновлюємо суму витрати зі 100 до 200 (User Story 4)
        await expSvc.UpdateExpenseAsync(created.Id, new NewExpenseRequest(
            "Internet Fibre", 200m, new DateTime(2026, 5, 20), ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 0)).ToList()));

        var bal2 = (await balSvc.GetBalancesAsync(h.Id)).ToDictionary(b => b.MemberId, b => b.Balance);
        Assert.Equal(100m, bal2[admin.Id]);
        Assert.Equal(-100m, bal2[nazar.Id]);
    }

    [Fact]
    public async Task Member_with_open_chore_cannot_be_deleted()
    {
        using var sp = TestHost.Build(seed: false);
        var hs = sp.GetRequiredService<IHouseholdService>();
        var (h, _) = await hs.CreateHouseholdAsync(new NewHouseholdRequest("Chore Home", "Uliana", "u@x.com", "pass123", new[] { "Nazar", "Anna" }));
        var members = await hs.GetMembersAsync(h.Id);
        var nazar = members.Single(m => m.DisplayName == "Nazar");

        // Баланс нульовий, але призначаємо відкрите завдання
        var choreSvc = sp.GetRequiredService<IChoreService>();
        var chore = await choreSvc.CreateChoreAsync(h.Id, new NewChoreRequest("Wash dishes", nazar.Id, DateTime.Today.AddDays(1), ChoreRotation.None));

        Assert.False(await hs.CanDeleteMemberAsync(nazar.Id));
        await Assert.ThrowsAsync<DomainException>(() => hs.DeleteMemberAsync(nazar.Id));

        // Закриваємо завдання -> тепер видалення дозволено
        await choreSvc.ChangeStatusAsync(chore.Id, ChoreStatus.Done);
        Assert.True(await hs.CanDeleteMemberAsync(nazar.Id));
        await hs.DeleteMemberAsync(nazar.Id);
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
        var summary = await reportSvc.GetPeriodSummaryAsync(login.Household!.Id, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        Assert.True(summary.TotalSpent > 0);
        Assert.NotEmpty(summary.ByCategory);
        Assert.NotEmpty(summary.ByMember);
        Assert.Equal(ExpenseCategory.Rent, summary.TopCategory);
    }

    [Fact]
    public async Task Backup_and_restore_completes_under_5_seconds()
    {
        using var sp = TestHost.Build();
        var login = await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        var backupSvc = sp.GetRequiredService<IBackupService>();
        var tempFile = Path.GetTempFileName();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await backupSvc.ExportBackupAsync(login.Household!.Id, tempFile);
        var restored = await backupSvc.RestoreBackupAsync(tempFile);
        sw.Stop();

        Assert.True(sw.Elapsed.TotalSeconds < 5.0);
        Assert.Equal(login.Household.Name, restored.Household.Name);
        Assert.NotEmpty(restored.Expenses);
        File.Delete(tempFile);
    }
}
