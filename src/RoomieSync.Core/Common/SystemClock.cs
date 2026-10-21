using RoomieSync.Core.Abstractions.Services;

namespace RoomieSync.Core.Common;

public sealed class SystemClock : IClock
{
    public DateTime Now => DateTime.Now;
}
