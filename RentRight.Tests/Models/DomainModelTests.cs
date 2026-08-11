using System.ComponentModel.DataAnnotations;
using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Models;

public class DomainModelTests
{
    [Fact]
    public void Property_RequiresTitleAndAddress()
    {
        var property = new Property
        {
            RentAmount = 1_000m
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            property,
            new ValidationContext(property),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(Property.Title)));
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(Property.Address)));
    }

    [Fact]
    public void MaintenanceRequest_StartsSubmittedWithUtcTimestamps()
    {
        var beforeCreation = DateTime.UtcNow;

        var request = new MaintenanceRequest();

        Assert.Equal(MaintenanceRequestStatus.Submitted, request.Status);
        Assert.InRange(request.DateSubmitted, beforeCreation, DateTime.UtcNow);
        Assert.InRange(request.LastUpdatedUtc, beforeCreation, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(MaintenanceRequestStatus.Submitted, MaintenanceRequestStatus.InProgress, true)]
    [InlineData(MaintenanceRequestStatus.Submitted, MaintenanceRequestStatus.Resolved, false)]
    [InlineData(MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Resolved, true)]
    [InlineData(MaintenanceRequestStatus.Resolved, MaintenanceRequestStatus.InProgress, true)]
    [InlineData(MaintenanceRequestStatus.Resolved, MaintenanceRequestStatus.Closed, true)]
    [InlineData(MaintenanceRequestStatus.Closed, MaintenanceRequestStatus.Submitted, false)]
    public void MaintenanceStatusTransition_EnforcesWorkflow(
        MaintenanceRequestStatus current,
        MaintenanceRequestStatus next,
        bool expected)
    {
        Assert.Equal(expected, current.CanTransitionTo(next));
    }

    [Theory]
    [InlineData(MaintenanceRequestStatus.Submitted, MaintenanceRequestStatus.InProgress)]
    [InlineData(MaintenanceRequestStatus.InProgress, MaintenanceRequestStatus.Resolved)]
    [InlineData(
        MaintenanceRequestStatus.Resolved,
        MaintenanceRequestStatus.InProgress,
        MaintenanceRequestStatus.Closed)]
    [InlineData(MaintenanceRequestStatus.Closed)]
    public void MaintenanceStatusAllowedTransitions_EachWorkflowState_ReturnsExactNextStates(
        MaintenanceRequestStatus current,
        params MaintenanceRequestStatus[] expected)
    {
        Assert.Equal(expected, current.GetAllowedTransitions());
    }

    [Fact]
    public void MaintenanceStatusTransitions_UndefinedStatus_HasNoAllowedTransitions()
    {
        var undefined = (MaintenanceRequestStatus)999;

        Assert.Empty(undefined.GetAllowedTransitions());
        Assert.False(MaintenanceRequestStatus.Submitted.CanTransitionTo(undefined));
    }

    [Fact]
    public void Tenancy_RejectsEndDateBeforeStartDate()
    {
        var tenancy = new Tenancy
        {
            PropertyId = 1,
            TenantId = "tenant-id",
            StartDate = new DateOnly(2026, 8, 10),
            EndDate = new DateOnly(2026, 8, 9)
        };
        var validationResults = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(
            tenancy,
            new ValidationContext(tenancy),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result =>
            result.MemberNames.Contains(nameof(Tenancy.EndDate)));
    }

    [Theory]
    [InlineData(TenancyStatus.Active, "2026-08-01", null, "2026-08-11", true)]
    [InlineData(TenancyStatus.Active, "2026-08-11", null, "2026-08-11", true)]
    [InlineData(TenancyStatus.Active, "2026-08-01", "2026-08-11", "2026-08-11", true)]
    [InlineData(TenancyStatus.Active, "2026-08-12", null, "2026-08-11", false)]
    [InlineData(TenancyStatus.Active, "2026-08-01", "2026-08-10", "2026-08-11", false)]
    [InlineData(TenancyStatus.Ended, "2026-08-01", null, "2026-08-11", false)]
    public void Tenancy_IsActiveOn_UsesStatusAndDates(
        TenancyStatus status,
        string startDate,
        string? endDate,
        string date,
        bool expected)
    {
        var tenancy = new Tenancy
        {
            Status = status,
            StartDate = DateOnly.Parse(startDate),
            EndDate = endDate is null ? null : DateOnly.Parse(endDate)
        };

        Assert.Equal(expected, tenancy.IsActiveOn(DateOnly.Parse(date)));
    }
}
