using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Services.Logging;

namespace RoomieSync.Services.Maintenance;

public sealed class BackupService(
    IHouseholdRepository households,
    IMemberRepository members,
    IUserAccountRepository accounts,
    IExpenseRepository expenses,
    ISettlementRepository settlements,
    IChoreRepository chores,
    INotificationRepository notifications,
    ILogger<BackupService>? logger = null) : IBackupService
{
    private readonly ILogger _log = logger ?? NullLogger<BackupService>.Instance;

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
        _log.LogInformation("Backup of household {HouseholdId} exported to {File}: {Members} member(s), {Expenses} expense(s)",
            householdId, LogSanitizer.FileName(filePath), package.Members.Count, package.Expenses.Count);
    }

    public async Task<HouseholdBackupPackage> RestoreBackupAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            _log.LogWarning("Backup restore failed: file {File} not found", LogSanitizer.FileName(filePath));
            throw new DomainException("Backup file not found.");
        }

        _log.LogInformation("Restoring backup from {File}", LogSanitizer.FileName(filePath));
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

        _log.LogInformation("Backup restored for household {HouseholdId}: {Members} member(s), {Expenses} expense(s), {Chores} chore(s)",
            package.Household.Id, package.Members.Count, package.Expenses.Count, package.Chores.Count);
        return package;
    }
}


public sealed class ReceiptStorageService : IReceiptStorageService
{
    public const long DefaultMaxStorageBytes = 2L * 1024 * 1024 * 1024;
    private readonly string _storageDir;
    private readonly ILogger _log;

    public ReceiptStorageService(string? customDirectory = null, long maxStorageBytes = DefaultMaxStorageBytes,
        ILogger<ReceiptStorageService>? logger = null)
    {
        _log = logger ?? NullLogger<ReceiptStorageService>.Instance;
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
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
        {
            _log.LogWarning("Receipt image rejected: file {File} does not exist", LogSanitizer.FileName(sourceFilePath));
            throw new DomainException("Receipt file does not exist.");
        }

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".png")
        {
            _log.LogWarning("Receipt image rejected: unsupported file type {Extension}", ext);
            throw new DomainException("Invalid file type. Only JPG and PNG are allowed.");
        }

        var info = new FileInfo(sourceFilePath);
        var currentUsage = GetCurrentStorageUsageBytes();
        if (currentUsage + info.Length > MaxStorageBytes)
        {
            _log.LogWarning("Receipt storage limit reached: {Used} + {Size} > {Max} bytes", currentUsage, info.Length, MaxStorageBytes);
            throw new DomainException("Receipt storage limit (2 GB) exceeded. Please clean up old receipts.");
        }

        var destFileName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}{ext}";
        var destPath = Path.Combine(_storageDir, destFileName);

        await using var src = File.OpenRead(sourceFilePath);
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst, ct);

        _log.LogInformation("Receipt image saved as {File} ({Bytes} bytes)", destFileName, info.Length);
        return destPath;
    }
}
