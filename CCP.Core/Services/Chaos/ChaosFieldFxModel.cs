using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>What one field-FX primitive is drawn as.</summary>
public enum FieldFxKind { Ring, Shard, Residue, TrailDot, Tether }

/// <summary>An ARGB colour without any UI framework type.</summary>
public readonly record struct FieldFxColor(byte A, byte R, byte G, byte B);

/// <summary>One primitive to draw this frame, in the overlay's local DIPs. Ring: stroked circle of
/// <see cref="Radius"/>; Shard/Tether: a line X,Y -> X2,Y2; Residue/TrailDot: a radial-gradient disc
/// of <see cref="Radius"/>. <see cref="Opacity"/> multiplies the colour's own alpha.</summary>
public readonly record struct FieldFxPrim(FieldFxKind Kind, double X, double Y, double X2, double Y2,
    double Radius, double Thickness, double Opacity, FieldFxColor Color, bool Warm = false);

/// <summary>
/// The motion half of WPF <c>ConditioningControlPanel/Chaos/ChaosFieldFxOverlay.cs</c>: Size Queen's
/// pop-ring, the Ripple cast (cyan front, pink echo, eight shards), Aftermath's flickering residue,
/// Tail-Plug's 90-dot recycled sparkle trail and The Bound's elastic tethers. WPF ran each as a
/// WPF Storyboard; here every element is sampled from its start time, so a head draws what
/// <see cref="Frame"/> returns on its shared frame clock. Times are milliseconds on the caller's clock.
/// </summary>
public sealed class ChaosFieldFxModel
{
    public const int TrailDotPool = 90;      // WPF TRAIL_DOT_POOL
    public const double TrailDotSize = 16;   // WPF TRAIL_DOT_SIZE

    public static readonly FieldFxColor RingColor = new(190, 0x7A, 0xE0, 0xFF);      // Size Queen snap-cyan, alpha 190
    public static readonly FieldFxColor CastFrontColor = new(200, 0x7A, 0xE0, 0xFF); // the water
    public static readonly FieldFxColor CastEchoColor = new(200, 0xFF, 0x69, 0xB4);  // the hand
    public static readonly FieldFxColor ShardColor = new(220, 0x7A, 0xE0, 0xFF);
    public static readonly FieldFxColor ResidueColor = new(255, 0x9C, 0x5C, 0xFF);   // Aftermath E-Stim violet
    public static readonly FieldFxColor ResidueCore = new(70, 0xBF, 0xEC, 0xFF);
    public static readonly FieldFxColor TrailColor = new(255, 0xFF, 0x4D, 0xC4);     // Tail-Plug rabbit pink
    public static readonly FieldFxColor WarmTrailColor = new(255, 0xFF, 0x8A, 0x14); // GG sweeper amber
    public static readonly FieldFxColor TrailCore = new(200, 0xFF, 0xE9, 0xA0);      // gold spark core
    public static readonly FieldFxColor TetherColor = new(150, 0xFF, 0x69, 0xB4);

    private struct Transient
    {
        public FieldFxKind Kind;
        public double X, Y, R, Start, Life, FromOpacity, Thickness, Ux, Uy;
        public bool Eased;
        public FieldFxColor Color;
        public double[]? Flicker;
    }

    private struct Dot { public double X, Y, Start, Life; public bool Warm, Used; }

    private readonly List<Transient> _transients = new();
    private readonly Dot[] _dots = new Dot[TrailDotPool];
    private int _dotIndex;
    private readonly Dictionary<int, (double Ax, double Ay, double Bx, double By)> _tethers = new();

    public int TransientCount => _transients.Count;
    public int TetherCount => _tethers.Count;

    /// <summary>WPF DrawRipple: one ring growing 0.05 -> 1 (cubic ease-out) while fading 0.95 -> 0.</summary>
    public void Ripple(double x, double y, double r, double lifeMs, double now) =>
        AddRing(x, y, r, lifeMs, now, RingColor, 6, 0.95, eased: true);

    /// <summary>WPF DrawSnapRipple: a LINEAR cyan front (it IS the kill front), an eased pink echo at
    /// 0.82 r, and eight shards flying 0.2 r -> 0.85 r over 70% of the life.</summary>
    public void SnapRipple(double x, double y, double r, double lifeMs, double now)
    {
        AddRing(x, y, r, lifeMs, now, CastFrontColor, 6, 0.95, eased: false);
        AddRing(x, y, r * 0.82, lifeMs, now, CastEchoColor, 3.5, 0.65, eased: true);
        for (int i = 0; i < 8; i++)
        {
            double ang = Math.PI * 2 * i / 8 + 0.39;   // offset so no shard sits on an axis
            _transients.Add(new Transient
            {
                Kind = FieldFxKind.Shard, X = x, Y = y, R = r, Start = now, Life = Math.Max(1, lifeMs * 0.7),
                FromOpacity = 0.9, Thickness = 3, Ux = Math.Cos(ang), Uy = Math.Sin(ang), Color = ShardColor,
            });
        }
    }

    /// <summary>WPF DrawResidue: a flicker keyframe every 90 ms at 0.55..1.0, then a linear fade to 0 at the end.</summary>
    public void Residue(double x, double y, double r, double lifeMs, double now, Random? rng = null)
    {
        rng ??= Random.Shared;
        int steps = Math.Max(2, (int)(lifeMs / 90));
        var flicker = new double[steps];
        for (int i = 0; i < steps; i++) flicker[i] = 0.55 + rng.NextDouble() * 0.45;
        _transients.Add(new Transient
        {
            Kind = FieldFxKind.Residue, X = x, Y = y, R = r, Start = now, Life = Math.Max(1, lifeMs),
            Color = ResidueColor, Flicker = flicker,
        });
    }

    /// <summary>WPF DrawTrailDot: recycle the next of 90 sparks; opacity 0.65 -> 0, scale 1.1 -> 0.3 over max(0.3 s, life).</summary>
    public void TrailDot(double x, double y, double lifeSec, bool warm, double now)
    {
        _dots[_dotIndex] = new Dot { X = x, Y = y, Start = now, Life = Math.Max(0.3, lifeSec) * 1000, Warm = warm, Used = true };
        _dotIndex = (_dotIndex + 1) % TrailDotPool;
    }

    /// <summary>WPF UpdateTether: create or move the thread between a tethered pair.</summary>
    public void SetTether(int key, double ax, double ay, double bx, double by) => _tethers[key] = (ax, ay, bx, by);

    /// <summary>WPF RemoveTether.</summary>
    public void ClearTether(int key) => _tethers.Remove(key);

    public void Clear()
    {
        _transients.Clear();
        _tethers.Clear();
        Array.Clear(_dots);
        _dotIndex = 0;
    }

    /// <summary>WPF hides the window when no ripple/residue lives, no tether is up and the rabbit
    /// trail boon is off (<paramref name="rabbitTrailSec"/> &lt;= 0) once the last dot has faded.</summary>
    public bool IsIdle(double now, double rabbitTrailSec)
    {
        if (_transients.Count > 0 || _tethers.Count > 0 || rabbitTrailSec > 0) return false;
        foreach (var d in _dots) if (d.Used && now - d.Start < d.Life) return false;
        return true;
    }

    /// <summary>Samples every live element at <paramref name="now"/> into <paramref name="into"/> (cleared
    /// first) and drops the finished ones. Order: tethers, residues/rings/shards in birth order, then dots.</summary>
    public void Frame(double now, List<FieldFxPrim> into)
    {
        into.Clear();
        foreach (var t in _tethers.Values)
        {
            double dx = t.Bx - t.Ax, dy = t.By - t.Ay;
            double th = TetherThickness(Math.Sqrt(dx * dx + dy * dy));
            into.Add(new FieldFxPrim(FieldFxKind.Tether, t.Ax, t.Ay, t.Bx, t.By, 0, th, 0.55, TetherColor));
        }

        _transients.RemoveAll(t => now - t.Start >= t.Life);
        foreach (var t in _transients)
        {
            double p = Math.Clamp((now - t.Start) / t.Life, 0, 1);
            switch (t.Kind)
            {
                case FieldFxKind.Ring:
                {
                    double s = 0.05 + 0.95 * (t.Eased ? EaseOutCubic(p) : p);
                    // WPF scaled the whole ellipse, stroke included; the stroke sits inside the bounds.
                    into.Add(new FieldFxPrim(FieldFxKind.Ring, t.X, t.Y, 0, 0,
                        (t.R - t.Thickness / 2) * s, t.Thickness * s, t.FromOpacity * (1 - p), t.Color));
                    break;
                }
                case FieldFxKind.Shard:
                {
                    double d = t.R * (0.2 + 0.65 * EaseOutCubic(p));
                    double x1 = t.X + t.Ux * d, y1 = t.Y + t.Uy * d;
                    into.Add(new FieldFxPrim(FieldFxKind.Shard, x1, y1, x1 + t.Ux * t.R * 0.16, y1 + t.Uy * t.R * 0.16,
                        0, t.Thickness, t.FromOpacity * (1 - p), t.Color));
                    break;
                }
                case FieldFxKind.Residue:
                    into.Add(new FieldFxPrim(FieldFxKind.Residue, t.X, t.Y, 0, 0, t.R, 0,
                        ResidueOpacity(t.Flicker!, now - t.Start, t.Life), t.Color));
                    break;
            }
        }

        foreach (var d in _dots)
        {
            if (!d.Used) continue;
            double p = (now - d.Start) / d.Life;
            if (p >= 1 || p < 0) continue;
            double s = 1.1 + (0.3 - 1.1) * p;
            into.Add(new FieldFxPrim(FieldFxKind.TrailDot, d.X, d.Y, 0, 0, TrailDotSize / 2 * s, 0,
                0.65 * (1 - p), d.Warm ? WarmTrailColor : TrailColor, d.Warm));
        }
    }

    /// <summary>The Bound's elastic read: the further it stretches, the thinner the thread.</summary>
    public static double TetherThickness(double dist) => Math.Clamp(5.0 - dist / 250.0, 1.5, 5.0);

    public static double EaseOutCubic(double p) { double q = 1 - p; return 1 - q * q * q; }

    /// <summary>WPF's keyframes: discrete value i held from i*90 ms; after the last one a linear ramp to 0 at life.</summary>
    public static double ResidueOpacity(double[] flicker, double elapsed, double life)
    {
        int last = flicker.Length - 1;
        double lastAt = Math.Min(last * 90.0, life);
        if (elapsed < lastAt) return flicker[Math.Clamp((int)(elapsed / 90), 0, last)];
        double span = life - lastAt;
        return span <= 0 ? 0 : flicker[last] * Math.Max(0, 1 - (elapsed - lastAt) / span);
    }

    private void AddRing(double x, double y, double r, double lifeMs, double now, FieldFxColor c,
        double thickness, double fromOpacity, bool eased) =>
        _transients.Add(new Transient
        {
            Kind = FieldFxKind.Ring, X = x, Y = y, R = r, Start = now, Life = Math.Max(1, lifeMs),
            Color = c, Thickness = thickness, FromOpacity = fromOpacity, Eased = eased,
        });
}
