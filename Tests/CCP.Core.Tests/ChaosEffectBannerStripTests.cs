using System;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF ChaosEffectBannerOverlay's rules on a hand-set clock: one live label per id, arrival
/// order, fade-in 200 ms, fade-out 380 ms from the current opacity, then gone; 1.0-1.03 throb.</summary>
public class ChaosEffectBannerStripTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double ms) => T0.AddMilliseconds(ms);

    [Fact]
    public void Labels_keep_arrival_order_and_a_live_id_never_stacks()
    {
        var s = new ChaosEffectBannerStrip();
        Assert.NotNull(s.Show("vibe", "VibePopping", null, T0));
        Assert.NotNull(s.Show("freeze", "Freeze", null, At(10)));
        Assert.Null(s.Show("vibe", "VibePopping", null, At(20)));
        Assert.Equal(new[] { "vibe", "freeze" }, s.Row.Select(e => e.Id));
        Assert.Equal("pendulum", s.Show("slowmo", "Pendulum", "pendulum", At(30))!.ArtKey);
        Assert.Equal("freeze", s.Row[1].ArtKey);
    }

    [Fact]
    public void End_fades_for_380ms_then_the_label_leaves_and_the_id_can_show_again_beside_it()
    {
        var s = new ChaosEffectBannerStrip();
        var e = s.Show("dvd", "Porn DVD", null, T0)!;
        Assert.Equal(0, e.Opacity(T0), 6);
        Assert.Equal(0.5, e.Opacity(At(100)), 6);
        Assert.Equal(1, e.Opacity(At(5000)), 6);

        Assert.Null(s.End("nope", At(1000)));
        Assert.Same(e, s.End("dvd", At(1000)));
        Assert.False(s.IsLive("dvd"));
        Assert.Null(s.End("dvd", At(1001)));   // already fading
        Assert.Equal(0.5, e.Opacity(At(1190)), 6);

        var again = s.Show("dvd", "Porn DVD", null, At(1200))!;   // WPF: a new label beside the fading one
        Assert.Equal(2, s.Row.Count);

        Assert.Empty(s.Prune(At(1379)));
        Assert.Same(e, Assert.Single(s.Prune(At(1380))));
        Assert.Same(again, Assert.Single(s.Row));
        Assert.Equal(0, e.Opacity(At(2000)), 6);
    }

    [Fact]
    public void End_mid_fade_in_fades_from_the_current_opacity()
    {
        var s = new ChaosEffectBannerStrip();
        var e = s.Show("estim", "E-Stim", null, T0)!;
        s.End("estim", At(50));                         // 0.25 at End
        Assert.Equal(0.125, e.Opacity(At(50 + 190)), 6);
    }

    [Fact]
    public void Throb_breathes_1_to_1_03_on_an_850ms_sine_and_reverses()
    {
        var e = new ChaosEffectBannerStrip().Show("freeze", "Freeze", null, T0)!;
        Assert.Equal(1.0, e.Scale(T0), 6);
        Assert.Equal(1.015, e.Scale(At(425)), 6);
        Assert.Equal(1.03, e.Scale(At(850)), 6);
        Assert.Equal(1.015, e.Scale(At(1275)), 6);
        Assert.Equal(1.0, e.Scale(At(1700)), 6);
        Assert.True(e.Scale(At(100)) < 1.0 + 0.03 * 100 / 850);   // eased, not linear
    }

    [Fact]
    public void Clear_empties_the_row()
    {
        var s = new ChaosEffectBannerStrip();
        s.Show("rabbits", "Rabbit Storm", null, T0);
        s.Clear();
        Assert.Empty(s.Row);
        Assert.False(s.IsLive("rabbits"));
    }
}
