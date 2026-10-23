using System;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The away rule both heads share (owner, 2026-10-19): locked, display off or going to sleep = no fresh
/// Scrolller fetch; dimmed is still watched; the tracker sets <see cref="ScrolllerPolicy.Away"/> and never leaves it stuck.</summary>
[Collection(SessionStatics.Name)]   // ScrolllerPolicy is process-wide
public sealed class ScrolllerAwayRuleTests : IDisposable
{
    public void Dispose() => ScrolllerPolicy.Away = false;

    [Theory]
    [InlineData(false, ScrolllerAwayRule.DisplayOn, false, false)]
    [InlineData(false, ScrolllerAwayRule.DisplayDimmed, false, false)]
    [InlineData(false, ScrolllerAwayRule.DisplayOff, false, true)]
    [InlineData(true, ScrolllerAwayRule.DisplayOn, false, true)]
    [InlineData(false, ScrolllerAwayRule.DisplayOn, true, true)]
    public void Rule(bool locked, int display, bool sleeping, bool away)
        => Assert.Equal(away, ScrolllerAwayRule.IsAway(locked, display, sleeping));

    [Fact]
    public void The_tracker_sets_away_while_any_reason_holds()
    {
        var t = new ScrolllerAwayTracker();
        t.SetLocked(true);
        Assert.True(ScrolllerPolicy.Away);
        t.SetSleeping(true);
        t.SetLocked(false);
        Assert.True(ScrolllerPolicy.Away);    // still asleep
        t.SetSleeping(false);
        Assert.False(ScrolllerPolicy.Away);
        t.SetDisplay(ScrolllerAwayRule.DisplayDimmed);
        Assert.False(ScrolllerPolicy.Away);
        t.SetDisplay(ScrolllerAwayRule.DisplayOff);
        Assert.True(ScrolllerPolicy.Away);
        t.Reset();                             // the hooks stopped: never stuck away
        Assert.False(ScrolllerPolicy.Away);
    }
}
