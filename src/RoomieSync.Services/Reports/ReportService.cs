using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Reports;

public sealed class ReportService(IReportRepository reports) : IReportService
{
    public async Task<HouseholdReportSummary> GetPeriodSummaryAsync(Guid householdId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var byCategory = await reports.GetCategorySpendingAsync(householdId, from, to, ct);
        var byMonth = await reports.GetMonthlySpendingAsync(householdId, from, to, ct);
        var byMember = await reports.GetMemberSpendingAsync(householdId, from, to, ct);

        var totalSpent = byCategory.Sum(c => c.TotalAmount);
        var expenseCount = byCategory.Sum(c => c.ExpenseCount);
        var months = Math.Max(1, (to.Year - from.Year) * 12 + to.Month - from.Month + 1);
        var monthlyAverage = Math.Round(totalSpent / months, 2);
        var topCategory = byCategory.FirstOrDefault()?.Category;

        return new HouseholdReportSummary(
            totalSpent,
            monthlyAverage,
            expenseCount,
            topCategory,
            byCategory,
            byMonth,
            byMember);
    }
}
