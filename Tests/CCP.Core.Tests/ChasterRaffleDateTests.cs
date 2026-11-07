using System;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ChasterRaffleDateTests
{
    [Theory]
    [InlineData("2026-09", 30)]
    [InlineData("2026-02", 28)]
    [InlineData("2028-02", 29)]
    public void CountedDatesStayInsideTheCalendarMonth(string month, int last)
    {
        var card = new RaffleCard(month, 31, 31, new[] { last, last + 1 }, 900, 25, 111600, false, false, null);
        var day = Assert.Single(ChasterRaffle.CountedDates(card));
        Assert.Equal(last, day.Day);
        Assert.Equal(DateTime.Parse(month + "-01").Month, day.Month);
    }
}
