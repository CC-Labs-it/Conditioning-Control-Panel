// Motion budgets from WPF 80a8cdfc9. No UI types: both heads can use these poses.
using System;
namespace ConditioningControlPanel.Services.Billboard;
public static class PosterMotion
{
    public const double BaseScale = 1.034;
    /// <summary>The Ken Burns breath on top of the floor (settled max 1.039).</summary>
    public const double BreathScale = 0.005;
    /// <summary>The landing pop's extra zoom at its peak, gone in <see cref="LandSeconds"/>.</summary>
    public const double LandKick = 0.010;
    public const double LandSeconds = 0.62;
    /// <summary>Slow pan, as a fraction of the card's width and height.</summary>
    public const double PanX = 0.0035, PanY = 0.0025;
    /// <summary>Pointer parallax, as a fraction of width and height at full hover.</summary>
    public const double ParallaxX = 0.005, ParallaxY = 0.004;
    /// <summary>The lean toward the pointer and the idle sway, in degrees, before the aspect cap.</summary>
    public const double LeanDegrees = 0.45, SwayDegrees = 0.12;
    /// <summary>How far a lean may move a far corner, as a fraction of the short axis.</summary>
    public const double LeanShiftCap = 0.006;

    public readonly record struct Pose(double Scale, double X, double Y, double SkewX, double SkewY);

    /// <summary>The still pose (Motion Off, ambient loops off, never played).</summary>
    public static Pose Still => new(BaseScale, 0, 0, 0, 0);

    /// <summary>The landing pop's zoom at <paramref name="seconds"/> after the card landed:
    /// up to the kick, a small dip past rest, then rest.</summary>
    public static double Land(double seconds)
    {
        if (!(seconds >= 0) || seconds >= LandSeconds) return 0;
        double u = seconds / LandSeconds;
        // A damped spring: the kick at the start, a small dip past rest near two thirds, 0 at the end.
        return LandKick * Math.Cos(u * Math.PI * 1.5) * (1 - u) * (1 - u);
    }

    /// <summary>
    /// The pose at clock <paramref name="phase"/> (radians), pointer <paramref name="pointer"/>
    /// (-1..1 each axis, already eased), hover <paramref name="energy"/> (0..1) and
    /// <paramref name="land"/> (extra zoom from <see cref="Land"/>), for a card w x h.
    /// </summary>
    public static Pose At(double phase, (double X, double Y) pointer, double energy, double land, double w, double h)
    {
        if (!double.IsFinite(phase)) phase = 0;
        energy = double.IsFinite(energy) ? Math.Clamp(energy, 0, 1) : 0;
        double px = double.IsFinite(pointer.X) ? Math.Clamp(pointer.X, -1, 1) : 0;
        double py = double.IsFinite(pointer.Y) ? Math.Clamp(pointer.Y, -1, 1) : 0;
        w = Math.Max(1, w); h = Math.Max(1, h);

        double scale = BaseScale + BreathScale * 0.5 * (1 - Math.Cos(phase)) + Math.Clamp(land, -LandKick, LandKick);
        double x = PanX * w * Math.Sin(phase * 0.7 + 0.4) - ParallaxX * w * px * energy;
        double y = PanY * h * Math.Sin(phase * 0.53 + 1.1) - ParallaxY * h * py * energy;

        // The lean: toward the pointer on hover, a slow sway at rest. A lean of a degrees moves a far
        // corner by half the OTHER axis times tan(a), so the cap follows the card's aspect.
        double capY = Math.Atan(LeanShiftCap * 2 * h / w) * 180 / Math.PI;
        double capX = Math.Atan(LeanShiftCap * 2 * w / h) * 180 / Math.PI;
        double skewY = Math.Clamp(-px * energy * LeanDegrees + SwayDegrees * Math.Sin(phase * 0.9), -capY, capY);
        double skewX = Math.Clamp(py * energy * LeanDegrees * 0.7 + SwayDegrees * 0.6 * Math.Sin(phase * 0.6 + 2), -capX, capX);
        return EnsureCover(new Pose(scale, x, y, skewX, skewY), w, h);
    }


    // Affine coefficients about the card centre, in row-vector order.
    public static (double A, double B, double C, double D, double X, double Y) Matrix(Pose p, double w, double h)
    {
        double a = p.Scale, b = Math.Tan(p.SkewY * Math.PI / 180) * a;
        double c = Math.Tan(p.SkewX * Math.PI / 180) * a, d = a;
        return (a, b, c, d, w / 2 + p.X - w / 2 * a - h / 2 * c,
            h / 2 + p.Y - w / 2 * b - h / 2 * d);
    }
    public static bool Covers(Pose p, double w, double h)
    {
        var m = Matrix(p, w, h);
        double det = m.A * m.D - m.B * m.C;
        if (!(det > 0)) return false;
        foreach (var (x, y) in new[] { (0d, 0d), (w, 0d), (0d, h), (w, h) })
        {
            double sx = ((x - m.X) * m.D - (y - m.Y) * m.C) / det;
            double sy = ((y - m.Y) * m.A - (x - m.X) * m.B) / det;
            if (sx < -1e-6 || sy < -1e-6 || sx > w + 1e-6 || sy > h + 1e-6) return false;
        }
        return true;
    }
    public static Pose EnsureCover(Pose p, double w, double h)
    {
        for (int i = 0; i < 40 && !Covers(p, w, h); i++) p = p with { Scale = p.Scale + 0.001 };
        return p;
    }
}
