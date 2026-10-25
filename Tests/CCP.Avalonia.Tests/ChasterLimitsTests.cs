using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChasterLimitsTests
{
    private static Task Run(Action<ChasterTabView> check) => AvaloniaTestDispatcher.RunAsync(() => ChasterBillTests.Run((_, _) =>
    {
        var s = CoreSettings.Current;
        var old = (s.ChasterDayLimit, s.ChasterBacklogLimit, s.ChasterRelockPastEnd);
        s.ChasterDayLimit = new(180, 0, null);
        s.ChasterBacklogLimit = new(600, 0, null);
        s.ChasterRelockPastEnd = false;
        var tab = new ChasterTabView();
        var window = new Window { Content = tab, Width = 1200, Height = 900 };
        try { window.Show(); check(tab); }
        finally
        {
            window.Close();
            (s.ChasterDayLimit, s.ChasterBacklogLimit, s.ChasterRelockPastEnd) = old;
        }
        return Task.CompletedTask;
    }));

    private static T Find<T>(ChasterTabView tab, string name) where T : Control =>
        Assert.IsType<T>(tab.FindControl<T>(name));

    [Fact]
    public Task LimitsOpenAndCloseAndRelockPersists() => Run(tab =>
    {
        var card = Find<Border>(tab, "LimitsCard");
        var button = Find<Button>(tab, "BtnLimits");
        Assert.False(card.IsVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(card.IsVisible);
        Find<CheckBox>(tab, "ChkRelock").IsChecked = true;
        Assert.True(CoreSettings.Current.ChasterRelockPastEnd);
        tab.Refresh();
        Assert.True(Find<CheckBox>(tab, "ChkRelock").IsChecked);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(card.IsVisible);
    });

    [Fact]
    public Task LowerAppliesNowRaiseWaitsAndBacklogFollowsDay() => Run(tab =>
    {
        var day = Find<Slider>(tab, "SliderDayLimit");
        var backlog = Find<Slider>(tab, "SliderBacklogLimit");
        day.Value = 120;
        Assert.Equal(120, CoreSettings.Current.ChasterDayLimit.Minutes);
        Assert.False(CoreSettings.Current.ChasterDayLimit.HasPending);
        day.Value = 240;
        var pending = CoreSettings.Current.ChasterDayLimit;
        Assert.Equal(120, pending.Minutes);
        Assert.Equal(240, pending.PendingMinutes);
        Assert.InRange(pending.PendingAtUtc!.Value, DateTime.UtcNow.AddHours(24).AddSeconds(-10), DateTime.UtcNow.AddHours(24));
        Assert.True(Find<TextBlock>(tab, "TxtDayPending").IsVisible);
        tab.Refresh();
        Assert.Equal(240, day.Value);
        Assert.Equal(CircesTab.Format(120 * 60, signed: false), Find<TextBlock>(tab, "TxtDayLimit").Text);
        backlog.Value = 60;
        Assert.Equal(240, backlog.Value);
        day.Value = 90;
        Assert.Equal(90, CoreSettings.Current.ChasterDayLimit.Minutes);
        Assert.False(Find<TextBlock>(tab, "TxtDayPending").IsVisible);
    });

    [Fact]
    public Task PresetRefreshesLimitsAndFooterHasAllFourFacts() => Run(tab =>
    {
        var key = Find<ToggleButton>(tab, "BtnPresetStrict");
        key.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var preset = TabPresets.Find(TabPresets.Strict)!;
        Assert.Equal(preset.DayMinutes, Find<Slider>(tab, "SliderDayLimit").Value);
        Assert.Equal(preset.BacklogMinutes, Find<Slider>(tab, "SliderBacklogLimit").Value);
        var text = Find<WrapPanel>(tab, "FootRow").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        foreach (var id in new[] { "chaster_fact_panic", "chaster_fact_push_launch", "chaster_fact_floor_long", "chaster_fact_unlink" })
            Assert.Contains(Loc.Get(id), text);
    });
}
