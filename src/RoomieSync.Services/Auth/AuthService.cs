using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomieSync.Core.Abstractions.Repositories;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Common;
using RoomieSync.Core.Models;
using RoomieSync.Core.Security;
using RoomieSync.Services.Logging;

namespace RoomieSync.Services.Auth;

public sealed class AuthService : IAuthService
{
    private const int MaxFailedAttempts = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(60);

    private readonly IUserAccountRepository _accounts;
    private readonly IMemberRepository _members;
    private readonly IHouseholdRepository _households;
    private readonly IPasswordHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserAccountRepository accounts,
        IMemberRepository members,
        IHouseholdRepository households,
        IPasswordHasher? hasher = null,
        IClock? clock = null,
        ILogger<AuthService>? logger = null)
    {
        _accounts = accounts;
        _members = members;
        _households = households;
        _hasher = hasher ?? new Pbkdf2PasswordHasher();
        _clock = clock ?? new SystemClock();
        _logger = logger ?? NullLogger<AuthService>.Instance;
    }

    public async Task<LoginResult> LoginAsync(
        string email,
        string password,
        CancellationToken ct = default)
    {
        email = email?.Trim() ?? string.Empty;
        if (email.Length == 0 || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("Login rejected: empty email or password");
            return new LoginResult(LoginStatus.InvalidCredentials, AttemptsLeft: MaxFailedAttempts);
        }

        var maskedEmail = LogSanitizer.MaskEmail(email);
        _logger.LogDebug("Login attempt for {Email}", maskedEmail);

        var allAccounts = await _accounts.GetAllByEmailAsync(email, ct);
        if (allAccounts.Count == 0)
        {
            var single = await _accounts.GetByEmailAsync(email, ct);
            if (single is not null)
                allAccounts = new[] { single };
        }

        if (allAccounts.Count == 0)
        {
            _logger.LogWarning("Login failed for {Email}: account not found", maskedEmail);
            return new LoginResult(LoginStatus.InvalidCredentials, AttemptsLeft: MaxFailedAttempts);
        }

        var now = _clock.Now;
        var primaryAcc = allAccounts[0];

        if (primaryAcc.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            _logger.LogWarning("Login blocked for {Email}: account is locked for another {Seconds:0} s",
                maskedEmail, (lockedUntil - now).TotalSeconds);
            return new LoginResult(LoginStatus.LockedOut, LockoutRemaining: lockedUntil - now);
        }

        var validMemberships = new List<(Member Member, Household Household)>();

        foreach (var acc in allAccounts)
        {
            if (!_hasher.Verify(password, acc.PasswordHash, acc.PasswordSalt))
                continue;

            var member = await _members.GetByIdAsync(acc.MemberId, ct);
            if (member is not { IsActive: true })
                continue;

            var household = await _households.GetByIdAsync(member.HouseholdId, ct);
            if (household is not null)
            {
                if (acc.FailedAttempts > 0 || acc.LockedUntil is not null)
                {
                    acc.FailedAttempts = 0;
                    acc.LockedUntil = null;
                    await _accounts.UpdateAsync(acc, ct);
                }
                validMemberships.Add((member, household));
            }
        }

        if (validMemberships.Count > 0)
        {
            var first = validMemberships[0];
            _logger.LogInformation("Login succeeded: member {MemberId}, household {HouseholdId} ({Count} membership(s))",
                first.Member.Id, first.Household.Id, validMemberships.Count);
            return new LoginResult(
                LoginStatus.Success,
                first.Member,
                first.Household,
                AvailableMemberships: validMemberships);
        }

        primaryAcc.FailedAttempts++;
        if (primaryAcc.FailedAttempts >= MaxFailedAttempts)
        {
            primaryAcc.FailedAttempts = 0;
            primaryAcc.LockedUntil = now + LockoutDuration;
            await _accounts.UpdateAsync(primaryAcc, ct);
            _logger.LogWarning("Account {Email} locked for {Seconds} s after {Max} failed login attempts",
                maskedEmail, LockoutDuration.TotalSeconds, MaxFailedAttempts);
            return new LoginResult(LoginStatus.LockedOut, LockoutRemaining: LockoutDuration);
        }

        await _accounts.UpdateAsync(primaryAcc, ct);
        var attemptsLeft = Math.Max(0, MaxFailedAttempts - primaryAcc.FailedAttempts);
        _logger.LogWarning("Login failed for {Email}: wrong password, {AttemptsLeft} attempt(s) left",
            maskedEmail, attemptsLeft);
        return new LoginResult(LoginStatus.InvalidCredentials, AttemptsLeft: attemptsLeft);
    }

    public async Task SetCredentialsAsync(
        Guid memberId,
        string email,
        string password,
        CancellationToken ct = default)
    {
        email = email?.Trim() ?? string.Empty;
        if (!email.Contains('@') || email.Length < 5)
            throw new DomainException("Enter a valid email address.");

        if (string.IsNullOrEmpty(password) || password.Length < 6)
            throw new DomainException("Password must be at least 6 characters.");

        var targetMember = await _members.GetByIdAsync(memberId, ct)
            ?? throw new DomainException("Member not found.");

        var existingByEmail = await _accounts.GetAllByEmailAsync(email, ct);
        foreach (var acc in existingByEmail)
        {
            if (acc.MemberId == memberId) continue;

            var otherMember = await _members.GetByIdAsync(acc.MemberId, ct);
            if (otherMember is { IsActive: true } &&
                otherMember.HouseholdId == targetMember.HouseholdId)
            {
                throw new DomainException(
                    "This email is already used by another member in this household.");
            }
        }

        var (hash, salt) = _hasher.Hash(password);
        var current = await _accounts.GetByMemberIdAsync(memberId, ct);

        if (current is null)
        {
            await _accounts.AddAsync(new UserAccount
            {
                MemberId = memberId,
                Email = email,
                PasswordHash = hash,
                PasswordSalt = salt
            }, ct);
        }
        else
        {
            current.Email = email;
            current.PasswordHash = hash;
            current.PasswordSalt = salt;
            current.FailedAttempts = 0;
            current.LockedUntil = null;
            await _accounts.UpdateAsync(current, ct);
        }

        _logger.LogInformation("Credentials {Action} for member {MemberId}",
            current is null ? "created" : "updated", memberId);
    }

    public async Task<string?> GetEmailAsync(
        Guid memberId,
        CancellationToken ct = default)
        => (await _accounts.GetByMemberIdAsync(memberId, ct))?.Email;

    public async Task ConfirmMembershipAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var member = await _members.GetByIdAsync(memberId, ct);
        if (member is null) return;

        member.IsEmailConfirmed = true;
        await _members.UpdateAsync(member, ct);
    }

    public async Task<IReadOnlyList<(Household Household, Member Member)>> GetUserHouseholdsAsync(
        Guid memberId,
        CancellationToken ct = default)
    {
        var currentAcc = await _accounts.GetByMemberIdAsync(memberId, ct);
        if (currentAcc is null)
            return Array.Empty<(Household, Member)>();

        var result = new List<(Household, Member)>();
        var allAccounts = await _accounts.GetAllByEmailAsync(currentAcc.Email, ct);

        foreach (var acc in allAccounts)
        {
            var m = await _members.GetByIdAsync(acc.MemberId, ct);
            if (m is not { IsActive: true }) continue;

            var h = await _households.GetByIdAsync(m.HouseholdId, ct);
            if (h is not null)
                result.Add((h, m));
        }

        return result;
    }
}
