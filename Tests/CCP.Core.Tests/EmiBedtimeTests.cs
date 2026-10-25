using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EmiBedtimeTests
{
    [Theory]
    [InlineData(5, 59, 0)]
    [InlineData(6, 0, 1)]
    [InlineData(23, 59, 1)]
    public void QuietEndsAtNextLocalSixEvenAcrossMonthBoundary(int hour, int minute, int days)
    {
        var now = new DateTime(2026, 10, 31, hour, minute, 0, DateTimeKind.Local);
        var expected = now.Date.AddDays(days).AddHours(6).ToUniversalTime();
        var until = EmiBedtime.UntilUtc(now);
        Assert.Equal(DateTimeKind.Utc, until.Kind);
        Assert.Equal(expected, until);
    }
}
