using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The numbers behind Rabbit Caller's cursor halo, read from the WPF overlay
/// (ConditioningControlPanel/Chaos/ChaosCursorGlowOverlay.cs) and its driver
/// (ChaosModeService.ArmRabbitCall, :2862). WPF does no follow smoothing: every 16 ms tick
/// snaps the window's centre onto the cursor, and the only motion of its own is the breath.
/// </summary>
public static class ChaosCursorGlowMath
{
    /// <summary>Window and halo edge in DIPs (WPF SIZE = 76).</summary>
    public const double Size = 76;

    /// <summary>Breath range: WPF DoubleAnimation(0.85, 1.12) on ScaleX/ScaleY.</summary>
    public const double PulseMin = 0.85, PulseMax = 1.12;

    /// <summary>One way of the breath (620 ms, AutoReverse, RepeatBehavior.Forever).</summary>
    public static readonly TimeSpan PulseHalfPeriod = TimeSpan.FromMilliseconds(620);

    /// <summary>The aim timer's interval (WPF DispatcherTimer at Render priority, 16 ms).</summary>
    public static readonly TimeSpan FollowInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>Where the window's top-left goes, in physical pixels, so its centre sits on the
    /// cursor. WPF converts the cursor to DIPs and subtracts SIZE/2; in pixel space that is
    /// SIZE/2 times the monitor scaling.</summary>
    public static (int X, int Y) TopLeftPx(int cursorX, int cursorY, double scaling)
    {
        if (!(scaling > 0) || double.IsInfinity(scaling)) scaling = 1;
        var half = Size / 2 * scaling;
        return ((int)Math.Round(cursorX - half), (int)Math.Round(cursorY - half));
    }

    /// <summary>Parked off-screen until armed: WPF Left = Top = -SIZE * 2 DIPs.</summary>
    public static (int X, int Y) ParkedPx(double scaling)
    {
        if (!(scaling > 0) || double.IsInfinity(scaling)) scaling = 1;
        var p = (int)Math.Round(-Size * 2 * scaling);
        return (p, p);
    }

    /// <summary>The halo's scale <paramref name="elapsed"/> after the breath started: a
    /// SineEase(EaseInOut) from <see cref="PulseMin"/> to <see cref="PulseMax"/> over
    /// <see cref="PulseHalfPeriod"/>, then back, forever.</summary>
    public static double PulseScale(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var t = elapsed.TotalMilliseconds / PulseHalfPeriod.TotalMilliseconds;
        var cycle = t % 2;
        var phase = cycle <= 1 ? cycle : 2 - cycle;
        var eased = (1 - Math.Cos(Math.PI * phase)) / 2;   // WPF SineEase EaseInOut
        return PulseMin + (PulseMax - PulseMin) * eased;
    }
}
