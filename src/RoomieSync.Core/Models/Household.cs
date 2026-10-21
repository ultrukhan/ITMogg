namespace RoomieSync.Core.Models;

public sealed class Household
{
    public const int MinMembers = 2;
    public const int MaxMembers = 6;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Currency { get; set; } = "UAH";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
