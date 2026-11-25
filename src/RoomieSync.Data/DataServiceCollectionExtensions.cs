using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Common;
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
            services.AddLoggedSingleton<IHouseholdRepository, PgHouseholdRepository>();
            services.AddLoggedSingleton<IMemberRepository, PgMemberRepository>();
            services.AddLoggedSingleton<IUserAccountRepository, PgUserAccountRepository>();
            services.AddLoggedSingleton<IExpenseRepository, PgExpenseRepository>();
            services.AddLoggedSingleton<ISettlementRepository, PgSettlementRepository>();
            services.AddLoggedSingleton<IChoreRepository, PgChoreRepository>();
            services.AddLoggedSingleton<INotificationRepository, PgNotificationRepository>();
            services.AddLoggedSingleton<IReportRepository, PgReportRepository>();
        }
        else
        {
            services.AddSingleton<InMemoryDataStore>();
            services.AddLoggedSingleton<IHouseholdRepository, InMemoryHouseholdRepository>();
            services.AddLoggedSingleton<IMemberRepository, InMemoryMemberRepository>();
            services.AddLoggedSingleton<IUserAccountRepository, InMemoryUserAccountRepository>();
            services.AddLoggedSingleton<IExpenseRepository, InMemoryExpenseRepository>();
            services.AddLoggedSingleton<ISettlementRepository, InMemorySettlementRepository>();
            services.AddLoggedSingleton<IChoreRepository, InMemoryChoreRepository>();
            services.AddLoggedSingleton<INotificationRepository, InMemoryNotificationRepository>();
            services.AddLoggedSingleton<IReportRepository, InMemoryReportRepository>();
        }

        services.AddSingleton<DemoDataSeeder>();
        return services;
    }

    private static IServiceCollection AddLoggedSingleton<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(provider =>
            ExceptionLoggingProxy<TService>.Create(provider.GetRequiredService<TImplementation>()));
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
