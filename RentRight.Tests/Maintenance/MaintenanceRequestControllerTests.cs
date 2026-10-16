using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RentRight.Controllers;
using RentRight.Features.Maintenance;
using RentRight.Features.Maintenance.Contracts;
using RentRight.Models;
using RentRight.ViewModels;
using Xunit;

namespace RentRight.Tests.Maintenance;

public class MaintenanceRequestControllerTests
{
    [Fact]
    public async Task Index_Success_MapsExplicitRowsToExistingViewModel()
    {
        var query = new FakeQueryService
        {
            ListOutcome = MaintenanceListOutcome.Success(new MaintenanceRequestList(
                MaintenanceListAudience.Landlord,
                CanCreate: true,
                [new MaintenanceRequestRow(
                    10,
                    "Property 1",
                    "Tenant One",
                    "Leaking kitchen tap",
                    new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc),
                    MaintenanceRequestStatus.Submitted,
                    CanUpdateStatus: true)],
                Page: 1,
                PageSize: 50))
        };
        var controller = CreateController(query, new FakeCommandService());

        var result = await controller.Index(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MaintenanceRequestIndexViewModel>(view.Model);
        Assert.Equal("Maintenance Requests for My Properties", model.Heading);
        Assert.True(model.CanCreate);
        var row = Assert.Single(model.Requests);
        Assert.Equal(10, row.Id);
        Assert.True(row.CanUpdateStatus);
    }

    [Theory]
    [InlineData(MaintenanceOutcomeStatus.Unauthenticated)]
    [InlineData(MaintenanceOutcomeStatus.Forbidden)]
    public async Task Index_AuthorizationOutcome_MapsToExpectedMvcResult(
        MaintenanceOutcomeStatus status)
    {
        var query = new FakeQueryService
        {
            ListOutcome = status == MaintenanceOutcomeStatus.Unauthenticated
                ? MaintenanceListOutcome.Unauthenticated()
                : MaintenanceListOutcome.Forbidden()
        };
        var controller = CreateController(query, new FakeCommandService());

        var result = await controller.Index(CancellationToken.None);

        if (status == MaintenanceOutcomeStatus.Unauthenticated)
        {
            Assert.IsType<ChallengeResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    [Theory]
    [InlineData(MaintenanceOutcomeStatus.Unauthenticated)]
    [InlineData(MaintenanceOutcomeStatus.Forbidden)]
    [InlineData(MaintenanceOutcomeStatus.NotFound)]
    [InlineData(MaintenanceOutcomeStatus.ConcealedNotFound)]
    public async Task Details_TerminalOutcome_MapsToExpectedMvcResult(
        MaintenanceOutcomeStatus status)
    {
        var query = new FakeQueryService
        {
            DetailOutcome = status switch
            {
                MaintenanceOutcomeStatus.Unauthenticated =>
                    MaintenanceDetailOutcome.Unauthenticated(),
                MaintenanceOutcomeStatus.Forbidden =>
                    MaintenanceDetailOutcome.Forbidden(),
                MaintenanceOutcomeStatus.NotFound =>
                    MaintenanceDetailOutcome.NotFound(),
                MaintenanceOutcomeStatus.ConcealedNotFound =>
                    MaintenanceDetailOutcome.ConcealedNotFound(),
                _ => throw new InvalidOperationException()
            }
        };
        var controller = CreateController(query, new FakeCommandService());

        var result = await controller.Details(10, CancellationToken.None);

        switch (status)
        {
            case MaintenanceOutcomeStatus.Unauthenticated:
                Assert.IsType<ChallengeResult>(result);
                break;
            case MaintenanceOutcomeStatus.Forbidden:
                Assert.IsType<ForbidResult>(result);
                break;
            default:
                Assert.IsType<NotFoundResult>(result);
                break;
        }
    }

    [Fact]
    public async Task Details_Success_MapsDetailContractToViewModel()
    {
        var detail = CreateDetail();
        var query = new FakeQueryService
        {
            DetailOutcome = MaintenanceDetailOutcome.Success(detail)
        };
        var controller = CreateController(query, new FakeCommandService());

        var result = await controller.Details(10, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MaintenanceRequestDetailsViewModel>(view.Model);
        Assert.Equal(detail.PropertyAddress, model.PropertyAddress);
        Assert.Equal(detail.LastUpdatedUtc, model.LastUpdatedUtc);
        Assert.True(model.CanUpdateStatus);
    }

    [Fact]
    public async Task CreatePost_Validation_RehydratesChoicesAndMapsFieldErrors()
    {
        var query = new FakeQueryService
        {
            CreateFormOutcome = MaintenanceCreateFormOutcome.Success(
                new MaintenanceCreateForm(
                    [new MaintenanceTenancyOption(1, "Property 1", "1 Example Street")]))
        };
        var command = new FakeCommandService
        {
            CreateOutcome = CreateMaintenanceRequestOutcome.Validation(
                [new MaintenanceValidationError("Description", "Description is invalid.")])
        };
        var controller = CreateController(query, command);
        var input = new MaintenanceRequestCreateViewModel
        {
            TenancyId = 1,
            Description = "   "
        };

        var result = await controller.Create(input, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MaintenanceRequestCreateViewModel>(view.Model);
        Assert.Equal(1, model.TenancyId);
        Assert.Equal("   ", model.Description);
        Assert.Single(model.AvailableTenancies);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(
            "Description is invalid.",
            controller.ModelState["Description"]!.Errors.Select(error => error.ErrorMessage));
    }

    [Fact]
    public async Task CreatePost_Success_RedirectsToDetailsAndSetsConfirmation()
    {
        var command = new FakeCommandService
        {
            CreateOutcome = CreateMaintenanceRequestOutcome.Success(73)
        };
        var controller = CreateController(new FakeQueryService(), command);

        var result = await controller.Create(
            new MaintenanceRequestCreateViewModel
            {
                TenancyId = 1,
                Description = "A valid maintenance description"
            },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MaintenanceRequestController.Details), redirect.ActionName);
        Assert.Equal(73, redirect.RouteValues!["id"]);
        Assert.Equal("Maintenance request submitted.", controller.TempData["Success"]);
    }

    [Theory]
    [InlineData(MaintenanceOutcomeStatus.Unauthenticated, typeof(ChallengeResult))]
    [InlineData(MaintenanceOutcomeStatus.Forbidden, typeof(ForbidResult))]
    [InlineData(MaintenanceOutcomeStatus.NotFound, typeof(NotFoundResult))]
    [InlineData(MaintenanceOutcomeStatus.ConcealedNotFound, typeof(NotFoundResult))]
    [InlineData(MaintenanceOutcomeStatus.Conflict, typeof(ConflictResult))]
    public async Task CreatePost_TerminalOutcome_MapsToExpectedMvcResult(
        MaintenanceOutcomeStatus status,
        Type expectedResultType)
    {
        var command = new FakeCommandService
        {
            CreateOutcome = status switch
            {
                MaintenanceOutcomeStatus.Unauthenticated =>
                    CreateMaintenanceRequestOutcome.Unauthenticated(),
                MaintenanceOutcomeStatus.Forbidden =>
                    CreateMaintenanceRequestOutcome.Forbidden(),
                MaintenanceOutcomeStatus.NotFound =>
                    CreateMaintenanceRequestOutcome.NotFound(),
                MaintenanceOutcomeStatus.ConcealedNotFound =>
                    CreateMaintenanceRequestOutcome.ConcealedNotFound(),
                MaintenanceOutcomeStatus.Conflict =>
                    CreateMaintenanceRequestOutcome.Conflict(),
                _ => throw new InvalidOperationException()
            }
        };
        var controller = CreateController(new FakeQueryService(), command);

        var result = await controller.Create(
            new MaintenanceRequestCreateViewModel(),
            CancellationToken.None);

        Assert.IsType(expectedResultType, result);
    }

    [Fact]
    public async Task UpdateStatusGet_Success_MapsAllowedTransitionsToSelectItems()
    {
        var query = new FakeQueryService
        {
            StatusOutcome = MaintenanceStatusUpdateOutcome.Success(
                new MaintenanceStatusUpdate(
                    10,
                    "Property 1",
                    "Tenant One",
                    MaintenanceRequestStatus.Resolved,
                    [MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Closed]))
        };
        var controller = CreateController(query, new FakeCommandService());

        var result = await controller.UpdateStatus(10, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MaintenanceRequestStatusUpdateViewModel>(view.Model);
        Assert.Equal(2, model.AllowedStatuses.Count);
        Assert.Equal("In Progress", model.AllowedStatuses[0].Text);
    }

    [Fact]
    public async Task UpdateStatusPost_Validation_RehydratesCurrentStatusForm()
    {
        var query = new FakeQueryService
        {
            StatusOutcome = MaintenanceStatusUpdateOutcome.Success(
                new MaintenanceStatusUpdate(
                    10,
                    "Property 1",
                    "Tenant One",
                    MaintenanceRequestStatus.Submitted,
                    [MaintenanceRequestStatus.InProgress]))
        };
        var command = new FakeCommandService
        {
            UpdateOutcome = UpdateMaintenanceRequestStatusOutcome.Validation(
                [new MaintenanceValidationError("NewStatus", "Transition is invalid.")])
        };
        var controller = CreateController(query, command);

        var result = await controller.UpdateStatus(
            10,
            new MaintenanceRequestStatusUpdateInputModel
            {
                Id = 10,
                NewStatus = MaintenanceRequestStatus.Closed
            },
            CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<MaintenanceRequestStatusUpdateViewModel>(view.Model);
        Assert.Equal(MaintenanceRequestStatus.Closed, model.NewStatus);
        Assert.False(controller.ModelState.IsValid);
    }

    [Fact]
    public async Task UpdateStatusPost_Success_RedirectsToDetailsAndSetsConfirmation()
    {
        var command = new FakeCommandService
        {
            UpdateOutcome = UpdateMaintenanceRequestStatusOutcome.Success(10)
        };
        var controller = CreateController(new FakeQueryService(), command);

        var result = await controller.UpdateStatus(
            10,
            new MaintenanceRequestStatusUpdateInputModel
            {
                Id = 10,
                NewStatus = MaintenanceRequestStatus.InProgress
            },
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(MaintenanceRequestController.Details), redirect.ActionName);
        Assert.Equal(10, redirect.RouteValues!["id"]);
        Assert.Equal(
            "Maintenance request status updated.",
            controller.TempData["Success"]);
    }

    [Theory]
    [InlineData(MaintenanceOutcomeStatus.Unauthenticated, typeof(ChallengeResult))]
    [InlineData(MaintenanceOutcomeStatus.Forbidden, typeof(ForbidResult))]
    [InlineData(MaintenanceOutcomeStatus.NotFound, typeof(NotFoundResult))]
    [InlineData(MaintenanceOutcomeStatus.ConcealedNotFound, typeof(NotFoundResult))]
    [InlineData(MaintenanceOutcomeStatus.Conflict, typeof(ConflictObjectResult))]
    public async Task UpdateStatusPost_TerminalOutcome_MapsToExpectedMvcResult(
        MaintenanceOutcomeStatus status,
        Type expectedResultType)
    {
        var command = new FakeCommandService
        {
            UpdateOutcome = status switch
            {
                MaintenanceOutcomeStatus.Unauthenticated =>
                    UpdateMaintenanceRequestStatusOutcome.Unauthenticated(),
                MaintenanceOutcomeStatus.Forbidden =>
                    UpdateMaintenanceRequestStatusOutcome.Forbidden(),
                MaintenanceOutcomeStatus.NotFound =>
                    UpdateMaintenanceRequestStatusOutcome.NotFound(),
                MaintenanceOutcomeStatus.ConcealedNotFound =>
                    UpdateMaintenanceRequestStatusOutcome.ConcealedNotFound(),
                MaintenanceOutcomeStatus.Conflict =>
                    UpdateMaintenanceRequestStatusOutcome.Conflict(),
                _ => throw new InvalidOperationException()
            }
        };
        var controller = CreateController(new FakeQueryService(), command);

        var result = await controller.UpdateStatus(
            10,
            new MaintenanceRequestStatusUpdateInputModel { Id = 10 },
            CancellationToken.None);

        Assert.IsType(expectedResultType, result);
    }

    private static MaintenanceRequestDetail CreateDetail() =>
        new(
            10,
            "Property 1",
            "1 Example Street",
            "Tenant One",
            "Leaking kitchen tap",
            new DateTime(2026, 8, 11, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 8, 11, 11, 0, 0, DateTimeKind.Utc),
            MaintenanceRequestStatus.InProgress,
            CanUpdateStatus: true);

    private static MaintenanceRequestController CreateController(
        IMaintenanceQueryService queryService,
        IMaintenanceCommandService commandService)
    {
        var controller = new MaintenanceRequestController(queryService, commandService);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            new InMemoryTempDataProvider());
        return controller;
    }

    private sealed class FakeQueryService : IMaintenanceQueryService
    {
        public MaintenanceListOutcome ListOutcome { get; init; } =
            MaintenanceListOutcome.Forbidden();
        public MaintenanceDetailOutcome DetailOutcome { get; init; } =
            MaintenanceDetailOutcome.Forbidden();
        public MaintenanceCreateFormOutcome CreateFormOutcome { get; init; } =
            MaintenanceCreateFormOutcome.Forbidden();
        public MaintenanceStatusUpdateOutcome StatusOutcome { get; init; } =
            MaintenanceStatusUpdateOutcome.Forbidden();

        public Task<MaintenanceListOutcome> ListAsync(
            MaintenanceRequestsQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(ListOutcome);

        public Task<MaintenanceDetailOutcome> GetDetailAsync(
            int requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(DetailOutcome);

        public Task<MaintenanceCreateFormOutcome> GetCreateFormAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateFormOutcome);

        public Task<MaintenanceStatusUpdateOutcome> GetStatusUpdateAsync(
            int requestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(StatusOutcome);
    }

    private sealed class FakeCommandService : IMaintenanceCommandService
    {
        public CreateMaintenanceRequestOutcome CreateOutcome { get; init; } =
            CreateMaintenanceRequestOutcome.Forbidden();
        public UpdateMaintenanceRequestStatusOutcome UpdateOutcome { get; init; } =
            UpdateMaintenanceRequestStatusOutcome.Forbidden();

        public Task<CreateMaintenanceRequestOutcome> CreateAsync(
            CreateMaintenanceRequestCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(CreateOutcome);

        public Task<UpdateMaintenanceRequestStatusOutcome> UpdateStatusAsync(
            UpdateMaintenanceRequestStatusCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(UpdateOutcome);
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
