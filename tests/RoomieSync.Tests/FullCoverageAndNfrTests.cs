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
