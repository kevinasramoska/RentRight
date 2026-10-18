using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RentRight.Application.Abstractions;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using RentRight.Integrations.Geocoding;
using Xunit;

namespace RentRight.Tests.Geocoding;

public sealed class PropertyLocationServiceTests
{
    private const string Version = "AAAAAAAAAAE=";

    [Theory]
    [InlineData(null, "", "owner", LocationRefreshStatus.Unauthenticated)]
    [InlineData("tenant", "Tenant", "owner", LocationRefreshStatus.Forbidden)]
    [InlineData("other", "Landlord", "owner", LocationRefreshStatus.Forbidden)]
    [InlineData("owner", "Landlord", "owner", LocationRefreshStatus.Updated)]
    [InlineData("admin", "Admin", "owner", LocationRefreshStatus.Updated)]
    public async Task Refresh_ActorAndOwnershipMatrix_RequiresLocalManagerAndOwnResource(string? id, string role, string owner, LocationRefreshStatus expected)
    {
        var store = new Store { Property = new(1, "Title", "Synthetic address", 1000, null, owner, Version) };
        var geo = new Client();
        var writer = new Writer();
        var service = Service(store, writer, geo, new User(id, role));
        Assert.Equal(expected, await service.RefreshAsync(1, Version, default));
        Assert.Equal(expected == LocationRefreshStatus.Updated ? 1 : 0, geo.Calls);
        Assert.Equal(expected == LocationRefreshStatus.Updated ? 1 : 0, writer.Calls);
    }

    [Theory]
    [InlineData(null, LocationRefreshStatus.InvalidVersion)]
    [InlineData("invalid", LocationRefreshStatus.InvalidVersion)]
    [InlineData("AAAAAAAAAAI=", LocationRefreshStatus.Conflict)]
    public async Task Refresh_InvalidOrStaleVersion_DoesNotCallProvider(string? version, LocationRefreshStatus expected)
    {
        var geo = new Client();
        var service = Service(new Store(), new Writer(), geo);
        Assert.Equal(expected, await service.RefreshAsync(1, version, default));
        Assert.Equal(0, geo.Calls);
    }

    [Theory]
    [InlineData(GeocodingStatus.NoResult)]
    [InlineData(GeocodingStatus.Unavailable)]
    [InlineData(GeocodingStatus.RateLimited)]
    [InlineData(GeocodingStatus.InvalidResponse)]
    public async Task Refresh_ProviderFailure_PreservesPropertyAndDoesNotWriteLocation(GeocodingStatus status)
    {
        var writer = new Writer();
        var service = Service(new Store(), writer, new Client { Result = new(status) });
        Assert.Equal(LocationRefreshStatus.Unavailable, await service.RefreshAsync(1, Version, default));
        Assert.Equal(0, writer.Calls);
    }

    [Fact]
    public async Task Refresh_DisabledOrMissingProperty_DoesNotCallProvider()
    {
        var geo = new Client { IsEnabled = false };
        Assert.Equal(LocationRefreshStatus.Unavailable, await Service(new Store(), new Writer(), geo).RefreshAsync(1, Version, default));
        Assert.Equal(LocationRefreshStatus.NotFound, await Service(new Store { Property = null }, new Writer(), geo).RefreshAsync(1, Version, default));
        Assert.Equal(0, geo.Calls);
    }

    [Fact]
    public async Task Refresh_ConcurrentPersistenceFailure_ReturnsConflict()
    {
        var writer = new Writer { Accept = false };
        Assert.Equal(LocationRefreshStatus.Conflict, await Service(new Store(), writer, new Client()).RefreshAsync(1, Version, default));
        Assert.Equal(Version, writer.Snapshot!.Version);
    }

    [Fact]
    public async Task EnrichAfterWrite_DatabaseErrorOrCancellation_DoesNotUndoCoreSave()
    {
        var writer = new Writer { Error = new DbUpdateException("private database/address details") };
        await Service(new Store(), writer, new Client()).EnrichAfterWriteAsync(1, default);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var geo = new Client();
        await Service(new Store(), new Writer(), geo).EnrichAfterWriteAsync(1, canceled.Token);
        Assert.Equal(0, geo.Calls);
    }

    private static PropertyLocationService Service(Store store, Writer writer, Client client, User? user = null) =>
        new(store, writer, client, user ?? new("owner", "Landlord"), TimeProvider.System, NullLogger<PropertyLocationService>.Instance);

    private sealed record User(string? UserId, string Role) : ICurrentUser
    {
        public bool IsAuthenticated => UserId is not null;
        public bool IsInRole(string roleName) => Role == roleName;
    }
    private sealed class Client : IGeocodingClient
    {
        public bool IsEnabled { get; init; } = true;
        public int Calls { get; private set; }
        public GeocodingResult Result { get; init; } = new(GeocodingStatus.Found, new(53.3m, -6.2m, "synthetic"));
        public Task<GeocodingResult> LookupAsync(string address, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(Result); }
    }
    private sealed class Writer : IPropertyLocationWriter
    {
        public bool Accept { get; init; } = true;
        public Exception? Error { get; init; }
        public int Calls { get; private set; }
        public PropertyRecord? Snapshot { get; private set; }
        public Task<bool> SaveAsync(PropertyRecord expected, GeocodedLocation location, DateTime utc, CancellationToken cancellationToken)
        { Calls++; Snapshot = expected; if (Error is not null) throw Error; return Task.FromResult(Accept); }
    }
    private sealed class Store : IPropertyStore
    {
        public PropertyRecord? Property { get; init; } = new(1, "Title", "Synthetic address", 1000, null, "owner", Version);
        public Task<PropertyRecord?> FindAsync(int propertyId, CancellationToken cancellationToken) => Task.FromResult(Property);
        public Task<PublishedPropertyPageRecord> SearchPublishedAsync(PublishedPropertyQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PublishedPropertyListItem?> FindPublishedAsync(int propertyId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<PropertyListRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<LatestPropertyListItem>> ListLatestAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CreateAsync(PropertyValues values, string landlordId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UpdateAsync(int propertyId, PropertyValues values, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PropertyDeleteStoreStatus> DeleteAsync(int propertyId, CancellationToken cancellationToken, byte[] expectedVersion, string expectedLandlordId) => throw new NotSupportedException();
    }
}
