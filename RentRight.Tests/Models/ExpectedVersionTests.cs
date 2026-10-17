using RentRight.Application;
using Xunit;

namespace RentRight.Tests.Models;

public sealed class ExpectedVersionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAE= ")]
    [InlineData(" AAAAAAAAAAE=")]
    [InlineData("AAAAAAAAAAF=")]
    [InlineData("not-a-version")]
    public void TryParse_MissingMalformedOrNonCanonical_ReturnsFalse(string? value)
    {
        Assert.False(ExpectedVersion.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_PersistedEightByteVersion_RoundTripsWithoutInterpretingIt()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 2, 3, 255];
        Assert.True(ExpectedVersion.TryParse(Convert.ToBase64String(bytes), out var parsed));
        Assert.Equal(bytes, parsed);
    }
}
