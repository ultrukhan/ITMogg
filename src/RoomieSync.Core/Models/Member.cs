namespace RoomieSync.Core.Models;

public sealed class Member
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HouseholdId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public MemberRole Role { get; set; } = MemberRole.Member;
    public string AvatarColor { get; set; } = "#F07B3F";
    public DateTime JoinedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;
    public bool IsEmailConfirmed { get; set; } = true;

    public bool IsAdmin => Role == MemberRole.Admin;

    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant()
            };
        }
    }

    public string FirstName => DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? DisplayName;

    public override string ToString() => DisplayName;
}
