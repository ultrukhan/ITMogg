using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Core.Abstractions.Services;

public interface ISessionContext
{
    Member? CurrentMember { get; }
    Household? CurrentHousehold { get; }
    bool IsAuthenticated { get; }
    void SignIn(Member member, Household household);
    void UpdateHousehold(Household household);
    void UpdateMember(Member member);
    void SignOut();
    event EventHandler? Changed;
}

public enum LoginStatus { Success, InvalidCredentials, LockedOut }

public sealed record LoginResult(
    LoginStatus Status,
    Member? Member = null,
    Household? Household = null,
    TimeSpan? LockoutRemaining = null,
    int AttemptsLeft = 0,
    IReadOnlyList<(Member Member, Household Household)>? AvailableMemberships = null);

public interface IAuthService
{
    Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default);

    Task SetCredentialsAsync(
        Guid memberId,
        string email,
        string password,
        CancellationToken ct = default);

    Task<string?> GetEmailAsync(
        Guid memberId,
        CancellationToken ct = default);

    Task ConfirmMembershipAsync(
        Guid memberId,
        CancellationToken ct = default);

    Task<IReadOnlyList<(Household Household, Member Member)>> GetUserHouseholdsAsync(
        Guid memberId,
        CancellationToken ct = default);
}

public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string hash, string salt);
}

public sealed record NewHouseholdRequest(
    string HouseholdName,
    string AdminName,
    string AdminEmail,
    string AdminPassword,
    IReadOnlyList<string> MemberNames,
    string Currency = "UAH");

public interface IHouseholdService
{
    Task<bool> AnyHouseholdExistsAsync(CancellationToken ct = default);
    Task<(Household Household, Member Admin)> CreateHouseholdAsync(NewHouseholdRequest request, CancellationToken ct = default);
    Task<Household> RenameAsync(Guid householdId, string newName, CancellationToken ct = default);
    Task<Household> ChangeCurrencyAsync(Guid householdId, string currencyCode, CancellationToken ct = default);
    Task<IReadOnlyList<Member>> GetMembersAsync(Guid householdId, CancellationToken ct = default);
    Task<Member> AddMemberAsync(Guid householdId, string displayName, CancellationToken ct = default);
    Task<Member> RenameMemberAsync(Guid memberId, string newName, CancellationToken ct = default);
    Task<bool> CanDeleteMemberAsync(Guid memberId, CancellationToken ct = default);
    Task DeleteMemberAsync(Guid memberId, CancellationToken ct = default);
}

public sealed record NewExpenseRequest(
    string Title,
    decimal Amount,
    DateTime Date,
    ExpenseCategory Category,
    Guid PaidByMemberId,
    SplitMethod SplitMethod,
    IReadOnlyList<ShareInput> Shares,
    string? Merchant = null,
    string? ReceiptImagePath = null);

public sealed record ShareInput(Guid MemberId, decimal Value);

public sealed record DeleteExpenseResult(bool Deleted, int ApprovedCount, int RequiredCount);

public interface IExpenseService
{
    Task<IReadOnlyList<Expense>> GetExpensesAsync(Guid householdId, DateTime? from = null, DateTime? to = null, CancellationToken ct = default);
    Task<Expense?> GetExpenseAsync(Guid id, CancellationToken ct = default);
    ValidationResult Validate(NewExpenseRequest request);
    IReadOnlyList<ExpenseShare> CalculateShares(decimal amount, SplitMethod method, IReadOnlyList<ShareInput> inputs);
    Task<Expense> AddExpenseAsync(Guid householdId, NewExpenseRequest request, CancellationToken ct = default);
    Task<Expense> UpdateExpenseAsync(Guid expenseId, NewExpenseRequest request, CancellationToken ct = default);
    Task DeleteExpenseAsync(Guid id, CancellationToken ct = default);
    Task<DeleteExpenseResult> RequestOrApproveDeleteAsync(Guid expenseId, Guid currentMemberId, CancellationToken ct = default);
}

public sealed record SettlementActionResult(bool Settled, string Message);

public interface IBalanceService
{
    Task<IReadOnlyList<MemberBalance>> GetBalancesAsync(Guid householdId, CancellationToken ct = default);
    Task<SettlementPlan> GetSettlementPlanAsync(Guid householdId, CancellationToken ct = default);
    Task<Settlement> RecordSettlementAsync(Guid householdId, Transfer transfer, CancellationToken ct = default);
    Task<SettlementActionResult> InitiateOrConfirmSettlementAsync(Guid householdId, Transfer transfer, Guid actorMemberId, CancellationToken ct = default);
    bool HasPendingConfirmation(Guid householdId, Transfer transfer, out Guid confirmedByMemberId);
    Task RequestSettlementAsync(Guid householdId, Transfer transfer, CancellationToken ct = default);
    Task<IReadOnlyList<Settlement>> GetSettlementsAsync(Guid householdId, CancellationToken ct = default);
}

public interface IDebtSimplifier
{
    IReadOnlyList<Transfer> Simplify(IEnumerable<MemberBalance> balances);
}

public sealed record NewChoreRequest(string Title, Guid? AssigneeId, DateTime? Deadline, ChoreRotation Rotation);

public interface IChoreService
{
    Task<IReadOnlyList<Chore>> GetChoresAsync(Guid householdId, CancellationToken ct = default);
    Task<Chore> CreateChoreAsync(Guid householdId, NewChoreRequest request, CancellationToken ct = default);
    Task<Chore> UpdateChoreAsync(Chore chore, CancellationToken ct = default);
    Task<Chore> AssignAsync(Guid choreId, Guid? assigneeId, CancellationToken ct = default);
    Task<Chore?> ChangeStatusAsync(Guid choreId, ChoreStatus status, CancellationToken ct = default);
    Task DeleteChoreAsync(Guid choreId, CancellationToken ct = default);
    Guid? GetNextAssignee(IReadOnlyList<Member> members, Guid? currentAssignee);
}

public interface INotificationService
{
    Task<IReadOnlyList<Notification>> GetNotificationsAsync(Guid memberId, CancellationToken ct = default);
    Task NotifyHouseholdAsync(Guid householdId, NotificationType type, string message, Guid? exceptMemberId = null, CancellationToken ct = default);
    Task NotifyMemberAsync(Guid householdId, Guid memberId, NotificationType type, string message, CancellationToken ct = default);
    Task CheckOverdueChoresAsync(Guid householdId, CancellationToken ct = default);
    Task MarkAllReadAsync(Guid memberId, CancellationToken ct = default);
    event EventHandler? NotificationsChanged;
}

public sealed class ReceiptParseException : Exception
{
    public bool IsTimeout { get; }
    public ReceiptParseException(string message, bool isTimeout = false, Exception? inner = null) : base(message, inner) => IsTimeout = isTimeout;
}

public interface IReceiptParser
{
    Task<ParsedReceipt> ParseReceiptAsync(byte[] image, string mimeType, string? fileName = null, CancellationToken ct = default);
}

public interface IValidationService
{
    ValidationResult ValidateReceipt(ParsedReceipt receipt);
}

public sealed record ReportRow(DateTime Date, string Title, string Category, string PaidBy, decimal Amount, decimal YourShare);

public interface IExportService
{
    Task ExportExpensesCsvAsync(string path, IEnumerable<ReportRow> rows, CancellationToken ct = default);
    Task ExportExpensesPdfAsync(string path, string title, IEnumerable<ReportRow> rows, CancellationToken ct = default);
    Task ExportSettlementPlanCsvAsync(string path, IEnumerable<(string From, string To, decimal Amount)> transfers, CancellationToken ct = default);
    Task ExportSettlementPlanPdfAsync(string path, string title, IEnumerable<(string From, string To, decimal Amount)> transfers, CancellationToken ct = default);
}

public interface IReportService
{
    Task<HouseholdReportSummary> GetPeriodSummaryAsync(Guid householdId, DateTime from, DateTime to, CancellationToken ct = default);
}

public interface IBackupService
{
    Task ExportBackupAsync(Guid householdId, string filePath, CancellationToken ct = default);
    Task<HouseholdBackupPackage> RestoreBackupAsync(string filePath, CancellationToken ct = default);
}

public interface IReceiptStorageService
{
    long MaxStorageBytes { get; }
    long GetCurrentStorageUsageBytes();
    Task<string> SaveReceiptImageAsync(string sourceFilePath, CancellationToken ct = default);
}

public interface IClock
{
    DateTime Now { get; }
}
