using RentRight.Application.Abstractions;

namespace RentRight.Application;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
