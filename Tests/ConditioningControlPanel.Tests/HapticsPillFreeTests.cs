using System.Linq;
using ConditioningControlPanel.Controls.NavRail;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 review (wave 6, item 4, second desk run, free account): the Haptics pill in the Studio strip wore
/// the "BASIC" lock plate while the page opened unlocked. Toys are free (owner 2026-11-11): every
/// haptics door asks <see cref="HapticsAccess.IsOpen"/>, and the nav table's tier for the pill now does too.
/// No account service here reads as locked for any tiered pill, so this is the signed-out case.
/// </summary>
public class HapticsPillFreeTests
{
    [Fact]
    public void The_haptics_pill_wears_no_lock_while_toys_are_free()
    {
        var tab = NavSections.Find(NavSections.Studio)!.Tabs.Single(t => t.Key == "haptics");
        Assert.True(HapticsAccess.IsOpen);
        Assert.Equal(0, tab.Tier);
        Assert.False(NavStripRules.PillLocked(tab.Tier));
    }
}
