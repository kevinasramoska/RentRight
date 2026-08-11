namespace RentRight.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}
