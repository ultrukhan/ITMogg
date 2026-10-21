namespace RoomieSync.Core.Models;

public sealed class UserAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MemberId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
}
