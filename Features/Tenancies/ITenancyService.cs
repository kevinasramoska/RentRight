using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Features.Tenancies;

public interface ITenancyService
{
    Task<ListTenanciesOutcome> ListForAdministrationAsync(
        CancellationToken cancellationToken);

    Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(
        CancellationToken cancellationToken);

    Task<CreateTenancyOutcome> CreateAsync(
        CreateTenancyCommand command,
        CancellationToken cancellationToken);

    Task<EndTenancyOutcome> EndAsync(
        int tenancyId,
        CancellationToken cancellationToken);
}
