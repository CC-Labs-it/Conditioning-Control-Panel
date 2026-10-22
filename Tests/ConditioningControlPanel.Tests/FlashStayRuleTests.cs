using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>"Stay until popped": ambient flashes wait for a click, with a 10-minute safety and a 40 cap.</summary>
public class FlashStayRuleTests
{
    [Fact]
    public void DefaultsOff() => Assert.False(new AppSettings().FlashStayUntilPopped);

    [Fact]
    public void OwnerNumbers()
    {
        Assert.Equal(600_000, FlashStayRule.SafetyLifetimeMs);
        Assert.Equal(40, FlashStayRule.MaxOnScreen);
    }

    [Theory]
    [InlineData(true, true, false, true)]   // ambient, clickable: stays
    [InlineData(true, true, true, false)]   // point-fired keeps its authored timing
    [InlineData(true, false, false, false)] // nobody can click it: normal lifetime
    [InlineData(false, true, false, false)] // option off
    public void Applies(bool on, bool clickable, bool pointFired, bool expected)
        => Assert.Equal(expected, FlashStayRule.Applies(on, clickable, pointFired));

    /// <summary>HYGIENE-5. "Needs clickable flashes" means a flash the mouse can pop. In Solid mode
    /// with the compositor off the shared host is click-through, so a flash there keeps its normal
    /// lifetime and cap instead of sitting ten minutes out of reach, forty at a time.</summary>
    [Theory]
    [InlineData(false, true, false)] // compositor off, Solid mode: the click-through shared host
    [InlineData(true, true, true)]   // compositor on: Solid mode is not the path taken
    [InlineData(false, false, true)] // compositor off, own windows: clickable
    public void A_flash_stays_only_where_the_mouse_can_pop_it(bool compositor, bool solidMode, bool stays)
    {
        var s = new AppSettings { FlashClickable = true, FlashStayUntilPopped = true, FlashSolidMode = solidMode };
        var solidHost = !compositor && solidMode;

        Assert.Equal(stays, FlashService.StayUntilPopped(s, pointFired: false, compositor));
        Assert.Equal(stays, FlashService.MouseClickable(s.FlashClickable, solidHost));
        if (solidHost)
            Assert.Equal(FlashService.ResolveFlashCap(false, true),
                FlashStayRule.Cap(FlashService.ResolveFlashCap(false, true), FlashService.StayUntilPopped(s, false, compositor), sharedHost: true));
    }

    [Fact]
    public void CapIsFortyOnTheSharedHostOnly()
    {
        Assert.Equal(40, FlashStayRule.Cap(30, stay: true, sharedHost: true));
        Assert.Equal(10, FlashStayRule.Cap(10, stay: true, sharedHost: false));
        Assert.Equal(30, FlashStayRule.Cap(30, stay: false, sharedHost: true));
    }

    [Fact]
    public void SchedulerWaitsWhileTheScreenIsFull()
    {
        Assert.True(FlashStayRule.ScreenFull(true, 40, 40));
        Assert.False(FlashStayRule.ScreenFull(true, 39, 40));
        Assert.False(FlashStayRule.ScreenFull(false, 99, 40));
    }

    [Theory]
    [InlineData(30, 20, 20)]   // shared host: the slider is the ceiling, not the 30 headroom
    [InlineData(30, 8, 8)]
    [InlineData(10, 20, 10)]   // classic windows keep their lower cap
    [InlineData(30, 99, 20)]   // a bad stored value never lifts it past 20
    [InlineData(30, 0, 1)]
    public void MaxOnScreenIsTheCeilingForEveryPath(int modeCap, int maxOnScreen, int expected)
        => Assert.Equal(expected, FlashStayRule.OnScreen(modeCap, maxOnScreen));

    // #1452: the stay copy promises "Up to 40 at once"; the 1..20 slider must not trim it.
    [Theory]
    [InlineData(20)]
    [InlineData(5)]
    [InlineData(1)]
    public void StayUntilPoppedReachesFortyOnASharedHost(int maxOnScreen)
    {
        int modeCap = FlashStayRule.Cap(FlashService.ResolveFlashCap(true, false), stay: true, sharedHost: true);
        Assert.Equal(40, FlashStayRule.OnScreen(modeCap, maxOnScreen, stay: true));
        Assert.False(FlashStayRule.ScreenFull(true, 20, FlashStayRule.OnScreen(modeCap, maxOnScreen, stay: true)));
    }

    [Fact]
    public void StayOnClassicWindowsKeepsTheirOwnCap()
        => Assert.Equal(10, FlashStayRule.OnScreen(FlashStayRule.Cap(10, stay: true, sharedHost: false), 20, stay: true));

    [Fact]
    public void NonStaySpawnsStillObeyTheSlider()
        => Assert.Equal(20, FlashStayRule.OnScreen(FlashStayRule.Cap(30, stay: false, sharedHost: true), 20, stay: false));
}
