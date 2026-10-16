using RentRight.Models;
using Xunit;

namespace RentRight.Tests.Tenancies;

public class TenancyLifecycleTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private static Tenancy Pending() => new()
    {
        TenantId = "tenant", PropertyId = 1, Status = TenancyStatus.Pending,
        StartDate = new(2026, 10, 1), InvitationTokenHash = new string('A', 64),
        InvitedUtc = Now.AddDays(-1), InvitationExpiresUtc = Now.AddDays(1), InvitedByUserId = "owner"
    };

    [Theory]
    [InlineData("pending", true)]
    [InlineData("wrong-tenant", false)]
    [InlineData("unknown-tenant", false)]
    [InlineData("expired", false)]
    [InlineData("boundary-expiry", false)]
    [InlineData("past-end", false)]
    [InlineData("ended", false)]
    [InlineData("active", false)]
    [InlineData("missing-hash", false)]
    public void Accept_StateMatrix_OnlyEligiblePendingTransitions(string scenario, bool expected)
    {
        var tenancy = Pending();
        if (scenario == "unknown-tenant") tenancy.TenantId = null;
        if (scenario == "expired") tenancy.InvitationExpiresUtc = Now.AddTicks(-1);
        if (scenario == "boundary-expiry") tenancy.InvitationExpiresUtc = Now;
        if (scenario == "past-end") tenancy.EndDate = DateOnly.FromDateTime(Now).AddDays(-1);
        if (scenario == "ended") tenancy.Status = TenancyStatus.Ended;
        if (scenario == "active") tenancy.Status = TenancyStatus.Active;
        if (scenario == "missing-hash") tenancy.InvitationTokenHash = null;
        var result = tenancy.Accept(scenario == "wrong-tenant" ? "other" : "tenant", Now);
        Assert.Equal(expected, result);
        Assert.Equal(expected ? "tenant" : null, tenancy.AcceptedByUserId);
        Assert.Equal(expected ? Now : (DateTime?)null, tenancy.AcceptedUtc);
        if (expected)
        {
            Assert.Equal(TenancyStatus.Active, tenancy.Status);
            Assert.Null(tenancy.InvitationTokenHash);
            Assert.False(tenancy.Accept("tenant", Now));
            Assert.True(tenancy.IsActiveOn(DateOnly.FromDateTime(Now)));
        }
    }

    [Theory]
    [InlineData(TenancyStatus.Pending, "2026-10-01", null, "Cancelled")]
    [InlineData(TenancyStatus.Active, "2026-10-01", "2026-10-16", "Ended")]
    [InlineData(TenancyStatus.Active, "2026-11-01", "2026-11-01", "Ended")]
    public void End_PendingCurrentOrFuture_PreservesHistoryAndAudit(TenancyStatus status, string start, string? end, string reason)
    {
        var tenancy = Pending();
        tenancy.Status = status;
        tenancy.StartDate = DateOnly.Parse(start);
        Assert.True(tenancy.End("owner", Now));
        Assert.Equal(TenancyStatus.Ended, tenancy.Status);
        Assert.Equal(end is null ? null : DateOnly.Parse(end), tenancy.EndDate);
        Assert.Equal("owner", tenancy.EndedByUserId);
        Assert.Equal(Now, tenancy.EndedUtc);
        Assert.Equal(reason, tenancy.EndReason);
        Assert.Equal("tenant", tenancy.TenantId);
        Assert.Null(tenancy.InvitationTokenHash);
        Assert.False(tenancy.End("other", Now.AddDays(1)));
        Assert.Equal("owner", tenancy.EndedByUserId);
        Assert.False(tenancy.IsActiveOn(DateOnly.FromDateTime(Now)));
    }
}
