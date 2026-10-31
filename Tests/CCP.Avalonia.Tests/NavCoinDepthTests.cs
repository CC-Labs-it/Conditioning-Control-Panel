using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public class NavCoinDepthTests
{
    [Fact]
    public void StandaloneCoinPaintsDepthWithoutTextOrAWindow() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var face = new Grid { RowDefinitions = new RowDefinitions("56,Auto") };
        foreach (var key in new[] { "DepthCoinDish", "DepthCoinSocket", "DepthCoinRim", "DepthCoinOuterRim", "DepthCoinInnerShadow", "DepthCoinSpecular", "DepthPressedBevel" })
            face.Resources[key] = Application.Current!.FindResource(key); // detached fixture has no resource parent
        var row = new MainShellWindow.NavSectionRow
        {
            Button = new Button { Content = face }, Hue = Colors.Coral,
            Tile = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(15) },
            Art = new Border(), Tint = new Border(), Ring = new Border(), Badge = new Border(),
        };
        foreach (var part in new[] { row.Tile, row.Art, row.Tint, row.Ring, row.Badge }) face.Children.Add(part);
        MainShellWindow.BuildNavCoin(row, face);
        var c = row.Coin!;
        Assert.Equal(Application.Current!.FindResource("DepthCoinSpecular"), c.Specular.Fill);
        Assert.False(c.Specular.Data!.FillContains(new Point(40, 40)));
        Assert.True(c.Specular.Data.FillContains(new Point(12, 1)));
        Assert.Equal(new Thickness(2.5), c.Inner.BorderThickness);
        Assert.Equal(1.5, c.DiscShift.X);
        row.Active = true;
        MainShellWindow.PaintNavCoin(row);
        Assert.Equal(1, c.Socket.Opacity);
        Assert.Equal(Application.Current.FindResource("DepthCoinSocket"), c.Socket.Background);
        Assert.Equal(DepthLaw.ActiveSinkPx, c.Lift.Y);
        Assert.Equal(0, c.Disc.Opacity);
        row.Active = false; row.Hovered = true;
        MainShellWindow.PaintNavCoin(row);
        Assert.Equal(-DepthLaw.HoverLiftPx, c.Lift.Y);
        Assert.Equal(5, c.DiscShift.Y);
        Assert.Equal(1, c.Disc.Opacity);
        c.From = 2; c.To = -2; c.Duration = 0.14; c.Spring = true;
        MainShellWindow.StepNavCoin(c, 0.084);
        Assert.Equal(-3, c.Lift.Y, 6);
        MainShellWindow.StepNavCoin(c, 0.14);
        Assert.Equal(-2, c.Lift.Y);
    });

    [Fact]
    public void EveryCoinHasOrderedLayersAndSeatedLightAndTravel() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var w = new MainShellWindow();
        try
        {
            Assert.Equal(8, w.NavSectionRowsForTests.Count);
            foreach (var row in w.NavSectionRowsForTests)
            {
                var c = Assert.IsType<MainShellWindow.NavCoinParts>(row.Coin);
                var parts = row.Face!.Children;
                Assert.True(parts.IndexOf(c.Disc) < parts.IndexOf(c.Outer));
                Assert.True(parts.IndexOf(c.Outer) < parts.IndexOf(row.Tile!));
                Assert.True(parts.IndexOf(row.Tile!) < parts.IndexOf(c.Dish));
                Assert.True(parts.IndexOf(c.Dish) < parts.IndexOf(row.Art!));
                Assert.True(parts.IndexOf(row.Tint!) < parts.IndexOf(c.Socket));
                Assert.True(parts.IndexOf(c.Inner) < parts.IndexOf(row.Ring!));
                Assert.True(parts.IndexOf(row.Ring!) < parts.IndexOf(c.Specular));
                Assert.True(parts.IndexOf(c.Specular) < parts.IndexOf(row.Badge!));
                Assert.Equal(1.5, c.DiscShift.X);
                Assert.Equal(58, c.Outer.Width);
                Assert.Equal(new Thickness(2.5), c.Inner.BorderThickness);
                Assert.NotNull(c.Specular.Data);
                Assert.NotNull(c.Specular.Fill);
                foreach (var part in new Control[] { c.Disc, c.Dish, c.Outer, c.Socket, c.Rim, c.Inner, c.Specular })
                { Assert.False(part.IsHitTestVisible); Assert.Null(part.Effect); }
                row.Active = false; row.Hovered = true;
                MainShellWindow.PaintNavCoin(row);
                Assert.Equal(-DepthLaw.HoverLiftPx, c.Lift.Y);
                Assert.Equal(5, c.DiscShift.Y);
                Assert.Equal(1, c.Disc.Opacity);
                row.Active = true;
                MainShellWindow.PaintNavCoin(row);
                Assert.Equal(DepthLaw.ActiveSinkPx, c.Lift.Y);
                Assert.Equal(0, c.Disc.Opacity);
                Assert.Equal(1, c.Socket.Opacity);
                Assert.Equal(w.FindResource("DepthCoinSocket"), c.Socket.Background);
                row.Pressed = true;
                MainShellWindow.PaintNavCoin(row);
                Assert.Equal(DepthLaw.PressTravelPx, c.Lift.Y);
                Assert.Equal(1, row.Badge!.BoxShadow.Count);
            }
            var hue = Color.FromRgb(100, 150, 200);
            w.PaintDepthRail(hue);
            var shadow = w.FindControl<Rectangle>("NavRailShadow")!;
            Assert.Equal(6, shadow.Width);
            Assert.False(shadow.IsHitTestVisible);
            Assert.Equal(Color.FromUInt32(DepthLaw.ShadowColor(hue.ToUInt32(), 0xBF / 255.0)),
                Assert.IsType<LinearGradientBrush>(shadow.Fill).GradientStops[0].Color);
        }
        finally { w.Close(); }
    });
}
