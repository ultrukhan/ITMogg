using System.Text.Json;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Maintenance;

public sealed class BackupService(
    IHouseholdRepository households,
    IMemberRepository members,
    IUserAccountRepository accounts,
    IExpenseRepository expenses,
    ISettlementRepository settlements,
    IChoreRepository chores,
    INotificationRepository notifications) : IBackupService
{
    private const long MaxBackupFileBytes = 50L * 1024L * 1024L;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task ExportBackupAsync(Guid householdId, string filePath, CancellationToken ct = default)
    {
        InputValidator.RequireNonEmptyGuid(householdId, "Household ID");
        filePath = InputValidator.ValidateFilePathWithExtension(filePath, ".json");

        var household = await households.GetByIdAsync(householdId, ct)
                        ?? throw new DomainException("Household not found for backup.");
        var memberList = await members.GetByHouseholdAsync(householdId, includeInactive: true, ct);
        var accountList = new List<UserAccount>();
        var notificationList = new List<Notification>();

        foreach (var m in memberList)
        {
            if (await accounts.GetByMemberIdAsync(m.Id, ct) is { } acc)
                accountList.Add(acc);
            var notes = await notifications.GetForMemberAsync(m.Id, 500, ct);
            notificationList.AddRange(notes);
        }

        var package = new HouseholdBackupPackage
        {
            CreatedAtUtc = DateTime.UtcNow,
            Household = household,
            Members = memberList.ToList(),
            Accounts = accountList,
            Expenses = (await expenses.GetByHouseholdAsync(householdId, ct: ct)).ToList(),
            Settlements = (await settlements.GetByHouseholdAsync(householdId, ct)).ToList(),
            Chores = (await chores.GetByHouseholdAsync(householdId, ct)).ToList(),
            Notifications = notificationList
        };

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, package, JsonOptions, ct);
    }

    public async Task<HouseholdBackupPackage> RestoreBackupAsync(string filePath, CancellationToken ct = default)
    {
        filePath = InputValidator.ValidateFilePathWithExtension(filePath, ".json");
        if (!File.Exists(filePath))
            throw new DomainException("Backup file not found.");

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length is <= 0 or > MaxBackupFileBytes)
            throw new DomainException("Backup file is empty or exceeds the 50 MB maximum size.");

        HouseholdBackupPackage? package;
        await using (var stream = File.OpenRead(filePath))
        {
            try
            {
                package = await JsonSerializer.DeserializeAsync<HouseholdBackupPackage>(stream, JsonOptions, ct);
            }
            catch (JsonException)
            {
                throw new DomainException("Invalid backup file format.");
            }
        }

        if (package is null)
            throw new DomainException("Invalid backup file format.");

        ValidateBackupPackage(package);

        var existingHousehold = await households.GetByIdAsync(package.Household.Id, ct);
        if (existingHousehold is null)
            await households.AddAsync(package.Household, ct);
        else
            await households.UpdateAsync(package.Household, ct);

        foreach (var m in package.Members)
        {
            if (await members.GetByIdAsync(m.Id, ct) is null)
                await members.AddAsync(m, ct);
            else
                await members.UpdateAsync(m, ct);
        }

        foreach (var acc in package.Accounts)
        {
            if (await accounts.GetByMemberIdAsync(acc.MemberId, ct) is null)
                await accounts.AddAsync(acc, ct);
            else
                await accounts.UpdateAsync(acc, ct);
        }

        foreach (var e in package.Expenses)
        {
            if (await expenses.GetByIdAsync(e.Id, ct) is null)
                await expenses.AddAsync(e, ct);
            else
                await expenses.UpdateAsync(e, ct);
        }

        var existingSettlementIds = (await settlements.GetByHouseholdAsync(package.Household.Id, ct))
            .Select(s => s.Id).ToHashSet();
        foreach (var s in package.Settlements.Where(s => !existingSettlementIds.Contains(s.Id)))
            await settlements.AddAsync(s, ct);

        foreach (var c in package.Chores)
        {
            if (await chores.GetByIdAsync(c.Id, ct) is null)
                await chores.AddAsync(c, ct);
            else
                await chores.UpdateAsync(c, ct);
        }

        return package;
    }

    private static void ValidateBackupPackage(HouseholdBackupPackage package)
    {
        if (package.SchemaVersion < 1 || package.Household is null
            || package.Members is null || package.Accounts is null
            || package.Expenses is null || package.Settlements is null
            || package.Chores is null || package.Notifications is null)
        {
            throw new DomainException("Backup package structure is incomplete or unsupported.");
        }

        InputValidator.RequireNonEmptyGuid(package.Household.Id, "Backup household ID");
        package.Household.Name = InputValidator.ValidateHouseholdName(package.Household.Name);
        package.Household.Currency = InputValidator.ValidateCurrencyCode(package.Household.Currency);

        var activeMembers = package.Members.Where(m => m is { IsActive: true }).ToList();
        if (activeMembers.Count < Household.MinMembers || activeMembers.Count > Household.MaxMembers)
            throw new DomainException($"Backup household must contain between {Household.MinMembers} and {Household.MaxMembers} active members.");

        if (!activeMembers.Any(m => m.IsAdmin))
            throw new DomainException("Backup household must include at least one active admin.");

        var memberIds = new HashSet<Guid>();
        foreach (var m in package.Members)
        {
            if (m is null || m.HouseholdId != package.Household.Id || !Enum.IsDefined(m.Role))
                throw new DomainException("Backup contains an invalid member record.");
            InputValidator.RequireNonEmptyGuid(m.Id, "Backup member ID");
            if (!memberIds.Add(m.Id))
                throw new DomainException("Backup contains duplicate member IDs.");
            m.DisplayName = InputValidator.ValidateMemberName(m.DisplayName);
        }

        if (activeMembers.Select(m => m.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != activeMembers.Count)
            throw new DomainException("Backup contains duplicate active member names.");

        foreach (var acc in package.Accounts)
        {
            if (acc is null || !memberIds.Contains(acc.MemberId)
                || string.IsNullOrWhiteSpace(acc.PasswordHash)
                || string.IsNullOrWhiteSpace(acc.PasswordSalt)
                || acc.FailedAttempts < 0)
            {
                throw new DomainException("Backup contains an invalid user account record.");
            }
            acc.Email = InputValidator.ValidateEmail(acc.Email);
        }

        foreach (var e in package.Expenses)
        {
            if (e is null || e.HouseholdId != package.Household.Id
                || !memberIds.Contains(e.PaidByMemberId)
                || !Enum.IsDefined(e.Category) || !Enum.IsDefined(e.SplitMethod)
                || e.Shares is null || e.Shares.Count == 0)
            {
                throw new DomainException("Backup contains an invalid expense record.");
            }
            InputValidator.RequireNonEmptyGuid(e.Id, "Backup expense ID");
            e.Title = InputValidator.ValidateChoreTitle(e.Title);
            InputValidator.ValidatePositiveMoney(e.Amount, "Backup expense amount");
            if (e.Shares.Any(s => s is null || !memberIds.Contains(s.MemberId) || s.Amount < 0m || !InputValidator.HasValidScale(s.Amount))
                || Money.Round(e.Shares.Sum(s => s.Amount)) != Money.Round(e.Amount))
            {
                throw new DomainException("Backup expense shares are invalid or do not match expense total.");
            }
        }

        foreach (var s in package.Settlements)
        {
            if (s is null || s.HouseholdId != package.Household.Id
                || !memberIds.Contains(s.FromMemberId) || !memberIds.Contains(s.ToMemberId)
                || s.FromMemberId == s.ToMemberId)
            {
                throw new DomainException("Backup contains an invalid settlement record.");
            }
            InputValidator.RequireNonEmptyGuid(s.Id, "Backup settlement ID");
            InputValidator.ValidatePositiveMoney(s.Amount, "Backup settlement amount");
        }

        foreach (var c in package.Chores)
        {
            if (c is null || c.HouseholdId != package.Household.Id
                || !Enum.IsDefined(c.Status) || !Enum.IsDefined(c.Rotation)
                || (c.AssigneeId is { } aid && !memberIds.Contains(aid)))
            {
                throw new DomainException("Backup contains an invalid chore record.");
            }
            InputValidator.RequireNonEmptyGuid(c.Id, "Backup chore ID");
            c.Title = InputValidator.ValidateChoreTitle(c.Title);
        }

        foreach (var n in package.Notifications)
        {
            if (n is null || n.HouseholdId != package.Household.Id
                || !memberIds.Contains(n.RecipientMemberId)
                || !Enum.IsDefined(n.Type))
            {
                throw new DomainException("Backup contains an invalid notification record.");
            }
            InputValidator.RequireNonEmptyGuid(n.Id, "Backup notification ID");
            n.Message = InputValidator.ValidateNotificationMessage(n.Message);
        }
    }
}


public sealed class ReceiptStorageService : IReceiptStorageService
{
    public const long DefaultMaxStorageBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _storageDir;

    public ReceiptStorageService(string? customDirectory = null, long maxStorageBytes = DefaultMaxStorageBytes)
    {
        if (maxStorageBytes <= 0)
            throw new DomainException("Maximum receipt storage limit must be positive.");

        MaxStorageBytes = maxStorageBytes;
        _storageDir = customDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RoomieSync",
            "Receipts");
        Directory.CreateDirectory(_storageDir);
    }

    public long MaxStorageBytes { get; }

    public long GetCurrentStorageUsageBytes()
    {
        if (!Directory.Exists(_storageDir)) return 0;
        return new DirectoryInfo(_storageDir)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    public async Task<string> SaveReceiptImageAsync(string sourceFilePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath)
            || sourceFilePath.Length > InputValidator.MaxReceiptPathLength
            || InputValidator.ContainsControlChars(sourceFilePath)
            || !File.Exists(sourceFilePath))
        {
            throw new DomainException("Receipt file does not exist or path is invalid.");
        }

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".png")
            throw new DomainException("Invalid file type. Only JPG and PNG are allowed.");

        var info = new FileInfo(sourceFilePath);
        if (info.Length <= 0)
            throw new DomainException("Receipt image file cannot be empty.");
        if (info.Length > InputValidator.MaxSingleReceiptBytes)
            throw new DomainException("Receipt image exceeds the 20 MB maximum file size.");

        var currentUsage = GetCurrentStorageUsageBytes();
        if (currentUsage + info.Length > MaxStorageBytes)
            throw new DomainException("Receipt storage limit (2 GB) exceeded. Please clean up old receipts.");

        await using var src = File.OpenRead(sourceFilePath);
        var header = new byte[8];
        var bytesRead = await src.ReadAsync(header.AsMemory(0, header.Length), ct);
        if (!InputValidator.HasValidImageSignature(header.AsSpan(0, bytesRead), ext))
            throw new DomainException("File content does not match a valid JPEG or PNG image signature.");

        src.Position = 0;
        var destFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(_storageDir, destFileName);

        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct);

        return destPath;
    }
}
