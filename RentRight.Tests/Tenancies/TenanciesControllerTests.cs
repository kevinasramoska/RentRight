using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RentRight.Controllers;
using RentRight.Features.Tenancies;
using RentRight.Features.Tenancies.Contracts;
using RentRight.ViewModels;
using Xunit;

namespace RentRight.Tests.Tenancies;

public class TenanciesControllerTests
{
    [Theory]
    [InlineData(TenancyQueryStatus.Unauthenticated)]
    [InlineData(TenancyQueryStatus.Forbidden)]
    public async Task Index_AuthorizationOutcome_MapsToExpectedMvcResult(
        TenancyQueryStatus status)
    {
        var service = new FakeTenancyService
        {
            ListOutcome = status == TenancyQueryStatus.Unauthenticated
                ? ListTenanciesOutcome.Unauthenticated()
                : ListTenanciesOutcome.Forbidden()
        };
        var controller = CreateController(service);

        var result = await controller.Index(CancellationToken.None);

        if (status == TenancyQueryStatus.Unauthenticated)
        {
            Assert.IsType<ChallengeResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    [Fact]
    public async Task CreateGet_Success_MapsContractToFormViewModel()
    {
        var service = new FakeTenancyService();
        var controller = CreateController(service);

        var result = await controller.Create(CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TenancyCreateViewModel>(view.Model);
        Assert.Equal(new DateOnly(2026, 8, 11), model.StartDate);
        Assert.Single(model.AvailableProperties);
    }

    [Theory]
    [InlineData(CreateTenancyStatus.ValidationFailed, "EndDate")]
    [InlineData(CreateTenancyStatus.PropertyNotFound, "PropertyId")]
    [InlineData(CreateTenancyStatus.Conflict, "PropertyId")]
    public async Task CreatePost_RecoverableOutcome_ReturnsFormWithFieldError(
        CreateTenancyStatus status,
        string expectedField)
    {
        var outcome = status switch
        {
            CreateTenancyStatus.ValidationFailed =>
                CreateTenancyOutcome.ValidationFailed(
                    [new TenancyValidationError("EndDate", "Invalid date.")]),
            CreateTenancyStatus.PropertyNotFound => CreateTenancyOutcome.PropertyNotFound(),
            CreateTenancyStatus.Conflict => CreateTenancyOutcome.Conflict(),
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(new FakeTenancyService { CreateOutcome = outcome });

        var result = await controller.Create(ValidModel(), CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<TenancyCreateViewModel>(view.Model);
        Assert.NotEmpty(controller.ModelState[expectedField]!.Errors);
        Assert.Single(model.AvailableProperties);
    }

    [Theory]
    [InlineData(CreateTenancyStatus.Unauthenticated)]
    [InlineData(CreateTenancyStatus.Forbidden)]
    public async Task CreatePost_TerminalAuthorizationOutcome_MapsToExpectedMvcResult(
        CreateTenancyStatus status)
    {
        var outcome = status == CreateTenancyStatus.Unauthenticated
            ? CreateTenancyOutcome.Unauthenticated()
            : CreateTenancyOutcome.Forbidden();
        var controller = CreateController(new FakeTenancyService { CreateOutcome = outcome });

        var result = await controller.Create(ValidModel(), CancellationToken.None);

        if (status == CreateTenancyStatus.Unauthenticated)
        {
            Assert.IsType<ChallengeResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    [Fact]
    public async Task CreatePost_Success_ShowsOneTimeReceipt()
    {
        var controller = CreateController(new FakeTenancyService
        {
            CreateOutcome = CreateTenancyOutcome.Success(new InvitationReceipt(new string('A', 64), DateTime.UtcNow.AddDays(7)))
        });

        var result = await controller.Create(ValidModel(), CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Invitation", view.ViewName);
        Assert.Empty(controller.TempData);
    }

    [Theory]
    [InlineData(EndTenancyStatus.Unauthenticated)]
    [InlineData(EndTenancyStatus.Forbidden)]
    [InlineData(EndTenancyStatus.NotFound)]
    public async Task End_TerminalOutcome_MapsToExpectedMvcResult(EndTenancyStatus status)
    {
        var outcome = status switch
        {
            EndTenancyStatus.Unauthenticated => EndTenancyOutcome.Unauthenticated(),
            EndTenancyStatus.Forbidden => EndTenancyOutcome.Forbidden(),
            EndTenancyStatus.NotFound => EndTenancyOutcome.NotFound(),
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(new FakeTenancyService { EndOutcome = outcome });

        var result = await controller.End(42, CancellationToken.None);

        switch (status)
        {
            case EndTenancyStatus.Unauthenticated:
                Assert.IsType<ChallengeResult>(result);
                break;
            case EndTenancyStatus.Forbidden:
                Assert.IsType<ForbidResult>(result);
                break;
            case EndTenancyStatus.NotFound:
                Assert.IsType<NotFoundResult>(result);
                break;
        }
    }

    [Fact]
    public async Task End_Conflict_RedirectsWithRecoverableError()
    {
        var controller = CreateController(new FakeTenancyService
        {
            EndOutcome = EndTenancyOutcome.Conflict()
        });

        var result = await controller.End(42, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(TenanciesController.Index), redirect.ActionName);
        Assert.Contains("changed", Assert.IsType<string>(controller.TempData["Error"]));
    }

    private static TenanciesController CreateController(ITenancyService service)
    {
        var controller = new TenanciesController(service);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            new InMemoryTempDataProvider());
        return controller;
    }

    private static TenancyCreateViewModel ValidModel() => new()
    {
        PropertyId = 10,
        TenantId = "tenant-1",
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 11)
    };

    private sealed class FakeTenancyService : ITenancyService
    {
        private static readonly TenancyCreateForm Form = new(
            new DateOnly(2026, 8, 11),
            [new TenancySelectionOption("10", "Home — Address")]);

        public ListTenanciesOutcome ListOutcome { get; init; } =
            ListTenanciesOutcome.Success([]);
        public GetTenancyCreateFormOutcome FormOutcome { get; init; } =
            GetTenancyCreateFormOutcome.Success(Form);
        public CreateTenancyOutcome CreateOutcome { get; init; } =
            CreateTenancyOutcome.Success(new InvitationReceipt(new string('A', 64), DateTime.UtcNow.AddDays(7)));
        public EndTenancyOutcome EndOutcome { get; init; } =
            EndTenancyOutcome.Success(changed: true);

        public Task<AcceptTenancyOutcome> AcceptAsync(AcceptTenancyCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new AcceptTenancyOutcome(AcceptTenancyStatus.Success));
        public Task<GetTenancyOutcome> GetAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(new GetTenancyOutcome(TenancyQueryStatus.Success));
        public Task<ListTenanciesPageOutcome> ListAsync(int page, int size, CancellationToken cancellationToken) =>
            Task.FromResult(new ListTenanciesPageOutcome(TenancyQueryStatus.Success, new TenancyPage([], 0)));

        public Task<ListTenanciesOutcome> ListForAdministrationAsync(
            CancellationToken cancellationToken) => Task.FromResult(ListOutcome);

        public Task<GetTenancyCreateFormOutcome> GetCreateFormAsync(
            CancellationToken cancellationToken) => Task.FromResult(FormOutcome);

        public Task<CreateTenancyOutcome> CreateAsync(
            CreateTenancyCommand command,
            CancellationToken cancellationToken) => Task.FromResult(CreateOutcome);

        public Task<EndTenancyOutcome> EndAsync(
            int tenancyId,
            CancellationToken cancellationToken, string? expectedVersion = null) => Task.FromResult(EndOutcome);
    }

    private sealed class InMemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
