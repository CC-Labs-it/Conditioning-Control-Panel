using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The rail medallions as coins (WPF MainWindow.NavRail.cs:67-497,
/// nav polish waves 10/11), driven from the shell's own startup and real pointer input.</summary>
public sealed class NavRailCoinTests
{
    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var motion = CoreSettings.Current.MotionLevel;
        var perf = CoreSettings.Current.PerformanceMode;
        MainShellWindow? window = null;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.PerformanceMode = false;
            window = new MainShellWindow();
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.OpenNavSection(NavSections.Home); Dispatcher.UIThread.RunJobs();
            body(window);
        }
        finally
        {
            window?.Close();
            CoreSettings.Current.MotionLevel = motion;
            CoreSettings.Current.PerformanceMode = perf;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static MainShellWindow.NavSectionRow Row(MainShellWindow w, string section) =>
        w.NavSectionRowsForTests.Single(r => r.Section == section);

    private static Point TileAt(MainShellWindow w, MainShellWindow.NavSectionRow row, double fx, double fy) =>
        row.Tile!.TranslatePoint(new Point(row.Tile.Bounds.Width * fx, row.Tile.Bounds.Height * fy), w)!.Value;

    [Fact]
    public void EveryRowIsACoinStackedLikeWpfAndTheLitOneSitsInItsSocket() => Run(w =>
    {
        foreach (var row in w.NavSectionRowsForTests)
        {
            var coin = Assert.IsType<MainShellWindow.NavCoinParts>(row.Coin);
            var kids = row.Face!.Children;
            int[] order =
            {
                kids.IndexOf(coin.Disc), kids.IndexOf(coin.Outer), kids.IndexOf(row.Tile!), kids.IndexOf(coin.Dish),
                kids.IndexOf(row.Art!), kids.IndexOf(row.Tint!), kids.IndexOf(coin.Socket), kids.IndexOf(coin.Rim),
                kids.IndexOf(coin.Inner), kids.IndexOf(row.Ring!), kids.IndexOf(coin.Specular),
            };
            Assert.True(order.Zip(order.Skip(1)).All(p => p.First >= 0 && p.First < p.Second), row.Section + ": " + string.Join(",", order));
        }

        var studio = Row(w, NavSections.Studio);
        Assert.Equal(0, studio.Lift.Y);                                   // idle: raised, at rest
        Assert.Equal(DepthLaw.RaisedPx, studio.Coin!.DiscShift.Y);
        Assert.Equal(1, studio.Coin.Disc.Opacity);
        Assert.Equal(0, studio.Coin.Socket.Opacity);

        w.ShowTab("studio"); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DepthLaw.ActiveSinkPx, studio.Lift.Y);               // lit: sunk into its socket
        Assert.Equal(1, studio.Coin.Socket.Opacity);
        Assert.Equal(0, studio.Coin.Disc.Opacity);

    });

    [Fact]
    public void HoverLiftsPressTravelsAndReleaseSettlesTheCoinIntoItsSocket() => Run(w =>
    {
        var studio = Row(w, NavSections.Studio);
        var at = TileAt(w, studio, 0.5, 0.5);
        w.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(-DepthLaw.HoverLiftPx, studio.Lift.Y);
        Assert.Equal(DepthLaw.RaisedPx + DepthLaw.HoverLiftPx, studio.Coin!.DiscShift.Y);

        w.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DepthLaw.PressTravelPx, studio.Lift.Y);
        Assert.Equal(0, studio.Coin.Disc.Opacity);
        Assert.Equal(1, studio.Coin.Socket.Opacity);

        w.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(NavSections.Studio, NavSections.SectionForTab(w.CurrentTab));
        Assert.Equal(DepthLaw.ActiveSinkPx, studio.Lift.Y);
    });

    [Fact]
    public void AnIdleCoinLeansTowardThePointerAndTheReleaseSpringOvershootsOnce() => Run(w =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var studio = Row(w, NavSections.Studio);
        w.MouseMove(TileAt(w, studio, 0.95, 0.05), RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        double lean = studio.Tilt.GetBaseValue(RotateTransform.AngleProperty).Value;
        Assert.InRange(lean, 0.5, DepthLaw.TiltDegrees);

        CoreSettings.Current.PerformanceMode = true;                      // the Performance tier never leans
        w.MouseMove(TileAt(w, studio, 0.9, 0.1), RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, studio.Tilt.GetBaseValue(RotateTransform.AngleProperty).Value);

        var ease = new MainShellWindow.NavSpringEase(1.5);
        Assert.Equal(1.5, ease.Ease(0.6), 6);
        Assert.Equal(1.0, ease.Ease(1.0), 6);
    });

    [Fact]
    public void ReleasingAtFullMotionSpringsPastTheSocketByTheOvershootThenSettles() => Run(w =>
    {
        var studio = Row(w, NavSections.Studio);
        var at = TileAt(w, studio, 0.5, 0.5);
        w.MouseMove(at, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        w.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DepthLaw.PressTravelPx, studio.Lift.Y);              // Off: pressed, snapped

        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        w.MouseUp(at, MouseButton.Left, RawInputModifiers.None); Dispatcher.UIThread.RunJobs();
        Assert.Equal(DepthLaw.ActiveSinkPx, studio.Lift.GetBaseValue(TranslateTransform.YProperty).Value);
        var spring = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(studio.Lift.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(DepthLaw.ReleaseMs), spring.Duration);
        Assert.IsType<MainShellWindow.NavSpringEase>(spring.Easing);
        double peak = DepthLaw.PressTravelPx + spring.Easing.Ease(0.6) * (DepthLaw.ActiveSinkPx - DepthLaw.PressTravelPx);
        Assert.Equal(DepthLaw.ActiveSinkPx - DepthLaw.ReleaseOvershootPx, peak, 6);   // past the socket by 1 px
        Assert.Equal(DepthLaw.ActiveSinkPx - DepthLaw.ReleaseOvershootPx, MainShellWindow.CoinOvershoot(DepthLaw.PressTravelPx, DepthLaw.ActiveSinkPx), 6);
        Assert.Equal(1.0, spring.Easing.Ease(1.0), 6);
    });
    [Theory]
    [InlineData(false, MotionLevel.Full, PerformanceTier.Balanced, true)]
    [InlineData(false, MotionLevel.Reduced, PerformanceTier.Balanced, true)]
    [InlineData(true, MotionLevel.Full, PerformanceTier.Balanced, false)]
    [InlineData(false, MotionLevel.Off, PerformanceTier.Balanced, false)]
    [InlineData(false, MotionLevel.Full, PerformanceTier.Performance, false)]
    public void TiltHonorsSelectionMotionAndPerformance(bool active, MotionLevel level, PerformanceTier tier, bool allowed)
    {
        Assert.Equal(allowed, MainShellWindow.CoinTilts(active, level, tier));
        Assert.Equal(DepthLaw.TiltDegrees, MainShellWindow.CoinTilt(2, -2));
        Assert.Equal(-DepthLaw.TiltDegrees, MainShellWindow.CoinTilt(2, 2));
        Assert.Equal(0, MainShellWindow.CoinTilt(0, 1));
    }

    [Fact]
    public void CoinPartsAndStateTransitionsMatchWpfWithoutConstructingTheShell() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var tile = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(13) };
        var art = new Border(); var tint = new Border(); var ring = new Border();
        var face = new Grid();
        face.Children.AddRange(new Control[] { tile, art, tint, ring });
        var coin = MainShellWindow.BuildNavCoin(face, tile, tint, ring, art, Colors.Purple);
        var parts = new Control[] { coin.Disc, coin.Outer, tile, coin.Dish, art, tint,
            coin.Socket, coin.Rim, coin.Inner, ring, coin.Specular };
        Assert.Equal(parts, face.Children);
        Assert.All(parts.Where(p => p != tile && p != art && p != tint && p != ring), p => Assert.False(p.IsHitTestVisible));
        Assert.Equal(54, coin.Disc.Width); Assert.Equal(12, coin.Disc.Height);
        Assert.Equal(58, coin.Outer.Width); Assert.Equal(48, coin.Inner.Width);
        Assert.Equal(2.5, coin.Inner.BorderThickness.Left);
        Assert.NotNull(coin.Specular.Data);
        var lift = new TranslateTransform();
        MainShellWindow.ApplyNavCoinState(coin, lift, false, false, true, 0, false);
        Assert.Equal(-DepthLaw.HoverLiftPx, lift.Y);
        Assert.Equal(DepthLaw.RaisedPx + DepthLaw.HoverLiftPx, coin.DiscShift.Y);
        MainShellWindow.ApplyNavCoinState(coin, lift, true, true, true, 0, false);
        Assert.Equal(DepthLaw.PressTravelPx, lift.Y);
        Assert.Equal(0, coin.Disc.Opacity); Assert.Equal(1, coin.Socket.Opacity);
        MainShellWindow.ApplyNavCoinState(coin, lift, false, true, true, DepthLaw.ReleaseMs, true);
        var transition = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(lift.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(DepthLaw.ReleaseMs), transition.Duration);
        Assert.IsType<MainShellWindow.NavSpringEase>(transition.Easing);
        Assert.Equal(DepthLaw.ActiveSinkPx - DepthLaw.ReleaseOvershootPx,
            DepthLaw.PressTravelPx + transition.Easing.Ease(0.6) * (DepthLaw.ActiveSinkPx - DepthLaw.PressTravelPx), 6);
        Assert.Equal(1, transition.Easing.Ease(1));
        MainShellWindow.ApplyNavCoinState(coin, lift, false, false, false, 0, false);
        Assert.Null(lift.Transitions); Assert.Equal(0, lift.Y);
        Assert.Equal(1, coin.Disc.Opacity); Assert.Equal(0, coin.Socket.Opacity);
    });

    private static MainShellWindow.NavCoinParts BareCoin()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        return new() { Disc = new Ellipse(), Socket = new Border(), Rim = new Border() };
    }

    [Fact]
    public void PressAndHoverUseTheSharedTravelAndShadowLaw() => AvaloniaTestDispatcher.Run(() =>
    {
        var coin = BareCoin(); var lift = new TranslateTransform();
        MainShellWindow.ApplyNavCoinState(coin, lift, false, false, true, 0, false);
        Assert.Equal(-DepthLaw.HoverLiftPx, lift.Y);
        Assert.Equal(DepthLaw.RaisedPx + DepthLaw.HoverLiftPx, coin.DiscShift.Y);
        MainShellWindow.ApplyNavCoinState(coin, lift, true, false, true, 0, false);
        Assert.Equal(DepthLaw.PressTravelPx, lift.Y);
        Assert.Equal(0, coin.Disc.Opacity); Assert.Equal(1, coin.Socket.Opacity);
    });

    [Fact]
    public void ReleaseCurvePassesTheTargetAtSixtyPercentThenSettles() => AvaloniaTestDispatcher.Run(() =>
    {
        var coin = BareCoin(); var lift = new TranslateTransform { Y = DepthLaw.PressTravelPx };
        MainShellWindow.ApplyNavCoinState(coin, lift, false, true, false, DepthLaw.ReleaseMs, true);
        var transition = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(lift.Transitions!));
        Assert.IsType<MainShellWindow.NavSpringEase>(transition.Easing);
        Assert.Equal(0, DepthLaw.PressTravelPx + transition.Easing.Ease(0.6) *
            (DepthLaw.ActiveSinkPx - DepthLaw.PressTravelPx), 6);
        Assert.Equal(1, transition.Easing.Ease(1));
    });

}
