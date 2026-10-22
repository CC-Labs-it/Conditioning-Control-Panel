namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Gentle visuals maths for the overlay layers (Services/Safety/GentleVisuals): how a layer's
/// visibility alpha walks toward shown or gone, and the shape of an eased overlay pulse.
/// Pure so it can be unit-tested; only ever consulted while gentle visuals are on.
/// </summary>
public static class GentleFade
{
    /// <summary>Move <paramref name="alpha"/> toward <paramref name="target"/> by one tick of a
    /// linear fade that takes <paramref name="fadeSeconds"/> end to end. A zero fade snaps.</summary>
    public static double Step(double alpha, double target, double dtSeconds, double fadeSeconds)
    {
        if (double.IsNaN(alpha) || fadeSeconds <= 0 || double.IsNaN(fadeSeconds)) return target;
        var step = Math.Max(0, dtSeconds) / fadeSeconds;
        return target > alpha ? Math.Min(target, alpha + step) : Math.Max(target, alpha - step);
    }

    /// <summary>
    /// The eased overlay pulse, 0..1: a smooth rise over <paramref name="riseMs"/>, a hold until
    /// <paramref name="holdUntilMs"/> (the legacy pulse's 1 s), then a smooth fall over
    /// <paramref name="fallMs"/>. Replaces the instant doubling and the 1 s snap back.
    /// </summary>
    public static double PulseEnvelope(double tMs, double riseMs, double holdUntilMs, double fallMs)
    {
        if (double.IsNaN(tMs) || tMs <= 0) return 0;
        if (tMs < riseMs) return Smooth(tMs / riseMs);
        var fallStart = Math.Max(riseMs, holdUntilMs);
        if (tMs <= fallStart) return 1;
        if (fallMs <= 0) return 0;
        var t = (tMs - fallStart) / fallMs;
        return t >= 1 ? 0 : 1 - Smooth(t);
    }

    /// <summary>Wall time of a whole <see cref="PulseEnvelope"/>.</summary>
    public static double PulseTotalMs(double riseMs, double holdUntilMs, double fallMs)
        => Math.Max(riseMs, holdUntilMs) + Math.Max(0, fallMs);

    private static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
