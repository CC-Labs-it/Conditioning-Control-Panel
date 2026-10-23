using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiRingInputTests
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Reflection lets the regression compile against the base, where these paths did not exist.
    private static void Poll(EmiRingWindow ring) => typeof(EmiRingWindow).GetMethod("PollClickAway", Instance)!.Invoke(ring, null);
    private static PixelRect[] Rects(EmiRingWindow ring) =>
        (PixelRect[])typeof(EmiRingWindow).GetField("_inputRects", Instance)!.GetValue(ring)!;

    private static Task WithRing(Action<EmiRingWindow, Action<PixelPoint, bool, bool>> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var ring = new EmiRingWindow(new PixelRect(590, 290, 220, 420), new PixelPoint(700, 500));
        (PixelPoint At, bool Pressed, bool Escape)? input = null;
        try
        {
            typeof(EmiRingWindow).GetField("ReadPointer", Instance)!.SetValue(ring,
                (Func<(PixelPoint At, bool Pressed, bool Escape)?>)(() => input));
            ring.OpenRing();
            Assert.True(ring.IsOpen);
            test(ring, (at, down, esc) => { input = (at, down, esc); Poll(ring); });
        }
        finally { ring.Kill(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OutsidePressClosesOnceButOpeningPressAndBodyAreExempt() => WithRing((ring, read) =>
    {
        int closed = 0;
        ring.RingClosed += (_, picked) => { Assert.False(picked); closed++; };
        var outside = new PixelPoint(-100, -100);
        read(outside, true, false);                 // opening click still held
        Assert.True(ring.IsOpen);
        read(outside, false, false);
        read(new PixelPoint(700, 500), true, false); // her silhouette is hot
        Assert.True(ring.IsOpen);
        read(outside, false, false);
        read(outside, true, false);                 // left OR right button (platform snapshot)
        Assert.False(ring.IsOpen);
        Assert.Empty(Rects(ring));                  // fold is immediately click-through
        read(outside, false, false);
        read(outside, true, false);
        ring.CloseRing();
        Assert.Equal(1, closed);
        Assert.Null(typeof(EmiRingWindow).GetField("_awayPoll", Instance)!.GetValue(ring));
    });

    [Fact]
    public Task SilentCloseHidesAtOnceAndCutsAFoldShort() => WithRing((ring, _) =>
    {
        int closed = 0;
        ring.RingClosed += (_, _) => closed++;
        Assert.True(ring.IsVisible);
        ring.CloseRing(animate: false);             // panic: no fold, no cue
        Assert.False(ring.IsOpen);
        Assert.False(ring.IsVisible);
        Assert.Equal(1, closed);
        ring.OpenRing();
        ring.CloseRing();                           // a fold starts...
        Assert.True(ring.IsVisible);
        ring.CloseRing(animate: false);             // ...and panic cuts it
        Assert.False(ring.IsVisible);
        Assert.Equal(2, closed);
    });

    [Fact]
    public Task CardPressStaysOpenAndEscapeFoldsEvenWithoutFocus() => WithRing((ring, read) =>
    {
        read(default, false, false);
        var card = ring.Cards[0];
        var at = new PixelPoint(ring.Position.X + (int)Canvas.GetLeft(card) + 68,
                                ring.Position.Y + (int)Canvas.GetTop(card) + 51);
        read(at, true, false);
        Assert.True(ring.IsOpen);
        read(at, false, true);
        Assert.False(ring.IsOpen);
    });

    [Fact]
    public Task RelayoutAndRebuildRefreshOnlyTheCardInputRegions() => WithRing((ring, read) =>
    {
        var old = Rects(ring);
        Assert.Equal(ring.Cards.Count, old.Length);  // the silhouette and gaps must pass through
        ring.SetWidgetGeometry(new PixelRect(20, 20, 120, 230), new PixelPoint(80, 135));
        ring.Relayout();
        Assert.False(old.SequenceEqual(Rects(ring)));
        read(default, false, false);
        read(new PixelPoint(80, 135), true, false);  // updated silhouette, physical pixels
        Assert.True(ring.IsOpen);
        ring.Rebuild();
        Assert.Equal(ring.Cards.Count, Rects(ring).Length);
        Assert.All(ring.Cards, c => Assert.Equal(1, c.Opacity));
    });

    [Fact]
    public Task ReopenDuringFoldRearmsEscapeAndKillStopsPolling() => WithRing((ring, read) =>
    {
        ring.CloseRing();
        ring.OpenRing();
        Assert.True(ring.IsOpen);
        read(default, false, false);
        read(default, false, true);
        Assert.False(ring.IsOpen);
        ring.OpenRing();
        ring.Kill();
        Assert.Null(typeof(EmiRingWindow).GetField("_awayPoll", Instance)!.GetValue(ring));
        Assert.Empty(Rects(ring));
    });

    [Fact]
    public Task LocalFallbackKeepsRightCardClicksAndMiddleClicksInert() => WithRing((ring, read) =>
    {
        ring.Rebuild();
        Dispatcher.UIThread.RunJobs();
        var card = ring.Cards[0];
        var at = new Point(Canvas.GetLeft(card) + 68, Canvas.GetTop(card) + 51);
        ring.MouseDown(at, MouseButton.Right);
        ring.MouseUp(at, MouseButton.Right);
        Assert.True(ring.IsOpen);
        ring.MouseDown(new Point(1, 1), MouseButton.Middle);
        ring.MouseUp(new Point(1, 1), MouseButton.Middle);
        Assert.True(ring.IsOpen);
        ring.MouseDown(new Point(1, 1), MouseButton.Right);
        ring.MouseUp(new Point(1, 1), MouseButton.Right);
        Assert.False(ring.IsOpen);
    });

    // Pure numeric regression: no fonts, display, timer or native input required.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.4, 0.9551901729331276)]
    [InlineData(0.7, 1.0458115294937453)]
    [InlineData(0.9, 1.0082705098312483)]
    [InlineData(1, 1)]
    public void PopMoveUsesWpfAmplitudePointThree(double progress, double expected)
    {
        var property = typeof(EmiRingWindow).GetProperty("PopMoveEasing", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var easing = (Easing)property.GetValue(null)!;
        Assert.Equal(expected, easing.Ease(progress), 12);
    }
}
