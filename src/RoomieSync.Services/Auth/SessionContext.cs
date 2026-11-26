using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Auth;

public sealed class SessionContext(ILogger<SessionContext>? logger = null) : ISessionContext
{
    private readonly ILogger _logger = logger ?? NullLogger<SessionContext>.Instance;

    public Member? CurrentMember { get; private set; }
    public Household? CurrentHousehold { get; private set; }
    public bool IsAuthenticated => CurrentMember is not null && CurrentHousehold is not null;

    public event EventHandler? Changed;

    public void SignIn(Member member, Household household)
    {
        CurrentMember = member;
        CurrentHousehold = household;
        _logger.LogInformation("User session started: member {MemberId} in household {HouseholdId}", member.Id, household.Id);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateHousehold(Household household)
    {
        CurrentHousehold = household;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateMember(Member member)
    {
        if (CurrentMember?.Id != member.Id) return;
        CurrentMember = member;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut()
    {
        if (CurrentMember is not null)
            _logger.LogInformation("User session ended: member {MemberId} signed out", CurrentMember.Id);
        CurrentMember = null;
        CurrentHousehold = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
