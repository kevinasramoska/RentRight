using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RentRight.Controllers;
using RentRight.Features.Administration;
using RentRight.Features.Administration.Contracts;
using RentRight.ViewModels;
using Xunit;

namespace RentRight.Tests.Administration;

public class AdminControllerTests
{
    [Fact]
    public async Task Index_Success_MapsServicePageToExplicitViewRows()
    {
        var page = new AdministrationUsersPage(
            [new AdministrationUserRow(
                "user-1",
                "Example User",
                "user@example.com",
                ["Admin", "Landlord"])],
            ["Admin", "Landlord", "Tenant"],
            Page: 2,
            PageSize: 25,
            TotalUsers: 31);
        var service = new FakeAdministrationService
        {
            ListOutcome = ListAdministrationUsersOutcome.Success(page)
        };
        var controller = CreateController(service);

        var result = await controller.Index(2, CancellationToken.None);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<AdminUserRolesViewModel>(view.Model);
        var row = Assert.Single(model.Users);
        Assert.Equal("user-1", row.UserId);
        Assert.Equal("Example User", row.FullName);
        Assert.Equal("user@example.com", row.Email);
        Assert.Equal(["Admin", "Landlord"], row.Roles);
        Assert.Equal(2, model.Page);
        Assert.Equal(2, model.TotalPages);
        Assert.Equal(31, model.TotalUsers);
    }

    [Theory]
    [InlineData(ListAdministrationUsersStatus.Unauthenticated)]
    [InlineData(ListAdministrationUsersStatus.Forbidden)]
    public async Task Index_AuthorizationOutcome_MapsToExpectedMvcResult(
        ListAdministrationUsersStatus status)
    {
        var service = new FakeAdministrationService
        {
            ListOutcome = status == ListAdministrationUsersStatus.Unauthenticated
                ? ListAdministrationUsersOutcome.Unauthenticated()
                : ListAdministrationUsersOutcome.Forbidden()
        };
        var controller = CreateController(service);

        var result = await controller.Index(1, CancellationToken.None);

        if (status == ListAdministrationUsersStatus.Unauthenticated)
        {
            Assert.IsType<ChallengeResult>(result);
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }
    }

    [Theory]
    [InlineData(UpdateUserRolesStatus.Unauthenticated)]
    [InlineData(UpdateUserRolesStatus.Forbidden)]
    [InlineData(UpdateUserRolesStatus.UserNotFound)]
    public async Task UpdateRoles_TerminalOutcome_MapsToExpectedMvcResult(
        UpdateUserRolesStatus status)
    {
        var outcome = status switch
        {
            UpdateUserRolesStatus.Unauthenticated => UpdateUserRolesOutcome.Unauthenticated(),
            UpdateUserRolesStatus.Forbidden => UpdateUserRolesOutcome.Forbidden(),
            UpdateUserRolesStatus.UserNotFound => UpdateUserRolesOutcome.UserNotFound(),
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(new FakeAdministrationService
        {
            UpdateOutcome = outcome
        });

        var result = await controller.UpdateRoles(
            "user-1",
            ["Tenant"],
            CancellationToken.None);

        switch (status)
        {
            case UpdateUserRolesStatus.Unauthenticated:
                Assert.IsType<ChallengeResult>(result);
                break;
            case UpdateUserRolesStatus.Forbidden:
                Assert.IsType<ForbidResult>(result);
                break;
            case UpdateUserRolesStatus.UserNotFound:
                Assert.IsType<NotFoundResult>(result);
                break;
        }
    }

    [Theory]
    [InlineData(UpdateUserRolesStatus.InvalidRoles, "One or more selected roles are invalid.")]
    [InlineData(UpdateUserRolesStatus.LastAdministrator, "At least one administrator account must remain")]
    [InlineData(UpdateUserRolesStatus.IdentityFailure, "Identity rejected the update.")]
    [InlineData(UpdateUserRolesStatus.ConcurrentUpdate, "Roles changed concurrently.")]
    public async Task UpdateRoles_RecoverableFailure_MapsToValidationAndRedirect(
        UpdateUserRolesStatus status,
        string expectedMessage)
    {
        var outcome = status switch
        {
            UpdateUserRolesStatus.InvalidRoles =>
                UpdateUserRolesOutcome.InvalidRoles(["Invalid"]),
            UpdateUserRolesStatus.LastAdministrator =>
                UpdateUserRolesOutcome.LastAdministrator(),
            UpdateUserRolesStatus.IdentityFailure =>
                UpdateUserRolesOutcome.IdentityFailure(["Identity rejected the update."]),
            UpdateUserRolesStatus.ConcurrentUpdate =>
                UpdateUserRolesOutcome.ConcurrentUpdate(),
            _ => throw new InvalidOperationException()
        };
        var controller = CreateController(new FakeAdministrationService
        {
            UpdateOutcome = outcome
        });

        var result = await controller.UpdateRoles(
            "user-1",
            ["Tenant"],
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminController.Index), redirect.ActionName);
        Assert.False(controller.ModelState.IsValid);
        Assert.Contains(expectedMessage, Assert.IsType<string>(controller.TempData["Error"]));
    }

    [Fact]
    public async Task UpdateRoles_Success_MapsToRedirectAndConfirmation()
    {
        var controller = CreateController(new FakeAdministrationService
        {
            UpdateOutcome = UpdateUserRolesOutcome.Success("user@example.com")
        });

        var result = await controller.UpdateRoles(
            "user-1",
            ["Tenant"],
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(AdminController.Index), redirect.ActionName);
        Assert.Equal(
            "Updated roles for user@example.com",
            controller.TempData["Success"]);
    }

    private static AdminController CreateController(IAdministrationService service)
    {
        var controller = new AdminController(service);
        controller.TempData = new TempDataDictionary(
            new DefaultHttpContext(),
            new InMemoryTempDataProvider());
        return controller;
    }

    private sealed class FakeAdministrationService : IAdministrationService
    {
        public ListAdministrationUsersOutcome ListOutcome { get; init; } =
            ListAdministrationUsersOutcome.Forbidden();

        public UpdateUserRolesOutcome UpdateOutcome { get; init; } =
            UpdateUserRolesOutcome.Forbidden();

        public Task<ListAdministrationUsersOutcome> ListUsersAsync(
            AdministrationUsersQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(ListOutcome);

        public Task<UpdateUserRolesOutcome> UpdateUserRolesAsync(
            UpdateUserRolesCommand command,
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
