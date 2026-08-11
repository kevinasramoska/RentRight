using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RentRight.Controllers;
using RentRight.Features.Properties;
using RentRight.Features.Properties.Contracts;
using Xunit;

namespace RentRight.Tests.Properties;

public class PropertiesControllerTests
{
    [Fact]
    public async Task Index_Success_MapsProjectedListToView()
    {
        IReadOnlyList<PropertyListItem> properties =
        [
            new(1, "Apartment", "1 Main Street", 1_200m, CanManage: true)
        ];
        var controller = CreateController(new FakePropertyService
        {
            ListOutcome =
                PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>.Success(properties)
        });

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(properties, view.Model);
    }

    [Fact]
    public async Task Details_MissingRecord_MapsToNotFound()
    {
        var controller = CreateController(new FakePropertyService
        {
            DetailsOutcome = PropertyQueryOutcome<PropertyDetails>.NotFound()
        });

        var result = await controller.Details(404, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(PropertyOperationStatus.Unauthenticated)]
    [InlineData(PropertyOperationStatus.Forbidden)]
    [InlineData(PropertyOperationStatus.NotFound)]
    public async Task Edit_QueryFailure_MapsToExpectedMvcResult(
        PropertyOperationStatus status)
    {
        var outcome = status switch
        {
            PropertyOperationStatus.Unauthenticated =>
                PropertyQueryOutcome<PropertyEditInput>.Unauthenticated(),
            PropertyOperationStatus.Forbidden =>
                PropertyQueryOutcome<PropertyEditInput>.Forbidden(),
            PropertyOperationStatus.NotFound =>
                PropertyQueryOutcome<PropertyEditInput>.NotFound(),
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(new FakePropertyService
        {
            EditOutcome = outcome
        });

        var result = await controller.Edit(1, CancellationToken.None);

        Assert.Equal(
            status switch
            {
                PropertyOperationStatus.Unauthenticated => typeof(ChallengeResult),
                PropertyOperationStatus.Forbidden => typeof(ForbidResult),
                _ => typeof(NotFoundResult)
            },
            result.GetType());
    }

    [Fact]
    public async Task Create_ServiceValidation_MapsFieldErrorsBackToInputView()
    {
        var service = new FakePropertyService
        {
            CreateOutcome = PropertyMutationOutcome.ValidationFailed(
                new Dictionary<string, string[]>
                {
                    [nameof(PropertyCreateInput.Title)] = ["A title is required."]
                })
        };
        var controller = CreateController(service);
        var input = ValidCreateInput();

        var result = await controller.Create(input, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Same(input, view.Model);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            "A title is required.",
            controller.ModelState[nameof(PropertyCreateInput.Title)]!.Errors
                .Select(error => error.ErrorMessage));
    }

    [Fact]
    public async Task Edit_RouteAndInputIdMismatch_ReturnsNotFoundWithoutCallingService()
    {
        var service = new FakePropertyService();
        var controller = CreateController(service);

        var result = await controller.Edit(
            1,
            ValidEditInput(id: 2),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(0, service.UpdateCallCount);
    }

    [Fact]
    public async Task Edit_ForbiddenOutcome_MapsToForbid()
    {
        var controller = CreateController(new FakePropertyService
        {
            UpdateOutcome = PropertyMutationOutcome.Forbidden()
        });

        var result = await controller.Edit(
            1,
            ValidEditInput(1),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Edit_SuccessOutcome_RedirectsToIndex()
    {
        var controller = CreateController(new FakePropertyService
        {
            UpdateOutcome = PropertyMutationOutcome.Success(1)
        });

        var result = await controller.Edit(
            1,
            ValidEditInput(1),
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PropertiesController.Index), redirect.ActionName);
    }

    [Fact]
    public async Task DeleteConfirmed_HistoryOutcome_RedirectsWithExistingUserMessage()
    {
        var controller = CreateController(new FakePropertyService
        {
            DeleteOutcome = PropertyMutationOutcome.HasHistory()
        });

        var result = await controller.DeleteConfirmed(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PropertiesController.Index), redirect.ActionName);
        Assert.Equal(
            "A property with tenancy history cannot be deleted.",
            controller.TempData["Error"]);
    }

    [Fact]
    public async Task DeleteConfirmed_ConflictOutcome_RedirectsWithRetryMessage()
    {
        var controller = CreateController(new FakePropertyService
        {
            DeleteOutcome = PropertyMutationOutcome.Conflict()
        });

        var result = await controller.DeleteConfirmed(1, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(PropertiesController.Index), redirect.ActionName);
        Assert.Contains("changed", Assert.IsType<string>(controller.TempData["Error"]));
    }

    [Fact]
    public async Task DeleteConfirmed_MissingRecord_MapsToNotFound()
    {
        var controller = CreateController(new FakePropertyService
        {
            DeleteOutcome = PropertyMutationOutcome.NotFound()
        });

        var result = await controller.DeleteConfirmed(404, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    private static PropertiesController CreateController(IPropertyService service)
    {
        var controller = new PropertiesController(service);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            new InMemoryTempDataProvider());
        return controller;
    }

    private static PropertyCreateInput ValidCreateInput() =>
        new()
        {
            Title = "Apartment",
            Address = "1 Main Street",
            RentAmount = 1_200m
        };

    private static PropertyEditInput ValidEditInput(int id) =>
        new()
        {
            Id = id,
            Title = "Apartment",
            Address = "1 Main Street",
            RentAmount = 1_200m
        };

    private sealed class FakePropertyService : IPropertyService
    {
        public PropertyQueryOutcome<IReadOnlyList<PropertyListItem>> ListOutcome { get; init; } =
            PropertyQueryOutcome<IReadOnlyList<PropertyListItem>>.Success([]);
        public PropertyQueryOutcome<PropertyDetails> DetailsOutcome { get; init; } =
            PropertyQueryOutcome<PropertyDetails>.NotFound();
        public PropertyMutationOutcome CreateOutcome { get; init; } =
            PropertyMutationOutcome.Success(1);
        public PropertyQueryOutcome<PropertyEditInput> EditOutcome { get; init; } =
            PropertyQueryOutcome<PropertyEditInput>.NotFound();
        public PropertyMutationOutcome UpdateOutcome { get; init; } =
            PropertyMutationOutcome.Success(1);
        public PropertyQueryOutcome<PropertyDetails> DeleteQueryOutcome { get; init; } =
            PropertyQueryOutcome<PropertyDetails>.NotFound();
        public PropertyMutationOutcome DeleteOutcome { get; init; } =
            PropertyMutationOutcome.Success(1);
        public int UpdateCallCount { get; private set; }

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
            Task.FromResult(ListOutcome);

        public Task<PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>> ListLatestAsync(
            LatestPropertiesQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                PropertyQueryOutcome<IReadOnlyList<LatestPropertyListItem>>.Success([]));

        public Task<PropertyQueryOutcome<PropertyDetails>> GetDetailsAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(DetailsOutcome);

        public Task<PropertyMutationOutcome> CreateAsync(
            PropertyCreateInput input,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateOutcome);

        public Task<PropertyQueryOutcome<PropertyEditInput>> GetEditAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(EditOutcome);

        public Task<PropertyMutationOutcome> UpdateAsync(
            int propertyId,
            PropertyEditInput input,
            CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            return Task.FromResult(UpdateOutcome);
        }

        public Task<PropertyQueryOutcome<PropertyDetails>> GetDeleteAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeleteQueryOutcome);

        public Task<PropertyMutationOutcome> DeleteAsync(
            int propertyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(DeleteOutcome);
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(
            HttpContext context,
            IDictionary<string, object> values)
        {
        }
    }
}
