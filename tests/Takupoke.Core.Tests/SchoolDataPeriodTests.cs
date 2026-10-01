using System.Globalization;
using Takupoke.Core;
using Xunit;

namespace Takupoke.Core.Tests;

public sealed class SchoolDataPeriodTests
{
    [Theory]
    [InlineData("2032-03-31T14:59:59.9999999Z", 2031, 2)]
    [InlineData("2032-03-31T15:00:00Z", 2032, 1)]
    [InlineData("2032-09-30T14:59:59.9999999Z", 2032, 1)]
    [InlineData("2032-09-30T15:00:00Z", 2032, 2)]
    [InlineData("2032-12-31T15:00:00Z", 2032, 2)]
    [InlineData("2033-03-31T14:59:59.9999999Z", 2032, 2)]
    [InlineData("2033-03-31T15:00:00Z", 2033, 1)]
    [InlineData("2032-02-29T12:00:00Z", 2031, 2)]
    [InlineData("2032-04-01T00:00:00+09:00", 2032, 1)]
    [InlineData("2032-03-31T08:00:00-07:00", 2032, 1)]
    [InlineData("2032-10-01T00:00:00+09:00", 2032, 2)]
    [InlineData("2032-09-30T08:00:00-07:00", 2032, 2)]
    public void RetentionUsesJapanTimeAndSchoolYear(string timestamp, int year, int half)
    {
        var instant = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture);

        var period = SchoolDataPeriod.FromInstant(instant);

        Assert.Equal(year, period.SchoolYear);
        Assert.Equal(half, period.Half);
    }

    [Fact]
    public void MidnightAtOctoberBoundaryInvalidatesPreviousPeriod()
    {
        var boundary = new DateTimeOffset(2032, 10, 1, 0, 0, 0, TimeSpan.FromHours(9));

        Assert.NotEqual(
            SchoolDataPeriod.FromInstant(boundary.AddTicks(-1)),
            SchoolDataPeriod.FromInstant(boundary));
    }

    [Fact]
    public void JanuaryDoesNotInvalidateOctoberPeriod()
    {
        var october = new DateTimeOffset(2032, 10, 1, 0, 0, 0, TimeSpan.FromHours(9));
        var january = new DateTimeOffset(2033, 1, 1, 0, 0, 0, TimeSpan.FromHours(9));

        Assert.Equal(SchoolDataPeriod.FromInstant(october), SchoolDataPeriod.FromInstant(january));
    }
}
