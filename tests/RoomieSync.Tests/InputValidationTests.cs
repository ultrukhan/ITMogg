using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Core.Security;
using RoomieSync.Services.Balances;
using RoomieSync.Services.Expenses;
using RoomieSync.Services.Maintenance;
using static RoomieSync.Tests.TestData;

namespace RoomieSync.Tests;

public sealed class InputValidationTests
{
    private static readonly byte[] PngHeaderBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2];

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@missinglocal.com")]
    [InlineData("missingdomain@")]
    [InlineData("two@@signs.com")]
    [InlineData(".leadingdot@domain.com")]
    [InlineData("trailingdot.@domain.com")]
    [InlineData("double..dot@domain.com")]
    [InlineData("space in@domain.com")]
    [InlineData("bad\nchar@domain.com")]
    [InlineData("bad$char@domain.com")]
    [InlineData("user@nodotdomain")]
    [InlineData("user@-badlabel.com")]
    [InlineData("user@badlabel-.com")]
    [InlineData("user@domain.12")]
    [InlineData("user@domain.c")]
    public void Invalid_email_addresses_are_rejected(string? email)
    {
        Assert.Throws<DomainException>(() => InputValidator.ValidateEmail(email));
    }

    [Fact]
    public void Oversized_email_and_local_part_are_rejected()
    {
        var longLocal = new string('a', 65) + "@lnu.edu.ua";
        var longTotal = "user@" + new string('a', 60) + "." + new string('b', 60) + "." + new string('c', 60) + "." + new string('d', 63) + ".com";
        var valid = InputValidator.ValidateEmail("  student.name+tag@lnu.edu.ua  ");

        Assert.True(
            !InputValidator.IsValidEmail(longLocal)
            && !InputValidator.IsValidEmail(longTotal)
            && valid == "student.name+tag@lnu.edu.ua");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Short1A")]
    [InlineData("alllowercase123")]
    [InlineData("ALLUPPERCASE123")]
    [InlineData("NoDigitsHereAtAll")]
    [InlineData("Has Space123")]
    [InlineData("Has\tControl123")]
    public void Weak_or_malformed_passwords_are_rejected(string? password)
    {
        Assert.Throws<DomainException>(() => InputValidator.ValidatePassword(password));
    }

    [Fact]
    public void Oversized_password_is_rejected_by_validator_and_hasher()
    {
        var tooLong = "Aa1" + new string('x', 130);
        var hasher = new Pbkdf2PasswordHasher();
        var (validHash, validSalt) = hasher.Hash("StrongPass123");

        var validatorThrew = ThrowsDomain(() => InputValidator.ValidatePassword(tooLong));
        var hasherThrew = ThrowsAny(() => hasher.Hash(tooLong));
        var verifyTooLong = hasher.Verify(tooLong, validHash, validSalt);
        var verifyCorruptHash = hasher.Verify("StrongPass123", "not-base64!!!", validSalt);
        var verifyShortSalt = hasher.Verify("StrongPass123", validHash, Convert.ToBase64String(new byte[] { 1, 2, 3 }));

        Assert.True(validatorThrew && hasherThrew && !verifyTooLong && !verifyCorruptHash && !verifyShortSalt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("A")]
    [InlineData("12345")]
    [InlineData("---")]
    [InlineData("Bad\nName")]
    [InlineData("<script>alert(1)</script>")]
    public void Invalid_household_and_member_names_are_rejected(string? rawName)
    {
        var hhRejected = ThrowsDomain(() => InputValidator.ValidateHouseholdName(rawName));
        var memRejected = ThrowsDomain(() => InputValidator.ValidateMemberName(rawName));
        Assert.True(hhRejected && memRejected);
    }

    [Fact]
    public void Names_collapse_whitespace_and_enforce_max_length()
    {
        var normalizedHh = InputValidator.ValidateHouseholdName("  Apt   #42  (Lviv)  ");
        var normalizedMember = InputValidator.ValidateMemberName("  Іван-Марія   О’Коннор  ");
        var oversizedHh = ThrowsDomain(() => InputValidator.ValidateHouseholdName(new string('A', 101)));
        var oversizedMem = ThrowsDomain(() => InputValidator.ValidateMemberName(new string('B', 101)));

        Assert.True(
            normalizedHh == "Apt #42 (Lviv)"
            && normalizedMember == "Іван-Марія О’Коннор"
            && oversizedHh
            && oversizedMem);
    }

    [Fact]
    public async Task Household_and_auth_services_reject_bypass_attempts()
    {
        using var sp = TestHost.Build(seed: false);
        var households = sp.GetRequiredService<IHouseholdService>();
        var auth = sp.GetRequiredService<IAuthService>();
        var memberRepo = sp.GetRequiredService<IMemberRepository>();

        var nullReq = await ThrowsDomainAsync(() => households.CreateHouseholdAsync(null!));
        var nullMembers = await ThrowsDomainAsync(() => households.CreateHouseholdAsync(
            new NewHouseholdRequest("Flat 10", "Maya", "maya@lnu.edu.ua", "Secret123", null!)));
        var badCurrency = await ThrowsDomainAsync(() => households.CreateHouseholdAsync(
            new NewHouseholdRequest("Flat 10", "Maya", "maya@lnu.edu.ua", "Secret123", ["Jake"], "BTC")));
        var spacedDuplicate = await ThrowsDomainAsync(() => households.CreateHouseholdAsync(
            new NewHouseholdRequest("Flat 10", "Maya   Torres", "maya@lnu.edu.ua", "Secret123", ["Maya Torres"])));

        var (h, _) = await households.CreateHouseholdAsync(
            new NewHouseholdRequest("Flat 10", "Maya Torres", "maya@lnu.edu.ua", "Secret123", ["Jake Miller"]));

        var ghostHhAdd = await ThrowsDomainAsync(() => households.AddMemberAsync(Guid.NewGuid(), "Olena"));
        var emptyGuidGet = await ThrowsDomainAsync(() => households.GetMembersAsync(Guid.Empty));
        var emptyCanDelete = await households.CanDeleteMemberAsync(Guid.Empty);
        var badChangeCurr = await ThrowsDomainAsync(() => households.ChangeCurrencyAsync(h.Id, "XYZ"));

        var jake = (await households.GetMembersAsync(h.Id)).Single(m => m.DisplayName == "Jake Miller");
        jake.IsActive = false;
        await memberRepo.UpdateAsync(jake);
        var inactiveCreds = await ThrowsDomainAsync(() => auth.SetCredentialsAsync(jake.Id, "jake@lnu.edu.ua", "Secret123"));
        var controlPassLogin = await auth.LoginAsync("maya@lnu.edu.ua", "Secret\0123");

        Assert.True(
            nullReq && nullMembers && badCurrency && spacedDuplicate
            && ghostHhAdd && emptyGuidGet && !emptyCanDelete && badChangeCurr
            && inactiveCreds && controlPassLogin.Status == LoginStatus.InvalidCredentials);
    }

    [Fact]
    public async Task Expense_service_and_split_calculator_enforce_strict_constraints()
    {
        using var sp = TestHost.Build(seed: false);
        var households = sp.GetRequiredService<IHouseholdService>();
        var expenses = sp.GetRequiredService<IExpenseService>();

        var (h1, admin1) = await households.CreateHouseholdAsync(
            new NewHouseholdRequest("House One", "Admin One", "a1@lnu.edu.ua", "Secret123", ["Peer One"]));
        var (h2, admin2) = await households.CreateHouseholdAsync(
            new NewHouseholdRequest("House Two", "Admin Two", "a2@lnu.edu.ua", "Secret123", ["Peer Two"]));
        var peer1 = (await households.GetMembersAsync(h1.Id)).Single(m => m.DisplayName == "Peer One");

        var nullReq = expenses.Validate(null!);
        var subCentAmount = expenses.Validate(new NewExpenseRequest(
            "Groceries", 10.123m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m)]));
        var duplicateShares = expenses.Validate(new NewExpenseRequest(
            "Groceries", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m), new ShareInput(admin1.Id, 1m)]));
        var ancientDate = expenses.Validate(new NewExpenseRequest(
            "Groceries", 20m, new DateTime(1995, 1, 1), ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m)]));
        var badMerchantAndReceipt = expenses.Validate(new NewExpenseRequest(
            "Groceries", 20m, DefaultTestNow.Date, (ExpenseCategory)99, admin1.Id, (SplitMethod)99,
            [new ShareInput(Guid.Empty, 1.999m)], new string('M', 205), "C:\\temp\\malware.exe"));
        var zeroExactShare = expenses.Validate(new NewExpenseRequest(
            "Groceries", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.ByExactAmount,
            [new ShareInput(admin1.Id, 20m), new ShareInput(peer1.Id, 0m)]));
        var zeroPctShare = expenses.Validate(new NewExpenseRequest(
            "Groceries", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.ByPercentage,
            [new ShareInput(admin1.Id, 100m), new ShareInput(peer1.Id, 0m)]));
        var controlTitle = expenses.Validate(new NewExpenseRequest(
            "Bad\nTitle", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m)]));
        var oneCharTitle = expenses.Validate(new NewExpenseRequest(
            "X", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m)]));
        var symbolsOnlyTitle = expenses.Validate(new NewExpenseRequest(
            "---", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m)]));

        var foreignShareErr = await ThrowsDomainAsync(() => expenses.AddExpenseAsync(h1.Id, new NewExpenseRequest(
            "Shared", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
            [new ShareInput(admin1.Id, 1m), new ShareInput(admin2.Id, 1m)])));
        var badRangeErr = await ThrowsDomainAsync(() => expenses.GetExpensesAsync(h1.Id, DefaultTestNow.Date, DefaultTestNow.Date.AddDays(-5)));
        var foreignDeleteErr = await ThrowsDomainAsync(async () =>
        {
            var created = await expenses.AddExpenseAsync(h1.Id, new NewExpenseRequest(
                "Milk & Bread", 20m, DefaultTestNow.Date, ExpenseCategory.Groceries, admin1.Id, SplitMethod.Equally,
                [new ShareInput(admin1.Id, 1m), new ShareInput(peer1.Id, 1m)], "  Silpo   Market  "));
            await expenses.RequestOrApproveDeleteAsync(created.Id, admin2.Id);
        });

        var splitBadScale = ThrowsDomain(() => SplitCalculator.Calculate(10.555m, SplitMethod.Equally, [new ShareInput(admin1.Id, 1m)]));
        var splitBadShare = ThrowsDomain(() => SplitCalculator.Calculate(10m, SplitMethod.Equally, [new ShareInput(Guid.Empty, 1m)]));

        Assert.True(
            !nullReq.IsValid && !subCentAmount.IsValid && !duplicateShares.IsValid && !ancientDate.IsValid
            && !badMerchantAndReceipt.IsValid && !zeroExactShare.IsValid && !zeroPctShare.IsValid
            && !controlTitle.IsValid && !oneCharTitle.IsValid && !symbolsOnlyTitle.IsValid
            && foreignShareErr && badRangeErr && foreignDeleteErr && splitBadScale && splitBadShare);
    }

    [Fact]
    public async Task Balance_chore_report_and_notification_services_reject_invalid_inputs()
    {
        using var sp = TestHost.Build(seed: false);
        var households = sp.GetRequiredService<IHouseholdService>();
        var balances = sp.GetRequiredService<IBalanceService>();
        var chores = sp.GetRequiredService<IChoreService>();
        var reports = sp.GetRequiredService<IReportService>();
        var notifications = sp.GetRequiredService<INotificationService>();

        var (h1, admin1) = await households.CreateHouseholdAsync(
            new NewHouseholdRequest("House Bal", "Admin One", "b1@lnu.edu.ua", "Secret123", ["Peer One"]));
        var (_, admin2) = await households.CreateHouseholdAsync(
            new NewHouseholdRequest("House Other", "Admin Two", "b2@lnu.edu.ua", "Secret123", ["Peer Two"]));
        var peer1 = (await households.GetMembersAsync(h1.Id)).Single(m => m.DisplayName == "Peer One");

        var selfSettle = await ThrowsDomainAsync(() => balances.RecordSettlementAsync(h1.Id, new Transfer(admin1.Id, admin1.Id, 10m)));
        var crossHouseholdSettle = await ThrowsDomainAsync(() => balances.RecordSettlementAsync(h1.Id, new Transfer(admin1.Id, admin2.Id, 10m)));
        var subCentSettle = await ThrowsDomainAsync(() => balances.RecordSettlementAsync(h1.Id, new Transfer(peer1.Id, admin1.Id, 10.999m)));
        var nullTransferPending = balances.HasPendingConfirmation(h1.Id, null!, out _);

        var simplifier = new DebtSimplifier();
        var nullSimplify = ThrowsDomain(() => simplifier.Simplify(null!));
        var badScaleSimplify = ThrowsDomain(() => simplifier.Simplify([new MemberBalance(admin1.Id, 1.234m)]));

        var pastChore = await ThrowsDomainAsync(() => chores.CreateChoreAsync(h1.Id,
            new NewChoreRequest("Clean Floor", peer1.Id, DefaultTestNow.AddDays(-2), ChoreRotation.Weekly)));
        var farFutureChore = await ThrowsDomainAsync(() => chores.CreateChoreAsync(h1.Id,
            new NewChoreRequest("Clean Floor", peer1.Id, DefaultTestNow.AddYears(10), ChoreRotation.Weekly)));
        var foreignAssigneeChore = await ThrowsDomainAsync(() => chores.CreateChoreAsync(h1.Id,
            new NewChoreRequest("Clean Floor", admin2.Id, DefaultTestNow.AddDays(2), ChoreRotation.Weekly)));
        var invalidRotationChore = await ThrowsDomainAsync(() => chores.CreateChoreAsync(h1.Id,
            new NewChoreRequest("Clean Floor", peer1.Id, DefaultTestNow.AddDays(2), (ChoreRotation)77)));
        var invalidStatusChange = await ThrowsDomainAsync(() => chores.ChangeStatusAsync(Guid.NewGuid(), (ChoreStatus)77));
        var nullMemberListNext = ThrowsDomain(() => chores.GetNextAssignee(null!, null));

        var badReportOrder = await ThrowsDomainAsync(() => reports.GetPeriodSummaryAsync(h1.Id, DefaultTestNow, DefaultTestNow.AddDays(-10)));
        var ancientReportDate = await ThrowsDomainAsync(() => reports.GetPeriodSummaryAsync(h1.Id, new DateTime(1990, 1, 1), DefaultTestNow));

        var emptyNotifMsg = await ThrowsDomainAsync(() => notifications.NotifyMemberAsync(h1.Id, admin1.Id, NotificationType.NewExpense, "  "));
        var longNotifMsg = await ThrowsDomainAsync(() => notifications.NotifyHouseholdAsync(h1.Id, NotificationType.NewExpense, new string('N', 501)));
        var badNotifType = await ThrowsDomainAsync(() => notifications.NotifyHouseholdAsync(h1.Id, (NotificationType)99, "Hello"));

        Assert.True(
            selfSettle && crossHouseholdSettle && subCentSettle && !nullTransferPending
            && nullSimplify && badScaleSimplify
            && pastChore && farFutureChore && foreignAssigneeChore && invalidRotationChore && invalidStatusChange && nullMemberListNext
            && badReportOrder && ancientReportDate
            && emptyNotifMsg && longNotifMsg && badNotifType);
    }

    [Fact]
    public async Task Export_backup_receipt_storage_and_receipt_validation_block_malformed_files()
    {
        using var sp = TestHost.Build();
        var export = sp.GetRequiredService<IExportService>();
        var backup = sp.GetRequiredService<IBackupService>();
        var validator = sp.GetRequiredService<IValidationService>();
        var parser = sp.GetRequiredService<IReceiptParser>();
        var auth = sp.GetRequiredService<IAuthService>();

        var tempDir = Path.Combine(Path.GetTempPath(), $"RoomieSyncStrict_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var wrongExtErr = await ThrowsDomainAsync(() => export.ExportExpensesCsvAsync(Path.Combine(tempDir, "out.txt"), Array.Empty<ReportRow>()));
            var emptyTitlePdfErr = await ThrowsDomainAsync(() => export.ExportExpensesPdfAsync(Path.Combine(tempDir, "out.pdf"), "  ", Array.Empty<ReportRow>()));

            var csvPath = Path.Combine(tempDir, "formula.csv");
            await export.ExportExpensesCsvAsync(csvPath,
            [
                new ReportRow(DefaultTestNow.Date, "=CMD|' /C calc'!A0", "Other", "Maya", 15m, 5m)
            ]);
            var csvContent = await File.ReadAllTextAsync(csvPath);
            var formulaNeutralized = csvContent.Contains("'=CMD|", StringComparison.Ordinal);

            var storage = new ReceiptStorageService(Path.Combine(tempDir, "receipts"));
            var badLimitErr = ThrowsDomain(() => _ = new ReceiptStorageService(tempDir, 0));

            var fakeJpg = Path.Combine(tempDir, "disguised.jpg");
            await File.WriteAllBytesAsync(fakeJpg, "MZ-not-a-real-jpeg"u8.ToArray());
            var fakeSignatureErr = await ThrowsDomainAsync(() => storage.SaveReceiptImageAsync(fakeJpg));

            var validPng = Path.Combine(tempDir, "valid.png");
            await File.WriteAllBytesAsync(validPng, PngHeaderBytes);
            var savedPng = await storage.SaveReceiptImageAsync(validPng);

            var emptyPayloadErr = await ThrowsDomainAsync(() => parser.ParseReceiptAsync(Array.Empty<byte>(), "image/png"));
            var badMimeErr = await ThrowsDomainAsync(() => parser.ParseReceiptAsync(PngHeaderBytes, "application/x-msdownload"));

            var invalidParsed = validator.ValidateReceipt(new ParsedReceipt
            {
                Merchant = "Bad\nMerchant",
                Total = 2_000_000m,
                Category = (ExpenseCategory)88,
                Items = { new ReceiptLineItem { Name = "  ", Price = 5m } }
            });

            var malformedJsonPath = Path.Combine(tempDir, "broken.json");
            await File.WriteAllTextAsync(malformedJsonPath, "{ not valid json ");
            var malformedBackupErr = await ThrowsDomainAsync(() => backup.RestoreBackupAsync(malformedJsonPath));

            var login = await auth.LoginAsync("maya@roomiesync.app", "Demo1234!");
            var goodBackupPath = Path.Combine(tempDir, "good.json");
            await backup.ExportBackupAsync(login.Household!.Id, goodBackupPath);

            var tamperedPackage = JsonSerializer.Deserialize<HouseholdBackupPackage>(
                await File.ReadAllTextAsync(goodBackupPath),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            tamperedPackage.Expenses[0].Amount = -999m;
            var tamperedPath = Path.Combine(tempDir, "tampered.json");
            await File.WriteAllTextAsync(tamperedPath, JsonSerializer.Serialize(
                tamperedPackage,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var tamperedBackupErr = await ThrowsDomainAsync(() => backup.RestoreBackupAsync(tamperedPath));

            Assert.True(
                wrongExtErr && emptyTitlePdfErr && formulaNeutralized
                && badLimitErr && fakeSignatureErr && File.Exists(savedPng)
                && emptyPayloadErr && badMimeErr && !invalidParsed.IsValid
                && malformedBackupErr && tamperedBackupErr);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static bool ThrowsDomain(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (DomainException)
        {
            return true;
        }
    }

    private static bool ThrowsAny(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<bool> ThrowsDomainAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (DomainException)
        {
            return true;
        }
    }
}
