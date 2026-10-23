using System;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Main 251b60807, ported: Core ScrolllerLedger (WPF ScrolllerAllowance moved to Core).
/// Bug hunt 2026-10-22: a clock set back kept the ledger's later day, which is right for a nudge
/// (winding the clock never buys a fresh share) but stuck a PC whose clock had run days ahead and
/// then synced: the day's share never reset at a real midnight until the wrong date came round.
/// </summary>
public class ScrolllerClockErrorTests
{
    private const long MB = ScrolllerAllowance.MB;

    [Fact]
    public void A_clock_that_ran_a_week_ahead_resets_at_the_next_real_midnight()
    {
        // The PC thought it was the 29th, spent the share, then synced back to the 22nd.
        var l = new ScrolllerLedger("2026-10-29", 300 * MB, "2026-10-20");
        Assert.False(l.Roll(new DateTime(2026, 10, 22, 9, 0, 0)));
        Assert.Equal("2026-10-22", l.Day);
        Assert.Equal(300 * MB, l.Used); // no fresh share for winding the clock

        Assert.True(l.Roll(new DateTime(2026, 10, 23, 0, 0, 5)));
        Assert.Equal(0, l.Used);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_nudge_back_still_keeps_the_later_day(int daysBack)
    {
        var l = new ScrolllerLedger("2026-10-22", 120 * MB, "2026-10-20");
        Assert.False(l.Roll(new DateTime(2026, 10, 22, 12, 0, 0).AddDays(-daysBack)));
        Assert.Equal("2026-10-22", l.Day);
        Assert.Equal(120 * MB, l.Used);
        // Back to the real day: still no fresh share.
        Assert.False(l.Roll(new DateTime(2026, 10, 22, 13, 0, 0)));
        Assert.Equal(120 * MB, l.Used);
    }

    [Fact]
    public void A_first_day_stamped_in_the_wrong_future_moves_back_with_it()
    {
        var l = new ScrolllerLedger("2026-11-30", 10 * MB, "2026-11-30");
        l.Roll(new DateTime(2026, 10, 22, 9, 0, 0));
        Assert.Equal("2026-10-22", l.FirstDay);
        Assert.True(l.IsFirstDay);
        l.Roll(new DateTime(2026, 10, 23, 9, 0, 0));
        Assert.False(l.IsFirstDay);
    }

    [Fact]
    public void An_earlier_first_day_in_the_wrong_future_never_comes_round_again()
    {
        var l = new ScrolllerLedger("2026-11-30", 10 * MB, "2026-11-25");
        l.Roll(new DateTime(2026, 10, 22, 9, 0, 0));
        Assert.Equal("2026-10-21", l.FirstDay);
        Assert.False(l.IsFirstDay);
        var later = new ScrolllerLedger(l.Day, l.Used, l.FirstDay);
        later.Roll(new DateTime(2026, 11, 25, 9, 0, 0));
        Assert.False(later.IsFirstDay);
    }

    [Fact]
    public void A_real_first_day_in_the_past_is_left_alone()
    {
        var l = new ScrolllerLedger("2026-11-30", 10 * MB, "2026-10-01");
        l.Roll(new DateTime(2026, 10, 22, 9, 0, 0));
        Assert.Equal("2026-10-01", l.FirstDay);
    }
}
