using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Perly's ticket (2026-10-19): the billboard lit days 1-12 for "12 of 25 days" while the tab's
/// lock sheet crossed out days 2-18. The raffle counts a UTC day when CCP time landed on the lock
/// that day (server chaster-raffle.js); the sheet crosses the days the lock has been served. Both
/// surfaces now read the raffle card's real days, and the sheet marks which served days counted.
/// </summary>
public class LocktoberDaysTests
{
    private static readonly DateTime Oct19 = new(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);

    // Perly's shape: locked since the 2nd, twelve days counted, none of them the 1st.
    private static readonly int[] PerlyDays = { 2, 3, 5, 6, 8, 9, 11, 12, 14, 15, 17, 18 };

    private static RaffleCard Card(string month, params int[] days) =>
        new(month, 31, 19, days, 14 * 3600, 25, 31 * 3600, false, false, null);

    private static string Loc(string key) => key switch
    {
        "billboard_card_locktober_days_title" => "{0} of {1} days",
        _ => key,
    };

    [Fact]
    public void The_reading_is_the_cards_real_days_when_the_card_is_this_utc_month()
    {
        var card = Card("2026-10", PerlyDays.Reverse().Append(2).Append(40).ToArray());
        var reading = RaffleReading.From(card, new LadderVerify(true, 99, null, 7), Oct19)!;
        Assert.Equal(PerlyDays, reading.Days);
        Assert.Equal(12, reading.DaysCounted);
        Assert.Equal(14 * 3600, reading.Seconds);
    }

    [Fact]
    public void Without_this_months_card_the_verify_count_stands_but_names_no_day()
    {
        var verify = new LadderVerify(true, 3600, null, 12);
        var stale = RaffleReading.From(Card("2026-09", 1, 2), verify, Oct19)!;
        Assert.Equal(12, stale.DaysCounted);
        Assert.Null(stale.Days);
        Assert.Null(RaffleReading.From(null, new LadderVerify(false, 0, "no_lock"), Oct19));
        Assert.Null(RaffleReading.From(null, null, Oct19));
    }

    [Fact]
    public void The_month_is_the_utc_month_the_raffle_keeps()
    {
        var card = Card("2026-10", 31);
        Assert.NotNull(ChasterRaffle.CountedThisMonth(card, new DateTime(2026, 10, 31, 23, 59, 0, DateTimeKind.Utc)));
        Assert.Null(ChasterRaffle.CountedThisMonth(card, new DateTime(2026, 11, 1, 0, 0, 1, DateTimeKind.Utc)));
        Assert.Null(ChasterRaffle.CountedThisMonth(null, Oct19));
    }

    [Fact]
    public void The_banner_lights_the_counted_dates_not_the_first_n_days()
    {
        var card = EventCards.Locktober(true, Oct19, new RaffleReading(12, 3600, PerlyDays), Loc)!;
        Assert.Equal("12 of 25 days", card.Title);
        var info = BuiltInArt.CalendarData(card.ArtData, Oct19);
        Assert.Equal(12, info.Counted);
        Assert.Equal(19, info.Today);
        var lit = Enumerable.Range(1, 31).Where(info.IsLit).ToArray();
        Assert.Equal(PerlyDays, lit);
        Assert.False(info.IsLit(1));
        Assert.False(info.IsLit(19)); // today rings, and lights only once it counts
    }

    [Fact]
    public void Today_lights_once_it_counted()
    {
        var card = EventCards.Locktober(true, Oct19, new RaffleReading(13, 3600, PerlyDays.Append(19).ToArray()), Loc)!;
        Assert.True(BuiltInArt.CalendarData(card.ArtData, Oct19).IsLit(19));
    }

    [Fact]
    public void A_count_with_no_dates_fills_the_bar_and_lights_no_day()
    {
        var card = EventCards.Locktober(true, Oct19, new RaffleReading(12, 3600), Loc)!;
        var info = BuiltInArt.CalendarData(card.ArtData, Oct19);
        Assert.Equal(12, info.Counted);
        Assert.Equal(0, info.Lit);
        var unread = BuiltInArt.CalendarData(EventCards.Locktober(true, Oct19, null, Loc)!.ArtData, Oct19);
        Assert.Equal(0, unread.Lit);
    }

    [Fact]
    public void Day_masks_hold_a_whole_month_and_drop_days_past_its_end()
    {
        Assert.Equal(0, ChasterRaffle.DayMask(Array.Empty<int>()));
        Assert.Equal(1 | 1 << 30, ChasterRaffle.DayMask(new[] { 1, 31, 0, 32, -4 }));
        var thirty = BuiltInArt.CalendarData(new Dictionary<string, int> { ["counted"] = 2, ["mask"] = 1 << 29 | 1 << 30, ["days"] = 30, ["today"] = 5 }, Oct19);
        Assert.True(thirty.IsLit(30));
        Assert.False(thirty.IsLit(31));
    }

    [Fact]
    public void Counted_dates_are_calendar_dates_of_the_cards_month()
    {
        var dates = ChasterRaffle.CountedDates(Card("2026-10", 2, 5, 33));
        Assert.Equal(new[] { new DateTime(2026, 10, 2), new DateTime(2026, 10, 5) }, dates.OrderBy(d => d));
        Assert.Empty(ChasterRaffle.CountedDates(null));
        Assert.Empty(ChasterRaffle.CountedDates(Card("junk", 2)));
    }

    [Fact]
    public void The_lock_sheet_marks_the_served_days_the_raffle_counted_and_never_a_day_ahead()
    {
        var start = new DateTime(2026, 10, 2, 12, 31, 0);
        var end = new DateTime(2026, 11, 1, 12, 31, 0);
        var today = new DateTime(2026, 10, 19, 17, 0, 0);
        var counted = ChasterRaffle.CountedDates(Card("2026-10", PerlyDays.Append(19).Append(25).ToArray()));

        var cells = LockCalendar.CellsFor(start, end, today, counted);
        Assert.Equal(17, cells.Count(c => c.Served));
        Assert.Equal(PerlyDays, cells.Where(c => c.Served && c.RaffleCounted).Select(c => c.DayOfMonth));
        Assert.True(Assert.Single(cells, c => c.Today).RaffleCounted);
        Assert.DoesNotContain(cells, c => c.Locked && c.RaffleCounted);
        Assert.DoesNotContain(cells, c => c.IsKey && c.RaffleCounted);

        Assert.DoesNotContain(LockCalendar.CellsFor(start, end, today), c => c.RaffleCounted);
    }
}
