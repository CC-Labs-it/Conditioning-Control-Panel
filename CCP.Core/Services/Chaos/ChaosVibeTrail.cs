using System;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// The maths of VibePopping's pointer glow and fading trail (WPF
/// ConditioningControlPanel/Chaos/ChaosVibeTrailOverlay.cs): a fixed ring of trail dots, a new
/// dot once the pointer has travelled <see cref="EmitDist"/> since the last one, each dot fading
/// 0.5 -> 0 and shrinking 1 -> 0.35 over <see cref="FadeMs"/>, and the glow's sine buzz-pulse.
/// No timers: the head steps it with a clock, so a test plays a whole sweep in a loop.
/// </summary>
public sealed class ChaosVibeTrail
{
    public const double GlowSize = 58;      // WPF :24
    public const double DotSize = 20;       // :25
    public const int Dots = 14;             // :26
    public const double EmitDist = 9;       // :27
    public const int FadeMs = 340;          // :28
    public const int TickMs = 16;           // :29
    public const double DotStartOpacity = 0.5, DotEndScale = 0.35;            // :240-245
    public const double PulseMin = 0.9, PulseMax = 1.12; public const int PulseHalfMs = 180;   // :193

    private readonly double[] _x = new double[Dots], _y = new double[Dots], _born = new double[Dots];
    private int _next;
    private double _lastX, _lastY;

    public ChaosVibeTrail() { Clear(); Begin(); }

    public double GlowX { get; private set; }
    public double GlowY { get; private set; }

    /// <summary>WPF BeginFollow: forget the last emit point, so the first tick drops a dot.</summary>
    public void Begin() { _lastX = double.MinValue; _lastY = double.MinValue; }

    /// <summary>WPF EndFollow: every dot gone at once.</summary>
    public void Clear() { for (int i = 0; i < Dots; i++) _born[i] = double.NaN; }

    /// <summary>One follow tick at window-local DIPs. Returns the slot of the dot it dropped, or -1.</summary>
    public int Move(double x, double y, double nowMs)
    {
        GlowX = x; GlowY = y;
        double dx = x - _lastX, dy = y - _lastY;
        if (dx * dx + dy * dy < EmitDist * EmitDist) return -1;
        _lastX = x; _lastY = y;
        int slot = _next;
        _next = (_next + 1) % Dots;
        _x[slot] = x; _y[slot] = y; _born[slot] = nowMs;
        return slot;
    }

    /// <summary>Centre, opacity and scale of dot <paramref name="i"/> at <paramref name="nowMs"/>
    /// (WPF's linear DoubleAnimations, holding their end values once done).</summary>
    public (double X, double Y, double Opacity, double Scale) Dot(int i, double nowMs)
    {
        if (double.IsNaN(_born[i])) return (_x[i], _y[i], 0, 1);
        double t = Math.Clamp((nowMs - _born[i]) / FadeMs, 0, 1);
        return (_x[i], _y[i], DotStartOpacity * (1 - t), 1 - (1 - DotEndScale) * t);
    }

    /// <summary>The glow's buzz: 0.9 -> 1.12 over 180 ms with SineEase EaseInOut, auto-reversed, forever.</summary>
    public static double GlowScale(double ms)
    {
        double p = ((ms % (2 * PulseHalfMs)) + 2 * PulseHalfMs) % (2 * PulseHalfMs);
        double t = p < PulseHalfMs ? p / PulseHalfMs : (2 * PulseHalfMs - p) / PulseHalfMs;
        return PulseMin + (PulseMax - PulseMin) * (1 - Math.Cos(Math.PI * t)) / 2;
    }
}
