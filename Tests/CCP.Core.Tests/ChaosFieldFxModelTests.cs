using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ChaosFieldFxModel against WPF ChaosFieldFxOverlay.cs's storyboards, on a stepped clock.</summary>
public class ChaosFieldFxModelTests
{
    private static List<FieldFxPrim> At(ChaosFieldFxModel m, double now)
    {
        var l = new List<FieldFxPrim>();
        m.Frame(now, l);
        return l;
    }

    [Fact]
    public void Ripple_grows_eased_from_0_05_and_fades_from_0_95_then_is_gone()
    {
        var m = new ChaosFieldFxModel();
        m.Ripple(100, 100, 50, 1000, 0);
        var p0 = Assert.Single(At(m, 0));
        Assert.Equal(0.95, p0.Opacity, 6);
        Assert.Equal(6 * 0.05, p0.Thickness, 6);
        Assert.Equal(ChaosFieldFxModel.RingColor, p0.Color);
        var mid = Assert.Single(At(m, 500));
        Assert.Equal(0.95 * 0.5, mid.Opacity, 6);
        Assert.Equal((50 - 3) * (0.05 + 0.95 * 0.875), mid.Radius, 6);   // cubic ease-out at 0.5 = 0.875
        Assert.Empty(At(m, 1000));
        Assert.Equal(0, m.TransientCount);
    }

    [Fact]
    public void SnapRipple_front_is_linear_echo_eased_and_eight_shards_die_at_70_percent()
    {
        var m = new ChaosFieldFxModel();
        m.SnapRipple(0, 0, 100, 1000, 0);
        var s0 = At(m, 0).First(p => p.Kind == FieldFxKind.Shard);
        Assert.Equal(Math.Cos(0.39) * 20, s0.X, 6);   // starts 0.2 r out along a non-axis angle
        var f = At(m, 500);
        var rings = f.Where(p => p.Kind == FieldFxKind.Ring).ToList();
        Assert.Equal(2, rings.Count);
        Assert.Equal((100 - 3) * (0.05 + 0.95 * 0.5), rings[0].Radius, 6);          // linear = the kill front
        Assert.Equal((82 - 1.75) * (0.05 + 0.95 * 0.875), rings[1].Radius, 6);      // eased echo at 0.82 r
        Assert.Equal(ChaosFieldFxModel.CastEchoColor, rings[1].Color);
        Assert.Equal(0.65 * 0.5, rings[1].Opacity, 6);
        Assert.Equal(8, f.Count(p => p.Kind == FieldFxKind.Shard));
        Assert.DoesNotContain(At(m, 700), p => p.Kind == FieldFxKind.Shard);
    }

    [Fact]
    public void Residue_flickers_on_90ms_keyframes_then_ramps_to_zero()
    {
        var flicker = new[] { 0.6, 0.9, 0.7 };
        Assert.Equal(0.6, ChaosFieldFxModel.ResidueOpacity(flicker, 10, 300), 6);
        Assert.Equal(0.9, ChaosFieldFxModel.ResidueOpacity(flicker, 100, 300), 6);
        Assert.Equal(0.7, ChaosFieldFxModel.ResidueOpacity(flicker, 180, 300), 6);
        Assert.Equal(0.35, ChaosFieldFxModel.ResidueOpacity(flicker, 240, 300), 6);
        Assert.Equal(0, ChaosFieldFxModel.ResidueOpacity(flicker, 300, 300), 6);

        var m = new ChaosFieldFxModel();
        m.Residue(0, 0, 40, 900, 0, new Random(1));
        var o = Assert.Single(At(m, 0)).Opacity;
        Assert.InRange(o, 0.55, 1.0);
    }

    [Theory]
    [InlineData(2000, 2000)]
    [InlineData(500, 1050)]
    public void Gentle_residue_rises_holds_and_fades_without_flicker(double requestedLife, double life)
    {
        var m = new ChaosFieldFxModel();
        m.Residue(0, 0, 40, requestedLife, 0, gentle: true);
        Assert.Equal(0, Assert.Single(At(m, 0)).Opacity, 6);
        Assert.Equal(0.4, Assert.Single(At(m, 150)).Opacity, 6);
        Assert.Equal(0.8, Assert.Single(At(m, 300)).Opacity, 6);
        Assert.Equal(0.8, Assert.Single(At(m, life - 750)).Opacity, 6);
        Assert.Equal(0.4, Assert.Single(At(m, life - 375)).Opacity, 6);
        Assert.Empty(At(m, life));
    }

    [Fact]
    public void Trail_pool_recycles_90_sparks_and_fades_0_65_and_shrinks_1_1_to_0_3()
    {
        var m = new ChaosFieldFxModel();
        for (int i = 0; i < 95; i++) m.TrailDot(i, 0, 1, warm: i == 94, now: 0);
        var f = At(m, 500);
        Assert.Equal(ChaosFieldFxModel.TrailDotPool, f.Count);
        Assert.Contains(f, p => p.X == 94 && p.Warm && p.Color == ChaosFieldFxModel.WarmTrailColor);
        Assert.DoesNotContain(f, p => p.X < 5);   // the first five were overwritten
        var d = f.First(p => !p.Warm);
        Assert.Equal(0.65 * 0.5, d.Opacity, 6);
        Assert.Equal(8 * 0.7, d.Radius, 6);
        m.TrailDot(0, 0, 0.1, false, 0);   // floor of 0.3 s
        Assert.Contains(At(m, 250), p => p.X == 0);
    }

    [Fact]
    public void Tether_thins_with_stretch_and_idle_follows_the_wpf_hide_rule()
    {
        Assert.Equal(5.0, ChaosFieldFxModel.TetherThickness(0));
        Assert.Equal(3.0, ChaosFieldFxModel.TetherThickness(500));
        Assert.Equal(1.5, ChaosFieldFxModel.TetherThickness(5000));
        var m = new ChaosFieldFxModel();
        Assert.True(m.IsIdle(0, 0));
        m.SetTether(3, 0, 0, 500, 0);
        var t = Assert.Single(At(m, 0));
        Assert.Equal((FieldFxKind.Tether, 3.0, 0.55), (t.Kind, t.Thickness, t.Opacity));
        Assert.False(m.IsIdle(0, 0));
        m.ClearTether(3);
        Assert.True(m.IsIdle(0, 0));
        Assert.False(m.IsIdle(0, 2.0));   // rabbit trail boon on keeps it up
        m.TrailDot(0, 0, 1, false, 0);
        Assert.False(m.IsIdle(999, 0));
        Assert.True(m.IsIdle(1000, 0));
    }
}
