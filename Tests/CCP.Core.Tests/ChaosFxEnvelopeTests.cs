using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF ChaosFxWindow's key frames and peaks, and ChaosModeService.UpdateHeatTint's quantizer.</summary>
public class ChaosFxEnvelopeTests
{
    [Fact]
    public void Gentle_pulse_rises_over_300ms_then_keeps_its_260ms_fade()
    {
        var ramp = ChaosFxEnvelope.Pulse(0, 0.5,
            ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(40, true));
        Assert.Equal(0.235, ramp.Sample(150), 6);
        Assert.Equal(0.47, ramp.Sample(300), 6);
        Assert.Equal(0.235, ramp.Sample(430), 6);
        Assert.Equal(0, ramp.Sample(560));
    }

    [Fact]
    public void Pulse_snaps_to_its_peak_in_40ms_and_is_gone_at_300ms()
    {
        var r = ChaosFxEnvelope.Pulse(0, 0.5);   // peak 0.22 + 0.25
        Assert.Equal(0, r.Sample(0), 6);
        Assert.Equal(0.235, r.Sample(20), 6);
        Assert.Equal(0.47, r.Sample(40), 6);
        Assert.Equal(0.235, r.Sample(170), 6);
        Assert.Equal(0, r.Sample(300), 6);
        Assert.Equal(0, r.Sample(5000), 6);
        Assert.Equal(300, r.DurationMs);
        Assert.Equal(0.72, ChaosFxEnvelope.Pulse(0, 5).Sample(40), 6);    // clamped high
        Assert.Equal(0.15, ChaosFxEnvelope.Pulse(0, -1).Sample(40), 6);   // clamped low
    }

    [Fact]
    public void A_ramp_starts_from_the_current_opacity_like_a_WPF_animation_without_From()
    {
        var r = ChaosFxEnvelope.EdgeHoldOut(0.6);
        Assert.Equal(0.6, r.Sample(0), 6);
        Assert.Equal(0.3, r.Sample(225), 6);
        Assert.Equal(0, r.Sample(450), 6);
    }

    [Fact]
    public void Freeze_edge_and_heat_use_WPF_timings_and_clamps()
    {
        var f = ChaosFxEnvelope.FreezeBurst(0);
        Assert.Equal(0.6, f.Sample(50), 6);
        Assert.Equal(0.3, f.Sample(350), 6);
        Assert.Equal(650, f.DurationMs);

        Assert.Equal(0.475, ChaosFxEnvelope.EdgeHoldIn(0, 0.5).Sample(180), 6);
        Assert.Equal(0.7, ChaosFxEnvelope.EdgeHoldIn(0, 9).End, 6);
        Assert.Equal(0.2, ChaosFxEnvelope.EdgeHoldIn(0, -9).End, 6);
        Assert.Equal(180, ChaosFxEnvelope.EdgeHoldIn(0, 0.5).DurationMs);

        Assert.Equal(0.15, ChaosFxEnvelope.HeatIn(0, 0.3).Sample(200), 6);
        Assert.Equal(0.5, ChaosFxEnvelope.HeatIn(0, 2).End, 6);
        Assert.Equal(400, ChaosFxEnvelope.HeatIn(0, 0.3).DurationMs);
        Assert.Equal(600, ChaosFxEnvelope.HeatOut(0.3).DurationMs);
    }

    [Fact]
    public void Heat_tint_shows_above_the_floor_quantized_and_only_on_change()
    {
        Assert.Equal(0, ChaosFxEnvelope.HeatTintLevel(0.30, -1));   // at the floor: off
        Assert.Null(ChaosFxEnvelope.HeatTintLevel(0.2, 0));          // still off: no churn
        Assert.Equal(0.5, ChaosFxEnvelope.HeatTintLevel(0.65, 0));   // (0.65-0.3)/0.7
        Assert.Null(ChaosFxEnvelope.HeatTintLevel(0.66, 0.5));      // rounds to the same level
        Assert.Equal(1.0, ChaosFxEnvelope.HeatTintLevel(1.0, 0.5));
    }
}
