using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// One opacity animation of a Chaos FX layer: starts at <see cref="From"/> (the layer's current
/// opacity, as a WPF <c>DoubleAnimation</c> with no From does) and walks linear key frames
/// (<c>LinearDoubleKeyFrame</c>) to the last one, which it then holds (FillBehavior.HoldEnd).
/// </summary>
public sealed class ChaosFxRamp
{
    private readonly (double Ms, double To)[] _keys;

    public ChaosFxRamp(double from, params (double Ms, double To)[] keys)
    {
        if (keys.Length == 0) throw new ArgumentException("A ramp needs at least one key frame.", nameof(keys));
        From = from;
        _keys = keys;
    }

    public double From { get; }
    public double DurationMs => _keys[^1].Ms;
    public double End => _keys[^1].To;

    /// <summary>Opacity <paramref name="ms"/> after the ramp began.</summary>
    public double Sample(double ms)
    {
        if (ms <= 0) return From;
        double prevMs = 0, prev = From;
        foreach (var (km, kv) in _keys)
        {
            if (ms < km) return prev + (kv - prev) * ((ms - prevMs) / (km - prevMs));
            prevMs = km;
            prev = kv;
        }
        return End;
    }
}

/// <summary>
/// The timing and peak maths of WPF <c>ChaosFxWindow</c> (ConditioningControlPanel/Chaos/ChaosFxWindow.cs)
/// and of <c>ChaosModeService.UpdateHeatTint</c> (:3131), so a head only draws. Every number is WPF's.
/// </summary>
public static class ChaosFxEnvelope
{
    /// <summary>ChaosModeService.cs:116-118 / :3038.</summary>
    public static readonly (byte R, byte G, byte B) HeatTintColor = (255, 90, 40);
    public const double HEAT_TINT_MIN = 0.30;
    public const double HEAT_TINT_MAX_OPACITY = 0.30;
    public static readonly (byte R, byte G, byte B) FreezeEdgeColor = (150, 210, 255);

    /// <summary>Pulse (:151): peak = clamp(0.22 + s*0.5, 0.15, 0.72); snap up 40 ms, fade out by 300 ms.</summary>
    public static ChaosFxRamp Pulse(double from, double strength) =>
        new(from, (40, Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72)), (300, 0.0));

    /// <summary>FreezeBurst (:126): to 0.6 in 50 ms, melt to 0 by 650 ms.</summary>
    public static ChaosFxRamp FreezeBurst(double from) => new(from, (50, 0.6), (650, 0.0));

    /// <summary>BeginEdgeHold (:59): peak = clamp(0.25 + s*0.45, 0.2, 0.7) over 180 ms, held.</summary>
    public static ChaosFxRamp EdgeHoldIn(double from, double strength) =>
        new(from, (180, Math.Clamp(0.25 + strength * 0.45, 0.2, 0.7)));

    /// <summary>EndEdgeHold (:82): to 0 over 450 ms.</summary>
    public static ChaosFxRamp EdgeHoldOut(double from) => new(from, (450, 0.0));

    /// <summary>SetHeatTint (:92): clamp(opacity, 0, 0.5) over 400 ms, held.</summary>
    public static ChaosFxRamp HeatIn(double from, double opacity) => new(from, (400, Math.Clamp(opacity, 0.0, 0.5)));

    /// <summary>EndHeatTint (:116): to 0 over 600 ms.</summary>
    public static ChaosFxRamp HeatOut(double from) => new(from, (600, 0.0));

    /// <summary>
    /// UpdateHeatTint (:3131): the heat level above the 0.30 floor, rescaled to 0..1 and rounded to
    /// one decimal. Returns null when it moved less than 0.05 from <paramref name="lastLevel"/> (no
    /// new animation every 250 ms tick); otherwise the new level, where 0 means EndHeatTint and
    /// anything else SetHeatTint(level * <see cref="HEAT_TINT_MAX_OPACITY"/>).
    /// </summary>
    public static double? HeatTintLevel(double heat, double lastLevel)
    {
        double level = heat <= HEAT_TINT_MIN ? 0 : (heat - HEAT_TINT_MIN) / (1.0 - HEAT_TINT_MIN);
        double q = Math.Round(level, 1);
        return Math.Abs(q - lastLevel) < 0.05 ? null : q;
    }
}
