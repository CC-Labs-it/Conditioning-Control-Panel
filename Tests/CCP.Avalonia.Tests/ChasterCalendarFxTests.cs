using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChasterCalendarFxTests
{
    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    public void RebuiltVisibleSheetDrawsStrokesAndSplat(MotionLevel level) => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = level;
        var tab = new ChasterTabView();
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            var today = new DateTime(2026, 5, 5);
            tab.BuildCalendar((today.AddDays(-2), today.AddDays(3)), today);
            var lines = tab.GetVisualDescendants().OfType<Canvas>().SelectMany(c => c.Children.OfType<Line>())
                .Where(l => l.StrokeDashArray != null).ToArray();
            if (level == MotionLevel.Full)
            {
                Assert.NotEmpty(lines);
                var crosses = lines.Select(l => (Canvas)l.Parent!).Distinct().ToArray();
                var splats = crosses.Select(c => c.Children.OfType<Ellipse>().Single()).ToArray();
                Assert.All(splats, e => Assert.Equal(0, e.Opacity));
                Assert.All(lines, l => Assert.True(l.StrokeDashOffset > 0));
                var first = lines[0].StrokeDashOffset;
                tab.AdvanceCalendarDraw(100);
                Assert.True(lines[0].StrokeDashOffset < first);
                Assert.True(lines[0].StrokeDashOffset / lines[0].StrokeDashArray![0] < lines[2].StrokeDashOffset / lines[2].StrokeDashArray![0]);
                tab.AdvanceCalendarDraw(10000);
                Assert.All(lines, l => Assert.Equal(0, l.StrokeDashOffset));
                Assert.All(splats, e => Assert.Equal(0.9, e.Opacity));
            }
            else Assert.Empty(lines);
        }
        finally { window.Close(); CoreSettings.Current.MotionLevel = old; }
    });
    [Fact]
    public void CometParksOnHideAndNeedsBothCells() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var tab = new ChasterTabView();
        var parent = new Border { Child = tab };
        var window = new Window { Content = parent, Width = 1000, Height = 800 };
        try
        {
            window.Show();
            var today = new DateTime(2026, 5, 5);
            tab.BuildCalendar((today.AddDays(-2), today.AddDays(3)), today);
            Assert.True(tab.CalendarCometRunning);
            window.UpdateLayout();
            tab.AdvanceCalendarMotion(0);
            tab.CalendarComet();
            Assert.True(tab.CalendarCometVisible);
            tab.AdvanceCalendarMotion(1.6);
            Assert.False(tab.CalendarCometVisible);
            Assert.True(tab.CalendarRingScale > 1.24);
            tab.AdvanceCalendarMotion(2.2);
            Assert.Equal(1.04, tab.CalendarRingScale, 3);
            tab.IsVisible = false;
            Assert.False(tab.CalendarCometRunning);
            Assert.Equal(1, tab.CalendarRingScale);
            tab.IsVisible = true;
            Assert.True(tab.CalendarCometRunning);
            parent.IsVisible = false;
            Assert.False(tab.CalendarCometRunning);
            parent.IsVisible = true;
            Assert.True(tab.CalendarCometRunning);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(tab.CalendarCometRunning);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(tab.CalendarCometRunning);
            tab.BuildCalendar((today.AddDays(1), today.AddDays(3)), today);
            Assert.False(tab.CalendarCometRunning);
            tab.BuildCalendar((today.AddDays(-3), today.AddDays(-1)), today);
            Assert.False(tab.CalendarCometRunning);
        }
        finally { window.Close(); CoreSettings.Current.MotionLevel = old; }
    });

    [Fact]
    public void FlutterAndBookingNudgeSettleAtTheOriginalTilt() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var tab = new ChasterTabView();
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            var today = new DateTime(2026, 5, 5);
            tab.BuildCalendar((today.AddDays(-2), today.AddDays(3)), today);
            tab.AdvanceCalendarMotion(0);
            tab.FlutterCalendarSheet();
            tab.AdvanceCalendarMotion(0.135);
            Assert.Equal(-0.5, tab.CalendarSheetAngle, 3);
            tab.AdvanceCalendarMotion(0.3);
            Assert.Equal(-1.5, tab.CalendarSheetAngle, 3);
            tab.NudgeCalendarSheet();
            tab.AdvanceCalendarMotion(0.444);
            Assert.Equal(-2.7, tab.CalendarSheetAngle, 3);
            tab.AdvanceCalendarMotion(0.62);
            Assert.Equal(-1.5, tab.CalendarSheetAngle, 3);
        }
        finally { window.Close(); CoreSettings.Current.MotionLevel = old; }
    });
}
