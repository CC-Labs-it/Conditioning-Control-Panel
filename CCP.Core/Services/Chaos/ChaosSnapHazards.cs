using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>WPF BubbleService.cs:1630-1640,1663-1689,1703-1718,1739-1757.
/// Size Queen uses a stepped clock; Aftermath expires on wall time, even during a freeze.</summary>
public sealed class ChaosSnapHazards
{
    public const double RippleRadius = 430, RippleLifeMs = 550;
    public const double ResidueRadius = 170, ResidueLifeMs = 2000;
    private sealed class Wave(double x, double y)
    {
        public readonly double X = x, Y = y;
        public double Age;
    }
    private readonly List<Wave> _waves = new();
    private readonly List<(double X, double Y, DateTime Until)> _zones = new();
    public void Ripple(double x, double y) => _waves.Add(new Wave(x, y));
    public void Residue(double x, double y, DateTime now) =>
        _zones.Add((x, y, now.AddMilliseconds(ResidueLifeMs)));
    public void Clear() { _waves.Clear(); _zones.Clear(); }

    public void Step(IReadOnlyList<AmbientBubble> bubbles, DateTime now, bool frozen,
        Func<AmbientBubble, (double X, double Y)> centerPx, Action<AmbientBubble> pop,
        Action<double, double, AmbientBubble> strike)
    {
        if (frozen) return;
        static bool Eligible(AmbientBubble b) => !b.Popping && b.Spec != null
            && !b.Spec.IsDarter && !b.Spec.IsFreeze;
        static bool Within((double X, double Y) p, double x, double y, double r) =>
            (p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y) <= r * r;
        // Callbacks may append hazards. A cast begins on the next tick, like WPF's reverse loop.
        foreach (var wave in _waves.ToArray())
        {
            wave.Age += 32;
            double r = RippleRadius * Math.Min(1, wave.Age / RippleLifeMs);
            foreach (var b in bubbles)
                if (Eligible(b) && !b.Spec!.IsLive && Within(centerPx(b), wave.X, wave.Y, r)) pop(b);
            if (wave.Age >= RippleLifeMs) _waves.Remove(wave);
        }
        foreach (var zone in _zones.ToArray())
        {
            if (now >= zone.Until) { _zones.Remove(zone); continue; }
            foreach (var b in bubbles)
                if (Eligible(b) && Within(centerPx(b), zone.X, zone.Y, ResidueRadius))
                {
                    strike(zone.X, zone.Y, b);
                    pop(b);
                }
        }
    }
}
