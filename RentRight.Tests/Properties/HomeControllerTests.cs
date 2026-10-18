using Microsoft.AspNetCore.Mvc;
using RentRight.Controllers;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.Properties;

public class HomeControllerTests
{
    [Fact]
    public async Task Index_RootEntry_RedirectsToReactWithoutQueryingDatabase()
    {
        var service = new FakePropertyService([]);
        var controller = new HomeController(service) { ControllerContext = new ControllerContext
        { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
        controller.HttpContext.Request.Path = "/";
        var result = Assert.IsType<RedirectResult>(await controller.Index(default));
        Assert.Equal("/app/properties", result.Url);
        Assert.Null(service.Query);
    }

    [Fact]
    public async Task Index_Success_MapsBoundedLatestPropertyQueryToView()
    {
        IReadOnlyList<LatestPropertyListItem> properties =
        [
            new(2, "Latest", "2 Main Street", 1_200m, "Description")
        ];
        var service = new FakePropertyService(properties);
        var controller = new HomeController(service);
        using var cancellationSource = new CancellationTokenSource();

        var result = await controller.Index(cancellationSource.Token);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(properties, view.Model);
        Assert.Equal(LatestPropertiesQuery.DefaultLimit, service.Query!.Limit);
        Assert.Equal(cancellationSource.Token, service.CancellationToken);
    }

    private sealed class FakePropertyService(
        IReadOnlyList<LatestPropertyListItem> properties) : IPropertyService
    {
        public LatestPropertiesQuery? Query { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<PropertyQueryOutcome<PropertyPage<PublishedPropertyListItem>>>
            SearchPublishedAsync(
                PublishedPropertyQuery query,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyQueryOutcome<PublishedPropertyDetails>>
            GetPublishedDetailsAsync(
                int propertyId,
                CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>> ListAsync(
            PropertyListQuery query,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>> ListLatestAsync(
            LatestPropertiesQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            CancellationToken = cancellationToken;
            return Task.FromResult(
                PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>.Success(properties));
        }

        public Task<PropertyQueryOutcome<PropertyDetails>> GetDetailsAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyMutationOutcome> CreateAsync(
            PropertyCreateInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyQueryOutcome<PropertyEditInput>> GetEditAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyMutationOutcome> UpdateAsync(
            int propertyId,
            PropertyEditInput input,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyQueryOutcome<PropertyDetails>> GetDeleteAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<PropertyMutationOutcome> DeleteAsync(
            int propertyId,
            CancellationToken cancellationToken, string? expectedVersion = null) =>
            throw new NotSupportedException();
    }
}
