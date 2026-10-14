using System;
using ConditioningControlPanel.Services.Compositor;
using SkiaSharp;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The chaos sparkle field (WPF ChaosSkiaFxOverlay / ChaosFxLayer, now in Core): emit counts,
/// lifetimes, the linear ripple front, the particle cap, colours with additive bloom, and that the layer
/// goes idle (stops asking for frames) the moment nothing is live.</summary>
public class ChaosFxLayerTests
{
    private static readonly TimeSpan Frame = TimeSpan.FromSeconds(1 / 60.0);

    private static ChaosFxLayer New(out Func<int> wakes, int seed = 3)
    {
        int n = 0;
        wakes = () => n;
        return new ChaosFxLayer(() => n++, rng: new Random(seed));
    }

    private static void Run(ChaosFxLayer fx, double seconds)
    {
        for (double t = 0; t < seconds; t += Frame.TotalSeconds) fx.Update(Frame);
    }

    [Theory]
    [InlineData(1.0, 12)]   // ring = 10 + (int)(2 * scale)
    [InlineData(2.5, 15)]
    public void Burst_is_a_flash_a_ring_and_7_to_11_shards(double scale, int ring)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var fx = New(out _, seed);
            fx.EmitBurst(new SKPoint(100, 100), SKColors.Cyan, (float)scale);
            Assert.InRange(fx.ParticleCount, 1 + ring + 7, 1 + ring + 11);
        }
    }

    [Fact]
    public void Trail_is_a_core_flash_and_3_or_4_sparks()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var fx = New(out _, seed);
            fx.EmitTrail(new SKPoint(50, 50), 0.4, warm: seed % 2 == 0, 1, 0);
            Assert.InRange(fx.ParticleCount, 4, 5);
        }
    }

    [Fact]
    public void Burst_dies_within_its_longest_life_and_the_layer_goes_idle()
    {
        var fx = New(out var wakes);
        fx.EmitBurst(new SKPoint(100, 100), SKColors.Cyan, 1f);
        Assert.True(fx.IsActive);
        Assert.Equal(1, wakes());
        Run(fx, 0.2);                      // shortest life is the 0.24 s flash
        Assert.Equal(fx.ParticleCount > 0, fx.IsActive);
        Assert.True(fx.ParticleCount > 0);
        Run(fx, 0.5);                      // longest shard: 0.30 + 0.35 s
        Assert.Equal(0, fx.ParticleCount);
        Assert.False(fx.IsActive);
        Assert.Equal(2, wakes());          // one wake in, one out: no frame is asked for while idle
    }

    [Fact]
    public void Ripple_front_grows_linearly_and_is_culled_at_its_life()
    {
        var fx = New(out _);
        fx.EmitRipple(new SKPoint(300, 300), 200, 500, strong: true);
        Assert.Equal(14, fx.ParticleCount);   // strong: 14 front motes (7 when not)
        fx.Update(TimeSpan.FromSeconds(0.1));
        Assert.Equal(40f, fx.RippleFront(0), 2);
        fx.Update(TimeSpan.FromSeconds(0.1));
        Assert.Equal(80f, fx.RippleFront(0), 2);
        fx.Update(TimeSpan.FromSeconds(0.1));
        fx.Update(TimeSpan.FromSeconds(0.1));
        Assert.Equal(1, fx.RippleCount);
        fx.Update(TimeSpan.FromSeconds(0.11));
        Assert.Equal(0, fx.RippleCount);

        var weak = New(out _);
        weak.EmitRipple(new SKPoint(0, 0), 100, 500, strong: false);
        Assert.Equal(7, weak.ParticleCount);
    }

    [Fact]
    public void Bolts_live_200ms()
    {
        var fx = New(out _);
        fx.AddStrike(new[] { (new SKPoint(0, 0), new SKPoint(300, 0)), (new SKPoint(0, 50), new SKPoint(0, 400)) });
        Assert.Equal(2, fx.BoltCount);
        Run(fx, 0.18);
        Assert.Equal(2, fx.BoltCount);
        Run(fx, 0.05);
        Assert.Equal(0, fx.BoltCount);
        Assert.False(fx.IsActive);
    }

    [Fact]
    public void Armed_cursor_keeps_the_layer_live_and_sheds_a_spark_every_90ms()
    {
        var fx = New(out _);
        fx.ArmCursor();
        fx.MoveCursor(10, 10);
        fx.Update(TimeSpan.FromSeconds(0.05));
        Assert.Equal(0, fx.ParticleCount);
        fx.Update(TimeSpan.FromSeconds(0.05));   // 0.10 s >= 0.09 s
        Assert.Equal(1, fx.ParticleCount);
        Run(fx, 3);
        Assert.True(fx.IsActive);
        fx.DisarmCursor();
        Run(fx, 1);
        Assert.False(fx.IsActive);
    }

    [Fact]
    public void Particle_field_is_capped_at_2400()
    {
        var fx = New(out _);
        for (int i = 0; i < 200; i++) fx.EmitBurst(new SKPoint(i, i), SKColors.Gold, 1f);
        Assert.Equal(2400, fx.ParticleCount);
    }

    [Fact]
    public void Clear_drops_everything_at_once()
    {
        var fx = New(out _);
        fx.EmitBurst(new SKPoint(1, 1), SKColors.Gold, 1f);
        fx.EmitRipple(new SKPoint(1, 1), 50, 400, false);
        fx.AddStrike(new[] { (new SKPoint(0, 0), new SKPoint(9, 9)) });
        fx.ArmCursor();
        fx.Clear();
        Assert.Equal((0, 0, 0, false, false), (fx.ParticleCount, fx.BoltCount, fx.RippleCount, fx.CursorArmed, fx.IsActive));
    }

    private static SKColor PaintAt(Action<ChaosFxLayer> emit, int x = 100)
    {
        var fx = New(out _);
        emit(fx);
        fx.Update(TimeSpan.FromSeconds(0.01));
        using var bmp = new SKBitmap(new SKImageInfo(200, 200, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);
        fx.Render(canvas, new SKRectI(0, 0, 200, 200), 1, TimeSpan.Zero);
        canvas.Flush();
        return bmp.GetPixel(x, 100);
    }

    [Fact]
    public void Trail_paints_rabbit_pink_or_sweeper_amber_with_a_hot_core()
    {
        Assert.Equal(SKColors.White, PaintAt(fx => fx.EmitTrail(new SKPoint(100, 100), 1, warm: false)));   // gold core + body saturate
        // 32 px out only the core flash's wide bloom halo reaches: the body colour shows.
        var pink = PaintAt(fx => fx.EmitTrail(new SKPoint(100, 100), 1, warm: false), 132);
        var amber = PaintAt(fx => fx.EmitTrail(new SKPoint(100, 100), 1, warm: true), 132);
        Assert.True(pink.Alpha > 0 && amber.Alpha > 0, $"pink {pink} amber {amber}");
        Assert.True(pink.Blue > amber.Blue + 40, $"pink {pink} amber {amber}");   // FF4DC4 vs FF8A14
        Assert.True(amber.Green > amber.Blue, $"amber {amber}");
    }

    [Fact]
    public void Burst_core_blooms_to_white_in_any_payload_colour()
    {
        var c = PaintAt(fx => fx.EmitBurst(new SKPoint(100, 100), new SKColor(0x20, 0x40, 0xFF), 1f));
        // Additive (Plus) layers of the tinted body and the white core saturate the centre.
        Assert.True(c.Red > 200 && c.Green > 200 && c.Blue > 240, $"centre {c}");
    }
}
