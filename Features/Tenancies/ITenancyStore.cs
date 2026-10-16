using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Features.Tenancies;

// Scope is derived by the application service, never bound from HTTP input.
public sealed record TenancyActor(string UserId, bool IsAdmin, bool IsLandlord, bool IsTenant);
public sealed record InviteTenancyRecord(CreateTenancyCommand Command, string TokenHash, DateTime UtcNow);

public interface ITenancyStore
{
    Task<IReadOnlyList<TenancyAdminRow>> ListAsync(TenancyActor actor, DateOnly today, int skip, int take, CancellationToken cancellationToken);
    Task<int> CountAsync(TenancyActor actor, DateOnly today, CancellationToken cancellationToken);
    Task<TenancyAdminRow?> GetAsync(int id, TenancyActor actor, DateOnly today, CancellationToken cancellationToken);
    Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(TenancyActor actor, DateTime now, int limit, CancellationToken cancellationToken);
    Task<CreateTenancyStatus> InviteAsync(InviteTenancyRecord record, TenancyActor actor, CancellationToken cancellationToken);
    Task<AcceptTenancyStatus> AcceptAsync(string tokenHash, TenancyActor actor, DateTime now, CancellationToken cancellationToken);
    Task<EndTenancyOutcome> EndAsync(int id, TenancyActor actor, DateTime now, CancellationToken cancellationToken);
}
