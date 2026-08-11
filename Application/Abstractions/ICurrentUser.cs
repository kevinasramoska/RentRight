namespace RentRight.Application.Abstractions;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string? UserId { get; }

    bool IsInRole(string roleName);
}
