using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The "Show content on" pick (AppSettings.GlobalTargetMonitor) as every overlay resolves it. Spec:
/// WPF App.ResolveScreens + ResolveFollowGlobal (ConditioningControlPanel/App.ScreenResolver.cs:31-123).
/// </summary>
public sealed class GlobalTargetMonitorResolveTests
{
    private const int Follow = MonitorTarget.FollowGlobal, All = MonitorTarget.All;

    [Theory]
    // effect target, global pick, DualMonitorEnabled, screens, primary, expected
    [InlineData(Follow, 2, false, 3, 0, new[] { 2 })]          // the picked monitor
    [InlineData(Follow, 2, true, 3, 0, new[] { 2 })]           // the pick wins over the old toggle
    [InlineData(Follow, 5, false, 3, 1, new[] { 1 })]          // unplugged: the primary (App.ScreenResolver.cs:107-114)
    [InlineData(Follow, 5, true, 3, 1, new[] { 1 })]           // ...even with the toggle on
    [InlineData(Follow, Follow, true, 3, 0, new[] { 0, 1, 2 })] // legacy: All monitors
    [InlineData(Follow, Follow, false, 3, 2, new[] { 2 })]      // legacy: Primary only
    [InlineData(Follow, All, false, 2, 0, new[] { 0, 1 })]
    [InlineData(1, 2, false, 3, 0, new[] { 1 })]               // the effect's own pick wins (ScreenResolver.cs:55)
    [InlineData(4, 2, false, 3, 0, new[] { 2 })]               // its unplugged pick follows the GLOBAL pick (:57)
    [InlineData(All, 2, false, 3, 0, new[] { 0, 1, 2 })]
    public void Overlays_follow_the_global_pick_like_WPF(int target, int global, bool dual, int count, int primary, int[] expected) =>
        Assert.Equal(expected, PinkFilterOverlay.ResolveScreenIndices(target, global, dual, count, primary));

    [Fact]
    public void No_screens_reported_resolves_to_none() =>
        Assert.Empty(PinkFilterOverlay.ResolveScreenIndices(Follow, 2, true, 0, 0));
}
