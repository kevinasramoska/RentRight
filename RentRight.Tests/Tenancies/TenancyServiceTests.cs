using RentRight.Application.Abstractions;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Tenancies;

public class TenancyServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, false, TenancyQueryStatus.Unauthenticated)]
    [InlineData(true, false, TenancyQueryStatus.Forbidden)]
    public async Task ListForAdministration_UserWithoutAdminAccess_ReturnsAuthorizationOutcome(
        bool isAuthenticated,
        bool isAdministrator,
        TenancyQueryStatus expectedStatus)
    {
        var service = CreateService(
            new FakeTenancyStore(),
            new FakeCurrentUser(isAuthenticated, isAdministrator));

        var outcome = await service.ListForAdministrationAsync(CancellationToken.None);

        Assert.Equal(expectedStatus, outcome.Status);
    }

    [Fact]
    public async Task ListForAdministration_AdminUser_BoundsResultSet()
    {
        var store = new FakeTenancyStore();
        var service = CreateService(store);

        var outcome = await service.ListForAdministrationAsync(CancellationToken.None);

        Assert.Equal(TenancyQueryStatus.Success, outcome.Status);
        Assert.Equal(250, store.ListMaximumResults);
    }

    [Fact]
    public async Task GetCreateForm_AdminUser_UsesClockAndBoundsSelections()
    {
        var store = new FakeTenancyStore();
        var service = CreateService(store);

        var outcome = await service.GetCreateFormAsync(CancellationToken.None);

        Assert.Equal(TenancyQueryStatus.Success, outcome.Status);
        Assert.Equal(new DateOnly(2026, 8, 11), outcome.Form!.DefaultStartDate);
        Assert.Equal(200, store.PropertyMaximumResults);
        Assert.Equal(200, store.TenantMaximumResults);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-08-11", CreateTenancyStatus.Success)]
    [InlineData("2026-08-01", null, CreateTenancyStatus.Success)]
    [InlineData("2026-08-12", "2026-08-11", CreateTenancyStatus.ValidationFailed)]
    [InlineData("2026-08-01", "2026-08-10", CreateTenancyStatus.ValidationFailed)]
    public async Task Create_DateBoundaries_ReturnExpectedOutcome(
        string startDate,
        string? endDate,
        CreateTenancyStatus expectedStatus)
    {
        var store = new FakeTenancyStore();
        var service = CreateService(store);
        var command = new CreateTenancyCommand(
            10,
            "tenant-1",
            DateOnly.Parse(startDate),
            endDate is null ? null : DateOnly.Parse(endDate));

        var outcome = await service.CreateAsync(command, CancellationToken.None);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Equal(expectedStatus == CreateTenancyStatus.Success, store.CreateCalled);
    }

    [Fact]
    public async Task Create_DefaultStartDate_ReturnsFieldValidationError()
    {
        var service = CreateService(new FakeTenancyStore());

        var outcome = await service.CreateAsync(
            new CreateTenancyCommand(10, "tenant-1", default, null),
            CancellationToken.None);

        Assert.Equal(CreateTenancyStatus.ValidationFailed, outcome.Status);
        Assert.Contains(outcome.Errors!, error => error.Field == nameof(CreateTenancyCommand.StartDate));
    }

    [Fact]
    public async Task Create_MissingProperty_ReturnsPropertyNotFound()
    {
        var service = CreateService(new FakeTenancyStore { PropertyExists = false });

        var outcome = await service.CreateAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(CreateTenancyStatus.PropertyNotFound, outcome.Status);
    }

    [Fact]
    public async Task Create_AccountWithoutTenantRole_ReturnsTenantNotFound()
    {
        var service = CreateService(new FakeTenancyStore { TenantExists = false });

        var outcome = await service.CreateAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(CreateTenancyStatus.TenantNotFound, outcome.Status);
    }

    [Fact]
    public async Task Create_PropertyAlreadyHasActiveTenancy_ReturnsConflictWithoutPersisting()
    {
        var store = new FakeTenancyStore { HasActiveTenancy = true };
        var service = CreateService(store);

        var outcome = await service.CreateAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(CreateTenancyStatus.Conflict, outcome.Status);
        Assert.False(store.CreateCalled);
    }

    [Fact]
    public async Task Create_UniqueConflictDuringPersistence_TranslatesToConflict()
    {
        var store = new FakeTenancyStore
        {
            CreateResult = CreateTenancyPersistenceResult.ActiveTenancyConflict
        };
        var service = CreateService(store);

        var outcome = await service.CreateAsync(ValidCommand(), CancellationToken.None);

        Assert.Equal(CreateTenancyStatus.Conflict, outcome.Status);
        Assert.True(store.CreateCalled);
    }

    [Theory]
    [InlineData("2026-08-01", "2026-08-11")]
    [InlineData("2026-08-20", "2026-08-20")]
    public async Task End_ActiveCurrentOrFutureTenancy_UsesValidBoundary(
        string startDate,
        string expectedEndDate)
    {
        var store = new FakeTenancyStore
        {
            EndState = new TenancyEndState(
                42,
                DateOnly.Parse(startDate),
                TenancyStatus.Active)
        };
        var service = CreateService(store);

        var outcome = await service.EndAsync(42, CancellationToken.None);

        Assert.Equal(EndTenancyStatus.Success, outcome.Status);
        Assert.True(outcome.Changed);
        Assert.Equal(DateOnly.Parse(expectedEndDate), store.SavedEndDate);
    }

    [Fact]
    public async Task End_EndedTenancy_IsSuccessfulWithoutPersistence()
    {
        var store = new FakeTenancyStore
        {
            EndState = new TenancyEndState(42, new DateOnly(2026, 1, 1), TenancyStatus.Ended)
        };
        var service = CreateService(store);

        var outcome = await service.EndAsync(42, CancellationToken.None);

        Assert.Equal(EndTenancyStatus.Success, outcome.Status);
        Assert.False(outcome.Changed);
        Assert.Null(store.SavedEndDate);
    }

    [Fact]
    public async Task End_MissingTenancy_ReturnsNotFound()
    {
        var service = CreateService(new FakeTenancyStore { EndState = null });

        var outcome = await service.EndAsync(999, CancellationToken.None);

        Assert.Equal(EndTenancyStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task End_ConcurrentPersistenceFailure_ReturnsConflict()
    {
        var store = new FakeTenancyStore
        {
            EndState = new TenancyEndState(42, new DateOnly(2026, 8, 1), TenancyStatus.Active),
            EndResult = EndTenancyPersistenceResult.Conflict
        };
        var service = CreateService(store);

        var outcome = await service.EndAsync(42, CancellationToken.None);

        Assert.Equal(EndTenancyStatus.Conflict, outcome.Status);
    }

    private static TenancyService CreateService(
        FakeTenancyStore store,
        ICurrentUser? currentUser = null) =>
        new(
            store,
            currentUser ?? new FakeCurrentUser(true, true),
            new FakeClock(NowUtc));

    private static CreateTenancyCommand ValidCommand() =>
        new(10, "tenant-1", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 11));

    private sealed class FakeTenancyStore : ITenancyStore
    {
        public bool PropertyExists { get; init; } = true;
        public bool TenantExists { get; init; } = true;
        public bool HasActiveTenancy { get; init; }
        public bool CreateCalled { get; private set; }
        public int ListMaximumResults { get; private set; }
        public int PropertyMaximumResults { get; private set; }
        public int TenantMaximumResults { get; private set; }
        public CreateTenancyPersistenceResult CreateResult { get; init; } =
            CreateTenancyPersistenceResult.Success;
        public TenancyEndState? EndState { get; init; }
        public EndTenancyPersistenceResult EndResult { get; init; } =
            EndTenancyPersistenceResult.Success;
        public DateOnly? SavedEndDate { get; private set; }

        public Task<IReadOnlyList<TenancyAdminRow>> ListForAdministrationAsync(
            int maximumResults,
            CancellationToken cancellationToken)
        {
            ListMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<TenancyAdminRow>>([]);
        }

        public Task<IReadOnlyList<TenancySelectionOption>> ListAvailablePropertiesAsync(
            int maximumResults,
            CancellationToken cancellationToken)
        {
            PropertyMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<TenancySelectionOption>>([]);
        }

        public Task<IReadOnlyList<TenancySelectionOption>> ListTenantAccountsAsync(
            int maximumResults,
            CancellationToken cancellationToken)
        {
            TenantMaximumResults = maximumResults;
            return Task.FromResult<IReadOnlyList<TenancySelectionOption>>([]);
        }

        public Task<bool> PropertyExistsAsync(
            int propertyId,
            CancellationToken cancellationToken) => Task.FromResult(PropertyExists);

        public Task<bool> TenantAccountExistsAsync(
            string tenantId,
            CancellationToken cancellationToken) => Task.FromResult(TenantExists);

        public Task<bool> HasActiveTenancyAsync(
            int propertyId,
            CancellationToken cancellationToken) => Task.FromResult(HasActiveTenancy);

        public Task<CreateTenancyPersistenceResult> CreateAsync(
            CreateTenancyRecord tenancy,
            CancellationToken cancellationToken)
        {
            CreateCalled = true;
            return Task.FromResult(CreateResult);
        }

        public Task<TenancyEndState?> GetForEndAsync(
            int tenancyId,
            CancellationToken cancellationToken) => Task.FromResult(EndState);

        public Task<EndTenancyPersistenceResult> SaveEndAsync(
            int tenancyId,
            DateOnly endDate,
            CancellationToken cancellationToken)
        {
            SavedEndDate = endDate;
            return Task.FromResult(EndResult);
        }
    }

    private sealed class FakeCurrentUser(bool isAuthenticated, bool isAdministrator)
        : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public string? UserId => "admin-1";
        public bool IsInRole(string roleName) =>
            isAdministrator && roleName == "Admin";
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
