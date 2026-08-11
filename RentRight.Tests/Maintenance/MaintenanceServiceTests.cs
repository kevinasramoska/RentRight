using RentRight.Application.Abstractions;
using RentRight.Features.Maintenance;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Maintenance;

public class MaintenanceServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    public async Task CreateAsync_MissingOrWhitespaceDescription_ReturnsValidationWithoutThrowing(
        string? description)
    {
        var store = CreateStoreWithActiveTenancy();
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, description),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Validation, outcome.Status);
        Assert.Contains(outcome.Errors!, error => error.Field == "Description");
        Assert.Null(store.CreatedRequest);
    }

    [Fact]
    public async Task CreateAsync_DescriptionOverMaximumLength_ReturnsValidation()
    {
        var store = CreateStoreWithActiveTenancy();
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, new string('x', 2001)),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Validation, outcome.Status);
        Assert.Contains(outcome.Errors!, error => error.Field == "Description");
        Assert.Null(store.CreatedRequest);
    }

    [Theory]
    [InlineData(TenancyStatus.Ended, -10, null)]
    [InlineData(TenancyStatus.Active, 1, null)]
    [InlineData(TenancyStatus.Active, -10, -1)]
    public async Task CreateAsync_InactiveFutureOrExpiredTenancy_ReturnsValidation(
        TenancyStatus status,
        int startOffset,
        int? endOffset)
    {
        var store = new FakeMaintenanceStore();
        store.Tenancies.Add(CreateTenancy(
            id: 1,
            tenantId: "tenant-1",
            status,
            DateOnly.FromDateTime(Now).AddDays(startOffset),
            endOffset.HasValue
                ? DateOnly.FromDateTime(Now).AddDays(endOffset.Value)
                : null));
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, "A valid maintenance description"),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Validation, outcome.Status);
        Assert.Contains(outcome.Errors!, error => error.Field == "TenancyId");
        Assert.Null(store.CreatedRequest);
    }

    [Fact]
    public async Task CreateAsync_WrongUserTenancy_ReturnsConcealedNotFound()
    {
        var store = CreateStoreWithActiveTenancy(tenantId: "tenant-2");
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, "A valid maintenance description"),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.ConcealedNotFound, outcome.Status);
        Assert.Null(store.CreatedRequest);
    }

    [Fact]
    public async Task CreateAsync_MissingTenancy_ReturnsNotFound()
    {
        var service = CreateCommandService(new FakeMaintenanceStore(), "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(404, "A valid maintenance description"),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task CreateAsync_ValidInput_DerivesTenancyStatusAndUtcTimestampsOnServer()
    {
        var store = CreateStoreWithActiveTenancy();
        store.NextRequestId = 73;
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, "  Boiler pressure has fallen  "),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.Equal(73, outcome.RequestId);
        Assert.Equal(1, store.CreatedRequest!.TenancyId);
        Assert.Equal("Boiler pressure has fallen", store.CreatedRequest.Description);
        Assert.Equal(MaintenanceRequestStatus.Submitted, store.CreatedRequest.Status);
        Assert.Equal(Now, store.CreatedRequest.DateSubmitted);
        Assert.Equal(Now, store.CreatedRequest.LastUpdatedUtc);
    }

    [Fact]
    public async Task CreateAsync_NonTenantRole_ReturnsForbidden()
    {
        var store = CreateStoreWithActiveTenancy();
        var service = CreateCommandService(store, "landlord-1", "Landlord");

        var outcome = await service.CreateAsync(
            new CreateMaintenanceRequestCommand(1, "A valid maintenance description"),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Forbidden, outcome.Status);
        Assert.Null(store.CreatedRequest);
    }

    [Theory]
    [InlineData("Tenant", "tenant-2")]
    [InlineData("Landlord", "landlord-2")]
    public async Task GetDetailAsync_CrossAccountId_ReturnsConcealedNotFound(
        string role,
        string currentUserId)
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, currentUserId, role);

        var outcome = await service.GetDetailAsync(10, CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.ConcealedNotFound, outcome.Status);
    }

    [Fact]
    public async Task GetDetailAsync_AdminAccessesAnyRequestAndMayUpdateStatus()
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, "admin-1", "Admin");

        var outcome = await service.GetDetailAsync(10, CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.True(outcome.Detail!.CanUpdateStatus);
        Assert.Equal("Property 1", outcome.Detail.PropertyTitle);
    }

    [Fact]
    public async Task ListAsync_AdminMultiRole_UsesAdminScopeAndPreservesTenantCreateCapability()
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, "admin-1", "Admin", "Landlord", "Tenant");

        var outcome = await service.ListAsync(
            new MaintenanceRequestsQuery(),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.True(store.LastListAccess!.IncludeAll);
        Assert.False(store.LastListAccess.IncludeLandlordOwned);
        Assert.False(store.LastListAccess.IncludeTenantOwned);
        Assert.Equal(MaintenanceListAudience.Administrator, outcome.List!.Audience);
        Assert.True(outcome.List.CanCreate);
        Assert.True(Assert.Single(outcome.List.Requests).CanUpdateStatus);
    }

    [Fact]
    public async Task ListAsync_LandlordTenantMultiRole_UsesUnionScopeAndLandlordAudience()
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, "tenant-1", "Landlord", "Tenant");

        var outcome = await service.ListAsync(
            new MaintenanceRequestsQuery(),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.False(store.LastListAccess!.IncludeAll);
        Assert.True(store.LastListAccess.IncludeLandlordOwned);
        Assert.True(store.LastListAccess.IncludeTenantOwned);
        Assert.Equal(MaintenanceListAudience.Landlord, outcome.List!.Audience);
        Assert.True(outcome.List.CanCreate);
    }

    [Fact]
    public async Task ListAsync_OutOfRangeInputs_AreBoundedBeforeStoreQuery()
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, "admin-1", "Admin");

        var outcome = await service.ListAsync(
            new MaintenanceRequestsQuery(Page: int.MaxValue, PageSize: int.MaxValue),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.Equal(MaintenanceRequestsQuery.MaximumPage, store.LastPage);
        Assert.Equal(MaintenanceRequestsQuery.MaximumPageSize, store.LastPageSize);
    }

    [Fact]
    public async Task GetCreateFormAsync_UsesClockAndReturnsOnlyCurrentUsersActiveTenancies()
    {
        var store = CreateStoreWithActiveTenancy();
        store.Tenancies.Add(CreateTenancy(
            id: 2,
            tenantId: "tenant-1",
            TenancyStatus.Active,
            DateOnly.FromDateTime(Now).AddDays(-20),
            DateOnly.FromDateTime(Now).AddDays(-1)));
        var service = CreateQueryService(store, "tenant-1", "Tenant");

        var outcome = await service.GetCreateFormAsync(CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.Equal(DateOnly.FromDateTime(Now), store.LastActiveTenancyDate);
        Assert.Equal(1, Assert.Single(outcome.Form!.AvailableTenancies).TenancyId);
    }

    public static IEnumerable<object[]> EveryStatusPair()
    {
        foreach (var current in Enum.GetValues<MaintenanceRequestStatus>())
        {
            foreach (var next in Enum.GetValues<MaintenanceRequestStatus>())
            {
                yield return [current, next];
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryStatusPair))]
    public async Task UpdateStatusAsync_EveryTransition_UsesCentralTransitionRules(
        MaintenanceRequestStatus current,
        MaintenanceRequestStatus next)
    {
        var store = CreateStoreWithRequest(status: current);
        var service = CreateCommandService(store, "landlord-1", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(10, 10, next),
            CancellationToken.None);

        var expected = current.CanTransitionTo(next)
            ? MaintenanceOutcomeStatus.Success
            : MaintenanceOutcomeStatus.Validation;
        Assert.Equal(expected, outcome.Status);
        Assert.Equal(current.CanTransitionTo(next), store.UpdateCall is not null);
        if (store.UpdateCall is not null)
        {
            Assert.Equal(current, store.UpdateCall.ExpectedStatus);
            Assert.Equal(next, store.UpdateCall.NewStatus);
            Assert.Equal(Now, store.UpdateCall.LastUpdatedUtc);
        }
    }

    [Fact]
    public async Task UpdateStatusAsync_CrossLandlordId_ReturnsConcealedNotFoundWithoutMutation()
    {
        var store = CreateStoreWithRequest();
        var service = CreateCommandService(store, "landlord-2", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(
                10,
                10,
                MaintenanceRequestStatus.InProgress),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.ConcealedNotFound, outcome.Status);
        Assert.Null(store.UpdateCall);
    }

    [Fact]
    public async Task GetStatusUpdateAsync_CrossLandlordId_ReturnsConcealedNotFound()
    {
        var store = CreateStoreWithRequest();
        var service = CreateQueryService(store, "landlord-2", "Landlord");

        var outcome = await service.GetStatusUpdateAsync(10, CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.ConcealedNotFound, outcome.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_MismatchedSubmittedId_ReturnsConcealedNotFound()
    {
        var store = CreateStoreWithRequest();
        var service = CreateCommandService(store, "landlord-1", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(
                10,
                11,
                MaintenanceRequestStatus.InProgress),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.ConcealedNotFound, outcome.Status);
        Assert.Null(store.UpdateCall);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((MaintenanceRequestStatus)999)]
    public async Task UpdateStatusAsync_MissingOrUndefinedStatus_ReturnsValidation(
        MaintenanceRequestStatus? newStatus)
    {
        var store = CreateStoreWithRequest();
        var service = CreateCommandService(store, "landlord-1", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(10, 10, newStatus),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Validation, outcome.Status);
        Assert.Null(store.UpdateCall);
    }

    [Fact]
    public async Task UpdateStatusAsync_AdminOverridesPropertyOwnership()
    {
        var store = CreateStoreWithRequest();
        var service = CreateCommandService(store, "admin-1", "Admin", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(
                10,
                10,
                MaintenanceRequestStatus.InProgress),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Success, outcome.Status);
        Assert.NotNull(store.UpdateCall);
    }

    [Fact]
    public async Task UpdateStatusAsync_ConcurrentStatusChange_ReturnsConflict()
    {
        var store = CreateStoreWithRequest();
        store.UpdateSucceeds = false;
        var service = CreateCommandService(store, "landlord-1", "Landlord");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(
                10,
                10,
                MaintenanceRequestStatus.InProgress),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Conflict, outcome.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_TenantRole_ReturnsForbidden()
    {
        var store = CreateStoreWithRequest();
        var service = CreateCommandService(store, "tenant-1", "Tenant");

        var outcome = await service.UpdateStatusAsync(
            new UpdateMaintenanceRequestStatusCommand(
                10,
                10,
                MaintenanceRequestStatus.InProgress),
            CancellationToken.None);

        Assert.Equal(MaintenanceOutcomeStatus.Forbidden, outcome.Status);
        Assert.Null(store.UpdateCall);
    }

    private static MaintenanceCommandService CreateCommandService(
        FakeMaintenanceStore store,
        string userId,
        params string[] roles) =>
        new(store, new FakeCurrentUser(userId, roles), new FakeClock(Now));

    private static MaintenanceQueryService CreateQueryService(
        FakeMaintenanceStore store,
        string userId,
        params string[] roles) =>
        new(store, new FakeCurrentUser(userId, roles), new FakeClock(Now));

    private static FakeMaintenanceStore CreateStoreWithActiveTenancy(
        string tenantId = "tenant-1")
    {
        var store = new FakeMaintenanceStore();
        store.Tenancies.Add(CreateTenancy(
            id: 1,
            tenantId,
            TenancyStatus.Active,
            DateOnly.FromDateTime(Now).AddDays(-10),
            DateOnly.FromDateTime(Now).AddDays(10)));
        return store;
    }

    private static FakeMaintenanceStore CreateStoreWithRequest(
        MaintenanceRequestStatus status = MaintenanceRequestStatus.Submitted)
    {
        var store = new FakeMaintenanceStore();
        store.Requests.Add(new MaintenanceRequestStoreData(
            10,
            "Property 1",
            "1 Example Street",
            "Tenant One",
            "The boiler pressure has fallen.",
            Now.AddDays(-1),
            Now.AddDays(-1),
            status,
            "tenant-1",
            "landlord-1"));
        return store;
    }

    private static MaintenanceTenancyStoreData CreateTenancy(
        int id,
        string tenantId,
        TenancyStatus status,
        DateOnly startDate,
        DateOnly? endDate) =>
        new(
            id,
            tenantId,
            status,
            startDate,
            endDate,
            $"Property {id}",
            $"{id} Example Street");

    private sealed class FakeCurrentUser(
        string? userId,
        params string[] roles) : ICurrentUser
    {
        private readonly HashSet<string> _roles = new(roles, StringComparer.OrdinalIgnoreCase);

        public bool IsAuthenticated { get; } = userId is not null;
        public string? UserId { get; } = userId;
        public bool IsInRole(string roleName) => _roles.Contains(roleName);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakeMaintenanceStore : IMaintenanceStore
    {
        public List<MaintenanceRequestStoreData> Requests { get; } = [];
        public List<MaintenanceTenancyStoreData> Tenancies { get; } = [];
        public int NextRequestId { get; set; } = 100;
        public bool UpdateSucceeds { get; set; } = true;
        public NewMaintenanceRequest? CreatedRequest { get; private set; }
        public UpdateCallData? UpdateCall { get; private set; }
        public MaintenanceListAccess? LastListAccess { get; private set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public DateOnly? LastActiveTenancyDate { get; private set; }

        public Task<IReadOnlyList<MaintenanceListStoreRow>> ListAsync(
            MaintenanceListAccess access,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            LastListAccess = access;
            LastPage = page;
            LastPageSize = pageSize;
            var rows = Requests
                .Where(request => access.IncludeAll ||
                    (access.IncludeLandlordOwned && request.LandlordId == access.UserId) ||
                    (access.IncludeTenantOwned && request.TenantId == access.UserId))
                .OrderByDescending(request => request.DateSubmitted)
                .ThenByDescending(request => request.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(request => new MaintenanceListStoreRow(
                    request.Id,
                    request.PropertyTitle,
                    request.TenantName,
                    request.Description,
                    request.DateSubmitted,
                    request.Status,
                    request.LandlordId,
                    request.TenantId))
                .ToArray();
            return Task.FromResult<IReadOnlyList<MaintenanceListStoreRow>>(rows);
        }

        public Task<MaintenanceRequestStoreData?> GetRequestAsync(
            int requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Requests.SingleOrDefault(request => request.Id == requestId));

        public Task<IReadOnlyList<MaintenanceTenancyStoreData>> ListActiveTenanciesAsync(
            string tenantId,
            DateOnly date,
            CancellationToken cancellationToken)
        {
            LastActiveTenancyDate = date;
            var tenancies = Tenancies
                .Where(tenancy =>
                    tenancy.TenantId == tenantId &&
                    tenancy.Status == TenancyStatus.Active &&
                    tenancy.StartDate <= date &&
                    (tenancy.EndDate is null || tenancy.EndDate >= date))
                .OrderBy(tenancy => tenancy.PropertyTitle)
                .ThenBy(tenancy => tenancy.PropertyAddress)
                .ThenBy(tenancy => tenancy.Id)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MaintenanceTenancyStoreData>>(tenancies);
        }

        public Task<MaintenanceTenancyStoreData?> GetTenancyAsync(
            int tenancyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Tenancies.SingleOrDefault(tenancy => tenancy.Id == tenancyId));

        public Task<int> CreateAsync(
            NewMaintenanceRequest request,
            CancellationToken cancellationToken)
        {
            CreatedRequest = request;
            return Task.FromResult(NextRequestId);
        }

        public Task<bool> TryUpdateStatusAsync(
            int requestId,
            MaintenanceRequestStatus expectedStatus,
            MaintenanceRequestStatus newStatus,
            DateTime lastUpdatedUtc,
            CancellationToken cancellationToken)
        {
            UpdateCall = new UpdateCallData(
                requestId,
                expectedStatus,
                newStatus,
                lastUpdatedUtc);
            return Task.FromResult(UpdateSucceeds);
        }

        public sealed record UpdateCallData(
            int RequestId,
            MaintenanceRequestStatus ExpectedStatus,
            MaintenanceRequestStatus NewStatus,
            DateTime LastUpdatedUtc);
    }
}
