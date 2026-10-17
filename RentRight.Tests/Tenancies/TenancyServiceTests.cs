using System.Security.Cryptography;
using System.Text;
using RentRight.Application.Abstractions;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Tenancies;

public class TenancyServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static CreateTenancyCommand Valid => new(10, "tenant@example.test", new(2026, 10, 1), null);

    [Theory]
    [InlineData(false, "", CreateTenancyStatus.Unauthenticated)]
    [InlineData(true, "Tenant", CreateTenancyStatus.Forbidden)]
    [InlineData(true, "", CreateTenancyStatus.Forbidden)]
    [InlineData(true, "Admin", CreateTenancyStatus.Success)]
    [InlineData(true, "Landlord", CreateTenancyStatus.Success)]
    [InlineData(true, "Admin,Landlord,Tenant", CreateTenancyStatus.Success)]
    public async Task Invite_ActorMatrix_EnforcesCapabilitiesBeforeStore(bool authenticated, string roles, CreateTenancyStatus expected)
    {
        var store = new Store();
        var result = await Service(store, authenticated, roles).CreateAsync(Valid, default);
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == CreateTenancyStatus.Success, store.Invite is not null);
        if (store.Actor is not null) Assert.Equal(roles.Contains("Admin"), store.Actor.IsAdmin);
    }

    [Theory]
    [InlineData(null, "tenant", "2026-10-01", null, "PropertyId")]
    [InlineData(0, "tenant", "2026-10-01", null, "PropertyId")]
    [InlineData(-1, "tenant", "2026-10-01", null, "PropertyId")]
    [InlineData(10, "", "2026-10-01", null, "TenantId")]
    [InlineData(10, "   ", "2026-10-01", null, "TenantId")]
    [InlineData(10, "tenant", "0001-01-01", null, "StartDate")]
    [InlineData(10, "tenant", "2026-10-20", "2026-10-19", "EndDate")]
    [InlineData(10, "tenant", "2026-10-01", "2026-10-15", "EndDate")]
    public async Task Invite_InvalidInput_RejectsBeforePersistence(int? id, string tenant, string start, string? end, string field)
    {
        var store = new Store();
        var result = await Service(store).CreateAsync(new(id, tenant, DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end)), default);
        Assert.Equal(CreateTenancyStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors!, e => e.Field == field);
        Assert.Null(store.Invite);
    }

    [Fact]
    public async Task Invite_OverlongIdentifier_RejectsBeforePersistence()
    {
        var result = await Service(new Store()).CreateAsync(Valid with { TenantId = new string('x', 257) }, default);
        Assert.Equal(CreateTenancyStatus.ValidationFailed, result.Status);
    }

    [Theory]
    [InlineData("2026-10-01", "2026-10-16")]
    [InlineData("2026-11-01", null)]
    public async Task Invite_ValidBoundary_ProducesRandomOneTimeReceiptAndOnlyHashToStore(string start, string? end)
    {
        var store = new Store();
        using var source = new CancellationTokenSource();
        var service = Service(store);
        var first = await service.CreateAsync(Valid with { StartDate = DateOnly.Parse(start), EndDate = end is null ? null : DateOnly.Parse(end) }, source.Token);
        Assert.Equal(CreateTenancyStatus.Success, first.Status);
        Assert.Equal(64, first.Invitation!.Token.Length);
        Assert.Equal(Now.AddDays(7), first.Invitation.ExpiresUtc);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(first.Invitation.Token))), store.Invite!.TokenHash);
        Assert.NotEqual(first.Invitation.Token, store.Invite.TokenHash);
        Assert.Equal(Now, store.Invite.UtcNow);
        Assert.Equal("actor", store.Actor!.UserId);
        Assert.Equal(source.Token, store.Token);
        var second = await service.CreateAsync(Valid, default);
        Assert.NotEqual(first.Invitation.Token, second.Invitation!.Token);
    }

    [Theory]
    [InlineData(CreateTenancyStatus.PropertyNotFound)]
    [InlineData(CreateTenancyStatus.Conflict)]
    public async Task Invite_PersistenceFailure_DoesNotIssueToken(CreateTenancyStatus status)
    {
        var result = await Service(new Store { InviteStatus = status }).CreateAsync(Valid, default);
        Assert.Equal(status, result.Status);
        Assert.Null(result.Invitation);
    }

    [Theory]
    [InlineData(false, "Tenant", AcceptTenancyStatus.Unauthenticated)]
    [InlineData(true, "Landlord", AcceptTenancyStatus.Forbidden)]
    [InlineData(true, "Admin", AcceptTenancyStatus.Forbidden)]
    [InlineData(true, "Tenant", AcceptTenancyStatus.Success)]
    [InlineData(true, "Admin,Landlord,Tenant", AcceptTenancyStatus.Success)]
    public async Task Accept_ActorMatrix_RequiresExplicitTenantRole(bool authenticated, string roles, AcceptTenancyStatus expected)
    {
        var store = new Store();
        var result = await Service(store, authenticated, roles).AcceptAsync(new(new string('A', 64)), default);
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == AcceptTenancyStatus.Success, store.AcceptHash is not null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public async Task Accept_InvalidToken_IsConcealedWithoutQuery(string token)
    {
        var store = new Store();
        var result = await Service(store, roles: "Tenant").AcceptAsync(new(token), default);
        Assert.Equal(AcceptTenancyStatus.NotFound, result.Status);
        Assert.Null(store.AcceptHash);
    }

    [Theory]
    [InlineData(AcceptTenancyStatus.NotFound)]
    [InlineData(AcceptTenancyStatus.Conflict)]
    [InlineData(AcceptTenancyStatus.Success)]
    public async Task Accept_StoreOutcome_ForwardsHashActorClockCancellation(AcceptTenancyStatus expected)
    {
        var store = new Store { AcceptStatus = expected };
        using var source = new CancellationTokenSource();
        var result = await Service(store, roles: "Tenant").AcceptAsync(new(new string('A', 64)), source.Token);
        Assert.Equal(expected, result.Status);
        Assert.NotEqual(new string('A', 64), store.AcceptHash);
        Assert.Equal(Now, store.Now);
        Assert.Equal(source.Token, store.Token);
        Assert.True(store.Actor!.IsTenant);
    }

    [Theory]
    [InlineData(false, "", EndTenancyStatus.Unauthenticated)]
    [InlineData(true, "Tenant", EndTenancyStatus.Forbidden)]
    [InlineData(true, "Landlord", EndTenancyStatus.Success)]
    [InlineData(true, "Admin", EndTenancyStatus.Success)]
    [InlineData(true, "Admin,Landlord", EndTenancyStatus.Success)]
    public async Task End_ActorMatrix_EnforcesCapabilities(bool authenticated, string roles, EndTenancyStatus expected)
    {
        var store = new Store();
        var result = await Service(store, authenticated, roles).EndAsync(42, default);
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected == EndTenancyStatus.Success, store.EndId == 42);
    }

    [Theory]
    [InlineData(EndTenancyStatus.Success)]
    [InlineData(EndTenancyStatus.NotFound)]
    [InlineData(EndTenancyStatus.Conflict)]
    public async Task End_StoreOutcome_ForwardsClockAndCancellation(EndTenancyStatus expected)
    {
        var store = new Store { EndStatus = expected };
        using var source = new CancellationTokenSource();
        var result = await Service(store).EndAsync(42, source.Token);
        Assert.Equal(expected, result.Status);
        Assert.Equal(Now, store.Now);
        Assert.Equal(source.Token, store.Token);
    }

    [Theory]
    [InlineData(false, "", TenancyQueryStatus.Unauthenticated)]
    [InlineData(true, "", TenancyQueryStatus.Forbidden)]
    [InlineData(true, "Tenant", TenancyQueryStatus.Success)]
    [InlineData(true, "Landlord", TenancyQueryStatus.Success)]
    [InlineData(true, "Admin", TenancyQueryStatus.Success)]
    public async Task Queries_ActorMatrix_ScopesListAndDetail(bool authenticated, string roles, TenancyQueryStatus expected)
    {
        var store = new Store();
        var service = Service(store, authenticated, roles);
        var list = await service.ListAsync(2, 10, default);
        var detail = await service.GetAsync(42, default);
        Assert.Equal(expected, list.Status);
        Assert.Equal(expected, detail.Status);
        if (expected == TenancyQueryStatus.Success)
        {
            Assert.Equal(10, store.Skip);
            Assert.Equal(10, store.Take);
            Assert.Null(detail.Tenancy);
            Assert.Equal(DateOnly.FromDateTime(Now), store.Today);
        }
    }

    [Fact]
    public async Task GetCreateForm_Manager_HasBoundedOwnedPropertiesAndNoTenantDirectory()
    {
        var store = new Store();
        var result = await Service(store, roles: "Landlord").GetCreateFormAsync(default);
        Assert.Equal(TenancyQueryStatus.Success, result.Status);
        Assert.Equal(200, store.Take);
        Assert.Equal(DateOnly.FromDateTime(Now), result.Form!.DefaultStartDate);
    }

    private static TenancyService Service(Store store, bool authenticated = true, string roles = "Admin") => new(store, new User(authenticated, roles), new Clock());
    private sealed class User(bool authenticated, string roles) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => "actor";
        public bool IsInRole(string roleName) => roles.Split(',').Contains(roleName);
    }
    private sealed class Clock : IClock { public DateTime UtcNow => Now; }
    private sealed class Store : ITenancyStore
    {
        public InviteTenancyRecord? Invite;
        public TenancyActor? Actor;
        public string? AcceptHash;
        public int EndId, Skip, Take;
        public DateOnly Today;
        public DateTime Now;
        public CancellationToken Token;
        public CreateTenancyStatus InviteStatus = CreateTenancyStatus.Success;
        public AcceptTenancyStatus AcceptStatus = AcceptTenancyStatus.Success;
        public EndTenancyStatus EndStatus = EndTenancyStatus.Success;
        public Task<CreateTenancyStatus> InviteAsync(InviteTenancyRecord record, TenancyActor actor, CancellationToken cancellationToken)
        { Invite = record; Actor = actor; Token = cancellationToken; return Task.FromResult(InviteStatus); }
        public Task<AcceptTenancyStatus> AcceptAsync(string hash, TenancyActor actor, DateTime now, CancellationToken cancellationToken)
        { AcceptHash = hash; Actor = actor; Now = now; Token = cancellationToken; return Task.FromResult(AcceptStatus); }
        public Task<EndTenancyOutcome> EndAsync(int id, TenancyActor actor, DateTime now, CancellationToken cancellationToken, string? expectedVersion = null)
        { EndId = id; Actor = actor; Now = now; Token = cancellationToken; return Task.FromResult(new EndTenancyOutcome(EndStatus, EndStatus == EndTenancyStatus.Success)); }
        public Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(TenancyActor actor, DateTime now, int limit, CancellationToken cancellationToken)
        { Actor = actor; Take = limit; return Task.FromResult<IReadOnlyList<TenancySelectionOption>>([]); }
        public Task<IReadOnlyList<TenancyAdminRow>> ListAsync(TenancyActor actor, DateOnly today, int skip, int take, CancellationToken cancellationToken)
        { Actor = actor; Today = today; Skip = skip; Take = take; return Task.FromResult<IReadOnlyList<TenancyAdminRow>>([]); }
        public Task<int> CountAsync(TenancyActor actor, DateOnly today, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<TenancyAdminRow?> GetAsync(int id, TenancyActor actor, DateOnly today, CancellationToken cancellationToken)
        { Actor = actor; Today = today; return Task.FromResult<TenancyAdminRow?>(null); }
    }
}
