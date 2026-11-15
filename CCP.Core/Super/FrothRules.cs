namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Froth: bubbles that merge when they touch, turn ripe when they are big, and take their
/// neighbours with them when they pop. Pure rules; the head owns the bubbles.
/// Sizes are radii in whatever unit the head uses, as long as it is the same everywhere.
/// </summary>
public static class FrothRules
{
    /// <summary>Two bubbles merge when their centres are closer than this share of their radii added.</summary>
    public const double TouchShare = 0.82;
    /// <summary>A merged bubble never grows past this many times the size of a fresh one.</summary>
    public const double MaxGrow = 4.5;
    /// <summary>Ripe (gold rim) from this many times the size of a fresh one.</summary>
    public const double RipeGrow = 2.4;
    /// <summary>A pop reaches its neighbours from this many times the size of a fresh one.</summary>
    public const double ChainGrow = 1.4;
    /// <summary>How far a pop reaches, in radii of the bubble that popped.</summary>
    public const double ReachRadii = 3.2;
    /// <summary>The ring travels this many radii of a fresh bubble per second.</summary>
    public const double RingSpeed = 22;
    /// <summary>Most pictures a merged bubble shows floating inside it.</summary>
    public const int MaxPictures = 4;

    public static bool Touching(double ax, double ay, double ar, double bx, double by, double br)
    {
        double dx = ax - bx, dy = ay - by, t = (ar + br) * TouchShare;
        return dx * dx + dy * dy < t * t;
    }

    /// <summary>Areas add, so two equal bubbles make one about 1.41 times as wide.</summary>
    public static double MergedRadius(double ar, double br, double freshRadius)
        => Math.Min(freshRadius * MaxGrow, Math.Sqrt(ar * ar + br * br));

    /// <summary>The merged bubble sits at the area-weighted middle of the two.</summary>
    public static (double X, double Y) MergedCentre(double ax, double ay, double ar, double bx, double by, double br)
    {
        double a = ar * ar, b = br * br, t = a + b;
        return t <= 0 ? (ax, ay) : ((ax * a + bx * b) / t, (ay * a + by * b) / t);
    }

    /// <summary>Owner rule (2026-11-15): a merged bubble pays the sum of its parts, never a bonus.</summary>
    public static int MergedPayout(int a, int b) => a + b;

    public static bool Ripe(double r, double freshRadius) => r >= freshRadius * RipeGrow;
    public static bool Chains(double r, double freshRadius) => r >= freshRadius * ChainGrow;
    public static double Reach(double r) => r * ReachRadii;
    /// <summary>Seconds before a neighbour at <paramref name="distance"/> goes off.</summary>
    public static double ChainDelay(double distance, double freshRadius)
        => Math.Max(0.03, distance / Math.Max(1, freshRadius * RingSpeed));

    /// <summary>
    /// Where picture <paramref name="index"/> of <paramref name="count"/> floats inside a bubble
    /// at time <paramref name="t"/>, as an offset in radii from the centre, and how big it is in
    /// radii. One picture sits in the middle; more than one circle each other slowly.
    /// </summary>
    public static (double Dx, double Dy, double Size) PictureSlot(int index, int count, double t)
    {
        count = Math.Clamp(count, 1, MaxPictures);
        if (count == 1) return (0, 0, 1.25);
        double a = t * 0.7 + index * 2 * Math.PI / count, orbit = count == 2 ? 0.36 : 0.42;
        double size = count == 2 ? 0.8 : count == 3 ? 0.66 : 0.58;
        return (Math.Cos(a) * orbit, Math.Sin(a) * orbit * 0.85, size);
    }
}
