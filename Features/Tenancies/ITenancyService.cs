using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Features.Tenancies;

public interface ITenancyService
{
    Task<AcceptTenancyOutcome> AcceptAsync(AcceptTenancyCommand command, CancellationToken cancellationToken);
    Task<GetTenancyOutcome> GetAsync(int id, CancellationToken cancellationToken);
    Task<ListTenanciesPageOutcome> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<ListTenanciesOutcome> ListForAdministrationAsync(
        CancellationToken cancellationToken);

    Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(
        CancellationToken cancellationToken);

    Task<CreateTenancyOutcome> CreateAsync(
        CreateTenancyCommand command,
        CancellationToken cancellationToken);

    Task<EndTenancyOutcome> EndAsync(
        int tenancyId,
        CancellationToken cancellationToken, string? expectedVersion = null);
}
