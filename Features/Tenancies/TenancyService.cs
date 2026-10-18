using System.Security.Cryptography;
using System.Text;
using RentRight.Application.Abstractions;
using RentRight.Authorization;
using RentRight.Features.Tenancies.Contracts;

namespace RentRight.Features.Tenancies;

public sealed class TenancyService(ITenancyStore store, ICurrentUser user, IClock clock) : ITenancyService
{
    private TenancyActor Actor => new(user.UserId!, user.IsInRole(AppRoles.Admin), user.IsInRole(AppRoles.Landlord), user.IsInRole(AppRoles.Tenant));
    private bool Authenticated => user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId);
    private bool CanManage => user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Landlord);
    private TenancyQueryStatus QueryStatus => !Authenticated ? TenancyQueryStatus.Unauthenticated
        : CanManage || user.IsInRole(AppRoles.Tenant) ? TenancyQueryStatus.Success : TenancyQueryStatus.Forbidden;

    public async Task<ListTenanciesOutcome> ListForAdministrationAsync(CancellationToken cancellationToken)
    {
        if (!Authenticated) return ListTenanciesOutcome.Unauthenticated();
        if (!CanManage) return ListTenanciesOutcome.Forbidden();
        return ListTenanciesOutcome.Success(await store.ListAsync(Actor, DateOnly.FromDateTime(clock.UtcNow), 0, 250, cancellationToken));
    }

    public async Task<ListTenanciesPageOutcome> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        if (QueryStatus != TenancyQueryStatus.Success) return new(QueryStatus);
        if (page is < 1 or > 10000 || pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
        var today = DateOnly.FromDateTime(clock.UtcNow);
        var items = await store.ListAsync(Actor, today, (page - 1) * pageSize, pageSize, cancellationToken);
        return new(TenancyQueryStatus.Success, new(items, await store.CountAsync(Actor, today, cancellationToken)));
    }

    public async Task<GetTenancyOutcome> GetAsync(int id, CancellationToken cancellationToken)
    {
        if (QueryStatus != TenancyQueryStatus.Success) return new(QueryStatus);
        return new(TenancyQueryStatus.Success, await store.GetAsync(id, Actor, DateOnly.FromDateTime(clock.UtcNow), cancellationToken));
    }

    public async Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(CancellationToken cancellationToken)
    {
        if (!Authenticated) return GetTenancyCreateFormOutcome.Unauthenticated();
        if (!CanManage) return GetTenancyCreateFormOutcome.Forbidden();
        return GetTenancyCreateFormOutcome.Success(new(DateOnly.FromDateTime(clock.UtcNow),
            await store.ListAvailablePropertiesAsync(Actor, clock.UtcNow, 200, cancellationToken)));
    }

    public async Task<CreateTenancyOutcome> CreateAsync(CreateTenancyCommand command, CancellationToken cancellationToken)
    {
        if (!Authenticated) return CreateTenancyOutcome.Unauthenticated();
        if (!CanManage) return CreateTenancyOutcome.Forbidden();
        var errors = new List<TenancyValidationError>();
        if (command.PropertyId is null or <= 0) errors.Add(new(nameof(command.PropertyId), "Select a valid property."));
        // TenantId is retained as the MVC field name for compatibility; it now holds an exact login identifier, never a user ID.
        if (string.IsNullOrWhiteSpace(command.TenantId) || command.TenantId.Length > 256)
            errors.Add(new(nameof(command.TenantId), "Enter an exact tenant account identifier (up to 256 characters)."));
        if (command.StartDate == default) errors.Add(new(nameof(command.StartDate), "Enter a valid start date."));
        if (command.EndDate < command.StartDate || command.EndDate < DateOnly.FromDateTime(clock.UtcNow))
            errors.Add(new(nameof(command.EndDate), "End date must be on or after the start date and today."));
        if (errors.Count > 0) return CreateTenancyOutcome.ValidationFailed(errors);

        var now = clock.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var status = await store.InviteAsync(new(command, HashToken(token), now), Actor, cancellationToken);
        return status == CreateTenancyStatus.Success
            ? CreateTenancyOutcome.Success(new(token, now.AddDays(7)))
            : new(status);
    }

    public async Task<AcceptTenancyOutcome> AcceptAsync(AcceptTenancyCommand command, CancellationToken cancellationToken)
    {
        if (!Authenticated) return new(AcceptTenancyStatus.Unauthenticated);
        if (!user.IsInRole(AppRoles.Tenant)) return new(AcceptTenancyStatus.Forbidden);
        if (command.Token is null || command.Token.Length != 64 || !command.Token.All(Uri.IsHexDigit))
            return new(AcceptTenancyStatus.NotFound);
        return new(await store.AcceptAsync(HashToken(command.Token), Actor, clock.UtcNow, cancellationToken));
    }

    public Task<EndTenancyOutcome> EndAsync(int tenancyId, CancellationToken cancellationToken, string? expectedVersion = null)
    {
        if (!Authenticated) return Task.FromResult(EndTenancyOutcome.Unauthenticated());
        if (!CanManage) return Task.FromResult(EndTenancyOutcome.Forbidden());
        return store.EndAsync(tenancyId, Actor, clock.UtcNow, cancellationToken, expectedVersion);
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
