using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Households;

public sealed class HouseholdService(
    IHouseholdRepository households,
    IMemberRepository members,
    IChoreRepository chores,
    IAuthService auth,
    IBalanceService balances,
    IClock clock) : IHouseholdService
{
    private static readonly string[] Palette =
    {
        "#E07A5F", "#3B82F6", "#2E9E5B", "#8B5CF6", "#F5B82E", "#EC4899"
    };

    public Task<bool> AnyHouseholdExistsAsync(CancellationToken ct = default)
        => households.AnyAsync(ct);

    public async Task<(Household Household, Member Admin)> CreateHouseholdAsync(
        NewHouseholdRequest request,
        CancellationToken ct = default)
    {
        var name = request.HouseholdName?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new DomainException("Household name is required.");

        if (await households.GetByNameAsync(name, ct) is not null)
            throw new DomainException("A household with this name already exists.");

        var adminName = request.AdminName?.Trim() ?? string.Empty;
        if (adminName.Length == 0)
            throw new DomainException("Your name is required.");

        var adminEmail = request.AdminEmail?.Trim() ?? string.Empty;
        if (!adminEmail.Contains('@') || adminEmail.Length < 5)
            throw new DomainException("Enter a valid email address.");

        var adminPassword = request.AdminPassword ?? string.Empty;
        if (adminPassword.Length < 6)
            throw new DomainException("Password must be at least 6 characters.");

        var memberNames = request.MemberNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToList();

        var total = memberNames.Count + 1;
        if (total < Household.MinMembers)
            throw new DomainException(
                $"A household needs at least {Household.MinMembers} members (including you).");
        if (total > Household.MaxMembers)
            throw new DomainException(
                $"A household can have at most {Household.MaxMembers} members.");

        var allNames = new[] { adminName }.Concat(memberNames).ToList();
        if (allNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != allNames.Count)
            throw new DomainException(
                "Cannot create two member profiles with identical names in the same household.");

        var currency = string.IsNullOrWhiteSpace(request.Currency)
            ? "UAH"
            : request.Currency.Trim().ToUpperInvariant();

        var now = clock.Now;
        var household = new Household { Name = name, Currency = currency, CreatedAt = now };
        MoneyFormat.SetCurrency(currency);
        await households.AddAsync(household, ct);

        var admin = new Member
        {
            HouseholdId = household.Id,
            DisplayName = adminName,
            Role = MemberRole.Admin,
            AvatarColor = Palette[0],
            JoinedAt = now,
            IsEmailConfirmed = true
        };
        await members.AddAsync(admin, ct);
        await auth.SetCredentialsAsync(admin.Id, adminEmail, adminPassword, ct);

        for (var i = 0; i < memberNames.Count; i++)
        {
            await members.AddAsync(new Member
            {
                HouseholdId = household.Id,
                DisplayName = memberNames[i],
                AvatarColor = Palette[(i + 1) % Palette.Length],
                JoinedAt = now.AddMilliseconds(i + 1)
            }, ct);
        }

        return (household, admin);
    }

    public async Task<Household> RenameAsync(
        Guid householdId,
        string newName,
        CancellationToken ct = default)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            throw new DomainException("Household name is required.");

        var existing = await households.GetByNameAsync(newName, ct);
        if (existing is not null && existing.Id != householdId)
            throw new DomainException("A household with this name already exists.");

        var h = await households.GetByIdAsync(householdId, ct)
            ?? throw new DomainException("Household not found.");

        h.Name = newName;
        await households.UpdateAsync(h, ct);
        return h;
    }

    public async Task<Household> ChangeCurrencyAsync(
        Guid householdId,
        string currencyCode,
        CancellationToken ct = default)
    {
        currencyCode = string.IsNullOrWhiteSpace(currencyCode)
            ? "UAH"
            : currencyCode.Trim().ToUpperInvariant();

        var h = await households.GetByIdAsync(householdId, ct)
            ?? throw new DomainException("Household not found.");

        h.Currency = currencyCode;
        MoneyFormat.SetCurrency(currencyCode);
        await households.UpdateAsync(h, ct);
        return h;
    }

    public Task<IReadOnlyList<Member>> GetMembersAsync(
        Guid householdId,
        CancellationToken ct = default)
        => members.GetByHouseholdAsync(householdId, false, ct);

    public async Task<Member> AddMemberAsync(
        Guid householdId,
        string displayName,
        CancellationToken ct = default)
    {
        displayName = displayName.Trim();
        if (displayName.Length == 0)
            throw new DomainException("Member name is required.");

        var current = await members.GetByHouseholdAsync(householdId, false, ct);
        if (current.Count >= Household.MaxMembers)
            throw new DomainException(
                $"A household can have at most {Household.MaxMembers} members.");

        if (current.Any(m => string.Equals(
            m.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("A member with this name already exists.");

        var member = new Member
        {
            HouseholdId = householdId,
            DisplayName = displayName,
            AvatarColor = Palette[current.Count % Palette.Length],
            JoinedAt = clock.Now
        };
        await members.AddAsync(member, ct);
        return member;
    }

    public async Task<Member> RenameMemberAsync(
        Guid memberId,
        string newName,
        CancellationToken ct = default)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            throw new DomainException("Member name is required.");

        var m = await members.GetByIdAsync(memberId, ct)
            ?? throw new DomainException("Member not found.");

        var current = await members.GetByHouseholdAsync(m.HouseholdId, false, ct);
        if (current.Any(x => x.Id != memberId && string.Equals(
            x.DisplayName, newName, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("A member with this name already exists.");

        m.DisplayName = newName;
        await members.UpdateAsync(m, ct);
        return m;
    }

    public async Task<bool> CanDeleteMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var m = await members.GetByIdAsync(memberId, ct);
        if (m is null || m.IsAdmin) return false;

        var allBalances = await balances.GetBalancesAsync(m.HouseholdId, ct);
        var balance = allBalances.FirstOrDefault(b => b.MemberId == memberId)?.Balance ?? 0m;
        if (Math.Abs(balance) >= 0.01m) return false;

        var householdChores = await chores.GetByHouseholdAsync(m.HouseholdId, ct);
        var hasOpenChores = householdChores.Any(
            c => c.AssigneeId == memberId && c.Status != ChoreStatus.Done);
        return !hasOpenChores;
    }

    public async Task DeleteMemberAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        if (!await CanDeleteMemberAsync(memberId, ct))
            throw new DomainException(
                "Cannot delete a member with active debts, open chores, or the household admin.");

        var m = await members.GetByIdAsync(memberId, ct)
            ?? throw new DomainException("Member not found.");

        var count = (await members.GetByHouseholdAsync(m.HouseholdId, false, ct)).Count;
        if (count <= Household.MinMembers)
            throw new DomainException(
                $"A household needs at least {Household.MinMembers} members.");

        m.IsActive = false;
        await members.UpdateAsync(m, ct);
    }
}
