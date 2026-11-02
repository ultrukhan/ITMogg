using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Data.Seed;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public class PostgresIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("ROOMIESYNC_TEST_PG");

    [Fact]
    public async Task Full_flow_on_postgres()
    {
        var satisfied = string.IsNullOrEmpty(ConnectionString);
        if (!satisfied)
        {
            using var sp = TestHost.Build(settings: new()
            {
                ["Data:Provider"] = "Postgres",
                ["ConnectionStrings:RoomieSync"] = ConnectionString
            });
            var auth = sp.GetRequiredService<IAuthService>();
            var login = await auth.LoginAsync(DemoDataSeeder.DemoAdminEmail, DemoDataSeeder.DemoPassword);
            var h = login.Household!.Id;
            var plan = await sp.GetRequiredService<IBalanceService>().GetSettlementPlanAsync(h);
            var expenses = await sp.GetRequiredService<IExpenseService>().GetExpensesAsync(h);
            var chores = await sp.GetRequiredService<IChoreService>().GetChoresAsync(h);
            var next = await sp.GetRequiredService<IChoreService>().ChangeStatusAsync(
                chores.First(c => c.Status != Core.Models.ChoreStatus.Done).Id,
                Core.Models.ChoreStatus.Done);
            var notifications = await sp.GetRequiredService<INotificationService>().GetNotificationsAsync(login.Member!.Id);

            satisfied = login.Status == LoginStatus.Success
                && plan.Balances.Sum(b => b.Balance) == 0m
                && plan.Balances.Any(b => b.MemberId == login.Member!.Id && b.Balance == MayaSeedBalance)
                && expenses.Count > 0
                && expenses.All(e => e.Amount == e.Shares.Sum(s => s.Amount))
                && next is not null
                && notifications.Count > 0;
        }

        Assert.True(satisfied);
    }
}
