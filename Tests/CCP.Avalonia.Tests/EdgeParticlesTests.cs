using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.UI.EdgeEffects;
using Xunit;
namespace CCP.Avalonia.Tests;
public class EdgeParticlesTests
{
    [Fact]
    public async Task StripsFollowGateHueAndSimulation() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        var edge = new EdgeParticles { MotionOverride = MotionLevel.Full, TierAllowsParticlesOverride = true };
        edge.Mount(Colors.Pink);
        Assert.False(edge.IsHitTestVisible);
        Assert.Equal(4, edge.Strips.Count);
        Assert.Equal(new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left }, edge.Strips.Select(s => s.EdgeSide));
        var first = edge.Strips[0];
        Assert.Equal(56, first.Height);
        Assert.Equal(56, edge.Strips[1].Width);
        first.PrimeEdgeForTests(60);
        for (int i = 0; i < 8; i++) first.StepEdge(.7f);
        Assert.Equal(6, first.EdgeMoteCount);
        var before = first.EdgeMotesForTests();
        first.StepEdge(.05f);
        foreach (var m in first.EdgeMotesForTests())
        {
            Assert.InRange(m.Depth, .15f, .85f);
            var prior = before.Where(b => b.Depth == m.Depth).ToArray();
            if (prior.Length == 1) Assert.InRange(m.Along - prior[0].Along, .000899f, .001501f);
        }
        edge.Mount(Colors.Blue);
        Assert.Same(first, edge.Strips[0]);
        Assert.All(edge.Strips, s => Assert.Equal(Colors.Blue, s.Tint));
        edge.MotionOverride = MotionLevel.Off; edge.Refresh(); Assert.Empty(edge.Children);
        edge.MotionOverride = MotionLevel.Full; edge.Refresh(); Assert.Equal(4, edge.Strips.Count);
        edge.TierAllowsParticlesOverride = false; edge.Refresh(); Assert.Empty(edge.Children);
        return Task.CompletedTask;
    });

    // WPF FogPolish11Tests.cs:39 and :54.
    [Fact]
    public async Task ReducedMountsTheFogAloneAndALevelChangeRebuilds() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        Assert.Equal(new[] { EdgeFogMode.None, EdgeFogMode.None, EdgeFogMode.Reduced, EdgeFogMode.Full }, new[] { EdgeParticles.ModeFor(MotionLevel.Off, true),
            EdgeParticles.ModeFor(MotionLevel.Reduced, false), EdgeParticles.ModeFor(MotionLevel.Reduced, true), EdgeParticles.ModeFor(MotionLevel.Full, true) });
        Assert.Equal(1 << 7, (int)AmbientFxLayers.EdgeFog);
        var edge = new EdgeParticles { MotionOverride = MotionLevel.Reduced, TierAllowsParticlesOverride = true };
        edge.Mount(Colors.SkyBlue);
        Assert.Equal(EdgeFogMode.Reduced, edge.Mode);
        Assert.True(edge.FogLive); Assert.Equal(4, edge.Strips.Count);
        Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog, s.Layers));
        // A hue change reaches the fog (stopped strips swap at once) with the hue's gain.
        edge.Retint(Colors.Gold, 250);
        Assert.All(edge.Strips, s => Assert.Equal((Colors.Gold, (float)EdgeParticles.FogGain(Colors.Gold)), s.FogPaint));
        var reduced = edge.Strips[0];
        edge.MotionOverride = MotionLevel.Full;
        edge.Mount(Colors.Pink, 250);
        Assert.NotSame(reduced, edge.Strips[0]);
        Assert.All(edge.Strips, s => Assert.Equal(AmbientFxLayers.EdgeFog | AmbientFxLayers.EdgeDrift, s.Layers));
        edge.MotionOverride = MotionLevel.Off; edge.Refresh();
        Assert.False(edge.FogLive); Assert.Empty(edge.Strips);
        return Task.CompletedTask;
    });

    // WPF FogPolish11Tests.cs:149 and :186.
    [Fact]
    public async Task PuffsDriftInsideTheirStripAndTakeTheHue() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        foreach (var side in new[] { EdgeSide.Top, EdgeSide.Right, EdgeSide.Bottom, EdgeSide.Left })
        {
            bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
            double sw = horizontal ? 1661 : EdgeFogMath.StripPx, sh = horizontal ? EdgeFogMath.StripPx : 1002;
            var strip = new AmbientFxCanvas { Width = sw, Height = sh };
            strip.Measure(new Size(sw, sh)); strip.Arrange(new Rect(0, 0, sw, sh));
            strip.StartLayers(new AmbientFxConfig { Layers = AmbientFxLayers.EdgeFog, EdgeSide = side, Tint = Colors.SkyBlue });
            strip.PrimeEdgeForTests(60);
            int cap = EdgeFogMath.FullCount(true, horizontal) + EdgeFogMath.FullCount(false, horizontal);
            Assert.Equal(cap, strip.FogPuffCount);
            Assert.True(strip.GrainCount > 0);
            var first = strip.FogPuffsForTests();
            for (int f = 0; f < 600; f++)
            {
                strip.StepFog(1f / 30f);
                Assert.InRange(strip.FogPuffCount, 1, cap);
                foreach (var (x, y, size, _) in strip.FogPuffsForTests())
                {
                    double depth = side switch { EdgeSide.Top => y, EdgeSide.Bottom => sh - y, EdgeSide.Left => x, _ => sw - x };
                    Assert.InRange(depth + size / 2, 0, EdgeFogMath.StripPx + 1e-6);
                    Assert.True(depth >= -EdgeFogMath.DepthOutPx - 1e-6);
                }
            }
            var last = strip.FogPuffsForTests();
            Assert.DoesNotContain(last, p => first.Any(q => System.Math.Abs(q.X - p.X) < 1e-3 && System.Math.Abs(q.Y - p.Y) < 1e-3));
            Assert.Equal(Colors.SkyBlue, strip.FogPaint.Tint);
            // A stopped canvas swaps at once.
            strip.SetEdgeFog(Colors.Pink, EdgeParticles.FogGain(Colors.Pink), 250);
            Assert.Equal((Colors.Pink, (float)EdgeParticles.FogGain(Colors.Pink)), strip.FogPaint);
        }
        return Task.CompletedTask;
    });

    // WPF :580 lets the Reduced fog run the clock; WPF :228/:490 crossfade a running strip.
    [Fact]
    public Task ReducedTicksTheRealClockAndARunningFadeAdvances() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        LifecycleTestPump.Setup();
        var s = CoreSettings.Current;
        var (motion, perf, auto) = (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode);
        Window? w = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode) = (MotionLevel.Reduced, false, false);
            var edge = new EdgeParticles { TierAllowsParticlesOverride = true };
            edge.Mount(Colors.SkyBlue);
            w = new Window { Content = edge, Width = 600, Height = 400 };
            w.Show(); w.Activate();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var strip = edge.Strips[0];
            Assert.True(edge.Mode == EdgeFogMode.Reduced && strip.IsTicking, "the Reduced fog never started its clock");
            edge.Retint(Colors.Gold, 250);
            Assert.Equal(Colors.SkyBlue, strip.FogPaint.Tint);   // a running strip fades, it does not swap
            var seen = new System.Collections.Generic.HashSet<Color>();
            var gold = (Colors.Gold, (float)EdgeParticles.FogGain(Colors.Gold));
            // Record every colour on the way, but only stop once the fade has landed on gold.
            await LifecycleTestPump.UntilAsync(() => { seen.Add(strip.FogPaint.Tint); return strip.FogPaint == gold; }, "The fade never landed.");
            Assert.Contains(seen, c => c != Colors.SkyBlue && c != Colors.Gold);
        }
        finally { w?.Close(); (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode) = (motion, perf, auto); }
    });

    // WPF summed the puffs Plus on the SKElement's own surface (EdgeFog.cs :337/:549, Plus at
    // AmbientFxCanvas.cs :360), then composited that over the window source-over.
    [Fact]
    public Task FogAddsUpOnItsOwnLayerLikeWpf() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        LifecycleTestPump.Setup();
        var strip = new AmbientFxCanvas();
        var host = new Border { Child = strip };
        host.Measure(new Size(400, 56)); host.Arrange(new Rect(0, 0, 400, 56));
        strip.StartLayers(new AmbientFxConfig { Layers = AmbientFxLayers.EdgeFog, EdgeSide = EdgeSide.Top, Tint = Colors.Red });
        strip.PrimeEdgeForTests(60);
        byte[] Centre(int puffs)
        {
            strip.PinFogForTests(puffs, 200, 28, 0.2f);
            using var bmp = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 56));
            bmp.Render(host);
            var px = new byte[4];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(new PixelRect(200, 28, 1, 1), pin.AddrOfPinnedObject(), 4, 4); }
            finally { pin.Free(); }
            return px;   // BGRA
        }
        Assert.InRange(Centre(1)[3], 49, 53);    // one puff: 0.2
        Assert.InRange(Centre(2)[3], 100, 104);  // two add to 0.4 (source-over would stop at 0.36, 92)
        host.Background = Brushes.Gray;          // the layer goes over the window: 128 x (1 - 0.4)
        Assert.InRange(Centre(2)[1], 74, 80);    // a bare Plus would leave the green at 128
        return Task.CompletedTask;
    });

    [Fact]
    public async Task ShellMountsRetintsAndRefreshesEdge() => await AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (!System.OperatingSystem.IsWindows()) AmbientBeatLoopsTests.Setup();
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldLevel = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        var w = new MainShellWindow();
        try
        {
            w.Show();
            var edge = w.Named<EdgeParticles>("SectionEdgeParticles")!;
            edge.TierAllowsParticlesOverride = true; edge.Refresh();
            Assert.Equal(4, edge.Strips.Count);
            w.ShowTab("presets");
            var hue = NavStripRules.Accent(NavSections.SectionForTab("presets"));
            Assert.Equal(hue, edge.Hue);
            // WPF SectionEdge.cs:117: the band thins to 16 px at 70% while the fog runs, back at Off.
            w.OnMotionChrome();   // the same paint with no crossfade, so the brush reads final
            var band = w.Named<Panel>("SectionEdgeGlow")!.Children.OfType<Border>().ToList();
            byte full = NavStripRules.EdgeGlowAlpha(hue);
            Assert.True(edge.FogLive);
            Assert.Equal(16, band[0].Height);
            Assert.Equal(16, band[2].Width);
            Assert.Equal(NavStripRules.WithAlpha(hue, System.Math.Round(full * 0.7) / 255.0), ((ISolidColorBrush)band[0].Background!).Color);
            CoreSettings.Current.MotionLevel = MotionLevel.Off; w.OnMotionChrome();
            Assert.Empty(edge.Strips);
            Assert.Equal(28, band[0].Height);
            Assert.Equal(28, band[2].Width);
            Assert.Equal(NavStripRules.WithAlpha(hue, full / 255.0), ((ISolidColorBrush)band[0].Background!).Color);
            CoreSettings.Current.MotionLevel = MotionLevel.Full; w.OnMotionChrome();
            Assert.Equal(4, edge.Strips.Count);
        }
        finally { w.Close(); CoreSettings.Current.MotionLevel = oldLevel; }
        return Task.CompletedTask;
    });
}
