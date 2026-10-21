using RoomieSync.Core.Abstractions.Services;
using RoomieSync.Core.Models;

namespace RoomieSync.Services.Auth;

public sealed class SessionContext : ISessionContext
{
    public Member? CurrentMember { get; private set; }
    public Household? CurrentHousehold { get; private set; }
    public bool IsAuthenticated => CurrentMember is not null && CurrentHousehold is not null;

    public event EventHandler? Changed;

    public void SignIn(Member member, Household household)
    {
        CurrentMember = member;
        CurrentHousehold = household;
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
        CurrentMember = null;
        CurrentHousehold = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
