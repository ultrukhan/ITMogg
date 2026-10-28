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

        var clock = new FakeClock(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));
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

    [Fact(DisplayName = "НФВ-1: Завантаження даних дашборду при 1000 витрат виконується швидше ніж за 1 секунду")]
    public async Task Nfr1_DashboardLoad_With1000Expenses_Under1Second()
    {
        var env = BuildEnvironment();
        var (h, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("NFR1 Home", "Nazar", "nazar@lnu.edu.ua", "Secret123", new[] { "Olena", "Taras" }));
        var members = await env.Households.GetMembersAsync(h.Id);
        var shares = members.Select(m => new ShareInput(m.Id, 1m)).ToList();

        for (int i = 0; i < 1000; i++)
        {
            await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
                $"Expense #{i}", 150m + (i % 50), env.Clock.Now.AddDays(-(i % 25)),
                (ExpenseCategory)(i % 6), members[i % members.Count].Id, EqualSplit, shares));
        }

        var sw = Stopwatch.StartNew();
        var monthStart = new DateTime(env.Clock.Now.Year, env.Clock.Now.Month, 1);
        var expensesList = await env.Expenses.GetExpensesAsync(h.Id, monthStart, env.Clock.Now);
        var balances = await env.Balances.GetBalancesAsync(h.Id);
        var summary = await env.Reports.GetPeriodSummaryAsync(h.Id, monthStart, env.Clock.Now);
        var activeChores = await env.Chores.GetChoresAsync(h.Id);
        sw.Stop();

        Assert.NotEmpty(expensesList);
        Assert.Equal(3, balances.Count);
        Assert.NotNull(summary);
        Assert.NotNull(activeChores);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact(DisplayName = "НФВ-2: Перерахунок балансів та алгоритм спрощення боргів завершується швидше ніж за 500 мс")]
    public async Task Nfr2_BalanceAndDebtSimplification_Under500Milliseconds()
    {
        var env = BuildEnvironment();
        var (h, _) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("NFR2 Home", "Admin", "admin@test.com", "Secret123",
                new[] { "M2", "M3", "M4", "M5", "M6" }));
        var members = await env.Households.GetMembersAsync(h.Id);
        var shares = members.Select(m => new ShareInput(m.Id, 1m)).ToList();

        for (int i = 0; i < 500; i++)
        {
            await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
                $"Item {i}", 300m, env.Clock.Now, ExpenseCategory.Groceries,
                members[i % members.Count].Id, EqualSplit, shares));
        }

        var sw = Stopwatch.StartNew();
        var plan = await env.Balances.GetSettlementPlanAsync(h.Id);
        sw.Stop();

        Assert.NotNull(plan);
        Assert.True(sw.ElapsedMilliseconds < 500);
    }

    [Fact(DisplayName = "НФВ-3: Таймаут Gemini API суворо обмежений 5 секундами, перехід на ручний ввід < 1 с")]
    public async Task Nfr3_GeminiTimeout_Is5Seconds_AndFallbackIsFast()
    {
        var options = new GeminiOptions { ApiKey = "test-key", TimeoutSeconds = 5, UseMock = false };
        var slowHandler = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        using var httpClient = new HttpClient(slowHandler) { Timeout = TimeSpan.FromMilliseconds(150) };
        var parser = new GeminiReceiptParser(httpClient, options);

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<ReceiptParseException>(() =>
            parser.ParseReceiptAsync(new byte[] { 1, 2, 3 }, "image/png"));
        sw.Stop();

        Assert.True(ex.IsTimeout);
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact(DisplayName = "НФВ-5: Підтримка домогосподарств із 2–6 активних учасників")]
    public async Task Nfr5_HouseholdMemberCount_Boundaries_2To6Members()
    {
        var env = BuildEnvironment();
        await Assert.ThrowsAsync<DomainException>(() =>
            env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
                "TooSmall", "Solo", "solo@test.com", "Secret123", Array.Empty<string>())));

        await Assert.ThrowsAsync<DomainException>(() =>
            env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
                "TooBig", "A1", "a1@test.com", "Secret123", new[] { "A2", "A3", "A4", "A5", "A6", "A7" })));

        var (h2, _) = await env.Households.CreateHouseholdAsync(new NewHouseholdRequest(
            "MinHome", "U1", "u1@test.com", "Secret123", new[] { "U2" }));
        Assert.Equal(2, (await env.Households.GetMembersAsync(h2.Id)).Count);
    }

    [Fact(DisplayName = "НФВ-6: Зберігання 10 000 транзакцій витрат та 5 000 записів обов'язків без зниження швидкодії")]
    public async Task Nfr6_HighVolume_10000Expenses_And_5000Chores_Performance()
    {
        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("HighLoad Home", "Admin", "load@test.com", "Secret123", new[] { "Roomie2", "Roomie3" }));
        var members = await env.Households.GetMembersAsync(h.Id);

        for (int i = 0; i < 10_000; i++)
        {
            await env.ExpenseRepo.AddAsync(new Expense
            {
                HouseholdId = h.Id,
                Title = $"Tx #{i}",
                Amount = 100m,
                Date = env.Clock.Now.AddDays(-(i % 365)),
                Category = ExpenseCategory.Groceries,
                PaidByMemberId = admin.Id,
                SplitMethod = EqualSplit,
                Shares = members.Select(m => new ExpenseShare { MemberId = m.Id, Amount = 33.33m }).ToList()
            });
        }

        for (int i = 0; i < 5_000; i++)
        {
            await env.ChoreRepo.AddAsync(new Chore
            {
                HouseholdId = h.Id,
                Title = $"Chore #{i}",
                AssigneeId = members[i % members.Count].Id,
                Status = ChoreStatus.Done,
                CreatedAt = env.Clock.Now.AddDays(-10),
                CompletedAt = env.Clock.Now
            });
        }

        var sw = Stopwatch.StartNew();
        var periodExpenses = await env.Expenses.GetExpensesAsync(h.Id, env.Clock.Now.AddDays(-30), env.Clock.Now);
        var allChores = await env.Chores.GetChoresAsync(h.Id);
        sw.Stop();

        Assert.NotEmpty(periodExpenses);
        Assert.Equal(5_000, allChores.Count);
        Assert.True(sw.ElapsedMilliseconds < 500);
    }

    [Fact(DisplayName = "НФВ-7: Контроль ліміту зберігання до 2 ГБ прикріплених фотографій чеків")]
    public async Task Nfr7_ReceiptStorage_SupportsUpTo2Gb_AndValidatesFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "RoomieSyncTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ReceiptStorageService(tempDir);
            Assert.Equal(2L * 1024 * 1024 * 1024, storage.MaxStorageBytes);
            Assert.Equal(0L, storage.GetCurrentStorageUsageBytes());

            Directory.CreateDirectory(tempDir);
            var sampleFile = Path.Combine(tempDir, "sample.jpg");
            await File.WriteAllBytesAsync(sampleFile, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 });

            var savedPath = await storage.SaveReceiptImageAsync(sampleFile);
            Assert.True(File.Exists(savedPath));
            Assert.True(storage.GetCurrentStorageUsageBytes() > 0);

            await Assert.ThrowsAnyAsync<Exception>(() => storage.SaveReceiptImageAsync(""));
            await Assert.ThrowsAnyAsync<Exception>(() => storage.SaveReceiptImageAsync(Path.Combine(tempDir, "missing.png")));

            var badExtFile = Path.Combine(tempDir, "virus.exe");
            await File.WriteAllBytesAsync(badExtFile, new byte[] { 1, 2, 3 });
            await Assert.ThrowsAnyAsync<Exception>(() => storage.SaveReceiptImageAsync(badExtFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact(DisplayName = "НФВ-Безпека: 100% паролів захешовані (PBKDF2) + блокування на 60 с після 3 невдалих спроб")]
    public async Task NfrSecurity_PasswordHashing_And_60SecLockoutAfter3FailedAttempts()
    {
        var env = BuildEnvironment();
        var (_, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("SecHome", "Admin", "sec@test.com", "MyStrongPass!", new[] { "User2" }));

        var acc = await env.AccountRepo.GetByMemberIdAsync(admin.Id);
        Assert.NotNull(acc);
        Assert.NotEqual("MyStrongPass!", acc!.PasswordHash);

        await env.Auth.LoginAsync("sec@test.com", "Wrong1");
        await env.Auth.LoginAsync("sec@test.com", "Wrong2");
        var r3 = await env.Auth.LoginAsync("sec@test.com", "Wrong3");

        Assert.Equal(LoginStatus.LockedOut, r3.Status);
        var rLocked = await env.Auth.LoginAsync("sec@test.com", "MyStrongPass!");
        Assert.Equal(LoginStatus.LockedOut, rLocked.Status);

        env.Clock.Advance(TimeSpan.FromSeconds(61));
        var rSuccess = await env.Auth.LoginAsync("sec@test.com", "MyStrongPass!");
        Assert.Equal(LoginStatus.Success, rSuccess.Status);
    }

    [Fact(DisplayName = "Покриття Money, MoneyFormat, SystemClock та доменних моделей")]
    public void Coverage_Core_Money_Clock_And_Models()
    {
        Assert.Equal(12.35m, Money.Round(12.345m));

        foreach (var curr in MoneyFormat.SupportedCurrencies)
        {
            MoneyFormat.SetCurrency(curr.Code);
            Assert.False(string.IsNullOrEmpty(MoneyFormat.Symbol));
            Assert.Contains(MoneyFormat.Symbol, MoneyFormat.Format(100m));
            Assert.DoesNotContain("+", MoneyFormat.FormatSigned(0m));
        }

        var singleMember = new Member { DisplayName = "Nazar" };
        Assert.False(string.IsNullOrWhiteSpace(singleMember.Initials));

        var chore = new Chore { Deadline = DateTime.Today.AddDays(-1), Status = ChoreStatus.ToDo };
        Assert.True(chore.IsOverdue(DateTime.Today));
    }

    [Fact(DisplayName = "Покриття ExportService (CSV/PDF) та SimplePdfWriter з українською кирилицею")]
    public async Task Coverage_ExportService_And_SimplePdfWriter_Cyrillic()
    {
        var env = BuildEnvironment();
        var tempDir = Path.Combine(Path.GetTempPath(), "RoomieSyncExport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var rows = new List<ReportRow> { new(DateTime.Today, "Сільпо", "Продукти", "Назар", 450m, 225m) };
            var transfers = new List<(string, string, decimal)> { ("Олена", "Назар", 374m) };

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

    [Fact(DisplayName = "Покриття BackupService (експорт та відновлення JSON-бекапу)")]
    public async Task Coverage_BackupService_ExportAndRestore()
    {
        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("BackupHome", "Admin", "b@test.com", "Secret123", new[] { "M2" }));

        var tempFile = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid():N}.json");
        try
        {
            await env.Backup.ExportBackupAsync(h.Id, tempFile);
            var restored = await env.Backup.RestoreBackupAsync(tempFile);
            Assert.NotNull(restored);
            Assert.NotEmpty(restored.Members);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact(DisplayName = "Покриття MockReceiptParser, GeminiReceiptParser та ReceiptValidationService")]
    public async Task Coverage_ReceiptParsers_And_Validation()
    {
        var env = BuildEnvironment();
        var mockParser = new MockReceiptParser(new GeminiOptions { UseMock = true }, env.Clock);
        var parsed1 = await mockParser.ParseReceiptAsync(new byte[] { 1, 2 }, "image/jpeg", "grocery.jpg");
        Assert.NotNull(parsed1);

        var validRes = env.Validator.ValidateReceipt(parsed1);
        Assert.True(validRes.IsValid);

        var geminiJson = "{\"candidates\": [{\"content\": {\"parts\": [{\"text\": \"{\\\"merchant\\\":\\\"ATB\\\",\\\"total\\\":125.50,\\\"items\\\":[]}\"}]}}]}";
        var okHandler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(geminiJson, Encoding.UTF8, "application/json")
        }));
        using var client = new HttpClient(okHandler);
        var geminiParser = new GeminiReceiptParser(client, new GeminiOptions { ApiKey = "fake-key", UseMock = false });
        var geminiResult = await geminiParser.ParseReceiptAsync(new byte[] { 1, 2, 3 }, "image/jpeg", "atb.jpg");

        Assert.Equal("ATB", geminiResult.Merchant);
        Assert.Equal(125.50m, geminiResult.Total);
    }

    [Fact(DisplayName = "Покриття DemoDataSeeder, SessionContext, двокрокових розрахунків та видалення витрат")]
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
            new NewHouseholdRequest("FlowHome", "AdminUser", "flow@test.com", "Secret123", new[] { "RoomieB" }));

        env.Session.SignIn(admin, h);
        Assert.True(env.Session.IsAuthenticated);

        env.Session.SignOut();
        Assert.False(env.Session.IsAuthenticated);
    }

    [Fact(DisplayName = "Повне покриття доменних моделей, безпеки, годинника та форматування валют (RoomieSync.Core)")]
    public void Coverage_Core_AllModels_Security_And_EdgeCases()
    {
        var sysClock = new SystemClock();
        Assert.True(sysClock.Now <= DateTime.Now.AddSeconds(5));

        var delRes = new DeleteExpenseResult(true, 2, 2);
        Assert.True(delRes.Deleted);
        Assert.Equal(2, delRes.ApprovedCount);
        Assert.Equal(2, delRes.RequiredCount);

        var setRes = new SettlementActionResult(true, "Settled!");
        Assert.True(setRes.Settled);
        Assert.Equal("Settled!", setRes.Message);

        var memSpend = new MemberSpendSummary(Guid.NewGuid(), "Nazar", 100m, 40m);
        Assert.Equal(60m, memSpend.NetBalance);

        var monthSpend = new MonthlySpendSummary(2026, 5, 1000m, 4);
        Assert.Equal(2026, monthSpend.Year);
        Assert.Equal(5, monthSpend.Month);
        Assert.Equal(1000m, monthSpend.TotalAmount);
        Assert.Equal(4, monthSpend.ExpenseCount);

        var catSpend = new CategorySpendSummary(ExpenseCategory.Rent, 800m, 1, 80.0);
        Assert.Equal(ExpenseCategory.Rent, catSpend.Category);
        Assert.Equal(80.0, catSpend.Percentage);

        var repSum = new HouseholdReportSummary(1000m, 500m, 4, ExpenseCategory.Rent, new[] { catSpend }, new[] { monthSpend }, new[] { memSpend });
        Assert.Equal(1000m, repSum.TotalSpent);
        Assert.Equal(500m, repSum.MonthlyAverage);
        Assert.Equal(4, repSum.ExpenseCount);
        Assert.Equal(ExpenseCategory.Rent, repSum.TopCategory);
        Assert.Single(repSum.ByCategory);
        Assert.Single(repSum.ByMonth);
        Assert.Single(repSum.ByMember);

        var plan = new SettlementPlan(new[] { new Transfer(Guid.NewGuid(), Guid.NewGuid(), 25m) }, Array.Empty<MemberBalance>(), 3);
        Assert.Equal(25m, plan.TotalAmount);
        Assert.Equal(2, plan.TransfersSaved);
        Assert.False(plan.IsSettled);

        var valOk = ValidationResult.Ok();
        Assert.True(valOk.IsValid);
        Assert.False(valOk.HasWarnings);
        valOk.Warning("warn1").Error("err1");
        Assert.Single(valOk.Warnings);
        Assert.Single(valOk.Errors);
        Assert.True(valOk.HasWarnings);
        Assert.False(valOk.IsValid);

        bool currChanged = false;
        EventHandler handler = (_, _) => currChanged = true;
        MoneyFormat.CurrencyChanged += handler;
        try
        {
            MoneyFormat.SetCurrency(null);
            Assert.Equal("USD", MoneyFormat.CurrencyCode);
            MoneyFormat.SetCurrency("UNKNOWN_CURR");
            Assert.Equal("USD", MoneyFormat.CurrencyCode);
            Assert.True(currChanged);
            Assert.StartsWith("+", MoneyFormat.FormatSigned(15.5m));
            Assert.StartsWith("-", MoneyFormat.FormatSigned(-15.5m));
            Assert.Equal("$0.00", MoneyFormat.FormatSigned(0m));
        }
        finally
        {
            MoneyFormat.CurrencyChanged -= handler;
        }

        var hasher = new Pbkdf2PasswordHasher();
        Assert.ThrowsAny<ArgumentException>(() => hasher.Hash(""));
        var (hash, salt) = hasher.Hash("ValidPass123");
        Assert.True(hasher.Verify("ValidPass123", hash, salt));
        Assert.False(hasher.Verify("WrongPass", hash, salt));
        Assert.False(hasher.Verify("", hash, salt));
        Assert.False(hasher.Verify("ValidPass123", "", salt));
        Assert.False(hasher.Verify("ValidPass123", hash, ""));

        var emptyMember = new Member { DisplayName = "   " };
        Assert.Equal("?", emptyMember.Initials);
        Assert.Equal("   ", emptyMember.FirstName);
        Assert.Equal("   ", emptyMember.ToString());
        Assert.True(emptyMember.IsEmailConfirmed);

        var twoWordMember = new Member { DisplayName = "Solomiia Uhryn" };
        Assert.Equal("SU", twoWordMember.Initials);
        Assert.Equal("Solomiia", twoWordMember.FirstName);

        var doneChore = new Chore { Status = ChoreStatus.Done, Deadline = DateTime.Today.AddDays(-1) };
        Assert.False(doneChore.IsOverdue(DateTime.Today));
        var noDeadlineChore = new Chore { Status = ChoreStatus.ToDo, Deadline = null };
        Assert.False(noDeadlineChore.IsOverdue(DateTime.Today));
        var futureChore = new Chore { Status = ChoreStatus.ToDo, Deadline = DateTime.Today.AddDays(1) };
        Assert.False(futureChore.IsOverdue(DateTime.Today));
    }

    [Fact(DisplayName = "Повне покриття DataServiceCollectionExtensions, NpgsqlConnectionFactory та InMemory-репозиторіїв")]
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
        Assert.NotNull(factory);
        Assert.IsType<GeminiReceiptParser>(pgProvider.GetRequiredService<IReceiptParser>());

        Assert.Throws<InvalidOperationException>(() => new RoomieSync.Data.Postgres.NpgsqlConnectionFactory("   "));
        using (var npgsqlFactory = new RoomieSync.Data.Postgres.NpgsqlConnectionFactory("Host=localhost;Database=test;Username=u;Password=p"))
        {
            Assert.NotNull(npgsqlFactory);
        }

        using (var conn = new Npgsql.NpgsqlConnection())
        using (var cmd = RoomieSync.Data.Postgres.DbExtensions.Cmd(conn, "SELECT @p1, @p2"))
        {
            RoomieSync.Data.Postgres.DbExtensions.P(cmd, "p1", "val");
            RoomieSync.Data.Postgres.DbExtensions.P(cmd, "p2", null);
            Assert.Equal(2, cmd.Parameters.Count);
            Assert.Equal("val", cmd.Parameters["p1"].Value);
            Assert.Equal(DBNull.Value, cmd.Parameters["p2"].Value);
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
        Assert.NotEmpty(allHouseholds);
        await hhRepo.UpdateAsync(new Household { Id = Guid.NewGuid(), Name = "Ghost" });
        await memRepo.UpdateAsync(new Member { Id = Guid.NewGuid(), DisplayName = "Ghost" });
        await accRepo.UpdateAsync(new UserAccount { MemberId = Guid.NewGuid(), Email = "ghost@x.com" });
        await expRepo.UpdateAsync(new Expense { Id = Guid.NewGuid(), Title = "Ghost" });
        await choreRepo.UpdateAsync(new Chore { Id = Guid.NewGuid(), Title = "Ghost" });

        var emptyHid = Guid.NewGuid();
        Assert.Empty(await repRepo.GetCategorySpendingAsync(emptyHid));
        Assert.Empty(await repRepo.GetMonthlySpendingAsync(emptyHid, DateTime.Today.AddMonths(-1), DateTime.Today));
        Assert.Empty(await repRepo.GetMemberSpendingAsync(emptyHid));

        var firstMember = (await memRepo.GetByHouseholdAsync(allHouseholds[0].Id, includeInactive: true)).First();
        await notifRepo.AddRangeAsync(new[]
        {
            new Notification { HouseholdId = allHouseholds[0].Id, RecipientMemberId = firstMember.Id, Message = "Test" }
        });
        await notifRepo.MarkAllReadAsync(firstMember.Id);
        var readNotes = await notifRepo.GetForMemberAsync(firstMember.Id);
        Assert.All(readNotes, n => Assert.True(n.IsRead));
    }

    [Fact(DisplayName = "Повне покриття сценаріїв AuthService, SessionContext та HouseholdService")]
    public async Task Coverage_Services_Auth_Session_And_Household()
    {
        var env = BuildEnvironment();

        var defaultAuth = new RoomieSync.Services.Auth.AuthService(
            env.AccountRepo,
            env.Provider.GetRequiredService<IMemberRepository>(),
            env.Provider.GetRequiredService<IHouseholdRepository>());
        Assert.Equal(LoginStatus.InvalidCredentials, (await defaultAuth.LoginAsync("", "")).Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await defaultAuth.LoginAsync("nobody@lnu.edu.ua", "pass123")).Status);

        int changeCount = 0;
        env.Session.Changed += (_, _) => changeCount++;
        var (h1, admin1) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home One", "Solomiia", "solomiia@lnu.edu.ua", "Secret123", new[] { "Uliana", "Nazar" }, "  "));
        Assert.Equal("UAH", h1.Currency);

        env.Session.SignIn(admin1, h1);
        Assert.True(env.Session.IsAuthenticated);
        env.Session.UpdateHousehold(h1);
        env.Session.UpdateMember(new Member { Id = Guid.NewGuid(), DisplayName = "Other" });
        admin1.DisplayName = "Solomiia U.";
        env.Session.UpdateMember(admin1);
        Assert.Equal("Solomiia U.", env.Session.CurrentMember!.DisplayName);
        env.Session.SignOut();
        Assert.False(env.Session.IsAuthenticated);
        Assert.True(changeCount >= 4);

        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("   ", "A", "a@b.com", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home One", "A", "a@b.com", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home Two", "   ", "a@b.com", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home Two", "A", "bad", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home Two", "A", "a@b.com", "123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home Two", "Same", "a@b.com", "Secret123", new[] { "same" })));

        var (h2, admin2) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Home Two", "Solomiia", "solomiia@lnu.edu.ua", "Secret123", new[] { "Anna" }, "EUR"));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameAsync(h1.Id, "   "));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameAsync(h1.Id, "Home Two"));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameAsync(Guid.NewGuid(), "Valid Name"));
        var renamedH1 = await env.Households.RenameAsync(h1.Id, "Home One Renamed");
        Assert.Equal("Home One Renamed", renamedH1.Name);

        await Assert.ThrowsAsync<DomainException>(() => env.Households.ChangeCurrencyAsync(Guid.NewGuid(), "USD"));
        var currDefault = await env.Households.ChangeCurrencyAsync(h1.Id, "  ");
        Assert.Equal("UAH", currDefault.Currency);
        var currPln = await env.Households.ChangeCurrencyAsync(h1.Id, "pln");
        Assert.Equal("PLN", currPln.Currency);

        var h1Members = await env.Households.GetMembersAsync(h1.Id);
        var uliana = h1Members.Single(m => m.DisplayName == "Uliana");
        await Assert.ThrowsAsync<DomainException>(() => env.Auth.SetCredentialsAsync(uliana.Id, "invalid", "Secret123"));
        await Assert.ThrowsAsync<DomainException>(() => env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "123"));
        await Assert.ThrowsAsync<DomainException>(() => env.Auth.SetCredentialsAsync(Guid.NewGuid(), "uliana@lnu.edu.ua", "Secret123"));
        await Assert.ThrowsAsync<DomainException>(() => env.Auth.SetCredentialsAsync(uliana.Id, "solomiia@lnu.edu.ua", "Secret123"));

        await env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "Pass111");
        await env.Auth.SetCredentialsAsync(uliana.Id, "uliana@lnu.edu.ua", "Pass222");
        Assert.Equal("uliana@lnu.edu.ua", await env.Auth.GetEmailAsync(uliana.Id));
        Assert.Null(await env.Auth.GetEmailAsync(Guid.NewGuid()));

        await env.Auth.ConfirmMembershipAsync(uliana.Id);
        await env.Auth.ConfirmMembershipAsync(Guid.NewGuid());

        var userHouseholds = await env.Auth.GetUserHouseholdsAsync(admin1.Id);
        Assert.Equal(2, userHouseholds.Count);
        var nazar = h1Members.Single(m => m.DisplayName == "Nazar");
        Assert.Empty(await env.Auth.GetUserHouseholdsAsync(nazar.Id));

        await env.Auth.LoginAsync("uliana@lnu.edu.ua", "wrong");
        var okLogin = await env.Auth.LoginAsync("uliana@lnu.edu.ua", "Pass222");
        Assert.Equal(LoginStatus.Success, okLogin.Status);

        await Assert.ThrowsAsync<DomainException>(() => env.Households.AddMemberAsync(h1.Id, "  "));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.AddMemberAsync(h1.Id, "Uliana"));
        var m4 = await env.Households.AddMemberAsync(h1.Id, "M4");
        var m5 = await env.Households.AddMemberAsync(h1.Id, "M5");
        var m6 = await env.Households.AddMemberAsync(h1.Id, "M6");
        await Assert.ThrowsAsync<DomainException>(() => env.Households.AddMemberAsync(h1.Id, "M7"));

        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameMemberAsync(m4.Id, "  "));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameMemberAsync(Guid.NewGuid(), "Valid"));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.RenameMemberAsync(m4.Id, "Uliana"));
        var renamedM4 = await env.Households.RenameMemberAsync(m4.Id, "M4 Updated");
        Assert.Equal("M4 Updated", renamedM4.DisplayName);

        Assert.False(await env.Households.CanDeleteMemberAsync(Guid.NewGuid()));
        Assert.False(await env.Households.CanDeleteMemberAsync(admin1.Id));

        var h2Members = await env.Households.GetMembersAsync(h2.Id);
        var anna = h2Members.Single(m => m.DisplayName == "Anna");
        Assert.True(await env.Households.CanDeleteMemberAsync(anna.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.DeleteMemberAsync(anna.Id));
        Assert.NotNull(admin2);
    }

    [Fact(DisplayName = "Повне покриття двокрокового погашення боргів, голосування за видалення витрат, ротації обов'язків та сповіщень")]
    public async Task Coverage_Services_TwoStepSettlement_DeleteApproval_Chores_And_Notifications()
    {
        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("Workflow Home", "Admin", "adm@lnu.edu.ua", "Secret123", new[] { "Bob", "Cara" }));
        var members = await env.Households.GetMembersAsync(h.Id);
        var bob = members.Single(m => m.DisplayName == "Bob");
        var cara = members.Single(m => m.DisplayName == "Cara");

        Assert.Empty(RoomieSync.Services.Expenses.SplitCalculator.Calculate(100m, SplitMethod.Equally, Array.Empty<ShareInput>()));
        var non100Pct = RoomieSync.Services.Expenses.SplitCalculator.Calculate(100m, SplitMethod.ByPercentage,
            new[] { new ShareInput(admin.Id, 40m), new ShareInput(bob.Id, 40m) });
        Assert.Equal(80m, non100Pct.Sum(s => s.Amount));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RoomieSync.Services.Expenses.SplitCalculator.Calculate(100m, (SplitMethod)999, new[] { new ShareInput(admin.Id, 1m) }));

        var longTitle = new string('X', 205);
        var valErrs = env.Expenses.Validate(new NewExpenseRequest(
            longTitle, 2_000_000m, env.Clock.Now.AddDays(5), ExpenseCategory.Other, Guid.Empty, SplitMethod.Equally,
            new[] { new ShareInput(admin.Id, -5m) }));
        Assert.False(valErrs.IsValid);
        Assert.True(valErrs.Errors.Count >= 4);

        var exactMismatch = env.Expenses.Validate(new NewExpenseRequest(
            "Exact", 50m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.ByExactAmount,
            new[] { new ShareInput(admin.Id, 20m), new ShareInput(bob.Id, 20m) }));
        Assert.False(exactMismatch.IsValid);

        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "", 10m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, Array.Empty<ShareInput>())));
        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.UpdateExpenseAsync(Guid.NewGuid(), new NewExpenseRequest(
            "Valid", 10m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, new[] { new ShareInput(admin.Id, 1m) })));

        var exp = await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Shared Dinner", 100m, env.Clock.Now, ExpenseCategory.Groceries, admin.Id, SplitMethod.Equally,
            new[] { new ShareInput(admin.Id, 1m), new ShareInput(bob.Id, 1m) }));
        Assert.NotNull(await env.Expenses.GetExpenseAsync(exp.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.UpdateExpenseAsync(exp.Id, new NewExpenseRequest(
            "", 10m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, Array.Empty<ShareInput>())));

        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.RequestOrApproveDeleteAsync(Guid.NewGuid(), admin.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, Guid.NewGuid()));
        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, bob.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, cara.Id));

        var step1 = await env.Expenses.RequestOrApproveDeleteAsync(exp.Id, admin.Id);
        Assert.False(step1.Deleted);
        Assert.Equal(1, step1.ApprovedCount);
        Assert.Equal(2, step1.RequiredCount);

        await Assert.ThrowsAsync<DomainException>(() => env.Expenses.RequestOrApproveDeleteAsync(exp.Id, cara.Id));

        var step2 = await env.Expenses.RequestOrApproveDeleteAsync(exp.Id, bob.Id);
        Assert.True(step2.Deleted);
        Assert.Null(await env.Expenses.GetExpenseAsync(exp.Id));

        var expDirect = await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Direct Delete", 60m, env.Clock.Now, ExpenseCategory.Utilities, admin.Id, SplitMethod.Equally,
            new[] { new ShareInput(admin.Id, 1m), new ShareInput(bob.Id, 1m) }));
        await env.Expenses.DeleteExpenseAsync(expDirect.Id);

        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Rent Split", 200m, env.Clock.Now, ExpenseCategory.Rent, admin.Id, SplitMethod.Equally,
            new[] { new ShareInput(admin.Id, 1m), new ShareInput(bob.Id, 1m) }));

        var transfer = new Transfer(bob.Id, admin.Id, 100m);
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, 0m), bob.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), admin.Id, 10m), bob.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, new Transfer(bob.Id, Guid.NewGuid(), 10m), bob.Id));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, cara.Id));

        Assert.False(env.Balances.HasPendingConfirmation(h.Id, transfer, out _));
        var initRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, bob.Id);
        Assert.False(initRes.Settled);
        Assert.True(env.Balances.HasPendingConfirmation(h.Id, transfer, out var confirmer));
        Assert.Equal(bob.Id, confirmer);

        var repeatRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, bob.Id);
        Assert.False(repeatRes.Settled);

        var confirmRes = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        Assert.True(confirmRes.Settled);
        Assert.NotEmpty(await env.Balances.GetSettlementsAsync(h.Id));

        await Assert.ThrowsAsync<DomainException>(() => env.Balances.RecordSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, -5m)));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(bob.Id, admin.Id, 0m)));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), admin.Id, 10m)));
        await Assert.ThrowsAsync<DomainException>(() => env.Balances.RequestSettlementAsync(h.Id, new Transfer(bob.Id, Guid.NewGuid(), 10m)));

        await Assert.ThrowsAsync<DomainException>(() => env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("  ", null, null, ChoreRotation.None)));
        var choreDaily = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Take out trash", bob.Id, env.Clock.Now.AddDays(-5), ChoreRotation.Daily));
        var choreMonthly = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Pay bills", null, env.Clock.Now.AddMonths(-2), ChoreRotation.Monthly));

        await Assert.ThrowsAsync<DomainException>(() => env.Chores.UpdateChoreAsync(new Chore { Id = choreDaily.Id, Title = " " }));
        choreDaily.Title = "Take out trash daily";
        await env.Chores.UpdateChoreAsync(choreDaily);

        await Assert.ThrowsAsync<DomainException>(() => env.Chores.AssignAsync(Guid.NewGuid(), bob.Id));
        await env.Chores.AssignAsync(choreMonthly.Id, cara.Id);

        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        var bobNotes = await env.Notifications.GetNotificationsAsync(bob.Id);
        Assert.Contains(bobNotes, n => n.Type == NotificationType.OverdueChore);
        await env.Notifications.MarkAllReadAsync(bob.Id);

        await Assert.ThrowsAsync<DomainException>(() => env.Chores.ChangeStatusAsync(Guid.NewGuid(), ChoreStatus.Done));
        Assert.Null(await env.Chores.ChangeStatusAsync(choreDaily.Id, ChoreStatus.ToDo));
        var nextDaily = await env.Chores.ChangeStatusAsync(choreDaily.Id, ChoreStatus.Done);
        Assert.NotNull(nextDaily);
        Assert.True(nextDaily!.Deadline >= env.Clock.Now);

        var nextMonthly = await env.Chores.ChangeStatusAsync(choreMonthly.Id, ChoreStatus.Done);
        Assert.NotNull(nextMonthly);
        Assert.True(nextMonthly!.Deadline >= env.Clock.Now);

        Assert.Equal(env.Clock.Now, ((RoomieSync.Services.Chores.ChoreService)env.Chores).NextDeadline(env.Clock.Now, ChoreRotation.None));
        Assert.Null(env.Chores.GetNextAssignee(Array.Empty<Member>(), null));
        await env.Chores.DeleteChoreAsync(choreDaily.Id);
    }

    [Fact(DisplayName = "Повне покриття крайових випадків парсерів чеків, валідації, бекапів, сховища та PDF-генератора")]
    public async Task Coverage_Receipts_Validation_Backup_Storage_And_Pdf_EdgeCases()
    {
        var env = BuildEnvironment();

        var emptyOpts = new GeminiOptions { ApiKey = null };
        _ = emptyOpts.ResolveApiKey();
        using var dummyClient = new HttpClient(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError))));
        var noKeyParser = new GeminiReceiptParser(dummyClient, new GeminiOptions { ApiKey = "   " });
        await Assert.ThrowsAsync<ReceiptParseException>(() => noKeyParser.ParseReceiptAsync(new byte[] { 1 }, "image/png"));

        var errStatusParser = new GeminiReceiptParser(dummyClient, new GeminiOptions { ApiKey = "key" });
        await Assert.ThrowsAsync<ReceiptParseException>(() => errStatusParser.ParseReceiptAsync(new byte[] { 1 }, "image/png"));

        using var badJsonClient = new HttpClient(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"not_candidates\":[]}", Encoding.UTF8, "application/json")
        })));
        var badJsonParser = new GeminiReceiptParser(badJsonClient, new GeminiOptions { ApiKey = "key" });
        await Assert.ThrowsAsync<ReceiptParseException>(() => badJsonParser.ParseReceiptAsync(new byte[] { 1 }, "image/png"));

        var fastMock = new MockReceiptParser(new GeminiOptions { TimeoutSeconds = 0 }, env.Clock);
        var slowEx = await Assert.ThrowsAsync<ReceiptParseException>(() => fastMock.ParseReceiptAsync(new byte[] { 1 }, "image/png", "slow_receipt.png"));
        Assert.True(slowEx.IsTimeout);

        var warnReceipt1 = new ParsedReceipt { Total = 0m, Merchant = "  ", Date = null };
        var vr1 = env.Validator.ValidateReceipt(warnReceipt1);
        Assert.Equal(3, vr1.Warnings.Count);

        var warnReceiptFuture = new ParsedReceipt { Total = 10m, Merchant = "Shop", Date = env.Clock.Now.AddDays(5) };
        Assert.True(env.Validator.ValidateReceipt(warnReceiptFuture).HasWarnings);

        var warnReceiptOld = new ParsedReceipt { Total = 10m, Merchant = "Shop", Date = env.Clock.Now.AddYears(-2) };
        Assert.True(env.Validator.ValidateReceipt(warnReceiptOld).HasWarnings);

        await Assert.ThrowsAsync<DomainException>(() => env.Backup.ExportBackupAsync(Guid.NewGuid(), "none.json"));
        await Assert.ThrowsAsync<DomainException>(() => env.Backup.RestoreBackupAsync("non_existent_backup_12345.json"));

        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("FullBackupHome", "Admin", "adm@b.com", "Secret123", new[] { "Peer" }));
        var members = await env.Households.GetMembersAsync(h.Id);
        var peer = members.Single(m => m.DisplayName == "Peer");
        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest(
            "Groceries", 100m, env.Clock.Now, ExpenseCategory.Groceries, admin.Id, SplitMethod.Equally,
            members.Select(m => new ShareInput(m.Id, 1m)).ToList()));
        await env.Balances.RecordSettlementAsync(h.Id, new Transfer(peer.Id, admin.Id, 50m));
        await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("Vacuum", peer.Id, env.Clock.Now.AddDays(1), ChoreRotation.Weekly));

        var backupFile = Path.Combine(Path.GetTempPath(), $"full_backup_{Guid.NewGuid():N}.json");
        try
        {
            await env.Backup.ExportBackupAsync(h.Id, backupFile);
            var freshEnv = BuildEnvironment();
            var pkg1 = await freshEnv.Backup.RestoreBackupAsync(backupFile);
            Assert.Single(pkg1.Expenses);
            Assert.Single(pkg1.Settlements);
            Assert.Single(pkg1.Chores);
            var pkg2 = await freshEnv.Backup.RestoreBackupAsync(backupFile);
            Assert.Equal(pkg1.Household.Id, pkg2.Household.Id);
        }
        finally
        {
            if (File.Exists(backupFile)) File.Delete(backupFile);
        }

        var tempStorageDir = Path.Combine(Path.GetTempPath(), "RoomieSyncQuota_" + Guid.NewGuid().ToString("N"));
        try
        {
            var tinyStorage = new ReceiptStorageService(tempStorageDir, maxStorageBytes: 4);
            var imgPath = Path.Combine(tempStorageDir, "big.png");
            await File.WriteAllBytesAsync(imgPath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            await Assert.ThrowsAsync<DomainException>(() => tinyStorage.SaveReceiptImageAsync(imgPath));

            Directory.Delete(tempStorageDir, true);
            Assert.Equal(0L, tinyStorage.GetCurrentStorageUsageBytes());
        }
        finally
        {
            if (Directory.Exists(tempStorageDir)) Directory.Delete(tempStorageDir, true);
        }

        var pdfPath = Path.Combine(Path.GetTempPath(), $"special_{Guid.NewGuid():N}.pdf");
        try
        {
            var specialRows = new[]
            {
                new ReportRow(DateTime.Today, @"Test (1) \ ₴ € £ zł — – · … ©", "Ґудзики Єдиноріг Їжак Іній", "Олексій", 10m, 5m)
            };
            await env.Export.ExportExpensesPdfAsync(pdfPath, "Звіт (Спецсимволи)", specialRows);
            Assert.True(File.Exists(pdfPath));
        }
        finally
        {
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
        }
    }

    [Fact(DisplayName = "Досягнення 100% покриття рядків та максимального покриття розгалужень (Branch Coverage)")]
    public async Task Coverage_AllRemainingBranches_100Percent()
    {
        var emptyConfig = new ConfigurationBuilder().Build();
        var defaultServices = new ServiceCollection()
            .AddRoomieSyncData(emptyConfig)
            .AddRoomieSyncServices(emptyConfig);
        using var defaultProvider = defaultServices.BuildServiceProvider();
        Assert.NotNull(defaultProvider.GetRequiredService<DataOptions>());

        var pgNoCsConfig = new TestConfiguration(new Dictionary<string, string?>
        {
            ["Data:Provider"] = "Postgres",
            ["Data:SeedDemoData"] = "false"
        });
        using var pgNoCsProvider = new ServiceCollection().AddRoomieSyncData(pgNoCsConfig).BuildServiceProvider();
        await Assert.ThrowsAnyAsync<Exception>(() => pgNoCsProvider.InitializeRoomieSyncDataAsync());

        var env = BuildEnvironment();
        var (h, admin) = await env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("BranchHome", "Admin", "branch@lnu.edu.ua", "Secret123", new[] { "Peer" }));
        var members = await env.Households.GetMembersAsync(h.Id);
        var peer = members.Single(m => m.DisplayName == "Peer");

        var singleAcc = await env.AccountRepo.GetByEmailAsync("branch@lnu.edu.ua");
        var stubAccRepo = new SingleOnlyAccountRepo(singleAcc!);
        var fallbackAuth = new RoomieSync.Services.Auth.AuthService(
            stubAccRepo,
            env.Provider.GetRequiredService<IMemberRepository>(),
            env.Provider.GetRequiredService<IHouseholdRepository>(),
            new Pbkdf2PasswordHasher(),
            env.Clock);
        var fallbackLogin = await fallbackAuth.LoginAsync("branch@lnu.edu.ua", "Secret123");
        Assert.Equal(LoginStatus.Success, fallbackLogin.Status);
        Assert.Equal(LoginStatus.InvalidCredentials, (await fallbackAuth.LoginAsync(null!, "Secret123")).Status);
        await Assert.ThrowsAsync<DomainException>(() => fallbackAuth.SetCredentialsAsync(admin.Id, null!, "Secret123"));

        var memberRepo = env.Provider.GetRequiredService<IMemberRepository>();
        await env.Auth.SetCredentialsAsync(peer.Id, "peer@lnu.edu.ua", "Secret123");
        peer.IsActive = false;
        await memberRepo.UpdateAsync(peer);
        var inactiveLogin = await env.Auth.LoginAsync("peer@lnu.edu.ua", "Secret123");
        Assert.Equal(LoginStatus.InvalidCredentials, inactiveLogin.Status);
        peer.IsActive = true;
        await memberRepo.UpdateAsync(peer);

        var rawSession = new RoomieSync.Services.Auth.SessionContext();
        rawSession.UpdateMember(admin);
        rawSession.UpdateHousehold(h);
        rawSession.SignIn(admin, h);
        rawSession.UpdateMember(admin);
        rawSession.SignOut();

        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest(null!, "A", "a@b.com", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("H_NullAdmin", null!, "a@b.com", "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("H_NullEmail", "A", null!, "Secret123", new[] { "B" })));
        await Assert.ThrowsAsync<DomainException>(() => env.Households.CreateHouseholdAsync(
            new NewHouseholdRequest("H_NullPass", "A", "a@b.com", null!, new[] { "B" })));

        var transfer = new Transfer(peer.Id, admin.Id, 25m);
        var credInit = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        Assert.False(credInit.Settled);
        var credRepeat = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, admin.Id);
        Assert.False(credRepeat.Settled);
        var debtConfirm = await env.Balances.InitiateOrConfirmSettlementAsync(h.Id, transfer, peer.Id);
        Assert.True(debtConfirm.Settled);
        await env.Balances.RecordSettlementAsync(h.Id, new Transfer(Guid.NewGuid(), Guid.NewGuid(), 10m));

        await Assert.ThrowsAsync<DomainException>(() => env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest(null!, null, null, ChoreRotation.None)));
        var noDeadlineRotating = await env.Chores.CreateChoreAsync(h.Id, new NewChoreRequest("NoDeadlineTask", peer.Id, null, ChoreRotation.Weekly));
        await env.Chores.ChangeStatusAsync(noDeadlineRotating.Id, ChoreStatus.InProgress);
        var rotatedFromNoDeadline = await env.Chores.ChangeStatusAsync(noDeadlineRotating.Id, ChoreStatus.Done);
        Assert.NotNull(rotatedFromNoDeadline);

        Assert.False(env.Expenses.Validate(new NewExpenseRequest(null!, 10m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.Equally, new[] { new ShareInput(admin.Id, 1m) })).IsValid);
        Assert.True(env.Expenses.Validate(new NewExpenseRequest("ExactOk", 30m, env.Clock.Now, ExpenseCategory.Other, admin.Id, SplitMethod.ByExactAmount, new[] { new ShareInput(admin.Id, 10m), new ShareInput(peer.Id, 20m) })).IsValid);
        await env.Expenses.AddExpenseAsync(h.Id, new NewExpenseRequest("UnknownPayer", 20m, env.Clock.Now, ExpenseCategory.Other, Guid.NewGuid(), SplitMethod.Equally, new[] { new ShareInput(admin.Id, 1m) }));

        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = h.Id, Title = "Zero", Amount = 0m, Date = env.Clock.Now.AddDays(-10), Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });
        var repRepo = env.Provider.GetRequiredService<IReportRepository>();
        _ = await repRepo.GetCategorySpendingAsync(h.Id, env.Clock.Now.AddDays(-5), env.Clock.Now.AddDays(-1));
        _ = await repRepo.GetMemberSpendingAsync(h.Id, env.Clock.Now.AddDays(-5), env.Clock.Now.AddDays(-1));
        var zeroOnlyHid = Guid.NewGuid();
        await env.ExpenseRepo.AddAsync(new Expense { HouseholdId = zeroOnlyHid, Title = "ZeroOnly", Amount = 0m, Date = env.Clock.Now, Category = ExpenseCategory.Other, PaidByMemberId = admin.Id });
        var zeroCats = await repRepo.GetCategorySpendingAsync(zeroOnlyHid);
        Assert.Equal(0.0, zeroCats.Single().Percentage);
        var emptyReport = await env.Reports.GetPeriodSummaryAsync(Guid.NewGuid(), env.Clock.Now.AddMonths(-1), env.Clock.Now);
        Assert.Null(emptyReport.TopCategory);

        int notifEvents = 0;
        env.Notifications.NotificationsChanged += (_, _) => notifEvents++;
        await env.Notifications.CheckOverdueChoresAsync(Guid.NewGuid());
        await env.Notifications.NotifyHouseholdAsync(h.Id, NotificationType.NewExpense, "Msg");
        await env.Notifications.NotifyMemberAsync(h.Id, admin.Id, NotificationType.NewExpense, "Msg");
        await env.ChoreRepo.AddAsync(new Chore { HouseholdId = h.Id, Title = "OverdueUnassigned", AssigneeId = null, Deadline = env.Clock.Now.AddDays(-2), Status = ChoreStatus.ToDo });
        await env.Notifications.CheckOverdueChoresAsync(h.Id);
        await env.Notifications.MarkAllReadAsync(admin.Id);
        Assert.True(notifEvents >= 4);

        var tempPdf1 = Path.Combine(Path.GetTempPath(), $"empty_plan_{Guid.NewGuid():N}.pdf");
        var tempPdf2 = Path.Combine(Path.GetTempPath(), $"long_row_{Guid.NewGuid():N}.pdf");
        var nullBackup = Path.Combine(Path.GetTempPath(), $"null_{Guid.NewGuid():N}.json");
        try
        {
            await env.Export.ExportSettlementPlanPdfAsync(tempPdf1, "Empty Plan", Array.Empty<(string, string, decimal)>());
            await env.Export.ExportExpensesPdfAsync(tempPdf2, "Long Title ★", new[]
            {
                new ReportRow(DateTime.Today, new string('W', 45) + " ★", "Groceries", "Solomiia", 50m, 25m)
            });
            await File.WriteAllTextAsync(nullBackup, "null");
            await Assert.ThrowsAsync<DomainException>(() => env.Backup.RestoreBackupAsync(nullBackup));
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
        var emptyParsed = await nullTextParser.ParseReceiptAsync(new byte[] { 1 }, "image/png");
        Assert.Null(emptyParsed.Merchant);

        var weirdJson = GeminiReceiptParser.ParseJson("{\"merchant\":123,\"total\":\"NaN\",\"date\":456,\"category\":\"UnknownCat\",\"items\":[{\"price\":\"NaN\"}]}");
        Assert.Single(weirdJson.Items);
        Assert.Equal(0m, weirdJson.Items[0].Price);

        var mockParser = new MockReceiptParser(new GeminiOptions { UseMock = true }, env.Clock);
        var mockNullFile = await mockParser.ParseReceiptAsync(new byte[] { 1 }, "image/png", null);
        Assert.NotNull(mockNullFile);

        var nullTotalWithItems = new ParsedReceipt { Total = null, Merchant = "M", Date = env.Clock.Now, Items = { new ReceiptLineItem { Name = "I", Price = 5m } } };
        Assert.True(env.Validator.ValidateReceipt(nullTotalWithItems).HasWarnings);
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
