using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Data.Seed;

namespace RoomieSync.Tests;

/// <summary>
/// Інтеграційний тест ADO.NET-репозиторіїв на справжньому PostgreSQL.
/// Запускається лише якщо задано змінну ROOMIESYNC_TEST_PG (рядок підключення до ПОРОЖНЬОЇ тестової бази).
/// </summary>
public class PostgresIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("ROOMIESYNC_TEST_PG");

    [Fact]
    public async Task Full_flow_on_postgres()
    {
        if (string.IsNullOrEmpty(ConnectionString)) return; // пропускаємо без бази

        using var sp = TestHost.Build(settings: new()
        {
            ["Data:Provider"] = "Postgres",
            ["ConnectionStrings:RoomieSync"] = ConnectionString
        });
        var auth = sp.GetRequiredService<IAuthService>();
        var login = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
        Assert.Equal(LoginStatus.Success, login.Status);

        var h = login.Household!.Id;
        var balances = sp.GetRequiredService<IBalanceService>();
        var plan = await balances.GetSettlementPlanAsync(h);
        Assert.Equal(0m, plan.Balances.Sum(b => b.Balance));
        Assert.Contains(plan.Balances, b => b.MemberId == login.Member!.Id && b.Balance == 124.50m);

        var expenses = await sp.GetRequiredService<IExpenseService>().GetExpensesAsync(h);
        Assert.NotEmpty(expenses);
        Assert.All(expenses, e => Assert.Equal(e.Amount, e.Shares.Sum(s => s.Amount)));

        var chores = await sp.GetRequiredService<IChoreService>().GetChoresAsync(h);
        var next = await sp.GetRequiredService<IChoreService>().ChangeStatusAsync(chores.First(c => c.Status != Core.Models.ChoreStatus.Done).Id, Core.Models.ChoreStatus.Done);
        Assert.NotNull(next);

        Assert.NotEmpty(await sp.GetRequiredService<INotificationService>().GetNotificationsAsync(login.Member!.Id));
    }
}
