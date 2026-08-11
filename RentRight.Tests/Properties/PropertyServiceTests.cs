using RentRight.Application.Abstractions;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.Properties;

public class PropertyServiceTests
{
    [Fact]
    public async Task SearchPublishedAsync_UnboundedQuery_NormalizesPagingAndSearch()
    {
        var store = new FakePropertyStore(
            Property("Apartment", landlordId: "landlord-1", id: 1));
        var service = CreateService(store, userId: null);

        var outcome = await service.SearchPublishedAsync(
            new PublishedPropertyQuery(
                Page: int.MaxValue,
                PageSize: int.MaxValue,
                Search: "  Apartment  "),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        Assert.Equal(PublishedPropertyQuery.MaximumPage, outcome.Value!.Page);
        Assert.Equal(PublishedPropertyQuery.MaximumPageSize, outcome.Value.PageSize);
        Assert.Equal(PublishedPropertyQuery.MaximumPage, store.LastPublishedQuery!.Page);
        Assert.Equal(
            PublishedPropertyQuery.MaximumPageSize,
            store.LastPublishedQuery.PageSize);
        Assert.Equal("Apartment", store.LastPublishedQuery.Search);
    }

    [Fact]
    public async Task ListAsync_ExcessiveLimit_IsBoundedAndMarksOnlyOwnedPropertyManageable()
    {
        var store = new FakePropertyStore(
            Property("owner-property", landlordId: "landlord-1", id: 1),
            Property("other-property", landlordId: "landlord-2", id: 2));
        var service = CreateService(store, "landlord-1", "Landlord");

        var outcome = await service.ListAsync(
            new PropertyListQuery(500),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        var properties = outcome.Value!;
        Assert.Equal(PropertyListQuery.MaximumLimit, store.LastListLimit);
        Assert.True(properties.Single(property => property.Id == 1).CanManage);
        Assert.False(properties.Single(property => property.Id == 2).CanManage);
    }

    [Fact]
    public async Task ListLatestAsync_ExcessiveLimit_IsBoundedAndProjectsPublicFields()
    {
        var store = new FakePropertyStore(
            Property("Older", landlordId: "landlord-1", id: 1),
            Property("Latest", landlordId: "landlord-2", id: 2));
        var service = CreateService(store, userId: null);

        var outcome = await service.ListLatestAsync(
            new LatestPropertiesQuery(500),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        Assert.Equal(LatestPropertiesQuery.MaximumLimit, store.LastLatestListLimit);
        Assert.Collection(
            outcome.Value!,
            property =>
            {
                Assert.Equal(2, property.Id);
                Assert.Equal("Latest", property.Title);
                Assert.Equal("Description", property.Description);
            },
            property => Assert.Equal(1, property.Id));
    }

    [Fact]
    public async Task GetDetailsAsync_AnonymousUser_ReturnsPublicProjectionWithoutManagePermission()
    {
        var service = CreateService(
            new FakePropertyStore(Property("Apartment", "landlord-1", 1)),
            userId: null);

        var outcome = await service.GetDetailsAsync(1, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        Assert.Equal("Apartment", outcome.Value!.Title);
        Assert.False(outcome.Value.CanManage);
    }

    [Fact]
    public async Task CreateAsync_Landlord_AssignsAuthenticatedUserAndPermittedScalarsOnly()
    {
        var store = new FakePropertyStore();
        var service = CreateService(store, "landlord-1", "Landlord");
        var input = new PropertyCreateInput
        {
            Title = "Apartment",
            Address = "1 Main Street",
            RentAmount = 1_250m,
            Description = "City centre"
        };

        var outcome = await service.CreateAsync(input, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        var created = Assert.Single(store.Properties);
        Assert.Equal("landlord-1", created.LandlordId);
        Assert.Equal("Apartment", created.Title);
        Assert.Equal("1 Main Street", created.Address);
        Assert.Equal(1_250m, created.RentAmount);
        Assert.Equal("City centre", created.Description);
    }

    [Fact]
    public async Task CreateAsync_Unauthenticated_ReturnsUnauthenticatedWithoutPersistence()
    {
        var store = new FakePropertyStore();
        var service = CreateService(store, userId: null);

        var outcome = await service.CreateAsync(
            new PropertyCreateInput
            {
                Title = "Apartment",
                Address = "1 Main Street",
                RentAmount = 1_250m
            },
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Unauthenticated, outcome.Status);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task UpdateAsync_Owner_UpdatesOnlyPermittedScalarsAndPreservesOwnership()
    {
        var store = new FakePropertyStore(
            Property("Before", landlordId: "landlord-1", id: 1));
        var service = CreateService(store, "landlord-1", "Landlord");
        var input = EditInput(1, "After");

        var outcome = await service.UpdateAsync(1, input, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        var updated = Assert.Single(store.Properties);
        Assert.Equal("landlord-1", updated.LandlordId);
        Assert.Equal("After", updated.Title);
        Assert.Equal(input.Address, updated.Address);
        Assert.Equal(input.RentAmount, updated.RentAmount);
        Assert.Equal(input.Description, updated.Description);
    }

    [Fact]
    public async Task UpdateAsync_ForgedInputId_UpdatesOnlyExplicitPropertyId()
    {
        var store = new FakePropertyStore(
            Property("Target", landlordId: "landlord-1", id: 1),
            Property("Other", landlordId: "landlord-1", id: 2));
        var service = CreateService(store, "landlord-1", "Landlord");
        var input = EditInput(2, "Updated target");

        var outcome = await service.UpdateAsync(1, input, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        Assert.Equal("Updated target", store.Properties.Single(property => property.Id == 1).Title);
        Assert.Equal("Other", store.Properties.Single(property => property.Id == 2).Title);
    }

    [Fact]
    public async Task UpdateAsync_NonOwner_ReturnsForbiddenWithoutPersistence()
    {
        var store = new FakePropertyStore(
            Property("Before", landlordId: "landlord-2", id: 1));
        var service = CreateService(store, "landlord-1", "Landlord");

        var outcome = await service.UpdateAsync(
            1,
            EditInput(1, "Forged update"),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Forbidden, outcome.Status);
        Assert.Equal(0, store.UpdateCount);
        Assert.Equal("Before", Assert.Single(store.Properties).Title);
    }

    [Fact]
    public async Task UpdateAsync_AdminAndLandlordRoles_AdminPrecedenceAllowsNonOwnerUpdate()
    {
        var store = new FakePropertyStore(
            Property("Before", landlordId: "landlord-2", id: 1));
        var service = CreateService(store, "admin-1", "Admin", "Landlord");

        var outcome = await service.UpdateAsync(
            1,
            EditInput(1, "Admin update"),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        var updated = Assert.Single(store.Properties);
        Assert.Equal("landlord-2", updated.LandlordId);
        Assert.Equal("Admin update", updated.Title);
    }

    [Fact]
    public async Task UpdateAsync_MissingProperty_ReturnsNotFound()
    {
        var service = CreateService(new FakePropertyStore(), "landlord-1", "Landlord");

        var outcome = await service.UpdateAsync(
            404,
            EditInput(404, "Missing"),
            CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.NotFound, outcome.Status);
    }

    [Fact]
    public async Task CreateAsync_InvalidInput_ReturnsFieldValidationWithoutPersistence()
    {
        var store = new FakePropertyStore();
        var service = CreateService(store, "landlord-1", "Landlord");
        var input = new PropertyCreateInput
        {
            Title = string.Empty,
            Address = new string('a', 501),
            RentAmount = -1m
        };

        var outcome = await service.CreateAsync(input, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.ValidationFailed, outcome.Status);
        Assert.Contains(nameof(PropertyCreateInput.Title), outcome.ValidationErrors!.Keys);
        Assert.Contains(nameof(PropertyCreateInput.Address), outcome.ValidationErrors.Keys);
        Assert.Contains(nameof(PropertyCreateInput.RentAmount), outcome.ValidationErrors.Keys);
        Assert.Empty(store.Properties);
        Assert.Equal(0, store.CreateCount);
    }

    [Fact]
    public async Task CreateAsync_ValuesAtValidationBoundaries_Succeeds()
    {
        var store = new FakePropertyStore();
        var service = CreateService(store, "landlord-1", "Landlord");
        var input = new PropertyCreateInput
        {
            Title = new string('\u00e1', 200),
            Address = new string('\u03a9', 500),
            RentAmount = 9_999_999_999_999_999.99m,
            Description = new string('\u4f4f', 2_000)
        };

        var outcome = await service.CreateAsync(input, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Success, outcome.Status);
        var created = Assert.Single(store.Properties);
        Assert.Equal(200, created.Title.Length);
        Assert.Equal(500, created.Address.Length);
        Assert.Equal(2_000, created.Description!.Length);
        Assert.Equal(input.RentAmount, created.RentAmount);
    }

    [Fact]
    public async Task DeleteAsync_PropertyWithHistory_ReturnsHistoryOutcomeWithoutDeletion()
    {
        var store = new FakePropertyStore(
            Property("Retained", landlordId: "landlord-1", id: 1))
        {
            DeleteStatus = PropertyDeleteStoreStatus.HasHistory
        };
        var service = CreateService(store, "landlord-1", "Landlord");

        var outcome = await service.DeleteAsync(1, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.HasHistory, outcome.Status);
        Assert.Single(store.Properties);
    }

    [Fact]
    public async Task DeleteAsync_StoreConflict_ReturnsConflictOutcome()
    {
        var store = new FakePropertyStore(
            Property("Changed", landlordId: "landlord-1", id: 1))
        {
            DeleteStatus = PropertyDeleteStoreStatus.Conflict
        };
        var service = CreateService(store, "landlord-1", "Landlord");

        var outcome = await service.DeleteAsync(1, CancellationToken.None);

        Assert.Equal(PropertyOperationStatus.Conflict, outcome.Status);
    }

    [Fact]
    public void MutationContracts_DoNotExposeOwnershipOrNavigationFields()
    {
        var createProperties = typeof(PropertyCreateInput).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var editProperties = typeof(PropertyEditInput).GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("LandlordId", createProperties);
        Assert.DoesNotContain("LandlordId", editProperties);
        Assert.DoesNotContain("Tenancies", createProperties);
        Assert.DoesNotContain("Tenancies", editProperties);
        Assert.Equal(
            ["Address", "Description", "RentAmount", "Title"],
            createProperties.OrderBy(name => name).ToArray());
        Assert.Equal(
            ["Address", "Description", "Id", "RentAmount", "Title"],
            editProperties.OrderBy(name => name).ToArray());
    }

    private static PropertyService CreateService(
        FakePropertyStore store,
        string? userId,
        params string[] roles) =>
        new(store, new FakeCurrentUser(userId, roles));

    private static PropertyEditInput EditInput(int id, string title) =>
        new()
        {
            Id = id,
            Title = title,
            Address = "Updated address",
            RentAmount = 1_500m,
            Description = "Updated description"
        };

    private static PropertyRecord Property(string title, string landlordId, int id) =>
        new(id, title, $"{id} Main Street", 1_000m, "Description", landlordId);

    private sealed class FakeCurrentUser(
        string? userId,
        IReadOnlyCollection<string> roles) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null;
        public string? UserId => userId;

        public bool IsInRole(string roleName) =>
            roles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FakePropertyStore : IPropertyStore
    {
        private readonly Dictionary<int, PropertyRecord> _properties;

        public FakePropertyStore(params PropertyRecord[] properties)
        {
            _properties = properties.ToDictionary(property => property.Id);
        }

        public IReadOnlyCollection<PropertyRecord> Properties =>
            _properties.Values.ToArray();

        public int LastListLimit { get; private set; }
        public int LastLatestListLimit { get; private set; }
        public PublishedPropertyQuery? LastPublishedQuery { get; private set; }
        public int CreateCount { get; private set; }
        public int UpdateCount { get; private set; }
        public PropertyDeleteStoreStatus DeleteStatus { get; set; } =
            PropertyDeleteStoreStatus.Deleted;

        public Task<PublishedPropertyPageRecord> SearchPublishedAsync(
            PublishedPropertyQuery query,
            CancellationToken cancellationToken)
        {
            LastPublishedQuery = query;
            var properties = _properties.Values.AsEnumerable();
            if (query.Search is not null)
            {
                properties = properties.Where(property =>
                    property.Title.Contains(query.Search, StringComparison.OrdinalIgnoreCase) ||
                    property.Address.Contains(query.Search, StringComparison.OrdinalIgnoreCase));
            }

            if (query.MinimumRent is decimal minimumRent)
            {
                properties = properties.Where(property => property.RentAmount >= minimumRent);
            }

            if (query.MaximumRent is decimal maximumRent)
            {
                properties = properties.Where(property => property.RentAmount <= maximumRent);
            }

            var filtered = properties.ToArray();
            IReadOnlyList<PublishedPropertyListItem> items = filtered
                .OrderBy(property => property.Title)
                .ThenBy(property => property.Address)
                .ThenBy(property => property.Id)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .Select(property => new PublishedPropertyListItem(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.Description))
                .ToArray();
            return Task.FromResult(new PublishedPropertyPageRecord(items, filtered.Length));
        }

        public Task<PublishedPropertyListItem?> FindPublishedAsync(
            int propertyId,
            CancellationToken cancellationToken)
        {
            var property = _properties.GetValueOrDefault(propertyId);
            return Task.FromResult(property is null
                ? null
                : new PublishedPropertyListItem(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.Description));
        }

        public Task<IReadOnlyList<PropertyListRecord>> ListAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            LastListLimit = limit;
            IReadOnlyList<PropertyListRecord> rows = _properties.Values
                .Take(limit)
                .Select(property => new PropertyListRecord(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.LandlordId))
                .ToArray();
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<LatestPropertyListItem>> ListLatestAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            LastLatestListLimit = limit;
            IReadOnlyList<LatestPropertyListItem> rows = _properties.Values
                .OrderByDescending(property => property.Id)
                .Take(limit)
                .Select(property => new LatestPropertyListItem(
                    property.Id,
                    property.Title,
                    property.Address,
                    property.RentAmount,
                    property.Description))
                .ToArray();
            return Task.FromResult(rows);
        }

        public Task<PropertyRecord?> FindAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_properties.GetValueOrDefault(propertyId));

        public Task<int> CreateAsync(
            PropertyValues values,
            string landlordId,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var id = _properties.Count == 0 ? 1 : _properties.Keys.Max() + 1;
            _properties[id] = new PropertyRecord(
                id,
                values.Title,
                values.Address,
                values.RentAmount,
                values.Description,
                landlordId);
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(
            int propertyId,
            PropertyValues values,
            CancellationToken cancellationToken)
        {
            UpdateCount++;
            if (!_properties.TryGetValue(propertyId, out var property))
            {
                return Task.FromResult(false);
            }

            _properties[propertyId] = property with
            {
                Title = values.Title,
                Address = values.Address,
                RentAmount = values.RentAmount,
                Description = values.Description
            };
            return Task.FromResult(true);
        }

        public Task<PropertyDeleteStoreStatus> DeleteAsync(
            int propertyId,
            CancellationToken cancellationToken)
        {
            if (DeleteStatus == PropertyDeleteStoreStatus.Deleted)
            {
                _properties.Remove(propertyId);
            }

            return Task.FromResult(DeleteStatus);
        }
    }
}
