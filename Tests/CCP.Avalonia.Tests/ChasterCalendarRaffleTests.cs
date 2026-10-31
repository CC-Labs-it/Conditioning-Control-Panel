using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChasterCalendarRaffleTests
{
    [Fact]
    public Task CardsRedotExistingSquaresWithoutReplacingCrosses() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var tab = new ChasterTabView();
        var today = DateTime.UtcNow.Date;
        var first = new DateTime(today.Year, today.Month, 1);
        tab.BuildCalendar((first, first.AddDays(2)), first.AddDays(2));
        var calendar = tab.FindControl<UniformGrid>("Calendar")!;
        var square = (Border)calendar.Children[0];
        var plate = (Panel)square.Child!;
        var cross = plate.Children.Last();
        var card = new RaffleCard(first.ToString("yyyy-MM"), DateTime.DaysInMonth(first.Year, first.Month),
            today.Day, new[] { 1 }, 3600, 20, 36000, false, false, null);
        tab.ShowRaffleCard(card);
        Assert.Single(plate.Children.OfType<Ellipse>(), e => Equals(e.Tag, ChasterTabView.RaffleDotTag));
        Assert.True(tab.FindControl<DockPanel>("CalendarRaffleKey")!.IsVisible);
        tab.ShowRaffleCard(card);
        Assert.Same(square, calendar.Children[0]);
        Assert.Contains(cross, plate.Children);
        Assert.Single(plate.Children.OfType<Ellipse>(), e => Equals(e.Tag, ChasterTabView.RaffleDotTag));
        tab.ShowRaffleCard(card with { Days = Array.Empty<int>() });
        Assert.DoesNotContain(plate.Children.OfType<Ellipse>(), e => Equals(e.Tag, ChasterTabView.RaffleDotTag));
        Assert.False(tab.FindControl<DockPanel>("CalendarRaffleKey")!.IsVisible);
        tab.BuildCalendar(null);
        Assert.Empty(calendar.Children);
        return Task.CompletedTask;
    });
}
