using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Security;
using RoomieSync.Services.Auth;
using RoomieSync.Services.Balances;
using RoomieSync.Services.Chores;
using RoomieSync.Services.Expenses;
using RoomieSync.Services.Export;
using RoomieSync.Services.Households;
using RoomieSync.Services.Maintenance;
using RoomieSync.Services.Notifications;
using RoomieSync.Services.Receipts;
using RoomieSync.Services.Reports;

namespace RoomieSync.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRoomieSyncServices(this IServiceCollection services, IConfiguration configuration)
    {
        var gemini = configuration.GetSection(GeminiOptions.Section).Get<GeminiOptions>() ?? new GeminiOptions();
        services.AddSingleton(gemini);

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IHouseholdService, HouseholdService>();
        services.AddSingleton<IExpenseService, ExpenseService>();
        services.AddSingleton<IDebtSimplifier, DebtSimplifier>();
        services.AddSingleton<IBalanceService, BalanceService>();
        services.AddSingleton<IChoreService, ChoreService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IValidationService, ReceiptValidationService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IReceiptStorageService>(_ => new ReceiptStorageService());

        if (gemini.UseMock || string.IsNullOrWhiteSpace(gemini.ResolveApiKey()))
            services.AddSingleton<IReceiptParser, MockReceiptParser>();
        else
            services.AddHttpClient<IReceiptParser, GeminiReceiptParser>();

        return services;
    }
}
