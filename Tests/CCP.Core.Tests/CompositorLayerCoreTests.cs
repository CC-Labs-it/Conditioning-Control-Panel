using System;
using System.Drawing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.UI;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// compositor-core-a: the layer contract, PinkTintLayer and SubliminalLayer moved to CCP.Core with
/// two seams in place of the WPF engine and App. These pin both seams (engine wake, per-monitor
/// gate) and prove the moved layers raster on plain net10.0, natives included.
/// </summary>
public class CompositorLayerCoreTests
{
    private static readonly Rectangle Secondary = new(1920, 0, 1920, 1080);

    [Fact]
    public void ActivityFlipsWakeTheEngineOncePerTransition()
    {
        int wakes = 0;
        var pink = new PinkTintLayer(() => wakes++);

        pink.Show(255, 105, 180, 0.5);
        pink.Show(255, 105, 180, 0.6);   // already active: no second wake
        Assert.True(pink.IsActive);
        Assert.Equal(1, wakes);

        pink.Hide();
        Assert.False(pink.IsActive);
        Assert.Equal(2, wakes);
    }

    [Fact]
    public void FillGate_AsksTheHeadWithThePinkTargetAndTheScreen()
    {
        var pink = new PinkTintLayer(() => { });
        var oldProvider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = null;   // P02: the shared default instance, set explicitly below
        int oldTarget = CoreSettings.Current.PinkFilterTargetMonitor;
        try
        {
            CoreSettings.Current.PinkFilterTargetMonitor = MonitorTarget.FollowGlobal;
            BaseLayer.ScreenTargetGate = null;
            Assert.True(pink.ShouldRenderOnScreen(Secondary));   // no head: every monitor draws

            int seenTarget = int.MinValue;
            Rectangle seenBounds = default;
            BaseLayer.ScreenTargetGate = (target, bounds) => { seenTarget = target; seenBounds = bounds; return false; };

            Assert.False(pink.ShouldRenderOnScreen(Secondary));
            Assert.Equal(MonitorTarget.FollowGlobal, seenTarget);
            Assert.Equal(Secondary, seenBounds);

            CoreSettings.Current.PinkFilterTargetMonitor = 2;   // a pinned monitor reaches the head as-is
            pink.ShouldRenderOnScreen(Secondary);
            Assert.Equal(2, seenTarget);
        }
        finally
        {
            BaseLayer.ScreenTargetGate = null;
            CoreSettings.Current.PinkFilterTargetMonitor = oldTarget;
            CoreSettings.ServiceProvider = oldProvider;
        }
    }

    private sealed class FillProbe : BaseLayer
    {
        public FillProbe() : base(() => { }) { }
        public override int ZIndex => 0;
        public override bool ShouldRenderOnScreen(Rectangle b) => PrimaryOnlyUnlessDualMonitor(b);
        public override void Render(SKCanvas c, SKRectI b, double d, TimeSpan e) { }
    }

    [Fact]
    public void PrimaryOnlyUnlessDualMonitor_AsksTheHeadWithFollowGlobal_AndShowsWhenTheGateThrows()
    {
        var fill = new FillProbe();
        try
        {
            int seenTarget = int.MinValue;
            Rectangle seenBounds = default;
            BaseLayer.ScreenTargetGate = (target, bounds) => { seenTarget = target; seenBounds = bounds; return false; };
            Assert.False(fill.ShouldRenderOnScreen(Secondary));
            Assert.Equal(MonitorTarget.FollowGlobal, seenTarget);
            Assert.Equal(Secondary, seenBounds);

            BaseLayer.ScreenTargetGate = (_, _) => throw new InvalidOperationException("display transition");
            Assert.True(fill.ShouldRenderOnScreen(Secondary));   // errs toward showing
        }
        finally { BaseLayer.ScreenTargetGate = null; }
    }

    [Fact]
    public void PinkTint_RastersItsColorWithOpacityInTheAlpha()
    {
        var pink = new PinkTintLayer(() => { });
        pink.Show(255, 105, 180, 2.0);   // overshoot clamps to opaque

        using var bmp = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using (var canvas = new SKCanvas(bmp))
        {
            canvas.Clear(SKColors.Transparent);
            pink.Render(canvas, new SKRectI(0, 0, 4, 4), 1.0, TimeSpan.Zero);
        }

        Assert.Equal(new SKColor(255, 105, 180, 255), bmp.GetPixel(2, 2));
        Assert.True(pink.Dirty);
        pink.ClearDirty();
        pink.Set(255, 105, 180, 1.0);    // same clamped value: no repaint owed
        Assert.False(pink.Dirty);
    }

    [Fact]
    public void Subliminal_FlashActivatesAndClearRetires()
    {
        int wakes = 0;
        var sub = new SubliminalLayer(() => wakes++);
        var placements = new[] { new SubliminalLayer.Placement(new SKRectI(0, 0, 800, 600), 1f) };

        sub.Flash(placements, "GOOD GIRL", SKColors.Black, SKColors.Magenta, SKColors.White,
            bgTransparent: true, targetOpacity: 1, holdMs: 100);
        Assert.True(sub.IsActive);

        sub.Clear();
        Assert.False(sub.IsActive);
        Assert.Equal(2, wakes);
    }
}
