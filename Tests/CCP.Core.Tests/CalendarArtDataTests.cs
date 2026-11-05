using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CalendarArtDataTests
{
    private static readonly DateTime Now = new(2026, 10, 25);

    [Fact]
    public void EventCardLightsRealDatesAndUnknownDatesLightNothing()
    {
        var card = EventCards.Locktober(true, Now, new RaffleReading(3, 3600, new[] { 2, 9, 24 }), key => key)!;
        var data = CalendarArtData.Parse(card.ArtData, Now);
        Assert.Equal(new[] { 2, 9, 24 }, Enumerable.Range(1, 31).Where(data.IsLit));
        Assert.Equal(3, data.Counted);
        Assert.Equal(25, data.Today);
        card = EventCards.Locktober(true, Now, new RaffleReading(3, 3600), key => key)!;
        data = CalendarArtData.Parse(card.ArtData, Now);
        Assert.Equal(3, data.Counted);
        Assert.Equal(0, data.Lit);
    }

    [Fact]
    public void ProgramLegacyAndEmptyPayloadsKeepWpfDefaults()
    {
        var data = CalendarArtData.Parse(new Dictionary<string, int> { ["day"] = 5, ["days"] = 20 }, Now);
        Assert.Equal(new BillboardCalendarInfo(4, 20, 20, 5, 15), data);
        Assert.Equal(new BillboardCalendarInfo(2, 25, 31, 25, 3), CalendarArtData.Parse(new[] { 2, 25 }, Now));
        Assert.Equal(new BillboardCalendarInfo(0, 0, 28, 28, 0), CalendarArtData.Parse(null, new DateTime(2027, 2, 28)));
    }

    [Fact]
    public void ValuesAndMaskAreClampedWithoutLosingDayThirtyOne()
    {
        var data = CalendarArtData.Parse(new Dictionary<string, int>
            { ["counted"] = 99, ["need"] = -1, ["days"] = 99, ["today"] = 99, ["mask"] = 1 << 30 }, Now);
        Assert.Equal(new BillboardCalendarInfo(31, 0, 31, 31, 1 << 30), data);
        Assert.True(data.IsLit(31));
        Assert.False(data.IsLit(0));
        Assert.False(data.IsLit(32));
        data = CalendarArtData.Parse(new Dictionary<string, int>
            { ["counted"] = -9, ["need"] = 99, ["days"] = 0, ["today"] = -5, ["mask"] = -1 }, Now);
        Assert.Equal(new BillboardCalendarInfo(0, 31, 1, 1, 1), data);
    }

    [Fact]
    public void LockCalendarKeepsCountedSeparateFromServedAndRejectsFutureDates()
    {
        var dates = new HashSet<DateTime> { Now.AddDays(-1), Now.AddDays(1) };
        var days = LockCalendar.CellsFor(Now.AddDays(-2), Now.AddDays(1), Now, dates, Now);
        Assert.True(days[0].Served);
        Assert.False(days[0].RaffleCounted);
        Assert.True(days[1].RaffleCounted);
        Assert.False(days[3].RaffleCounted);
        Assert.True(days[^1].IsKey);
    }
}
