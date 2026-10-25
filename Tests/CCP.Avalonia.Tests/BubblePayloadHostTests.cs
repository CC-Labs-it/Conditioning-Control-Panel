using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BubblePayloadHostTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void RedPressIsCaughtAndReleasePopsOnlyOnce() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var field = BubbleOverlay.Field;
        var clock = field.Clock; var callback = field.NatashaPopped;
        var now = new DateTime(2026, 1, 1); var pops = 0;
        var b = new AmbientBubble { Screen = 0, X = 400, Y = 300, Size = 200, Natasha = true, Clickable = true };
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
        try
        {
            field.Bubbles.Clear(); field.Bubbles.Add(b); field.Clock = () => now;
            field.NatashaPopped = bubble => { pops++; bubble.Popping = true; };
            w.Show();
            w.MouseDown(new Point(500, 400), MouseButton.Left);
            Assert.True(b.Resisting); Assert.False(b.Popping);
            now = now.AddMilliseconds(300);
            w.MouseUp(new Point(500, 400), MouseButton.Left);
            field.Step(); field.Step();
            Assert.Equal(1, pops);
            Assert.True(b.Popping);
        }
        finally
        {
            w.Close(); field.Bubbles.Clear(); field.Clock = clock;
            field.NatashaPopped = callback; field.Held = false;
        }
    });

    [Fact]
    public void TriggerBubblesWearTheirShippedVariantSprite() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        Assert.NotNull(BubbleOverlay.SpriteFor(AmbientBubblePayloads.Build("pink")!));
        Assert.NotNull(BubbleOverlay.SpriteFor(AmbientBubblePayloads.Build("glitch")!));
        Assert.Same(BubbleOverlay.Image, BubbleOverlay.SpriteFor(AmbientBubblePayloads.Build("flash")!));
        Assert.Null(BubbleOverlay.SpriteFor(new EffectBubbleSpec { VariantId = "no_such_variant" }));
    });

    [Fact]
    public void ResistRingIsAClockwiseArcFromTwelve()
    {
        var quarter = BubbleOverlayWindow.RingArc(new Point(100, 100), 50, 0.25).Bounds;
        Assert.Equal(100, quarter.X, 3); Assert.Equal(50, quarter.Y, 3);
        Assert.Equal(50, quarter.Width, 3); Assert.Equal(50, quarter.Height, 3);
        Assert.Equal(new Rect(50, 50, 100, 100), BubbleOverlayWindow.RingArc(new Point(100, 100), 50, 1).Bounds);
    }

    [Fact]
    public void NatashaBubbleWearsARedHalo() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var field = BubbleOverlay.Field;
        var s = global::ConditioningControlPanel.CoreSettings.Current;
        var perf = s.PerformanceMode;
        var b = new AmbientBubble { Screen = 0, X = 400, Y = 300, Size = 200 };
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
        try
        {
            s.PerformanceMode = false;   // the halo is a glow: Performance tier drops it (WPF AllowGlow)
            field.Bubbles.Clear(); field.Bubbles.Add(b);
            w.Show(); w.Background = global::Avalonia.Media.Brushes.Black;
            var rShift = 16;   // Bgra8888 by default; Linux Skia may hand back Rgba8888
            uint Edge()
            {
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = w.CaptureRenderedFrame()!;
                rShift = frame.Format == global::Avalonia.Platform.PixelFormat.Rgba8888 ? 0 : 16;
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
                try
                {
                    frame.CopyPixels(new PixelRect(602, 400, 1, 1), buf, 4, 4);   // just outside the rim and the wash
                    return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf);
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            }
            var plain = Edge();
            b.Natasha = true; (w.Content as Control)!.InvalidateVisual();
            var red = Edge();
            uint R(uint px) => (px >> rShift) & 0xFF;
            uint B(uint px) => (px >> (16 - rShift)) & 0xFF;
            Assert.True(R(red) > R(plain) + 3, $"plain {plain:X8}, natasha {red:X8}");
            Assert.True(R(red) > B(red), $"natasha {red:X8}");
        }
        finally { w.Close(); field.Bubbles.Clear(); s.PerformanceMode = perf; }
    });

    [Fact]
    public void TimedTintDoesNotChangeSettingsAndPanicDropsItsTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        var s = global::ConditioningControlPanel.CoreSettings.Current;
        var enabled = s.PinkFilterEnabled; var opacity = s.PinkFilterOpacity;
        var host = new Window();
        PinkFilterOverlay.ShowTimed(host, 10000, 0.5);
        Assert.True(PinkFilterOverlay.HasTimedPayload);
        Assert.Equal(enabled, s.PinkFilterEnabled);
        Assert.Equal(opacity, s.PinkFilterOpacity);
        PinkFilterOverlay.CloseAll();
        Assert.False(PinkFilterOverlay.HasTimedPayload);
    });

    [Fact]
    public void PanicCancelsQueuedPayloadAndClearsSharedHost() => AvaloniaTestDispatcher.Run(() =>
    {
        EnsureApp();
        PinkFilterOverlay.ShowTimed(new Window(), 10000, 0.5);
        Action? queued = null; var fired = false;
        BubbleOverlay.Payloads.Run(new VideoPayload(), () => true, () => false, () => false,
            a => queued = a, _ => throw new Exception(), _ => fired = true);
        BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Natasha = true });
        PanicSurfaces.All.Single(s => s.Id == "bubble-host").Stop(null);
        queued!();
        Assert.False(fired);
        Assert.Empty(BubbleOverlay.Field.Bubbles);
        Assert.False(PinkFilterOverlay.HasTimedPayload);   // panic ends the timed tint (WPF OverlayService.Stop)
    });
}
