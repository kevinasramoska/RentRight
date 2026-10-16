using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RentRight.Application.Abstractions;
using RentRight.Data;
using RentRight.Features.Maintenance;
using RentRight.Features.Maintenance.Contracts;
using Xunit;

namespace RentRight.Tests.Maintenance;

public class TenantMaintenanceServiceTests
{
    [Theory]
    [InlineData(false, null, "Tenant", TenantMaintenanceStatus.Unauthenticated)]
    [InlineData(true, " ", "Tenant", TenantMaintenanceStatus.Unauthenticated)]
    [InlineData(true, "actor", "Landlord", TenantMaintenanceStatus.Forbidden)]
    [InlineData(true, "actor", "Admin", TenantMaintenanceStatus.Forbidden)]
    public async Task Journey_WithoutTenantIdentity_RejectsBeforeDatabase(bool authenticated, string? id, string role, TenantMaintenanceStatus expected)
    {
        await using var db = Context();
        var service = new TenantMaintenanceService(db, new User(authenticated, id, role), new Clock(), new Commands());
        Assert.Equal(expected, (await service.CreateAsync("A leaking kitchen sink", default)).Status);
        Assert.Equal(expected, (await service.ListAsync(1, 20, default)).Status);
        Assert.Equal(expected, (await service.GetAsync(1, default)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("          ")]
    [InlineData("   short   ")]
    public async Task Create_InvalidTrimmedDescription_RejectsBeforeDatabase(string? description)
    {
        await using var db = Context();
        Assert.Equal(TenantMaintenanceStatus.Invalid, (await Service(db).CreateAsync(description, default)).Status);
    }

    [Fact]
    public async Task Create_OversizedDescription_RejectsBeforeDatabase()
    {
        await using var db = Context();
        Assert.Equal(TenantMaintenanceStatus.Invalid, (await Service(db).CreateAsync(new string('x', 2001), default)).Status);
    }

    [Fact]
    public async Task Journey_DatabaseUnavailable_ReturnsUnavailableWithoutExceptionDetails()
    {
        await using var db = Context();
        var service = Service(db);
        Assert.Equal(TenantMaintenanceStatus.Unavailable, (await service.CreateAsync("A leaking kitchen sink", default)).Status);
        Assert.Equal(TenantMaintenanceStatus.Unavailable, (await service.ListAsync(1, 20, default)).Status);
        Assert.Equal(TenantMaintenanceStatus.Unavailable, (await service.GetAsync(1, default)).Status);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(10001, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_InvalidPaging_RejectsBeforeDatabase(int page, int pageSize)
    {
        await using var db = Context();
        Assert.Equal(TenantMaintenanceStatus.Invalid, (await Service(db).ListAsync(page, pageSize, default)).Status);
    }

    [Fact]
    public async Task LandlordQueue_DependencyFailure_ReturnsUnavailable()
    {
        await using var db = Context();
        var service = new LandlordMaintenanceService(db, new User(true, "owner", "Landlord"));
        Assert.Equal(TenantMaintenanceStatus.Unavailable, (await service.ListAsync(new(), default)).Status);
        Assert.Equal(TenantMaintenanceStatus.Unavailable, (await service.GetAsync(1, default)).Status);
        var commands = new MaintenanceCommandService(new EfMaintenanceStore(db), new User(true, "owner", "Landlord"), new Clock());
        Assert.Equal(MaintenanceOutcomeStatus.Unavailable, (await commands.UpdateStatusAsync(new(1, 1, RentRight.Models.MaintenanceRequestStatus.InProgress), default)).Status);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("Unknown")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxx")]
    public async Task LandlordQueue_InvalidStatus_RejectsBeforeDatabase(string status)
    {
        await using var db = Context();
        var service = new LandlordMaintenanceService(db, new User(true, "owner", "Landlord"));
        Assert.Equal(TenantMaintenanceStatus.Invalid, (await service.ListAsync(new() { Status = status }, default)).Status);
    }

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer("Server=unused;Database=unused;Integrated Security=true")
        .AddInterceptors(new UnavailableConnection()).Options);
    private static TenantMaintenanceService Service(ApplicationDbContext db) => new(db, new User(true, "actor", "Tenant"), new Clock(), new Commands());
    private sealed class User(bool authenticated, string? id, string role) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;
        public string? UserId => id;
        public bool IsInRole(string name) => name == role;
    }
    private sealed class Clock : IClock { public DateTime UtcNow => new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc); }
    private sealed class Commands : IMaintenanceCommandService
    {
        public Task<CreateMaintenanceRequestOutcome> CreateAsync(CreateMaintenanceRequestCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Command must not be invoked by these scenarios.");
        public Task<UpdateMaintenanceRequestStatusOutcome> UpdateStatusAsync(UpdateMaintenanceRequestStatusCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }
    private sealed class UnavailableConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default) => throw new TimeoutException("Do not expose dependency details.");
    }
}
