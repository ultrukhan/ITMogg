using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Expenses;

public sealed class ExpenseService(
    IExpenseRepository expenses,
    IMemberRepository members,
    INotificationService notifications,
    IClock clock) : IExpenseService
{
    public Task<IReadOnlyList<Expense>> GetExpensesAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default)
        => expenses.GetByHouseholdAsync(householdId, from, to, ct);

    public Task<Expense?> GetExpenseAsync(Guid id, CancellationToken ct = default) => expenses.GetByIdAsync(id, ct);

    public IReadOnlyList<ExpenseShare> CalculateShares(decimal amount, SplitMethod method, IReadOnlyList<ShareInput> inputs)
        => SplitCalculator.Calculate(amount, method, inputs);

    public ValidationResult Validate(NewExpenseRequest r)
    {
        var result = new ValidationResult();
        if (string.IsNullOrWhiteSpace(r.Title)) result.Error("Expense title is required.");
        if (r.Title?.Length > 200) result.Error("Expense title is too long (max 200 characters).");
        if (r.Amount <= 0) result.Error("Total amount must be greater than zero.");
        if (r.Amount > 1_000_000) result.Error("Total amount is too large.");
        if (r.Date.Date > clock.Now.Date.AddDays(1)) result.Error("Date cannot be in the future.");
        if (r.PaidByMemberId == Guid.Empty) result.Error("Choose who paid.");
        if (r.Shares.Count == 0) result.Error("Select at least one person to split with.");
        if (r.Shares.Any(s => s.Value < 0)) result.Error("Shares cannot be negative.");

        if (result.IsValid)
        {
            switch (r.SplitMethod)
            {
                case SplitMethod.ByPercentage:
                    var pct = r.Shares.Sum(s => s.Value);
                    if (Math.Abs(pct - 100m) > 0.01m) result.Error($"Percentages must add up to 100% (now {pct:0.##}%).");
                    break;
                case SplitMethod.ByExactAmount:
                    var sum = Money.Round(r.Shares.Sum(s => s.Value));
                    if (sum != Money.Round(r.Amount)) result.Error($"Exact amounts must add up to {MoneyFormat.Format(r.Amount)} (now {MoneyFormat.Format(sum)}).");
                    break;
            }
        }
        return result;
    }

    public async Task<Expense> AddExpenseAsync(Guid householdId, NewExpenseRequest request, CancellationToken ct = default)
    {
        var validation = Validate(request);
        if (!validation.IsValid) throw new DomainException(string.Join(Environment.NewLine, validation.Errors));

        var expense = new Expense
        {
            HouseholdId = householdId,
            Title = request.Title.Trim(),
            Merchant = request.Merchant,
            Amount = Money.Round(request.Amount),
            Date = request.Date.Date,
            Category = request.Category,
            PaidByMemberId = request.PaidByMemberId,
            SplitMethod = request.SplitMethod,
            ReceiptImagePath = request.ReceiptImagePath,
            CreatedAt = clock.Now,
            Shares = CalculateShares(request.Amount, request.SplitMethod, request.Shares).ToList()
        };
        await expenses.AddAsync(expense, ct);

        var payer = await members.GetByIdAsync(request.PaidByMemberId, ct);
        await notifications.NotifyHouseholdAsync(householdId, NotificationType.NewExpense,
            $"{payer?.FirstName ?? "Someone"} added \"{expense.Title}\" — {MoneyFormat.Format(expense.Amount)}", request.PaidByMemberId, ct);
        return expense;
    }

    public async Task<Expense> UpdateExpenseAsync(Guid expenseId, NewExpenseRequest request, CancellationToken ct = default)
    {
        var existing = await expenses.GetByIdAsync(expenseId, ct) ?? throw new DomainException("Expense not found.");
        var validation = Validate(request);
        if (!validation.IsValid) throw new DomainException(string.Join(Environment.NewLine, validation.Errors));

        existing.Title = request.Title.Trim();
        existing.Merchant = request.Merchant;
        existing.Amount = Money.Round(request.Amount);
        existing.Date = request.Date.Date;
        existing.Category = request.Category;
        existing.PaidByMemberId = request.PaidByMemberId;
        existing.SplitMethod = request.SplitMethod;
        existing.ReceiptImagePath = request.ReceiptImagePath;
        existing.Shares = CalculateShares(request.Amount, request.SplitMethod, request.Shares).ToList();

        await expenses.UpdateAsync(existing, ct);
        return existing;
    }

    public Task DeleteExpenseAsync(Guid id, CancellationToken ct = default) => expenses.DeleteAsync(id, ct);

    public async Task<DeleteExpenseResult> RequestOrApproveDeleteAsync(Guid expenseId, Guid currentMemberId, CancellationToken ct = default)
    {
        var expense = await expenses.GetByIdAsync(expenseId, ct) ?? throw new DomainException("Expense not found.");
        var actor = await members.GetByIdAsync(currentMemberId, ct) ?? throw new DomainException("Member not found.");

        var requiredIds = expense.RequiredApprovers;
        var isInitiation = expense.DeleteApprovedBy.Count == 0;

        // Звичайний учасник не може сам ініціювати видалення чужої витрати (зокрема витрати власника)
        if (isInitiation && expense.PaidByMemberId != currentMemberId && !actor.IsAdmin)
            throw new DomainException("Only the person who paid for this expense (or the household admin) can initiate its deletion.");

        if (!requiredIds.Contains(currentMemberId) && !actor.IsAdmin)
            throw new DomainException("You are not a participant of this expense.");

        expense.DeleteApprovedBy.Add(currentMemberId);

        var approvedCount = requiredIds.Count(id => expense.DeleteApprovedBy.Contains(id));
        if (approvedCount >= requiredIds.Count)
        {
            await expenses.DeleteAsync(expenseId, ct);
            await notifications.NotifyHouseholdAsync(
                expense.HouseholdId,
                NotificationType.NewExpense,
                $"Expense \"{expense.Title}\" ({MoneyFormat.Format(expense.Amount)}) was deleted after all participants approved.",
                currentMemberId,
                ct);
            return new DeleteExpenseResult(true, approvedCount, requiredIds.Count);
        }

        await expenses.UpdateAsync(expense, ct);

        foreach (var participantId in requiredIds.Where(id => !expense.DeleteApprovedBy.Contains(id)))
        {
            await notifications.NotifyMemberAsync(
                expense.HouseholdId,
                participantId,
                NotificationType.NewExpense,
                $"{actor.FirstName} requested to delete \"{expense.Title}\" ({MoneyFormat.Format(expense.Amount)}). Open Expenses → Details → Delete to approve ({approvedCount}/{requiredIds.Count}).",
                ct);
        }

        return new DeleteExpenseResult(false, approvedCount, requiredIds.Count);
    }
}
