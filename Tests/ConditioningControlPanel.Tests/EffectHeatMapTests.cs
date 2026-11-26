using System;
using System.Linq;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The read-only effect heat map for Play > Deeper (tier 2 testers, 2026-11-16: "keep the
/// effect heat map too"). Pure maths, so the buckets are pinned here.
/// </summary>
public class EffectHeatMapTests
{
    private static TimelineItem Fx(double start, double duration = 0, int ms = 1000, double intensity = 1.0) => new()
    {
        Kind = TimelineItemKind.Effect, EffectType = EffectTypes.Flash,
        Start = start, Duration = duration, EffectDurationMs = ms, EffectIntensity = intensity,
    };

    [Fact]
    public void An_empty_file_is_all_zeros_and_never_throws()
    {
        Assert.Equal(new double[10], EffectHeatMap.Compute(new Enhancement(), 60, 10));
        Assert.Equal(new double[10], EffectHeatMap.Compute(null, 60, 10));
        Assert.Equal(new double[10], EffectHeatMap.Compute(new Enhancement { TimelineItems = { Fx(5) } }, 0, 10));
        Assert.Empty(EffectHeatMap.Compute(new Enhancement(), 60, 0));
    }

    [Fact]
    public void The_busiest_bucket_reads_one_and_quiet_ones_stay_zero()
    {
        var enh = new Enhancement();
        // Three one-second flashes in 0-10 s, one in 50-60 s, nothing in between.
        enh.TimelineItems.AddRange(new[] { Fx(1), Fx(3), Fx(5), Fx(55) });
        var heat = EffectHeatMap.Compute(enh, 60, 6);

        Assert.Equal(1.0, heat[0], 6);
        Assert.Equal(1.0 / 3.0, heat[5], 6);
        Assert.All(heat.Skip(1).Take(4), v => Assert.Equal(0.0, v));
        Assert.All(heat, v => Assert.InRange(v, 0.0, 1.0));
    }

    [Fact]
    public void A_long_effect_spreads_over_every_bucket_it_covers()
    {
        var enh = new Enhancement { TimelineItems = { Fx(10, duration: 30) } };
        var heat = EffectHeatMap.Compute(enh, 60, 6);
        // 10-40 s covers buckets 1, 2 and 3 fully, nothing else.
        Assert.Equal(new[] { 0.0, 1.0, 1.0, 1.0, 0.0, 0.0 }, heat.Select(v => Math.Round(v, 6)).ToArray());
    }

    [Fact]
    public void Reactive_rules_are_not_on_the_map_but_haptic_events_are()
    {
        var enh = new Enhancement();
        enh.TimelineItems.Add(new TimelineItem { Kind = TimelineItemKind.Rule, Start = 5, Duration = 10 });
        Assert.All(EffectHeatMap.Compute(enh, 60, 6), v => Assert.Equal(0.0, v));

        enh.HapticTracks.Add(new HapticTrack { Events = { new HapticEvent { Start = 45, Duration = 2 } } });
        var heat = EffectHeatMap.Compute(enh, 60, 6);
        Assert.Equal(1.0, heat[4], 6);
    }

    [Fact]
    public void Effects_past_the_end_are_clipped_not_crashed()
    {
        var enh = new Enhancement { TimelineItems = { Fx(58, duration: 20), Fx(90) } };
        var heat = EffectHeatMap.Compute(enh, 60, 6);
        Assert.Equal(6, heat.Length);
        Assert.Equal(1.0, heat[5], 6);
    }

    [Fact]
    public void The_length_comes_from_the_media_then_the_file_then_the_last_effect()
    {
        var enh = new Enhancement { TimelineItems = { Fx(100, duration: 20) } };
        Assert.Equal(120, EffectHeatMap.ResolveDuration(enh));
        enh.Metadata.MediaDurationSeconds = 300;
        Assert.Equal(300, EffectHeatMap.ResolveDuration(enh));
        Assert.Equal(42, EffectHeatMap.ResolveDuration(enh, 42));
        Assert.Equal(0, EffectHeatMap.ResolveDuration(null));
    }
}
