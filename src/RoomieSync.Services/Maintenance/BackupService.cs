using System.Text.Json;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Maintenance;

/// <summary>
/// Модуль локальних резервних копій (backup/restore <= 5 секунд) та контролю сховища чеків до 2 ГБ (Бекенд 1 — Уляна Трухан).
/// </summary>
public sealed class BackupService(
    IHouseholdRepository households,
    IMemberRepository members,
    IUserAccountRepository accounts,
    IExpenseRepository expenses,
    ISettlementRepository settlements,
    IChoreRepository chores,
    INotificationRepository notifications) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task ExportBackupAsync(Guid householdId, string filePath, CancellationToken ct = default)
    {
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
        if (!File.Exists(filePath))
            throw new DomainException("Backup file not found.");

        await using var stream = File.OpenRead(filePath);
        var package = await JsonSerializer.DeserializeAsync<HouseholdBackupPackage>(stream, JsonOptions, ct)
                      ?? throw new DomainException("Invalid backup file format.");

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
}

/// <summary>
/// Локальне сховище прикріплених фотографій чеків з підтримкою до 2 ГБ (НФВ).
/// </summary>
public sealed class ReceiptStorageService : IReceiptStorageService
{
    public const long DefaultMaxStorageBytes = 2L * 1024 * 1024 * 1024; // 2 ГБ згідно з НФВ
    private readonly string _storageDir;

    public ReceiptStorageService(string? customDirectory = null, long maxStorageBytes = DefaultMaxStorageBytes)
    {
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
        if (!File.Exists(sourceFilePath))
            throw new DomainException("Receipt file does not exist.");

        var info = new FileInfo(sourceFilePath);
        var currentUsage = GetCurrentStorageUsageBytes();
        if (currentUsage + info.Length > MaxStorageBytes)
            throw new DomainException("Receipt storage limit (2 GB) exceeded. Please clean up old receipts.");

        var ext = Path.GetExtension(sourceFilePath);
        var destFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(_storageDir, destFileName);

        await using var src = File.OpenRead(sourceFilePath);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct);

        return destPath;
    }
}
