using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Data;
using RoomieSync.Data.Seed;
using RoomieSync.Services;
using RoomieSync.Services.Logging;
using RoomieSync.Services.Maintenance;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public class LoggingTests
{
    [Theory]
    [InlineData("maya@roomiesync.app", "m***@roomiesync.app")]
    [InlineData("  a@b.c ", "a***@b.c")]
    [InlineData("no-at-sign", "***")]
    [InlineData("", "(empty)")]
    [InlineData(null, "(empty)")]
    public void MaskEmail_HidesLocalPart(string? input, string expected)
        => Assert.Equal(expected, LogSanitizer.MaskEmail(input));

    [Fact]
    public void FileName_DropsDirectory()
        => Assert.Equal("receipt.jpg", LogSanitizer.FileName(Path.Combine("C:", "Users", "john", "receipt.jpg")));

    [Fact]
    public async Task SuccessfulLogin_IsLoggedAsInformation_WithoutEmailOrPassword()
    {
        var (sp, sink) = Build();
        await sp.GetRequiredService<IAuthService>().LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);

        var entry = Assert.Single(sink.Entries, e => e.Message.StartsWith("Login succeeded"));
        Assert.Equal(LogLevel.Information, entry.Level);
        sink.AssertNoSecrets(DemoDataSeeder.DemoPassword, DemoDataSeeder.DemoAdminEmail);
    }

    [Fact]
    public async Task FailedLoginsAndLockout_AreLoggedAsWarnings_WithoutPassword()
    {
        var (sp, sink) = Build();
        var auth = sp.GetRequiredService<IAuthService>();
        const string wrongPassword = "Wr0ng-Secret!";
        for (var i = 0; i < 3; i++)
            await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, wrongPassword);

        Assert.Equal(2, sink.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("wrong password")));
        Assert.Contains(sink.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("locked"));
        sink.AssertNoSecrets(wrongPassword, DemoDataSeeder.DemoAdminEmail);
    }

    [Fact]
    public async Task RejectedReceiptFile_IsLoggedAsWarning()
    {
        var sink = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
        var dir = Path.Combine(Path.GetTempPath(), "rs-log-" + Guid.NewGuid().ToString("N"));
        var exe = Path.Combine(dir, "virus.exe");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(exe, "x");
        try
        {
            var storage = new ReceiptStorageService(Path.Combine(dir, "store"), logger: factory.CreateLogger<ReceiptStorageService>());
            await Assert.ThrowsAsync<DomainException>(() => storage.SaveReceiptImageAsync(exe));
            Assert.Contains(sink.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains(".exe"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Services_WorkWithoutAnyLoggingProvider()
    {
        using var sp = TestHost.Build();
        Assert.NotNull(sp.GetRequiredService<ILogger<LoggingTests>>());
    }

    private static (ServiceProvider, CapturingLoggerProvider) Build()
    {
        var sink = new CapturingLoggerProvider();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Data:Provider"] = "InMemory",
            ["Data:SeedDemoData"] = "true",
            ["Gemini:UseMock"] = "true",
        }).Build();
        var services = new ServiceCollection().AddRoomieSyncData(config).AddRoomieSyncServices(config);
        services.AddSingleton<IClock>(new FakeClock(DefaultTestNow));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(sink));
        var sp = services.BuildServiceProvider();
        sp.InitializeRoomieSyncDataAsync().GetAwaiter().GetResult();
        sink.Clear();
        return (sp, sink);
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public sealed record Entry(LogLevel Level, string Category, string Message);

    private readonly object _lock = new();
    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries { get { lock (_lock) return _entries.ToList(); } }

    public void Clear() { lock (_lock) _entries.Clear(); }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
    public void Dispose() { }

    public void AssertNoSecrets(params string[] secrets)
    {
        foreach (var e in Entries)
            foreach (var s in secrets)
                Assert.DoesNotContain(s, e.Message, StringComparison.OrdinalIgnoreCase);
    }

    private void Add(Entry e) { lock (_lock) _entries.Add(e); }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => owner.Add(new Entry(logLevel, category, formatter(state, exception)));
    }
}
