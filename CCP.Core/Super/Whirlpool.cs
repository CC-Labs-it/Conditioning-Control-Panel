namespace ConditioningControlPanel.SuperCore;

/// <summary>What the pool does to one thing this tick.</summary>
public readonly record struct PoolPull(double Ax, double Ay, double Scale, bool Eaten, bool Inside);

/// <summary>
/// The Whirlpool: a spiral that sits under the cursor, grows while the mouse rests and shrinks
/// while it moves. Everything is in device pixels of the virtual desktop; <c>dip</c> is the
/// pixels-per-DIP of the monitor the pool is on, so the tuning below reads in DIPs.
/// No WPF, no Skia, no clock of its own: the head calls <see cref="Step"/> once a frame.
/// </summary>
public sealed class Whirlpool
{
    /// <summary>Seconds of rest from nothing to full size (owner: slow, it should creep up).</summary>
    public const double GrowSeconds = 14;
    /// <summary>Seconds from full size to nothing while the mouse keeps moving.</summary>
    public const double ShrinkSeconds = 6;
    /// <summary>Cursor speed (DIP/s) under which the mouse counts as resting.</summary>
    public const double RestSpeed = 45;
    /// <summary>Below this size the pool pulls nothing and eats nothing.</summary>
    public const double LiveSize = 0.06;
    /// <summary>Reach at size 0 and at size 1, as a share of the monitor's short side.</summary>
    public const double MinReach = 0.05, MaxReach = 0.42;
    /// <summary>How far in something must get to be eaten, in DIPs.</summary>
    public const double EatRadius = 18;
    /// <summary>Pull at the centre, DIP/s2, at size 0 and at size 1.</summary>
    public const double PullMin = 320, PullMax = 1500;
    /// <summary>Share of the pull that goes sideways (the swirl).</summary>
    public const double Swirl = 0.75;
    /// <summary>How quickly the pool catches up with the cursor (1/s). Low on purpose: it trails.</summary>
    public const double Follow = 5;

    public double X { get; private set; }
    public double Y { get; private set; }
    /// <summary>0..1, eased by <see cref="Eased"/> before it drives anything visible.</summary>
    public double Size { get; private set; }
    /// <summary>Turn of the drawn spiral, radians, always growing.</summary>
    public double Angle { get; private set; }
    public double Dip { get; private set; } = 1;
    public double ShortSide { get; private set; } = 1080;
    private bool _placed;

    public bool Live => Size > LiveSize;
    public double Eased => Ease(Size);
    /// <summary>The player's "Pool size" as a share of the stock reach (<see cref="WhirlpoolOptions.ReachScale"/>). 1 = as shipped.</summary>
    public double ReachScale { get; set; } = 1;
    /// <summary>Reach in pixels: nothing outside it feels the pool.</summary>
    public double Radius => (MinReach + (MaxReach - MinReach) * Eased) * ShortSide * ReachScale;
    /// <summary>
    /// How far an arm strays from the perfect spiral, in radians, at <paramref name="share"/> of the
    /// reach (0 = the eye, 1 = the rim) and in the direction <paramref name="phi"/>. It drifts with
    /// <paramref name="angle"/>, so the arms keep morphing, and it differs by direction, so the
    /// three arms are never copies of each other. The drawn arms and the fog arms both use it, so they stay on top of each other.
    /// </summary>
    public static double Wobble(double share, double phi, double angle)
    {
        double s = Math.Clamp(share, 0, 1);
        return Math.Pow(s, 0.6) * (0.4 * Math.Sin(s * 6.5 + angle * 0.13) + 0.13 * Math.Sin(s * 11 - angle * 0.19 + 1.7)
                    + 0.34 * Math.Sin(2 * phi + s * 4 - angle * 0.09) + 0.22 * Math.Sin(phi - s * 3 + angle * 0.14 + 2));
    }

    /// <summary>Turns of each arm from the eye to the rim. Few on purpose: a tight spiral reads as rings.</summary>
    public double Turns => 0.9 + 1.2 * Eased;
    /// <summary>Radians per second the drawn spiral (and the fog under it) turns.</summary>
    public double Spin => 0.9 + 1.9 * Eased;

    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
    }

    /// <summary>One frame. <paramref name="cursorSpeedPx"/> is px/s; <paramref name="gentle"/>
    /// (photosensitivity switch) halves the spin and never the stop.</summary>
    public void Step(double dt, double cursorX, double cursorY, double cursorSpeedPx, double dip, double shortSidePx, bool gentle = false)
    {
        if (dt <= 0) return;
        Dip = dip <= 0 ? 1 : dip;
        ShortSide = Math.Max(200, shortSidePx);
        if (!_placed) { X = cursorX; Y = cursorY; _placed = true; }
        double k = 1 - Math.Exp(-Follow * dt);
        X += (cursorX - X) * k;
        Y += (cursorY - Y) * k;
        double speedDip = cursorSpeedPx / Dip;
        if (speedDip < RestSpeed) Size += dt / GrowSeconds;
        else Size -= dt / ShrinkSeconds * Math.Clamp(speedDip / 500.0, 0.35, 1.6);
        Size = Math.Clamp(Size, 0, 1);
        Angle += dt * Spin * (gentle ? 0.5 : 1);
        if (Angle > 1e6) Angle -= 1e6;
    }

    /// <summary>Drop to nothing at once (panic, switch off, base feature off).</summary>
    public void Reset() { Size = 0; _placed = false; }

    /// <summary>
    /// The pull on a thing at (x, y). Heavier things (mass above 1) move less. Scale is how small
    /// to draw it: 1 at the rim, about 0.14 at the eye. Eaten is true once it reaches the eye.
    /// </summary>
    public PoolPull Pull(double x, double y, double mass = 1)
    {
        if (!Live) return new PoolPull(0, 0, 1, false, false);
        double dx = X - x, dy = Y - y, d = Math.Sqrt(dx * dx + dy * dy), r = Radius;
        if (d >= r) return new PoolPull(0, 0, 1, false, false);
        if (d < 1e-6) return new PoolPull(0, 0, 0.14, Size > 0.15, true);
        double e = Eased, f = (1 - d / r) * (PullMin + (PullMax - PullMin) * e) * Dip / Math.Max(0.2, mass);
        double nx = dx / d, ny = dy / d;
        double ax = nx * f - ny * f * Swirl, ay = ny * f + nx * f * Swirl;
        double scale = Math.Clamp(d / (r * 0.5), 0.14, 1);
        bool eaten = Size > 0.15 && d < EatRadius * Dip;
        return new PoolPull(ax, ay, scale, eaten, true);
    }
}
