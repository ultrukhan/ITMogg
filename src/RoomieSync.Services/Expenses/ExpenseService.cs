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
    {
        InputValidator.RequireNonEmptyGuid(householdId, "Household ID");
        if (from.HasValue && to.HasValue && from.Value.Date > to.Value.Date)
            throw new DomainException("Start date cannot be after end date.");

        return expenses.GetByHouseholdAsync(householdId, from, to, ct);
    }

    public Task<Expense?> GetExpenseAsync(Guid id, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(id, "Expense ID");
        return expenses.GetByIdAsync(id, ct);
    }

    public IReadOnlyList<ExpenseShare> CalculateShares(decimal amount, SplitMethod method, IReadOnlyList<ShareInput> inputs)
        => SplitCalculator.Calculate(amount, method, inputs);

    public ValidationResult Validate(NewExpenseRequest r)
    {
        var result = new ValidationResult();
        if (r is null)
            return result.Error("Expense request is required.");

        if (string.IsNullOrWhiteSpace(r.Title))
        {
            result.Error("Expense title is required.");
        }
        else if (InputValidator.ContainsControlChars(r.Title))
        {
            result.Error("Expense title contains invalid control characters.");
        }
        else
        {
            var normalizedTitle = InputValidator.CollapseWhitespace(r.Title);
            if (normalizedTitle.Length < InputValidator.MinTitleLength)
                result.Error($"Expense title must be at least {InputValidator.MinTitleLength} characters.");
            else if (normalizedTitle.Length > InputValidator.MaxExpenseTitleLength)
                result.Error($"Expense title is too long (max {InputValidator.MaxExpenseTitleLength} characters).");
            else if (!normalizedTitle.Any(char.IsLetterOrDigit))
                result.Error("Expense title must contain at least one letter or digit.");
        }

        if (!string.IsNullOrWhiteSpace(r.Merchant))
        {
            if (InputValidator.ContainsControlChars(r.Merchant))
                result.Error("Merchant name contains invalid control characters.");
            else if (InputValidator.CollapseWhitespace(r.Merchant).Length > InputValidator.MaxMerchantLength)
                result.Error($"Merchant name is too long (max {InputValidator.MaxMerchantLength} characters).");
        }

        if (!string.IsNullOrWhiteSpace(r.ReceiptImagePath) && !InputValidator.IsValidReceiptPath(r.ReceiptImagePath))
            result.Error("Receipt file path is invalid or uses an unsupported file extension.");

        if (r.Amount < InputValidator.MinMoneyAmount)
            result.Error("Total amount must be greater than zero.");
        else if (r.Amount > InputValidator.MaxMoneyAmount)
            result.Error("Total amount is too large.");
        else if (!InputValidator.HasValidScale(r.Amount))
            result.Error("Total amount cannot have more than 2 decimal places.");

        if (r.Date.Date < InputValidator.MinAllowedDate)
            result.Error($"Date cannot be earlier than {InputValidator.MinAllowedDate:yyyy-MM-dd}.");
        else if (r.Date.Date > clock.Now.Date.AddDays(1))
            result.Error("Date cannot be in the future.");

        if (!Enum.IsDefined(r.Category))
            result.Error("Invalid expense category.");
        if (!Enum.IsDefined(r.SplitMethod))
            result.Error("Invalid split method.");

        if (r.PaidByMemberId == Guid.Empty)
            result.Error("Choose who paid.");

        if (r.Shares is null || r.Shares.Count == 0)
        {
            result.Error("Select at least one person to split with.");
        }
        else
        {
            if (r.Shares.Count > Household.MaxMembers)
                result.Error($"Cannot split an expense between more than {Household.MaxMembers} members.");
            if (r.Shares.Any(s => s.MemberId == Guid.Empty))
                result.Error("All split participants must have a valid member ID.");
            if (r.Shares.Select(s => s.MemberId).Distinct().Count() != r.Shares.Count)
                result.Error("Duplicate members are not allowed in expense shares.");
            if (r.Shares.Any(s => s.Value < 0))
                result.Error("Shares cannot be negative.");
            else if (r.Shares.Any(s => !InputValidator.HasValidScale(s.Value)))
                result.Error("Share values cannot have more than 2 decimal places.");
        }

        if (result.IsValid)
        {
            switch (r.SplitMethod)
            {
                case SplitMethod.ByPercentage:
                    if (r.Shares!.Any(s => s.Value <= 0m || s.Value > 100m))
                        result.Error("Each selected participant's percentage must be greater than 0% and at most 100%.");
                    var pct = r.Shares!.Sum(s => s.Value);
                    if (Math.Abs(pct - 100m) > 0.01m)
                        result.Error($"Percentages must add up to 100% (now {pct:0.##}%).");
                    break;
                case SplitMethod.ByExactAmount:
                    if (r.Shares!.Any(s => s.Value <= 0m))
                        result.Error("Each selected participant's exact amount must be greater than zero.");
                    var sum = Money.Round(r.Shares!.Sum(s => s.Value));
                    if (sum != Money.Round(r.Amount))
                        result.Error($"Exact amounts must add up to {MoneyFormat.Format(r.Amount)} (now {MoneyFormat.Format(sum)}).");
                    break;
            }
        }

        return result;
    }

    public async Task<Expense> AddExpenseAsync(Guid householdId, NewExpenseRequest request, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(householdId, "Household ID");
        var validation = Validate(request);
        if (!validation.IsValid) throw new DomainException(string.Join(Environment.NewLine, validation.Errors));

        var payer = await EnsureValidParticipantsAsync(householdId, request, ct);

        var expense = new Expense
        {
            HouseholdId = householdId,
            Title = InputValidator.CollapseWhitespace(request.Title),
            Merchant = string.IsNullOrWhiteSpace(request.Merchant) ? null : InputValidator.CollapseWhitespace(request.Merchant),
            Amount = Money.Round(request.Amount),
            Date = request.Date.Date,
            Category = request.Category,
            PaidByMemberId = request.PaidByMemberId,
            SplitMethod = request.SplitMethod,
            ReceiptImagePath = string.IsNullOrWhiteSpace(request.ReceiptImagePath) ? null : request.ReceiptImagePath.Trim(),
            CreatedAt = clock.Now,
            Shares = CalculateShares(request.Amount, request.SplitMethod, request.Shares).ToList()
        };
        await expenses.AddAsync(expense, ct);

        await notifications.NotifyHouseholdAsync(householdId, NotificationType.NewExpense,
            $"{payer.FirstName} added \"{expense.Title}\" — {MoneyFormat.Format(expense.Amount)}", request.PaidByMemberId, ct);
        return expense;
    }

    public async Task<Expense> UpdateExpenseAsync(Guid expenseId, NewExpenseRequest request, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(expenseId, "Expense ID");
        var existing = await expenses.GetByIdAsync(expenseId, ct) ?? throw new DomainException("Expense not found.");
        var validation = Validate(request);
        if (!validation.IsValid) throw new DomainException(string.Join(Environment.NewLine, validation.Errors));

        await EnsureValidParticipantsAsync(existing.HouseholdId, request, ct);

        existing.Title = InputValidator.CollapseWhitespace(request.Title);
        existing.Merchant = string.IsNullOrWhiteSpace(request.Merchant) ? null : InputValidator.CollapseWhitespace(request.Merchant);
        existing.Amount = Money.Round(request.Amount);
        existing.Date = request.Date.Date;
        existing.Category = request.Category;
        existing.PaidByMemberId = request.PaidByMemberId;
        existing.SplitMethod = request.SplitMethod;
        existing.ReceiptImagePath = string.IsNullOrWhiteSpace(request.ReceiptImagePath) ? null : request.ReceiptImagePath.Trim();
        existing.Shares = CalculateShares(request.Amount, request.SplitMethod, request.Shares).ToList();

        await expenses.UpdateAsync(existing, ct);
        return existing;
    }

    public Task DeleteExpenseAsync(Guid id, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(id, "Expense ID");
        return expenses.DeleteAsync(id, ct);
    }

    public async Task<DeleteExpenseResult> RequestOrApproveDeleteAsync(Guid expenseId, Guid currentMemberId, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(expenseId, "Expense ID");
        InputValidator.RequireNonEmptyGuid(currentMemberId, "Member ID");

        var expense = await expenses.GetByIdAsync(expenseId, ct) ?? throw new DomainException("Expense not found.");
        var actor = await members.GetByIdAsync(currentMemberId, ct) ?? throw new DomainException("Member not found.");

        if (!actor.IsActive || actor.HouseholdId != expense.HouseholdId)
            throw new DomainException("Only active members of this household can approve deleting its expenses.");

        var requiredIds = expense.RequiredApprovers;
        var isInitiation = expense.DeleteApprovedBy.Count == 0;

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

    private async Task<Member> EnsureValidParticipantsAsync(Guid householdId, NewExpenseRequest request, CancellationToken ct)
    {
        var activeMembers = await members.GetByHouseholdAsync(householdId, includeInactive: false, ct);
        var byId = activeMembers.ToDictionary(m => m.Id);

        if (!byId.TryGetValue(request.PaidByMemberId, out var payer))
            throw new DomainException("The selected payer is not an active member of this household.");

        if (request.Shares.Any(s => !byId.ContainsKey(s.MemberId)))
            throw new DomainException("All split participants must be active members of this household.");

        return payer;
    }
}
