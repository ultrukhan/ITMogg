using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Data;
using RoomieSync.Services;

namespace RoomieSync.Tests;

public sealed class FakeClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
    public void Advance(TimeSpan by) => Now += by;
}

internal static class TestHost
{
    /// <summary>Повний DI-контейнер як у застосунку, але з InMemory-даними і керованим годинником.</summary>
    public static ServiceProvider Build(FakeClock? clock = null, bool seed = true, Dictionary<string, string?>? settings = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Data:Provider"] = "InMemory",
                ["Data:SeedDemoData"] = seed.ToString(),
                ["Gemini:UseMock"] = "true",
            }.Concat(settings ?? new()).GroupBy(k => k.Key).ToDictionary(g => g.Key, g => g.Last().Value))
            .Build();
        var services = new ServiceCollection()
            .AddRoomieSyncData(config)
            .AddRoomieSyncServices(config);
        services.AddSingleton<IClock>(clock ?? new FakeClock(new DateTime(2026, 5, 24, 12, 0, 0)));
        var provider = services.BuildServiceProvider();
        provider.InitializeRoomieSyncDataAsync().GetAwaiter().GetResult();
        return provider;
    }
}
