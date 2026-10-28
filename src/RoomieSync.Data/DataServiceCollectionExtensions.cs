using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Data.InMemory;
using RoomieSync.Data.Postgres;
using RoomieSync.Data.Seed;

namespace RoomieSync.Data;

public enum DataProvider { InMemory, Postgres }

public sealed class DataOptions
{
    public const string Section = "Data";

    /// <summary>InMemory — демо без бази; Postgres — справжня БД через ADO.NET.</summary>
    public DataProvider Provider { get; set; } = DataProvider.InMemory;

    /// <summary>Заповнювати порожню базу демо-даними з мокапів.</summary>
    public bool SeedDemoData { get; set; } = true;
}

public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Реєструє репозиторії. Перемикання між моками і PostgreSQL — лише налаштуванням
    /// "Data:Provider" в appsettings.json, код UI і сервісів не змінюється.
    /// </summary>
    public static IServiceCollection AddRoomieSyncData(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(DataOptions.Section).Get<DataOptions>() ?? new DataOptions();
        services.AddSingleton(options);

        if (options.Provider == DataProvider.Postgres)
        {
            var cs = configuration.GetConnectionString("RoomieSync") ?? string.Empty;
            services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(cs));
            services.AddSingleton<DatabaseInitializer>();
            services.AddSingleton<IHouseholdRepository, PgHouseholdRepository>();
            services.AddSingleton<IMemberRepository, PgMemberRepository>();
            services.AddSingleton<IUserAccountRepository, PgUserAccountRepository>();
            services.AddSingleton<IExpenseRepository, PgExpenseRepository>();
            services.AddSingleton<ISettlementRepository, PgSettlementRepository>();
            services.AddSingleton<IChoreRepository, PgChoreRepository>();
            services.AddSingleton<INotificationRepository, PgNotificationRepository>();
            services.AddSingleton<IReportRepository, PgReportRepository>();
        }
        else
        {
            services.AddSingleton<InMemoryDataStore>();
            services.AddSingleton<IHouseholdRepository, InMemoryHouseholdRepository>();
            services.AddSingleton<IMemberRepository, InMemoryMemberRepository>();
            services.AddSingleton<IUserAccountRepository, InMemoryUserAccountRepository>();
            services.AddSingleton<IExpenseRepository, InMemoryExpenseRepository>();
            services.AddSingleton<ISettlementRepository, InMemorySettlementRepository>();
            services.AddSingleton<IChoreRepository, InMemoryChoreRepository>();
            services.AddSingleton<INotificationRepository, InMemoryNotificationRepository>();
            services.AddSingleton<IReportRepository, InMemoryReportRepository>();
        }

        services.AddSingleton<DemoDataSeeder>();
        return services;
    }

    /// <summary>Викликається один раз при старті: створює схему (для Postgres) і, за потреби, демо-дані.</summary>
    public static async Task InitializeRoomieSyncDataAsync(this IServiceProvider provider, CancellationToken ct = default)
    {
        var options = provider.GetRequiredService<DataOptions>();
        if (options.Provider == DataProvider.Postgres)
            await provider.GetRequiredService<DatabaseInitializer>().EnsureSchemaAsync(ct);
        if (options.SeedDemoData)
            await provider.GetRequiredService<DemoDataSeeder>().SeedAsync(ct);
    }
}
