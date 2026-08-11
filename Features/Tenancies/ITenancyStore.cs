using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;

namespace RentRight.Features.Tenancies;

public interface ITenancyStore
{
    Task<IReadOnlyList<TenancyAdminRow>> ListForAdministrationAsync(
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(
        int maximumResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TenancySelectionOption>> ListTenantAccountsAsync(
        int maximumResults,
        CancellationToken cancellationToken);

    Task<bool> PropertyExistsAsync(int propertyId, CancellationToken cancellationToken);

    Task<bool> TenantAccountExistsAsync(
        string tenantId,
        CancellationToken cancellationToken);

    Task<bool> HasActiveTenancyAsync(
        int propertyId,
        CancellationToken cancellationToken);

    Task<CreateTenancyPersistenceResult> CreateAsync(
        CreateTenancyRecord tenancy,
        CancellationToken cancellationToken);

    Task<TenancyEndState?> GetForEndAsync(
        int tenancyId,
        CancellationToken cancellationToken);

    Task<EndTenancyPersistenceResult> SaveEndAsync(
        int tenancyId,
        DateOnly endDate,
        CancellationToken cancellationToken);
}

public sealed record CreateTenancyRecord(
    int PropertyId,
    string TenantId,
    DateOnly StartDate,
    DateOnly? EndDate);

public enum CreateTenancyPersistenceResult
{
    Success,
    ActiveTenancyConflict
}

public sealed record TenancyEndState(
    int Id,
    DateOnly StartDate,
    TenancyStatus Status);

public enum EndTenancyPersistenceResult
{
    Success,
    Conflict
}
