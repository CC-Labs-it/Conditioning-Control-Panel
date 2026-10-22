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

/// <summary>The rail medallions as coins and the rail's shadow (WPF MainWindow.NavRail.cs:67-497,
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
    public void EveryRowIsACoinStackedLikeWpfAndTheLitOneSitsInItsSocketUnderAHueShadow() => Run(w =>
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

        var shadow = w.Named<Rectangle>("NavRailShadow")!;
        Assert.Equal(DepthLaw.RailShadowPx, shadow.Width);
        var fill = Assert.IsType<LinearGradientBrush>(shadow.Fill);
        Assert.Equal(DepthLaw.ShadowColor(NavStripTable.AccentRgb(NavSections.Studio) | 0xFF000000u, 0xBF / 255.0),
            fill.GradientStops[0].Color.ToUInt32());
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
        Assert.Equal(MainShellWindow.CoinOvershoot(2, 1), 0.0);
    });
}
