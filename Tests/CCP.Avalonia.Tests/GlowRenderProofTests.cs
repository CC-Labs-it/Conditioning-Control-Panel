using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// lag-fixes-b review: the glows that left their Effect (P82 rule 3) must still DRAW. Each test renders the
/// real shell headless (Skia) and reads pixels just outside the glowing control, with the glow layer shown and
/// hidden: the halo must tint them. Set CCP_GLOW_PROOF_DIR to also save the frames as PNGs.
/// </summary>
public sealed class GlowRenderProofTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static uint Pixel(Bitmap frame, Point at)
    {
        var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            double sx = frame.PixelSize.Width / frame.Size.Width;
            frame.CopyPixels(new PixelRect((int)(at.X * sx), (int)(at.Y * sx), 1, 1), buf, 4, 4);
            return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf) & 0x00FFFFFFu;
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    private static Bitmap Frame(Window w, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = w.CaptureRenderedFrame()!;
        var dir = Environment.GetEnvironmentVariable("CCP_GLOW_PROOF_DIR");
        if (!string.IsNullOrEmpty(dir)) { System.IO.Directory.CreateDirectory(dir); frame.Save(System.IO.Path.Combine(dir, name + ".png")); }
        return frame;
    }

    private static int Diff(uint a, uint b) =>
        Math.Abs((int)(a & 0xFF) - (int)(b & 0xFF)) + Math.Abs((int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF))
        + Math.Abs((int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF));

    [Fact]
    public Task GlowBakeDrawsTheTextsBlurOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var source = new TextBlock { Text = "88:88", FontSize = 40, FontWeight = FontWeight.Bold };
        var bmp = ConditioningControlPanel.Avalonia.Helpers.GlowBake.Text(source, Brushes.White,
            new BlurEffect { Radius = 16 }, 32);
        Assert.NotNull(bmp);
        Assert.True(bmp!.Size.Width > 64 + 40, $"size {bmp.Size}");
        var c = bmp.Size;
        uint centre = Alpha(bmp, new Point(c.Width / 2, c.Height / 2)), corner = Alpha(bmp, new Point(1, 1));
        Assert.True(centre > 0x20, $"nothing drawn at the centre: {centre:X2}");
        Assert.True(corner < 0x08, $"the pad is not clear: {corner:X2}");
        return Task.CompletedTask;
    });

    private static uint Alpha(Bitmap b, Point at)
    {
        var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            b.CopyPixels(new PixelRect((int)at.X, (int)at.Y, 1, 1), buf, 4, 4);
            return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buf) >> 24;
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
    }

    [Fact]
    public Task FoldArrowGlowDrawsAHaloOutsideThePill() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var before = (s.DashboardBrowserCollapsed, s.MotionLevel, s.PerformanceMode);
        var (fxTime, beatTime) = (FxAdorner.Time, ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time);
        MainShellWindow? w = null;
        try
        {
            FxAdorner.Time = new SteppedTime();                                       // frozen: the frames differ only by the glow
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = new SteppedTime();
            (s.DashboardBrowserCollapsed, s.MotionLevel, s.PerformanceMode) = (true, MotionLevel.Full, false);
            w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var dash = w.Named<SettingsTabView>("SettingsTab")!;
            var btn = dash.FindControl<Button>("BtnFoldBrowser")!;
            var glow = dash.FindControl<Border>("FoldGlow")!;
            Assert.True(glow.IsVisible, "the fold glow is not shown - the proof proves nothing");
            glow.Opacity = 1;
            var p = btn.TranslatePoint(new Point(-4, btn.Bounds.Height / 2), w)!.Value;   // 4 DIP left of the pill
            var lit = Pixel(Frame(w, "fold-glow-on"), p);
            glow.IsVisible = false;
            var dark = Pixel(Frame(w, "fold-glow-off"), p);
            Assert.True(Diff(lit, dark) > 12, $"no halo beside the pill: {lit:X6} vs {dark:X6}");
        }
        finally
        {
            w?.Close();
            Dispatcher.UIThread.RunJobs();
            (FxAdorner.Time, ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time) = (fxTime, beatTime);
            (s.DashboardBrowserCollapsed, s.MotionLevel, s.PerformanceMode) = before;
        }
        return Task.CompletedTask;
    });

    /// <summary>Bounding box of the pixels with alpha above 8 (glyph ink, or ink plus halo).</summary>
    private static PixelRect Ink(Bitmap b)
    {
        int w = b.PixelSize.Width, h = b.PixelSize.Height;
        var px = new int[w * h];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { b.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), px.Length * 4, w * 4); }
        finally { handle.Free(); }
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (((uint)px[y * w + x] >> 24) > 8) { x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); }
        return x1 < 0 ? default : new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    [Fact]
    public Task PadlockGlowIsTheGlyphsBakedShadow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var before = (s.MotionLevel, s.PerformanceMode);
        var (premium, lab, free) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider);
        var (fxTime, beatTime) = (FxAdorner.Time, ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time);
        MainShellWindow? w = null;
        try
        {
            // Frozen clocks: the two frames differ only by the glow, not by the Ken Burns drift or a sheen.
            var fxClock = new SteppedTime();
            FxAdorner.Time = fxClock;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = new SteppedTime();
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Full, false);
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.HasLabProvider = null;
            CoreEntitlement.IsFreeTodayProvider = null;
            w = new MainShellWindow { Width = 1400, Height = 900 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.ShowTab("exclusives");
            Dispatcher.UIThread.RunJobs();
            var view = w.Named<ExclusivesTabView>("ExclusivesTab")!;
            fxClock.Advance(TimeSpan.FromSeconds(5)); view.FlairFrame();   // past the card entrance fade (frozen at 0 opacity otherwise)
            var lockGlyph = view.GetVisualDescendants().OfType<IconGlyph>().First(t => t.Name == "VeilLock" && t.IsEffectivelyVisible);
            var glow = ExclusivesTabView.LockGlow(lockGlyph)!;
            Assert.True(glow.IsVisible, "the padlock glow is not shown - the proof proves nothing");
            Assert.Null(lockGlyph.Effect);                                    // no live Effect on the glyph
            var baked = Assert.IsAssignableFrom<Bitmap>(glow.Source);          // the shadow, baked once
            var dir = Environment.GetEnvironmentVariable("CCP_GLOW_PROOF_DIR");
            if (!string.IsNullOrEmpty(dir)) baked.Save(System.IO.Path.Combine(dir, "padlock-glow-bake.png"));

            // Glyph-shaped: the bake's ink reaches past the bare glyph's ink on every side (the shadow's spread).
            double pad = -glow.Margin.Left;
            var plain = Ink(ConditioningControlPanel.Avalonia.Helpers.GlowBake.Icon(lockGlyph, Brushes.White, null, pad)!);
            var halo = Ink(baked);
            Assert.True(halo.X < plain.X - 2 && halo.Y < plain.Y - 2 && halo.Right > plain.Right + 2 && halo.Bottom > plain.Bottom + 2,
                $"no halo around the glyph: ink {plain}, with glow {halo}");

            // On screen: 2 DIP left of the glyph's ink, mid-height, the halo tints the frame.
            glow.Opacity = 1;
            var p = lockGlyph.TranslatePoint(new Point(plain.X - pad - 2, plain.Y - pad + plain.Height / 2.0), w)!.Value;
            var lit = Pixel(Frame(w, "padlock-glow-on"), p);
            glow.IsVisible = false;
            var dark = Pixel(Frame(w, "padlock-glow-off"), p);
            Assert.True(Diff(lit, dark) > 4, $"no halo beside the padlock: {lit:X6} vs {dark:X6}");
        }
        finally
        {
            w?.Close();
            Dispatcher.UIThread.RunJobs();
            (FxAdorner.Time, ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time) = (fxTime, beatTime);
            (CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider) = (premium, lab, free);
            (s.MotionLevel, s.PerformanceMode) = before;
        }
        return Task.CompletedTask;
    });
}
