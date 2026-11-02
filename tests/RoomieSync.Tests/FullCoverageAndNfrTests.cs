using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Core.Security;
using RoomieSync.Data;
using RoomieSync.Data.Seed;
using RoomieSync.Services;
using RoomieSync.Services.Maintenance;
using RoomieSync.Services.Receipts;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using Xunit;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public sealed class FullCoverageAndNfrTests
{
    private static readonly SplitMethod EqualSplit = Enum.GetValues<SplitMethod>()[0];

    private sealed record TestEnv(
        ServiceProvider Provider,
        IHouseholdService Households,
        IAuthService Auth,
        IExpenseService Expenses,
        IBalanceService Balances,
        IChoreService Chores,
        INotificationService Notifications,
        IReportService Reports,
        IExportService Export,
        IBackupService Backup,
        ISessionContext Session,
        IValidationService Validator,
        IExpenseRepository ExpenseRepo,
        IChoreRepository ChoreRepo,
        IUserAccountRepository AccountRepo,
        FakeClock Clock);

    private static TestEnv BuildEnvironment(bool seedDemo = false)
    {
        var config = new TestConfiguration(new Dictionary<string, string?>
        {
            ["Data:Provider"] = "InMemory",
            ["Data:SeedDemoData"] = seedDemo ? "true" : "false",
            ["Gemini:UseMock"] = "true",
            ["Gemini:TimeoutSeconds"] = "5",
            ["Gemini:Model"] = "gemini-2.5-flash"
        });

        var services = new ServiceCollection();
        services.AddRoomieSyncData(config);
        services.AddRoomieSyncServices(config);

        var clock = new FakeClock(OctTestNow);
        services.AddSingleton<IClock>(clock);

        var provider = services.BuildServiceProvider();

        return new TestEnv(
            provider,
            provider.GetRequiredService<IHouseholdService>(),
            provider.GetRequiredService<IAuthService>(),
            provider.GetRequiredService<IExpenseService>(),
            provider.GetRequiredService<IBalanceService>(),
            provider.GetRequiredService<IChoreService>(),
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<IReportService>(),
            provider.GetRequiredService<IExportService>(),
            provider.GetRequiredService<IBackupService>(),
            provider.GetRequiredService<ISessionContext>(),
            provider.GetRequiredService<IValidationService>(),
            provider.GetRequiredService<IExpenseRepository>(),
            provider.GetRequiredService<IChoreRepository>(),
            provider.GetRequiredService<IUserAccountRepository>(),
            clock);
    }

    private static async Task<Type?> CaptureExceptionTypeAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType();
        }
    }

    private static Type? CaptureSyncExceptionType(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return ex.GetType();
        }
    }

    [Fact]
    public async Task Nfr1_DashboardLoad_With1000Expenses_Under1Second()
    {
        var env = BuildEnvironment();
        var (h, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("NFR1 Home", "Nazar", "nazar@lnu.edu.ua", "Secret123", ["Olena", "Taras"]));
        var members = await env.Households.GetMembersAsync(h.Id);
        var shares = members.Select(m => new ShareInput(m.Id, EqualShareWeight)).ToList();

        for (int i = 0; i < DashboardExpenseCount; i++)
        {
            await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
                $"Expense #{i}", PartialSettlementAmount + (i % ExpenseAmountStepMod), env.Clock.Now.AddDays(-(i % ExpenseDayStepMod)),
                (ExpenseCategory)(i % ExpenseCategoryMod), members[i % members.Count].Id, EqualSplit, shares));
        }

        var sw = Stopwatch.StartNew();
        var monthStart = new DateTime(env.Clock.Now.Year, env.Clock.Now.Month, 1);
        var expensesList = await env.Expenses.GetExpensesAsync(h.Id, monthStart, env.Clock.Now);
        var balances = await env.Balances.GetBalancesAsync(h.Id);
        var summary = await env.Reports.GetPeriodSummaryAsync(h.Id, monthStart, env.Clock.Now);
        var activeChores = await env.Chores.GetChoresAsync(h.Id);
        sw.Stop();

        Assert.True(
            expensesList.Count > 0
            && balances.Count == InitialHouseholdSize
            && summary is not null
            && activeChores is not null
            && sw.ElapsedMilliseconds < MaxDashboardLoadMs);
    }

    [Fact]
    public async Task Nfr2_BalanceAndDebtSimplification_Under500Milliseconds()
    {
        var env = BuildEnvironment();
        var (h, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("NFR2 Home", "Admin", "admin@test.com", "Secret123",
                ["M2", "M3", "M4", "M5", "M6"]));
        var members = await env.Households.GetMembersAsync(h.Id);
        var shares = members.Select(m => new ShareInput(m.Id, EqualShareWeight)).ToList();

        for (int i = 0; i < BalancePerfExpenseCount; i++)
        {
            await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
                $"Item {i}", BalancePerfExpenseAmount, env.Clock.Now, ExpenseCategory.Groceries,
                members[i % members.Count].Id, EqualSplit, shares));
        }

        var sw = Stopwatch.StartNew();
        var plan = await env.Balances.GetSettlementPlanAsync(h.Id);
        sw.Stop();

        Assert.True(plan is not null && sw.ElapsedMilliseconds < MaxBalanceCalcMs);
    }

    [Fact]
    public async Task Nfr3_GeminiTimeout_Is5Seconds_AndFallbackIsFast()
    {
        var options = new GeminiOptions { ApiKey = "test-key", TimeoutSeconds = GeminiTimeoutSec, UseMock = false };
        var slowHandler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(SimulatedDelaySec), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(slowHandler) { Timeout = TimeSpan.FromMilliseconds(ShortClientTimeoutMs) };
        var parser = new GeminiReceiptParser(httpClient, options);

        var sw = Stopwatch.StartNew();
        ReceiptParseException? caught = null;
        try
        {
            await parser.ParseReceiptAsync(DummyImageBytes, "image/png");
        }
        catch (ReceiptParseException ex)
        {
            caught = ex;
        }
        sw.Stop();

        Assert.True(caught is not null && caught.IsTimeout && sw.ElapsedMilliseconds < MaxFallbackMs);
    }

    [Fact]
    public async Task Nfr5_HouseholdMemberCount_Boundaries_2To6Members()
    {
        var env = BuildEnvironment();
        var smallErr = await CaptureExceptionTypeAsync(() =>
            env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
                "TooSmall", "Solo", "solo@test.com", "Secret123", Array.Empty<string>())));

        var bigErr = await CaptureExceptionTypeAsync(() =>
            env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
                "TooBig", "A1", "a1@test.com", "Secret123", ["A2", "A3", "A4", "A5", "A6", "A7"])));

        var (h2, _) = await env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
            "MinHome", "U1", "u1@test.com", "Secret123", ["U2"]));
        var count = (await env.Households.GetMembersAsync(h2.Id)).Count;

        Assert.True(smallErr == typeof(DomainException) && bigErr == typeof(DomainException) && count == MinHouseholdSize);
    }

    [Fact]
    public async Task Nfr6_HighVolume_10000Expenses_And_5000Chores_Performance()
    {
        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("HighLoad Home", "Admin", "load@test.com", "Secret123", ["Roomie2", "Roomie3"]));
        var members = await env.Households.GetMembersAsync(h.Id);

        for (int i = 0; i < HighVolumeExpenseCount; i++)
        {
            await env.ExpenseRepo.AddAsync(new Expense
            {
                HouseholdId = h.Id,
                Title = $"Tx #{i}",
                Amount = RemainderSplitTotal,
                Date = env.Clock.Now.AddDays(-(i % DaysInYearMod)),
                Category = ExpenseCategory.Groceries,
                PaidByMemberId = admin.Id,
                SplitMethod = EqualSplit,
                Shares = members.Select(m => new ExpenseShare { MemberId = m.Id, Amount = ShareThirdRest }).ToList()
            });
        }

        for (int i = 0; i < HighVolumeChoreCount; i++)
        {
            await env.ChoreRepo.AddAsync(new Chore
            {
                HouseholdId = h.Id,
                Title = $"Chore #{i}",
                AssigneeId = members[i % members.Count].Id,
                Status = ChoreStatus.Done,
                CreatedAt = env.Clock.Now.AddDays(OverdueLongOffsetDays),
                CompletedAt = env.Clock.Now
            });
        }

        var sw = Stopwatch.StartNew();
        var periodExpenses = await env.Expenses.GetExpensesAsync(h.Id, env.Clock.Now.AddDays(MonthlyQueryOffsetDays), env.Clock.Now);
        var allChores = await env.Chores.GetChoresAsync(h.Id);
        sw.Stop();

        Assert.True(
            periodExpenses.Count > 0
            && allChores.Count == HighVolumeChoreCount
            && sw.ElapsedMilliseconds < MaxHighVolumeQueryMs);
    }

    [Fact]
    public async Task Nfr7_ReceiptStorage_SupportsUpTo2Gb_AndValidatesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RoomieSyncTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var defaultStorage = new ReceiptStorageService();
            var storage = new ReceiptStorageService(tempDir);
            var maxBytes = storage.MaxStorageBytes;
            var initialBytes = storage.GetCurrentStorageUsageBytes();

            Directory.CreateDirectory(tempDir);
            var sampleFile = Path.Combine(tempDir, "sample.jpg");
            await File.WriteAllBytesAsync(sampleFile, JpegHeaderBytes);

            var savedPath = await storage.SaveReceiptImageAsync(sampleFile);
            var fileSaved = File.Exists(savedPath);
            var afterBytes = storage.GetCurrentStorageUsageBytes();

            var emptyErr = await CaptureExceptionTypeAsync(() => storage.SaveReceiptImageAsync(""));
            var missingErr = await CaptureExceptionTypeAsync(() => storage.SaveReceiptImageAsync(Path.Combine(tempDir, "missing.png")));

            var badExtFile = Path.Combine(tempDir, "virus.exe");
            await File.WriteAllBytesAsync(badExtFile, DummyImageBytes);
            var badExtErr = await CaptureExceptionTypeAsync(() => storage.SaveReceiptImageAsync(badExtFile));

            Assert.True(
                defaultStorage.MaxStorageBytes == TwoGigabytes
                && maxBytes == TwoGigabytes
                && initialBytes == 0L
                && fileSaved
                && afterBytes > 0L
                && emptyErr is not null
                && missingErr is not null
                && badExtErr is not null);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task NfrSecurity_PasswordHashing_And_60SecLockoutAfter3FailedAttempts()
    {
        var env = BuildEnvironment();
        var (_, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("SecHome", "Admin", "sec@test.com", "MyStrongPass!", ["User2"]));

        var acc = await env.AccountRepo.GetByMemberIdAsync(admin.Id);
        await env.Auth.LoginAsync("sec@test.com", "Wrong1");
        await env.Auth.LoginAsync("sec@test.com", "Wrong2");
        var r3 = await env.Auth.LoginAsync("sec@test.com", "Wrong3");
        var rLocked = await env.Auth.LoginAsync("sec@test.com", "MyStrongPass!");

        env.Clock.Advance(LockoutWait61Sec);
        var rSuccess = await env.Auth.LoginAsync("sec@test.com", "MyStrongPass!");

        Assert.True(
            acc is not null
            && acc.PasswordHash != "MyStrongPass!"
            && r3.Status == LoginStatus.LockedOut
            && rLocked.Status == LoginStatus.LockedOut
            && rSuccess.Status == LoginStatus.Success);
    }

    [Fact]
    public void Coverage_Core_Money_Clock_And_Models()
    {
        var rounded = Money.Round(UnroundedMoney);
        var allCurrenciesValid = true;
        foreach (var curr in MoneyFormat.SupportedCurrencies)
        {
            MoneyFormat.SetCurrency(curr.Code);
            if (string.IsNullOrEmpty(MoneyFormat.Symbol)
                || !MoneyFormat.Format(RemainderSplitTotal).Contains(MoneyFormat.Symbol)
                || MoneyFormat.FormatSigned(0m).Contains('+'))
            {
                allCurrenciesValid = false;
            }
        }

        var singleMember = new Member { DisplayName = "Nazar" };
        var chore = new Chore { Deadline = DateTime.Today.AddDays((-TomorrowOffsetDays)), Status = ChoreStatus.ToDo };

        Assert.True(
            rounded == RoundedMoney
            && allCurrenciesValid
            && !string.IsNullOrWhiteSpace(singleMember.Initials)
            && chore.IsOverdue(DateTime.Today));
    }

    [Fact]
    public async Task Coverage_ExportService_And_SimplePdfWriter_Cyrillic()
    {
        var env = BuildEnvironment();
        var tempDir = Path.Combine(Path.GetTempPath(), "RoomieSyncExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rows = new List<ReportRow> { new(DateTime.Today, "Сільпо", "Продукти", "Назар", ExpectedNetAfterRentAndGroceries, EditedExpenseAmount) };
            var transfers = new List<(string, string, decimal)> { ("Олена", "Назар", ExpectedSolomiiaBalanceAfterEdit) };

            var expPdf = Path.Combine(tempDir, "expenses.pdf");
            await env.Export.ExportExpensesCsvAsync(Path.Combine(tempDir, "expenses.csv"), rows);
            await env.Export.ExportExpensesPdfAsync(expPdf, "Звіт", rows);
            await env.Export.ExportSettlementPlanCsvAsync(Path.Combine(tempDir, "settlements.csv"), transfers);
            await env.Export.ExportSettlementPlanPdfAsync(Path.Combine(tempDir, "settlements.pdf"), "План", transfers);

            Assert.True(File.Exists(expPdf));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Coverage_BackupService_ExportAndRestore()
    {
        var env = BuildEnvironment();
        var (h, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("BackupHome", "Admin", "b@test.com", "Secret123", ["M2"]));

        var tempFile = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid():N}.json");
        try
        {
            await env.Backup.ExportBackupAsync(h.Id, tempFile);
            var restored = await env.Backup.RestoreBackupAsync(tempFile);
            Assert.True(restored is not null && restored.Members.Count > 0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Coverage_ReceiptParsers_And_Validation()
    {
        var env = BuildEnvironment();
        var mockParser = new MockReceiptParser(new GeminiOptions { UseMock = true }, env.Clock);
        var parsed1 = await mockParser.ParseReceiptAsync(DummyImageBytes, "image/jpeg", "grocery.jpg");
        var validRes = env.Validator.ValidateReceipt(parsed1);

        var geminiJson = "{\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"{\\\"merchant\\\":\\\"ATB\\\",\\\"total\\\":125.50,\\\"items\\\":[]}\"}]}}]}";
        var okHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiJson, Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(okHandler);
        var geminiParser = new GeminiReceiptParser(client, new GeminiOptions { ApiKey = "fake-key", UseMock = false });
        var geminiResult = await geminiParser.ParseReceiptAsync(DummyImageBytes, "image/jpeg", "atb.jpg");

        Assert.True(
            parsed1 is not null
            && validRes.IsValid
            && geminiResult.Merchant == "ATB"
            && geminiResult.Total == FormattedSampleAmount);
    }

    [Fact]
    public async Task Coverage_DemoDataSeeder_SessionContext_And_Workflows()
    {
        var env = BuildEnvironment(seedDemo: true);
        var seeder = env.Provider.GetRequiredService<DemoDataSeeder>();
        var seedMethod = typeof(DemoDataSeeder).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).FirstOrDefault();
        if (seedMethod is not null)
        {
            var args = seedMethod.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            if (seedMethod.Invoke(seeder, args) is Task t1) await t1;
        }

        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("FlowHome", "AdminUser", "flow@test.com", "Secret123", ["RoomieB"]));

        env.Session.SignIn(admin, h);
        var signedIn = env.Session.IsAuthenticated;
        env.Session.SignOut();
        var signedOut = !env.Session.IsAuthenticated;

        Assert.True(signedIn && signedOut);
    }

    [Fact]
    public void Coverage_Core_AllModels_Security_And_EdgeCases()
    {
        var sysClock = new SystemClock();
        var delRes = new DeleteExpenseResult(true, MinHouseholdSize, MinHouseholdSize);
        var setRes = new SettlementActionResult(true, "Settled!");
        var memSpend = new MemberSpendSummary(Guid.NewGuid(), "Nazar", RemainderSplitTotal, PizzaExpenseAmount);
        var monthSpend = new MonthlySpendSummary(SampleReportYear, SampleReportMonth, TotalAnalyticsSpentAmount, MinValidationErrorsCount);
        var catSpend = new CategorySpendSummary(ExpenseCategory.Rent, RentMonthlyAmount, 1, ExpectedCategorySharePct);
        var repSum = new HouseholdReportSummary(TotalAnalyticsSpentAmount, SeedExpenseAmount, MinValidationErrorsCount, ExpenseCategory.Rent, [catSpend], [monthSpend], [memSpend]);
        var plan = new SettlementPlan([new Transfer(Guid.NewGuid(), Guid.NewGuid(), SettlementTransferAmount)], Array.Empty<MemberBalance>(), InitialHouseholdSize);

        var valOk = ValidationResult.Ok();
        var initiallyValid = valOk.IsValid && !valOk.HasWarnings;
        valOk.Warning("warn1").Error("err1");

        bool currChanged = false;
        EventHandler handler = (_, _) => currChanged = true;
        MoneyFormat.CurrencyChanged += handler;
        string codeAfterNull;
        string codeAfterUnknown;
        string posSigned;
        string negSigned;
        string zeroSigned;
        try
        {
            MoneyFormat.SetCurrency(null);
            codeAfterNull = MoneyFormat.CurrencyCode;
            MoneyFormat.SetCurrency("UNKNOWN_CURR");
            codeAfterUnknown = MoneyFormat.CurrencyCode;
            posSigned = MoneyFormat.FormatSigned(ShareLookupAmount);
            negSigned = MoneyFormat.FormatSigned((-ShareLookupAmount));
            zeroSigned = MoneyFormat.FormatSigned(0m);
        }
        finally
        {
            MoneyFormat.CurrencyChanged -= handler;
        }

        var hasher = new Pbkdf2PasswordHasher();
        var hashErr = CaptureSyncExceptionType(() => hasher.Hash(""));
        var (hash, salt) = hasher.Hash("ValidPass123");

        var emptyMember = new Member { DisplayName = "   " };
        var twoWordMember = new Member { DisplayName = "Solomiia Uhryn" };

        var doneChore = new Chore { Status = ChoreStatus.Done, Deadline = DateTime.Today.AddDays((-TomorrowOffsetDays)) };
        var noDeadlineChore = new Chore { Status = ChoreStatus.ToDo, Deadline = null };
        var futureChore = new Chore { Status = ChoreStatus.ToDo, Deadline = DateTime.Today.AddDays(TomorrowOffsetDays) };

        Assert.True(
            sysClock.Now <= DateTime.Now.AddSeconds(ClockToleranceSec)
            && delRes.Deleted && delRes.ApprovedCount == MinHouseholdSize && delRes.RequiredCount == MinHouseholdSize
            && setRes.Settled && setRes.Message == "Settled!"
            && memSpend.NetBalance == DinnerExpenseAmount
            && monthSpend.Year == SampleReportYear && monthSpend.Month == SampleReportMonth && monthSpend.TotalAmount == TotalAnalyticsSpentAmount && monthSpend.ExpenseCount == MinValidationErrorsCount
            && catSpend.Category == ExpenseCategory.Rent && catSpend.Percentage == ExpectedCategorySharePct
            && repSum.TotalSpent == TotalAnalyticsSpentAmount && repSum.MonthlyAverage == SeedExpenseAmount && repSum.ExpenseCount == MinValidationErrorsCount && repSum.TopCategory == ExpenseCategory.Rent
            && repSum.ByCategory.Count == 1 && repSum.ByMonth.Count == 1 && repSum.ByMember.Count == 1
            && plan.TotalAmount == SettlementTransferAmount && plan.TransfersSaved == MinHouseholdSize && !plan.IsSettled
            && initiallyValid && valOk.Warnings.Count == 1 && valOk.Errors.Count == 1 && valOk.HasWarnings && !valOk.IsValid
            && codeAfterNull == "USD" && codeAfterUnknown == "USD" && currChanged
            && posSigned.StartsWith("+") && negSigned.StartsWith("-") && zeroSigned == "$0.00"
            && hashErr is not null
            && hasher.Verify("ValidPass123", hash, salt)
            && !hasher.Verify("WrongPass", hash, salt)
            && !hasher.Verify("", hash, salt)
            && !hasher.Verify("ValidPass123", "", salt)
            && !hasher.Verify("ValidPass123", hash, "")
            && emptyMember.Initials == "?" && emptyMember.FirstName == "   " && emptyMember.ToString() == "   " && emptyMember.IsEmailConfirmed
            && twoWordMember.Initials == "SU" && twoWordMember.FirstName == "Solomiia"
            && !doneChore.IsOverdue(DateTime.Today) && !noDeadlineChore.IsOverdue(DateTime.Today) && !futureChore.IsOverdue(DateTime.Today));
    }

    [Fact]
    public async Task Coverage_Data_DI_PostgresConfig_And_InMemoryRepositories()
    {
        var pgConfig = new TestConfiguration(new Dictionary<string, string?>
        {
            ["Data:Provider"] = "Postgres",
            ["Data:SeedDemoData"] = "false",
            ["ConnectionStrings:RoomieSync"] = "Host=localhost;Database=roomiesync_test;Username=postgres;Password=postgres",
            ["Gemini:UseMock"] = "false",
            ["Gemini:ApiKey"] = "test-gemini-api-key"
        });
        var pgServices = new ServiceCollection()
            .AddRoomieSyncData(pgConfig)
            .AddRoomieSyncServices(pgConfig);
        using var pgProvider = pgServices.BuildServiceProvider();
        var factory = pgProvider.GetRequiredService<RoomieSync.Data.Postgres.IDbConnectionFactory>();
        var parser = pgProvider.GetRequiredService<IReceiptParser>();

        var emptyCsErr = CaptureSyncExceptionType(() => _ = new RoomieSync.Data.Postgres.NpgsqlConnectionFactory("   "));
        bool npgsqlCreated;
        using (var npgsqlFactory = new RoomieSync.Data.Postgres.NpgsqlConnectionFactory("Host=localhost;Database=test;Username=u;Password=p"))
        {
            npgsqlCreated = npgsqlFactory is not null;
        }

        int paramCount;
        object? p1Val;
        object? p2Val;
        using (var conn = new Npgsql.NpgsqlConnection())
        using (var cmd = RoomieSync.Data.Postgres.DbExtensions.Cmd(conn, "SELECT @p1, @p2"))
        {
            RoomieSync.Data.Postgres.DbExtensions.P(cmd, "p1", "val");
            RoomieSync.Data.Postgres.DbExtensions.P(cmd, "p2", null);
            paramCount = cmd.Parameters.Count;
            p1Val = cmd.Parameters["p1"].Value;
            p2Val = cmd.Parameters["p2"].Value;
        }

        var env = BuildEnvironment(seedDemo: true);
        await env.Provider.InitializeRoomieSyncDataAsync();
        await env.Provider.GetRequiredService<DemoDataSeeder>().SeedAsync();

        var hhRepo = env.Provider.GetRequiredService<IHouseholdRepository>();
        var memRepo = env.Provider.GetRequiredService<IMemberRepository>();
        var accRepo = env.Provider.GetRequiredService<IUserAccountRepository>();
        var expRepo = env.Provider.GetRequiredService<IExpenseRepository>();
        var choreRepo = env.Provider.GetRequiredService<IChoreRepository>();
        var notifRepo = env.Provider.GetRequiredService<INotificationRepository>();
        var repRepo = env.Provider.GetRequiredService<IReportRepository>();

        var allHouseholds = await hhRepo.GetAllAsync();
        await hhRepo.UpdateAsync(new Household { Id = Guid.NewGuid(), Name = "Ghost" });
        await memRepo.UpdateAsync(new Member { Id = Guid.NewGuid(), DisplayName = "Ghost" });
        await accRepo.UpdateAsync(new UserAccount { MemberId = Guid.NewGuid(), Email = "ghost@x.com" });
        await expRepo.UpdateAsync(new Expense { Id = Guid.NewGuid(), Title = "Ghost" });
        await choreRepo.UpdateAsync(new Chore { Id = Guid.NewGuid(), Title = "Ghost" });

        var emptyHid = Guid.NewGuid();
        var emptyCat = await repRepo.GetCategorySpendingAsync(emptyHid);
        var emptyMonth = await repRepo.GetMonthlySpendingAsync(emptyHid, DateTime.Today.AddMonths(PreviousMonthOffset), DateTime.Today);
        var emptyMem = await repRepo.GetMemberSpendingAsync(emptyHid);

        var firstMember = (await memRepo.GetByHouseholdAsync(allHouseholds[0].Id, includeInactive: true)).First();
        await notifRepo.AddRangeAsync(
        [
            new Notification { HouseholdId = allHouseholds[0].Id, RecipientMemberId = firstMember.Id, Message = "Test" }
        ]);
        await notifRepo.MarkAllReadAsync(firstMember.Id);
        var readNotes = await notifRepo.GetForMemberAsync(firstMember.Id);

        Assert.True(
            factory is not null
            && parser is GeminiReceiptParser
            && emptyCsErr == typeof(InvalidOperationException)
            && npgsqlCreated
            && paramCount == MinHouseholdSize && Equals(p1Val, "val") && Equals(p2Val, DBNull.Value)
            && allHouseholds.Count > 0
            && emptyCat.Count == 0 && emptyMonth.Count == 0 && emptyMem.Count == 0
            && readNotes.All(n => n.IsRead));
    }

    [Fact]
    public async Task Coverage_Services_Auth_Session_And_Household()
    {
        var env = BuildEnvironment();

        var defaultAuth = new RoomieSync.Services.Auth.AuthService(
            env.AccountRepo,
            env.Provider.GetRequiredService<IMemberRepository>(),
            env.Provider.GetRequiredService<IHouseholdRepository>());
        var emptyLogin = await defaultAuth.LoginAsync("", "");
        var unknownLogin = await defaultAuth.LoginAsync("nobody@lnu.edu.ua", "pass123");

        int changeCount = 0;
        env.Session.Changed += (_, _) => changeCount++;
        var (h1, admin1) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home 1", "Solomiia", "solomiia@lnu.edu.ua", "Secret123", ["Uliana", "Nazar"], "  "));

        env.Session.SignIn(admin1, h1);
        var authed = env.Session.IsAuthenticated;
        env.Session.UpdateHousehold(h1);
        env.Session.UpdateMember(new Member { Id = Guid.NewGuid(), DisplayName = "Other" });
        admin1.DisplayName = "Solomiia U.";
        env.Session.UpdateMember(admin1);
        var updatedName = env.Session.CurrentMember!.DisplayName;
        env.Session.SignOut();

        var e1 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("   ", "A", "a@b.com", "Secret123", ["B"])));
        var e2 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("Home 1", "A", "a@b.com", "Secret123", ["B"])));
        var e3 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("Home MinHouseholdSize", "   ", "a@b.com", "Secret123", ["B"])));
        var e4 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("Home MinHouseholdSize", "A", "bad", "Secret123", ["B"])));
        var e5 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("Home MinHouseholdSize", "A", "a@b.com", "123", ["B"])));
        var e6 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("Home MinHouseholdSize", "Same", "a@b.com", "Secret123", ["same"])));

        var (h2, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home MinHouseholdSize", "Solomiia", "solomiia@lnu.edu.ua", "Secret123", ["Anna"], "EUR"));
        var r1 = await CaptureExceptionTypeAsync(() => env.Households.RenameAsync(h1.Id, "   "));
        var r2 = await CaptureExceptionTypeAsync(() => env.Households.RenameAsync(h1.Id, "Home MinHouseholdSize"));
        var r3 = await CaptureExceptionTypeAsync(() => env.Households.RenameAsync(Guid.NewGuid(), "Valid Name"));
        var renamedH1 = await env.Households.RenameAsync(h1.Id, "Home 1 Renamed");

        var c1 = await CaptureExceptionTypeAsync(() => env.Households.ChangeCurrencyAsync(Guid.NewGuid(), "USD"));
        var currDefault = await env.Households.ChangeCurrencyAsync(h1.Id, "  ");
        var currPln = await env.Households.ChangeCurrencyAsync(h1.Id, "pln");

        var h1Members = await env.Households.GetMembersAsync(h1.Id);
        var uliana = h1Members.Single(m => m.DisplayName == "Uliana");
        var sc1 = await CaptureExceptionTypeAsync(() => env.Auth.SetCredentialsAsync(uliana.Id, "invalid", "Secret123"));
        var sc2 = await CaptureExceptionTypeAsync(() => env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "123"));
        var scEmptyPass = await CaptureExceptionTypeAsync(() => env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", ""));
        var sc3 = await CaptureExceptionTypeAsync(() => env.Auth.SetCredentialsAsync(Guid.NewGuid(), "uliana@lnu.edu.ua", "Secret123"));
        var sc4 = await CaptureExceptionTypeAsync(() => env.Auth.SetCredentialsAsync(uliana.Id, "solomiia@lnu.edu.ua", "Secret123"));

        await env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "Pass111");
        await env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "Pass222");
        var ulianaEmail = await env.Auth.GetEmailAsync(uliana.Id);
        var missingEmail = await env.Auth.GetEmailAsync(Guid.NewGuid());

        await env.Auth.ConfirmMembershipAsync(uliana.Id);
        await env.Auth.ConfirmMembershipAsync(Guid.NewGuid());

        var userHouseholds = await env.Auth.GetUserHouseholdsAsync(admin1.Id);
        var nazar = h1Members.Single(m => m.DisplayName == "Nazar");
        var nazarHouseholds = await env.Auth.GetUserHouseholdsAsync(nazar.Id);

        await env.Auth.LoginAsync("uliana@lnu.edu.ua", "wrong");
        var okLogin = await env.Auth.LoginAsync("uliana@lnu.edu.ua", "Pass222");

        var am1 = await CaptureExceptionTypeAsync(() => env.Households.AddMemberAsync(h1.Id, "  "));
        var am2 = await CaptureExceptionTypeAsync(() => env.Households.AddMemberAsync(h1.Id, "Uliana"));
        var m4 = await env.Households.AddMemberAsync(h1.Id, "M4");
        await env.Households.AddMemberAsync(h1.Id, "M5");
        await env.Households.AddMemberAsync(h1.Id, "M6");
        var amMax = await CaptureExceptionTypeAsync(() => env.Households.AddMemberAsync(h1.Id, "M7"));

        var rm1 = await CaptureExceptionTypeAsync(() => env.Households.RenameMemberAsync(m4.Id, "  "));
        var rm2 = await CaptureExceptionTypeAsync(() => env.Households.RenameMemberAsync(Guid.NewGuid(), "Valid"));
        var rm3 = await CaptureExceptionTypeAsync(() => env.Households.RenameMemberAsync(m4.Id, "Uliana"));
        var renamedM4 = await env.Households.RenameMemberAsync(m4.Id, "M4 Updated");

        var canDelMissing = await env.Households.CanDeleteMemberAsync(Guid.NewGuid());
        var canDelAdmin = await env.Households.CanDeleteMemberAsync(admin1.Id);

        var h2Members = await env.Households.GetMembersAsync(h2.Id);
        var anna = h2Members.Single(m => m.DisplayName == "Anna");
        var canDelAnna = await env.Households.CanDeleteMemberAsync(anna.Id);
        var delMinErr = await CaptureExceptionTypeAsync(() => env.Households.DeleteMemberAsync(anna.Id));

        Assert.True(
            emptyLogin.Status == LoginStatus.InvalidCredentials
            && unknownLogin.Status == LoginStatus.InvalidCredentials
            && h1.Currency == "UAH" && authed && updatedName == "Solomiia U." && !env.Session.IsAuthenticated && changeCount >= MinValidationErrorsCount
            && e1 == typeof(DomainException) && e2 == typeof(DomainException) && e3 == typeof(DomainException)
            && e4 == typeof(DomainException) && e5 == typeof(DomainException) && e6 == typeof(DomainException)
            && r1 == typeof(DomainException) && r2 == typeof(DomainException) && r3 == typeof(DomainException) && renamedH1.Name == "Home 1 Renamed"
            && c1 == typeof(DomainException) && currDefault.Currency == "UAH" && currPln.Currency == "PLN"
            && sc1 == typeof(DomainException) && sc2 == typeof(DomainException) && scEmptyPass == typeof(DomainException)
            && sc3 == typeof(DomainException) && sc4 == typeof(DomainException)
            && ulianaEmail == "uliana@lnu.edu.ua" && missingEmail is null
            && userHouseholds.Count == MinHouseholdSize && nazarHouseholds.Count == 0
            && okLogin.Status == LoginStatus.Success
            && am1 == typeof(DomainException) && am2 == typeof(DomainException) && amMax == typeof(DomainException)
            && rm1 == typeof(DomainException) && rm2 == typeof(DomainException) && rm3 == typeof(DomainException) && renamedM4.DisplayName == "M4 Updated"
            && !canDelMissing && !canDelAdmin && canDelAnna && delMinErr == typeof(DomainException));
    }

    [Fact]
    public async Task Coverage_Services_TwoStepSettlement_DeleteApproval_Chores_And_Notifications()
    {
        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Workflow Home", "Admin", "adm@lnu.edu.ua", "Secret123", ["Bob", "Cara"]));
        var members = await env.Households.GetMembersAsync(h.Id);
        var bob = members.Single(m => m.DisplayName == "Bob");
        var cara = members.Single(m => m.DisplayName == "Cara");

        var emptySplit = RoomieSync.Services.Expenses.SplitCalculator.Calculate(RemainderSplitTotal, SplitMethod.Equally, Array.Empty<ShareInput>());
        var non100Pct = RoomieSync.Services.Expenses.SplitCalculator.Calculate(RemainderSplitTotal, SplitMethod.ByPercentage,
            [new ShareInput(admin.Id, Non100PctShare), new ShareInput(bob.Id, Non100PctShare)]);
        var badMethodErr = CaptureSyncExceptionType(() =>
            RoomieSync.Services.Expenses.SplitCalculator.Calculate(RemainderSplitTotal, (SplitMethod)InvalidEnumValue, [new ShareInput(admin.Id, EqualShareWeight)]));

        var longTitle = new string('X', OverMaxTitleLength);
        var valErrs = env.Expenses.Validate(new NewExpenseRequest(
            longTitle, LargeFormattedAmount, env.Clock.Now.AddDays(FutureReceiptOffsetDays), ExpenseCategory.Other, Guid.Empty, SplitMethod.Equally,
            [new ShareInput(admin.Id, InvalidNegativeAmount)]));

        var exactMismatch = env.Expenses.Validate(new NewExpenseRequest(
            "Exact", DirectDebtAmount, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.ByExactAmount,
            [new ShareInput(admin.Id, SmallExpenseAmount), new ShareInput(bob.Id, SmallExpenseAmount)]));

        var validPct = env.Expenses.Validate(new NewExpenseRequest(
            "ValidPct", RemainderSplitTotal, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.ByPercentage,
            [new ShareInput(admin.Id, PrimaryPercentageShare), new ShareInput(bob.Id, PrimaryPercentageShare)]));

        var addBadErr = await CaptureExceptionTypeAsync(() => env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "", ChainDebtAmount, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, Array.Empty<ShareInput>())));
        var updMissingErr = await CaptureExceptionTypeAsync(() => env.Expenses.UpdateExpenseAsync(Guid.NewGuid(), new NewExpenseRequest(
            "Valid", ChainDebtAmount, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, [new ShareInput(admin.Id, EqualShareWeight)])));

        var exp = await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Shared Dinner", RemainderSplitTotal, env.Clock.Now, ExpenseCategory.Groceries, admin.Id, SplitMethod.Equally,
            [new ShareInput(admin.Id, EqualShareWeight), new ShareInput(bob.Id, EqualShareWeight)]));
        var fetchedExp = await env.Expenses.GetExpenseAsync(exp.Id);
        var updBadErr = await CaptureExceptionTypeAsync(() => env.Expenses.UpdateExpenseAsync(exp.Id, new NewExpenseRequest(
            "", ChainDebtAmount, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, Array.Empty<ShareInput>())));

        var delMissingExp = await CaptureExceptionTypeAsync(() => env.Expenses.RequestOrApproveDeleteAsync(Guid.NewGuid(), admin.Id));
        var delMissingActor = await CaptureExceptionTypeAsync(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, Guid.NewGuid()));
        var delBobInit = await CaptureExceptionTypeAsync(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, bob.Id));
        var delCaraInit = await CaptureExceptionTypeAsync(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, cara.Id));

        var step1 = await env.Expenses.RequestOrApproveDeleteAsync(exp.Id, admin.Id);
        var delCaraVote = await CaptureExceptionTypeAsync(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, cara.Id));
        var step2 = await env.Expenses.RequestOrApproveDeleteAsync(exp.Id, bob.Id);
        var afterDel = await env.Expenses.GetExpenseAsync(exp.Id);

        var expDirect = await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Direct Delete", DinnerExpenseAmount, env.Clock.Now, ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            [new ShareInput(admin.Id, EqualShareWeight), new ShareInput(bob.Id, EqualShareWeight)]));
        await env.Expenses.DeleteExpenseAsync(expDirect.Id);

        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Rent Split", GroceriesSampleAmount, env.Clock.Now, ExpenseCategory.Rent, admin.Id, SplitMethod.Equally,
            [new ShareInput(admin.Id, EqualShareWeight), new ShareInput(bob.Id, EqualShareWeight)]));

        var transfer = new Transfer(bob.Id, admin.Id, RemainderSplitTotal);
        var setZeroErr = await CaptureExceptionTypeAsync(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, 0m), bob.Id));
        var setFromErr = await CaptureExceptionTypeAsync(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), admin.Id, ChainDebtAmount), bob.Id));
        var setToErr = await CaptureExceptionTypeAsync(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(bob.Id, Guid.NewGuid(), ChainDebtAmount), bob.Id));
        var setCaraErr = await CaptureExceptionTypeAsync(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, cara.Id));

        var pendingBefore = env.Balances.HasPendingConfirmation(h.Id, transfer, out _);
        var initRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, bob.Id);
        var pendingAfter = env.Balances.HasPendingConfirmation(h.Id, transfer, out var confirmer);
        var repeatRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, bob.Id);
        var confirmRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        var settlements = await env.Balances.GetSettlementsAsync(h.Id);

        var recNegErr = await CaptureExceptionTypeAsync(() => env.Balances.RecordSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, InvalidNegativeAmount)));
        var reqZeroErr = await CaptureExceptionTypeAsync(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, 0m)));
        var reqFromErr = await CaptureExceptionTypeAsync(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), admin.Id, ChainDebtAmount)));
        var reqToErr = await CaptureExceptionTypeAsync(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(bob.Id, Guid.NewGuid(), ChainDebtAmount)));

        var choreEmptyErr = await CaptureExceptionTypeAsync(() => env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("  ", null, null, ChoreRotation.None)));
        var choreDaily = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Take out trash", bob.Id, env.Clock.Now.AddDays(OverdueMediumOffsetDays), ChoreRotation.Daily));
        var choreMonthly = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Pay bills", null, env.Clock.Now.AddMonths(TwoMonthsAgoOffset), ChoreRotation.Monthly));

        var updChoreErr = await CaptureExceptionTypeAsync(() => env.Chores.UpdateChoreAsync(new Chore { Id = choreDaily.Id, Title = " " }));
        choreDaily.Title = "Take out trash daily";
        await env.Chores.UpdateChoreAsync(choreDaily);

        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        var bobNotes = await env.Notifications.GetNotificationsAsync(bob.Id);

        var missingChoreErr = await CaptureExceptionTypeAsync(() => env.Chores.ChangeStatusAsync(Guid.NewGuid(), ChoreStatus.Done));
        var assignMissingErr = await CaptureExceptionTypeAsync(() => env.Chores.AssignAsync(Guid.NewGuid(), bob.Id));
        await env.Chores.AssignAsync(choreDaily.Id, cara.Id);
        var sameStatus = await env.Chores.ChangeStatusAsync(choreDaily.Id, ChoreStatus.ToDo);
        var nextDaily = await env.Chores.ChangeStatusAsync(choreDaily.Id, ChoreStatus.Done);
        var reopenDaily = await env.Chores.ChangeStatusAsync(choreDaily.Id, ChoreStatus.InProgress);
        var nextMonthly = await env.Chores.ChangeStatusAsync(choreMonthly.Id, ChoreStatus.Done);
        await env.Chores.DeleteChoreAsync(nextMonthly!.Id);
        var noAssignee = env.Chores.GetNextAssignee(Array.Empty<Member>(), null);

        Assert.True(
            emptySplit.Count == 0 && non100Pct.Sum(s => s.Amount) == ExpectedNon100PctSum && badMethodErr == typeof(ArgumentOutOfRangeException)
            && !valErrs.IsValid && valErrs.Errors.Count >= MinValidationErrorsCount && !exactMismatch.IsValid && validPct.IsValid
            && addBadErr == typeof(DomainException) && updMissingErr == typeof(DomainException)
            && fetchedExp is not null && updBadErr == typeof(DomainException)
            && delMissingExp == typeof(DomainException) && delMissingActor == typeof(DomainException)
            && delBobInit == typeof(DomainException) && delCaraInit == typeof(DomainException)
            && !step1.Deleted && step1.ApprovedCount == 1 && step1.RequiredCount == MinHouseholdSize
            && delCaraVote == typeof(DomainException) && step2.Deleted && afterDel is null
            && setZeroErr == typeof(DomainException) && setFromErr == typeof(DomainException)
            && setToErr == typeof(DomainException) && setCaraErr == typeof(DomainException)
            && !pendingBefore && !initRes.Settled && pendingAfter && confirmer == bob.Id && !repeatRes.Settled && confirmRes.Settled && settlements.Count > 0
            && recNegErr == typeof(DomainException) && reqZeroErr == typeof(DomainException) && reqFromErr == typeof(DomainException) && reqToErr == typeof(DomainException)
            && choreEmptyErr == typeof(DomainException) && updChoreErr == typeof(DomainException)
            && bobNotes.Any(n => n.Type == NotificationType.OverdueChore)
            && missingChoreErr == typeof(DomainException) && assignMissingErr == typeof(DomainException)
            && sameStatus is null && nextDaily is not null && reopenDaily is null && nextMonthly is not null && noAssignee is null);
    }

    [Fact]
    public async Task Coverage_Receipts_Validation_Backup_Storage_And_Pdf_EdgeCases()
    {
        var env = BuildEnvironment();

        var defOpts = new GeminiOptions();
        var noKeyParser = new GeminiReceiptParser(new HttpClient(), new GeminiOptions { ApiKey = null });
        var noKeyErr = await CaptureExceptionTypeAsync(() => noKeyParser.ParseReceiptAsync(DummyImageBytes, "image/png"));

        using var badHttpClient = new HttpClient(new FakeHttpMessageHandler((_, _) => throw new HttpRequestException("Network down")));
        var badHttpParser = new GeminiReceiptParser(badHttpClient, new GeminiOptions { ApiKey = "k" });
        var badHttpErr = await CaptureExceptionTypeAsync(() => badHttpParser.ParseReceiptAsync(DummyImageBytes, "image/png"));

        using var statusErrClient = new HttpClient(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest))));
        var statusErrParser = new GeminiReceiptParser(statusErrClient, new GeminiOptions { ApiKey = "k" });
        var statusErr = await CaptureExceptionTypeAsync(() => statusErrParser.ParseReceiptAsync(DummyImageBytes, "image/png"));

        var mdJson = "{\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"{\\\"merchant\\\":\\\"Silpo\\\",\\\"total\\\":50,\\\"items\\\":[]}\"}]}}]}";
        using var mdClient = new HttpClient(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mdJson, Encoding.UTF8, "application/json")
        })));
        var mdParser = new GeminiReceiptParser(mdClient, new GeminiOptions { ApiKey = "k" });
        var mdParsed = await mdParser.ParseReceiptAsync(DummyImageBytes, "image/png");

        var fastMock = new MockReceiptParser(new GeminiOptions { TimeoutSeconds = 0 }, env.Clock);
        ReceiptParseException? slowEx = null;
        try
        {
            await fastMock.ParseReceiptAsync(DummyImageBytes, "image/png", "slow_receipt.png");
        }
        catch (ReceiptParseException ex)
        {
            slowEx = ex;
        }

        var emptyReceipt = new ParsedReceipt { Total = 0m, Merchant = "", Date = null };
        var emptyWarn = env.Validator.ValidateReceipt(emptyReceipt);

        var futureReceipt = new ParsedReceipt
        {
            Total = ChainDebtAmount,
            Merchant = "Shop",
            Date = env.Clock.Now.AddDays(FutureReceiptOffsetDays),
            Items = { new ReceiptLineItem { Name = "BadItem", Price = InvalidNegativeAmount } }
        };
        var futureWarn = env.Validator.ValidateReceipt(futureReceipt);

        var oldReceipt = new ParsedReceipt
        {
            Total = ChainDebtAmount,
            Merchant = "Shop",
            Date = env.Clock.Now.AddYears(TwoMonthsAgoOffset)
        };
        var oldWarn = env.Validator.ValidateReceipt(oldReceipt);

        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("BackupFullHome", "Solomiia", "s@lnu.edu.ua", "Secret123", ["Uliana"]));
        var members = await env.Households.GetMembersAsync(h.Id);
        var uliana = members.Single(m => m.DisplayName == "Uliana");
        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest("Tea", SmallExpenseAmount, env.Clock.Now, ExpenseCategory.Groceries, admin.Id, SplitMethod.Equally,
            [new ShareInput(admin.Id, EqualShareWeight), new ShareInput(uliana.Id, EqualShareWeight)]));
        await env.Balances.RecordSettlementAsync(h.Id, new Transfer(uliana.Id, admin.Id, ChainDebtAmount));
        await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Clean", uliana.Id, env.Clock.Now.AddDays(TomorrowOffsetDays), ChoreRotation.None));

        var backupMissingHhErr = await CaptureExceptionTypeAsync(() => env.Backup.ExportBackupAsync(Guid.NewGuid(), "unused.json"));
        var backupMissingFileErr = await CaptureExceptionTypeAsync(() => env.Backup.RestoreBackupAsync(Path.Combine(Path.GetTempPath(), "nonexistent_" + Guid.NewGuid() + ".json")));

        var backupPath = Path.Combine(Path.GetTempPath(), $"full_backup_{Guid.NewGuid():N}.json");
        var badBackupPath = Path.Combine(Path.GetTempPath(), $"bad_backup_{Guid.NewGuid():N}.json");
        try
        {
            await env.Backup.ExportBackupAsync(h.Id, backupPath);

            var cleanEnv = BuildEnvironment();
            var restored1 = await cleanEnv.Backup.RestoreBackupAsync(backupPath);
            var restored2 = await cleanEnv.Backup.RestoreBackupAsync(backupPath);

            await File.WriteAllTextAsync(badBackupPath, "null");
            var badRestoreErr = await CaptureExceptionTypeAsync(() => cleanEnv.Backup.RestoreBackupAsync(badBackupPath));

            var tempStorageDir = Path.Combine(Path.GetTempPath(), $"storage_{Guid.NewGuid():N}");
            var tinyStorage = new ReceiptStorageService(tempStorageDir, maxStorageBytes: TinyStorageLimitBytes);
            Directory.Delete(tempStorageDir, true);
            var usageWhenDeleted = tinyStorage.GetCurrentStorageUsageBytes();

            Directory.CreateDirectory(tempStorageDir);
            var bigImg = Path.Combine(tempStorageDir, "big.png");
            await File.WriteAllBytesAsync(bigImg, JpegHeaderBytes);
            var limitErr = await CaptureExceptionTypeAsync(() => tinyStorage.SaveReceiptImageAsync(bigImg));
            if (Directory.Exists(tempStorageDir)) Directory.Delete(tempStorageDir, true);

            var pdfPath = Path.Combine(Path.GetTempPath(), $"special_{Guid.NewGuid():N}.pdf");
            var specialRows = new[]
            {
                new ReportRow(DateTime.Today, @"Test (1) \ ₴ € £ zł — – · … ©", "Ґудзики Єдиноріг Їжак Іній", "Олексій", ChainDebtAmount, ExportRowYourShare)
            };
            await env.Export.ExportExpensesPdfAsync(pdfPath, "Звіт (Спецсимволи)", specialRows);
            var pdfExists = File.Exists(pdfPath);
            if (pdfExists) File.Delete(pdfPath);

            Assert.True(
                defOpts.Model == "gemini-2.5-flash" && !defOpts.UseMock
                && noKeyErr == typeof(ReceiptParseException) && badHttpErr == typeof(ReceiptParseException) && statusErr == typeof(ReceiptParseException)
                && mdParsed.Merchant == "Silpo"
                && slowEx?.IsTimeout == true
                && emptyWarn.HasWarnings && emptyWarn.Warnings.Count >= InitialHouseholdSize
                && futureWarn.HasWarnings && futureWarn.Warnings.Count == MinHouseholdSize && oldWarn.HasWarnings
                && backupMissingHhErr == typeof(DomainException) && backupMissingFileErr == typeof(DomainException)
                && restored1.Household.Id == h.Id && restored2.Expenses.Count == 1 && badRestoreErr == typeof(DomainException)
                && usageWhenDeleted == 0L && limitErr == typeof(DomainException)
                && pdfExists);
        }
        finally
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            if (File.Exists(badBackupPath)) File.Delete(badBackupPath);
        }
    }

    [Fact]
    public async Task Coverage_AllRemainingBranches_100Percent()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var defaultServices = new ServiceCollection()
            .AddRoomieSyncData(emptyConfig)
            .AddRoomieSyncServices(emptyConfig);
        using var defaultProvider = defaultServices.BuildServiceProvider();
        var dataOpts = defaultProvider.GetRequiredService<DataOptions>();

        var pgNoCsConfig = new TestConfiguration(new Dictionary<string, string?>
        {
            ["Data:Provider"] = "Postgres",
            ["Data:SeedDemoData"] = "false"
        });
        using var pgNoCsProvider = new ServiceCollection().AddRoomieSyncData(pgNoCsConfig).BuildServiceProvider();
        var pgInitErr = await CaptureExceptionTypeAsync(() => pgNoCsProvider.InitializeRoomieSyncDataAsync());

        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("BranchHome", "Admin", "branch@lnu.edu.ua", "Secret123", ["Peer", "Third"]));
        var members = await env.Households.GetMembersAsync(h.Id);
        var peer = members.Single(m => m.DisplayName == "Peer");
        var third = members.Single(m => m.DisplayName == "Third");

        var singleAcc = await env.AccountRepo.GetByEmailAsync("branch@lnu.edu.ua");
        var stubAccRepo = new SingleOnlyAccountRepo(singleAcc!);
        var fallbackAuth = new RoomieSync.Services.Auth.AuthService(
            stubAccRepo,
            env.Provider.GetRequiredService<IMemberRepository>(),
            env.Provider.GetRequiredService<IHouseholdRepository>(),
            new Pbkdf2PasswordHasher(),
            env.Clock);
        var fallbackLogin = await fallbackAuth.LoginAsync("branch@lnu.edu.ua", "Secret123");
        var nullEmailLogin = await fallbackAuth.LoginAsync(null!, "Secret123");
        var nullSetCredErr = await CaptureExceptionTypeAsync(() => fallbackAuth.SetCredentialsAsync(admin.Id, null!, "Secret123"));

        var memberRepo = env.Provider.GetRequiredService<IMemberRepository>();
        var hasher = new Pbkdf2PasswordHasher();
        var (peerHash, peerSalt) = hasher.Hash("Secret123");

        await env.AccountRepo.AddAsync(new UserAccount
        {
            MemberId = Guid.NewGuid(),
            Email = "peer@lnu.edu.ua",
            PasswordHash = peerHash,
            PasswordSalt = peerSalt
        });
        var orphanMember = new Member { Id = Guid.NewGuid(), HouseholdId = Guid.NewGuid(), DisplayName = "Orphan", IsActive = true };
        await memberRepo.AddAsync(orphanMember);
        await env.AccountRepo.AddAsync(new UserAccount
        {
            MemberId = orphanMember.Id,
            Email = "peer@lnu.edu.ua",
            PasswordHash = peerHash,
            PasswordSalt = peerSalt
        });

        await env.Auth.SetCredentialsAsync(peer.Id, "peer@lnu.edu.ua", "Secret123");
        peer.IsActive = false;
        await memberRepo.UpdateAsync(peer);
        var inactiveLogin = await env.Auth.LoginAsync("peer@lnu.edu.ua", "Secret123");
        var canDelInactive = await env.Households.CanDeleteMemberAsync(peer.Id);
        await env.Auth.SetCredentialsAsync(third.Id, "peer@lnu.edu.ua", "Secret123");
        var peerHouseholds = await env.Auth.GetUserHouseholdsAsync(third.Id);
        peer.IsActive = true;
        await memberRepo.UpdateAsync(peer);

        await env.ChoreRepo.AddAsync(new Chore { HouseholdId = h.Id, Title = "UnassignedChore", AssigneeId = null, Status = ChoreStatus.ToDo });
        await env.ChoreRepo.AddAsync(new Chore { HouseholdId = h.Id, Title = "AdminChore", AssigneeId = admin.Id, Status = ChoreStatus.ToDo });
        var canDelPeerWithOtherChore = await env.Households.CanDeleteMemberAsync(peer.Id);

        var ghost = new Member { Id = Guid.NewGuid(), HouseholdId = h.Id, DisplayName = "Ghost", IsActive = true };
        var vanishingRepo = new VanishingMemberRepo(ghost);
        var vanishingHhSvc = new RoomieSync.Services.Households.HouseholdService(
            env.Provider.GetRequiredService<IHouseholdRepository>(),
            vanishingRepo,
            env.ChoreRepo,
            env.Auth,
            env.Balances,
            env.Clock);
        var vanishErr = await CaptureExceptionTypeAsync(() => vanishingHhSvc.DeleteMemberAsync(ghost.Id));

        var rawSession = new RoomieSync.Services.Auth.SessionContext();
        rawSession.UpdateMember(admin);
        rawSession.UpdateHousehold(h);
        rawSession.SignIn(admin, h);
        rawSession.UpdateMember(admin);
        rawSession.SignOut();

        var nh1 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest(null!, "A", "a@b.com", "Secret123", ["B"])));
        var nh2 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("H_NullAdmin", null!, "a@b.com", "Secret123", ["B"])));
        var nh3 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("H_NullEmail", "A", null!, "Secret123", ["B"])));
        var nh4 = await CaptureExceptionTypeAsync(() => env.Households.CreateHouseholdAsync(new NewHouseholdRequest("H_NullPass", "A", "a@b.com", null!, ["B"])));

        var transfer = new Transfer(peer.Id, admin.Id, SettlementTransferAmount);
        var credInit = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        var credRepeat = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        var debtConfirm = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, peer.Id);
        await env.Balances.RecordSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), Guid.NewGuid(), ChainDebtAmount));

        var nullChoreErr = await CaptureExceptionTypeAsync(() => env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest(null!, null, null, ChoreRotation.None)));
        var noDeadlineRotating = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("NoDeadlineTask", peer.Id, null, ChoreRotation.Weekly));
        await env.Chores.ChangeStatusAsync(noDeadlineRotating.Id, ChoreStatus.InProgress);
        var rotatedFromNoDeadline = await env.Chores.ChangeStatusAsync(noDeadlineRotating.Id, ChoreStatus.Done);
        var nonRotatingDeadline = ((RoomieSync.Services.Chores.ChoreService)env.Chores).NextDeadline(env.Clock.Now, ChoreRotation.None);

        var valNullTitle = env.Expenses.Validate(new NewExpenseRequest(null!, ChainDebtAmount, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, [new ShareInput(admin.Id, EqualShareWeight)]));
        var valExactOk = env.Expenses.Validate(new NewExpenseRequest("ExactOk", ExactSplitTotal, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.ByExactAmount, [new ShareInput(admin.Id, ChainDebtAmount), new ShareInput(peer.Id, SmallExpenseAmount)]));
        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest("UnknownPayer", SmallExpenseAmount, env.Clock.Now, ExpenseCategory.Other, Guid.NewGuid(), SplitMethod.Equally, [new ShareInput(admin.Id, EqualShareWeight)]));

        var fromDate = env.Clock.Now.AddDays(OverdueMediumOffsetDays);
        var toDate = env.Clock.Now.AddDays((-TomorrowOffsetDays));
        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = h.Id, Title = "BeforeRange", Amount = ChainDebtAmount, Date = env.Clock.Now.AddDays(OverdueLongOffsetDays), Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });
        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = h.Id, Title = "InsideRange", Amount = ChainDebtAmount, Date = env.Clock.Now.AddDays(OverdueShortOffsetDays), Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });
        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = h.Id, Title = "AfterRange", Amount = ChainDebtAmount, Date = env.Clock.Now.AddDays(FutureReceiptOffsetDays), Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });

        var repRepo = env.Provider.GetRequiredService<IReportRepository>();
        _ = await env.ExpenseRepo.GetByHouseholdAsync(h.Id, fromDate, toDate);
        _ = await repRepo.GetCategorySpendingAsync(h.Id, fromDate, toDate);
        _ = await repRepo.GetMemberSpendingAsync(h.Id, fromDate, toDate);
        _ = await repRepo.GetCategorySpendingAsync(h.Id);
        _ = await repRepo.GetMemberSpendingAsync(h.Id);

        var zeroOnlyHid = Guid.NewGuid();
        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = zeroOnlyHid, Title = "ZeroOnly", Amount = 0m, Date = env.Clock.Now, Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });
        var zeroCats = await repRepo.GetCategorySpendingAsync(zeroOnlyHid);
        var emptyReport = await env.Reports.GetPeriodSummaryAsync(Guid.NewGuid(), env.Clock.Now.AddMonths(PreviousMonthOffset), env.Clock.Now);

        await env.Notifications.MarkAllReadAsync(admin.Id);
        int notifEvents = 0;
        env.Notifications.NotificationsChanged += (_, _) => notifEvents++;
        await env.Notifications.CheckOverdueChoresAsync(Guid.NewGuid());
        await env.Notifications.NotifyHouseholdAsync(h.Id, NotificationType.NewExpense, "Msg");
        await env.Notifications.NotifyMemberAsync(h.Id, admin.Id, NotificationType.NewExpense, "Msg");
        await env.ChoreRepo.AddAsync(new Chore { HouseholdId = h.Id, Title = "OverdueUnassigned", AssigneeId = null, Deadline = env.Clock.Now.AddDays(OverdueShortOffsetDays), Status = ChoreStatus.ToDo });
        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        await env.Notifications.MarkAllReadAsync(admin.Id);

        var tempPdf1 = Path.Combine(Path.GetTempPath(), $"empty_plan_{Guid.NewGuid():N}.pdf");
        var tempPdf2 = Path.Combine(Path.GetTempPath(), $"long_row_{Guid.NewGuid():N}.pdf");
        var nullBackup = Path.Combine(Path.GetTempPath(), $"null_{Guid.NewGuid():N}.json");
        Type? nullBackupErr;
        try
        {
            await env.Export.ExportSettlementPlanPdfAsync(tempPdf1, "Empty Plan", Array.Empty<(string, string, decimal)>());
            await env.Export.ExportExpensesPdfAsync(tempPdf2, "Long Title ★",
            [
                new ReportRow(DateTime.Today, new string('W', LongRowTitleLength) + " ★", "Groceries", "Solomiia", DirectDebtAmount, SettlementTransferAmount)
            ]);
            await File.WriteAllTextAsync(nullBackup, "null");
            nullBackupErr = await CaptureExceptionTypeAsync(() => env.Backup.RestoreBackupAsync(nullBackup));
        }
        finally
        {
            if (File.Exists(tempPdf1)) File.Delete(tempPdf1);
            if (File.Exists(tempPdf2)) File.Delete(tempPdf2);
            if (File.Exists(nullBackup)) File.Delete(nullBackup);
        }

        var nullTextJson = "{\"candidates\": [{\"content\": {\"parts\": [{\"text\": null}]}}]}";
        using var nullTextClient = new HttpClient(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(nullTextJson, Encoding.UTF8, "application/json")
        })));
        var nullTextParser = new GeminiReceiptParser(nullTextClient, new GeminiOptions { ApiKey = "k" });
        var emptyParsed = await nullTextParser.ParseReceiptAsync(DummyImageBytes, "image/png");

        var weirdJson = GeminiReceiptParser.ParseJson("{\"merchant\":123,\"total\":\"NaN\",\"date\":456,\"category\":\"UnknownCat\",\"items\":[{\"name\":null},{\"price\":\"NaN\"}]}");

        var mockParser = new MockReceiptParser(new GeminiOptions { UseMock = true }, env.Clock);
        var mockNullFile = await mockParser.ParseReceiptAsync(DummyImageBytes, "image/png", null);

        var nullTotalWithItems = new ParsedReceipt { Total = null, Merchant = "M", Date = env.Clock.Now, Items = { new ReceiptLineItem { Name = "I", Price = ExportRowYourShare } } };
        var nullTotalValidation = env.Validator.ValidateReceipt(nullTotalWithItems);

        Assert.True(
            dataOpts is not null && pgInitErr is not null
            && fallbackLogin.Status == LoginStatus.Success && nullEmailLogin.Status == LoginStatus.InvalidCredentials && nullSetCredErr == typeof(DomainException)
            && inactiveLogin.Status == LoginStatus.InvalidCredentials && canDelInactive && peerHouseholds.Count == 1
            && canDelPeerWithOtherChore && vanishErr == typeof(DomainException)
            && nh1 == typeof(DomainException) && nh2 == typeof(DomainException) && nh3 == typeof(DomainException) && nh4 == typeof(DomainException)
            && !credInit.Settled && !credRepeat.Settled && debtConfirm.Settled
            && nullChoreErr == typeof(DomainException) && rotatedFromNoDeadline is not null && nonRotatingDeadline == env.Clock.Now
            && !valNullTitle.IsValid && valExactOk.IsValid
            && zeroCats.Single().Percentage == 0.0 && emptyReport.TopCategory is null
            && notifEvents >= MinValidationErrorsCount && nullBackupErr == typeof(DomainException)
            && emptyParsed.Merchant is null && weirdJson.Items.Count == MinHouseholdSize && weirdJson.Items[0].Price == 0m
            && mockNullFile is not null && nullTotalValidation.HasWarnings);
    }

    private sealed class VanishingMemberRepo(Member member) : IMemberRepository
    {
        private int _calls;
        public Task<IReadOnlyList<Member>> GetByHouseholdAsync(Guid householdId, bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Member>>([member]);
        public Task<Member?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_calls++ == 0 ? member : null);
        public Task AddAsync(Member m, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Member m, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SingleOnlyAccountRepo(UserAccount account) : IUserAccountRepository
    {
        public Task<UserAccount?> GetByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<UserAccount?>(string.Equals(email, account.Email, StringComparison.OrdinalIgnoreCase) ? account : null);
        public Task<IReadOnlyList<UserAccount>> GetAllByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<UserAccount>>(Array.Empty<UserAccount>());
        public Task<UserAccount?> GetByMemberIdAsync(Guid memberId, CancellationToken ct = default)
            => Task.FromResult<UserAccount?>(account.MemberId == memberId ? account : null);
        public Task AddAsync(UserAccount a, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(UserAccount a, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class TestConfiguration(Dictionary<string, string?> values) : IConfiguration
    {
        private readonly Dictionary<string, string?> _values = new(values, StringComparer.OrdinalIgnoreCase);

        public string? this[string key]
        {
            get => _values.TryGetValue(key, out var val) ? val : null;
            set => _values[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() =>
            _values.Keys.Select(k => k.Split(':')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Select(GetSection);

        public IChangeToken GetReloadToken() => NullChangeToken.Instance;
        public IConfigurationSection GetSection(string key) => new TestConfigurationSection(this, key, _values);
    }

    private sealed class TestConfigurationSection(TestConfiguration root, string path, Dictionary<string, string?> values) : IConfigurationSection
    {
        public string Key => path.Contains(':') ? path[(path.LastIndexOf(':') + 1)..] : path;
        public string Path => path;
        public string? Value { get => root[path]; set => root[path] = value; }
        public string? this[string key] { get => root[$"{path}:{key}"]; set => root[$"{path}:{key}"] = value; }

        public IEnumerable<IConfigurationSection> GetChildren()
        {
            var prefix = path + ":";
            return values.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k[prefix.Length..].Split(':')[0]).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(childKey => new TestConfigurationSection(root, $"{path}:{childKey}", values));
        }

        public IChangeToken GetReloadToken() => NullChangeToken.Instance;
        public IConfigurationSection GetSection(string key) => new TestConfigurationSection(root, $"{path}:{key}", values);
    }

    private sealed class NullChangeToken : IChangeToken
    {
        public static readonly NullChangeToken Instance = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
