using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF Bubble hit-area press: a press inside a bubble pops it and pays the ambient 5 XP
/// from the daily bucket; a press beside it pops nothing.</summary>
public sealed class BubbleOverlayTests
{
    /// <summary>WPF BubbleService.cs:221 LoadBubbleImage: each Start re-resolves bubble.png through the
    /// active mod, so after a mod switch the next start draws the new mod's sprite, not the cached one.</summary>
    [Fact]
    public async Task Each_start_reads_the_active_mods_bubble_sprite()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = System.IO.Directory.CreateTempSubdirectory("ccp-bubble-");
            var png = System.IO.Path.Combine(dir.FullName, "pill.png");
            using (var bmp = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(7, 3), new Vector(96, 96)))
                bmp.Save(png);
            var (previous, oldImage) = (CoreModArt.OverridePathProvider, BubbleOverlay.Image);
            try
            {
                CoreModArt.OverridePathProvider = _ => null;              // the default mod's sprite
                BubbleOverlay.LoadBubbleImage();
                Assert.NotEqual(7, BubbleOverlay.Image!.PixelSize.Width);
                CoreModArt.OverridePathProvider = p => p == "bubble.png" ? png : null;   // switched mod
                BubbleOverlay.LoadBubbleImage();
                Assert.Equal(7, BubbleOverlay.Image!.PixelSize.Width);
            }
            finally
            {
                CoreModArt.OverridePathProvider = previous;
                BubbleOverlay.Image = oldImage;
                dir.Delete(true);
            }
            return Task.CompletedTask;
        });
    }

    private static void HeadlessApp()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static byte[] PixelAt(global::Avalonia.Media.Imaging.WriteableBitmap frame, int x, int y)
    {
        var bytes = new byte[4];
        using var fb = frame.Lock();
        System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, bytes, 0, 4);
        return bytes;
    }

    [Fact]
    public Task Shine_only_lights_plain_chaos_bubbles_when_enabled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        HeadlessApp();
        var s = CoreSettings.Current;
        var oldFlag = s.ChaosSkiaFxEnabled;
        var (oldImage, oldDarter) = (BubbleOverlay.Image, BubbleOverlay.DarterImage);
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 400, 300), 1) { Width = 400, Height = 300 };
        try
        {
            BubbleOverlay.LoadBubbleImage();
            Assert.NotNull(BubbleOverlay.DarterImage);
            var b = new AmbientBubble { Screen = 0, X = 100, Y = 100, Size = 100,
                Spec = new ConditioningControlPanel.Services.Chaos.EffectBubbleSpec() };
            BubbleOverlay.Field.Bubbles.Add(b);
            w.Show();
            byte[] Capture(bool enabled)
            {
                s.ChaosSkiaFxEnabled = enabled;
                w.Sync();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = w.CaptureRenderedFrame()!;
                return PixelAt(frame, 134, 127);
            }
            var plainOff = Capture(false);
            var plainOn = Capture(true);
            Assert.True(plainOn[0] > plainOff[0] && plainOn[1] > plainOff[1] && plainOn[2] > plainOff[2],
                $"off {string.Join(",", plainOff)}, on {string.Join(",", plainOn)}");
            b.Spec = ConditioningControlPanel.Services.Chaos.ChaosBubbleVariants.BuildDarter(0);
            var darterOff = Capture(false);
            var darterOn = Capture(true);
            Assert.Equal(darterOff, darterOn);
            b.Spec = null;
            Assert.Equal(Capture(false), Capture(true));
        }
        finally
        {
            BubbleOverlay.Field.Bubbles.Clear(); w.Close();
            s.ChaosSkiaFxEnabled = oldFlag;
            BubbleOverlay.Image = oldImage; BubbleOverlay.DarterImage = oldDarter;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Darter_sprite_and_glow_are_visible_in_the_rendered_field() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        HeadlessApp();
        var (oldImage, oldBubble) = (BubbleOverlay.DarterImage, BubbleOverlay.Image);
        var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 400, 300), 1) { Width = 400, Height = 300 };
        try
        {
            BubbleOverlay.LoadBubbleImage();
            Assert.NotNull(BubbleOverlay.DarterImage);
            var spec = ConditioningControlPanel.Services.Chaos.ChaosBubbleVariants.BuildDarter(0);
            BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { Screen = 0, X = 100, Y = 100, Size = 100, Spec = spec });
            w.Show();
            w.Sync();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var withArt = w.CaptureRenderedFrame()!;
            var sprite = PixelAt(withArt, 150, 150);
            Assert.True(sprite[0] != sprite[2] && sprite[3] > 0, $"sprite pixel: {string.Join(",", sprite)}");
            Assert.True(PixelAt(withArt, 203, 150)[3] > 0);   // the glow extends past the image rectangle
            BubbleOverlay.DarterImage = null;
            w.Sync();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(sprite, PixelAt(w.CaptureRenderedFrame()!, 150, 150));
        }
        finally
        {
            BubbleOverlay.Field.Bubbles.Clear();
            w.Close();
            BubbleOverlay.DarterImage = oldImage;
            BubbleOverlay.Image = oldBubble;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Press_on_a_bubble_pops_it_and_pays_the_bucket()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (dayKey, paid) = (s.AmbientBubbleXpDayKey, s.AmbientBubbleXpPaidToday);   // process-global: put it back
            s.AmbientBubbleXpDayKey = "";   // fresh day
            var w = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1) { Width = 1280, Height = 720 };
            var card = new ConditioningControlPanel.Avalonia.Views.Features.BubblePopFeatureControl();
            var host = new global::Avalonia.Controls.Window { Content = card };
            host.Show();
            var line = card.FindControl<global::Avalonia.Controls.TextBlock>("TxtAmbientXpBudget")!;
            var b = new AmbientBubble { Screen = 0, X = 400, Y = 300, Size = 200, Clickable = true };
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.Add(b);
            try
            {
                w.Show();
                w.MouseDown(new Point(300, 400), MouseButton.Left);   // beside it
                Assert.False(b.Popping);
                w.MouseDown(new Point(500, 400), MouseButton.Right);  // its centre; right pops too
                Assert.True(b.Popping);
                Assert.Equal(5, AmbientBubbleXp.PaidToday(s));
                Assert.Contains("5/300", line.Text);   // the card's N/300 line follows the pop
            }
            finally
            {
                BubbleOverlay.Field.Bubbles.Clear(); w.Close(); host.Close();
                s.AmbientBubbleXpDayKey = dayKey; s.AmbientBubbleXpPaidToday = paid;
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Windows input region: the window takes clicks only while the cursor is inside a
    /// live bubble rect (Win32Overlay.SetInputRects toggles WS_EX_TRANSPARENT on this answer).</summary>
    [Fact]
    public void Win32_rect_hit_decision_takes_input_only_inside_live_rects()
    {
        var rects = new[] { new PixelRect(100, 100, 50, 50), new PixelRect(400, 0, 10, 10), new PixelRect(0, 0, 999, 999) };
        Assert.True(Win32Overlay.Hits(rects, 2, 120, 120));
        Assert.True(Win32Overlay.Hits(rects, 2, 405, 5));
        Assert.False(Win32Overlay.Hits(rects, 2, 150, 150));   // right/bottom edge is outside
        Assert.False(Win32Overlay.Hits(rects, 2, 300, 300));   // the third rect is past count
        Assert.False(Win32Overlay.Hits(rects, 0, 120, 120));   // empty field: fully click-through
    }
}
