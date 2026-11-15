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
        => MergedRadius(ar, br, freshRadius, MaxGrow);

    /// <summary>The same, with the player's own size cap (<see cref="FrothOptions.ClampMaxSize"/>). Size never changes pay.</summary>
    public static double MergedRadius(double ar, double br, double freshRadius, double maxGrow)
        => Math.Min(freshRadius * maxGrow, Math.Sqrt(ar * ar + br * br));

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

    /// <summary>The glass of a bubble is this share of its sprite's side (the art has a soft margin).</summary>
    public const double GlassOfSprite = 0.45;
    /// <summary>The hole a pop leaves in the fog, in radii of the bubble.</summary>
    public const double HoleRadii = 2.2;
    /// <summary>The cursor pushes a bubble from this far off its rim (the head's length unit, DIPs).</summary>
    public const double NudgeReach = 60;
    /// <summary>The push at the rim on a fresh bubble, length units per second squared.</summary>
    public const double NudgePush = 900;
    /// <summary>How fast a pushed bubble loses the push, per second.</summary>
    public const double Drag = 0.9;
    /// <summary>How fast a bubble inside the pool loses its own drift, per second.</summary>
    public const double PoolDrag = 1.6;
    /// <summary>How fast the drift comes back once the pool lets go, per second.</summary>
    public const double DriftBack = 0.6;
    /// <summary>The merge wobble lasts about this long, seconds.</summary>
    public const double WobbleSeconds = 0.62;

    /// <summary>A pop reaches a bubble when the ring touches its rim.</summary>
    public static bool InReach(double distance, double reach, double r) => reach > 0 && distance < reach + r;

    /// <summary>Is any of the bubble under the box (the bouncing word).</summary>
    public static bool UnderBox(double x, double y, double r, double left, double top, double right, double bottom)
    {
        double nx = Math.Clamp(x, Math.Min(left, right), Math.Max(left, right));
        double ny = Math.Clamp(y, Math.Min(top, bottom), Math.Max(top, bottom));
        double dx = x - nx, dy = y - ny;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>What the pool is told a bubble weighs: big ones are heavier.</summary>
    public static double Mass(double r, double freshRadius)
        => Math.Pow(Math.Max(0.05, r / Math.Max(1e-6, freshRadius)), 1.2);

    /// <summary>
    /// How hard the cursor pushes a bubble whose rim is <paramref name="gap"/> away (0 when the
    /// cursor is inside the bubble or out of reach). Lighter bubbles move more.
    /// </summary>
    public static double NudgeAccel(double gap, double r, double freshRadius)
    {
        if (gap <= 0 || gap >= NudgeReach) return 0;
        double heavy = Math.Pow(Math.Max(0.2, r / Math.Max(1e-6, freshRadius)), 1.6);
        return (1 - gap / NudgeReach) * NudgePush / heavy;
    }

    /// <summary>
    /// The squash of a bubble that has just merged: 1 is round, and the head draws it
    /// <c>k</c> wide and <c>2 - k</c> tall. <paramref name="wobble"/> runs 1 to 0. Gentle keeps
    /// the wobble, slower and shallower, so nothing snaps.
    /// </summary>
    public static double Squash(double wobble, double t, bool gentle)
    {
        if (wobble <= 0) return 1;
        return gentle ? 1 + wobble * 0.05 * Math.Sin(t * 7) : 1 + wobble * 0.12 * Math.Sin(t * 26);
    }

    /// <summary>
    /// One step of a size easing toward its target. The time constant is long enough that a
    /// merged bubble takes over 300 ms to reach its size; Gentle doubles it.
    /// </summary>
    public static double EaseSize(double shown, double target, double dt, bool gentle)
        => shown + (target - shown) * Math.Min(1, Math.Max(0, dt) / (gentle ? 0.30 : 0.15));

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
