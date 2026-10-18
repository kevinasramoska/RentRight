using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RentRight.Application;
using RentRight.Application.Abstractions;
using RentRight.Authorization;
using RentRight.Data;
using RentRight.Integrations.Geocoding;

namespace RentRight.Features.Properties;

public enum LocationRefreshStatus { Updated, Unavailable, Conflict, Unauthenticated, Forbidden, NotFound, InvalidVersion }

public interface IPropertyLocationService
{
    bool IsEnabled { get; }
    Task EnrichAfterWriteAsync(int propertyId, CancellationToken cancellationToken);
    Task<LocationRefreshStatus> RefreshAsync(int propertyId, string? expectedVersion, CancellationToken cancellationToken);
}

public interface IPropertyLocationWriter
{
    Task<bool> SaveAsync(PropertyRecord expected, GeocodedLocation location, DateTime utc, CancellationToken cancellationToken);
}

public sealed class EfPropertyLocationWriter(ApplicationDbContext context) : IPropertyLocationWriter
{
    public async Task<bool> SaveAsync(PropertyRecord expected, GeocodedLocation location, DateTime utc, CancellationToken cancellationToken)
    {
        if (!ExpectedVersion.TryParse(expected.Version, out var version)) return false;
        return await context.Properties.Where(property => property.Id == expected.Id &&
            property.LandlordId == expected.LandlordId && property.Address == expected.Address && property.RowVersion == version)
            .ExecuteUpdateAsync(update => update
                .SetProperty(property => property.Latitude, location.Latitude)
                .SetProperty(property => property.Longitude, location.Longitude)
                .SetProperty(property => property.GeocodingPlaceId, location.PlaceId)
                .SetProperty(property => property.GeocodedUtc, utc), cancellationToken) == 1;
    }
}

public sealed class PropertyLocationService(IPropertyStore store, IPropertyLocationWriter writer, IGeocodingClient geocoding,
    ICurrentUser user, TimeProvider clock, ILogger<PropertyLocationService> logger) : IPropertyLocationService
{
    public bool IsEnabled => geocoding.IsEnabled;

    public async Task EnrichAfterWriteAsync(int propertyId, CancellationToken cancellationToken)
    {
        if (!IsEnabled || cancellationToken.IsCancellationRequested) return;
        // The core save is already committed: optional lookup/persistence failures must
        // not report that successful write as failed or invite automatic duplicate POSTs.
        try { await RefreshAsync(propertyId, expectedVersion: null, cancellationToken, afterWrite: true); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public Task<LocationRefreshStatus> RefreshAsync(int propertyId, string? expectedVersion, CancellationToken cancellationToken) =>
        RefreshAsync(propertyId, expectedVersion, cancellationToken, afterWrite: false);

    private async Task<LocationRefreshStatus> RefreshAsync(int propertyId, string? expectedVersion,
        CancellationToken cancellationToken, bool afterWrite)
    {
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) return LocationRefreshStatus.Unauthenticated;
        if (!user.IsInRole(AppRoles.Admin) && !user.IsInRole(AppRoles.Landlord)) return LocationRefreshStatus.Forbidden;
        try
        {
            var property = await store.FindAsync(propertyId, cancellationToken);
            if (property is null) return LocationRefreshStatus.NotFound;
            if (!RentalAccess.CanManageProperty(user, property.LandlordId)) return LocationRefreshStatus.Forbidden;
            if (!afterWrite)
            {
                if (!ExpectedVersion.TryParse(expectedVersion, out _)) return LocationRefreshStatus.InvalidVersion;
                if (expectedVersion != property.Version) return LocationRefreshStatus.Conflict;
            }
            if (!IsEnabled) return LocationRefreshStatus.Unavailable;
            var result = await geocoding.LookupAsync(property.Address, cancellationToken);
            if (result.Status != GeocodingStatus.Found || result.Location is not { } point) return LocationRefreshStatus.Unavailable;
            if (point.Latitude is < -90 or > 90 || point.Longitude is < -180 or > 180 ||
                string.IsNullOrWhiteSpace(point.PlaceId) || point.PlaceId.Length > GeocodedLocation.MaximumPlaceIdLength) return LocationRefreshStatus.Unavailable;
            return await writer.SaveAsync(property, point, clock.GetUtcNow().UtcDateTime, cancellationToken)
                ? LocationRefreshStatus.Updated : LocationRefreshStatus.Conflict;
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException or HttpRequestException)
        {
            // Exception/URI/SQL payloads can contain addresses or API keys; never log them.
            logger.LogWarning("Optional property location enrichment unavailable.");
            return LocationRefreshStatus.Unavailable;
        }
    }
}
