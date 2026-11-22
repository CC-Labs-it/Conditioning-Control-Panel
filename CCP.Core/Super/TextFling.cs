namespace ConditioningControlPanel.SuperCore;

/// <summary>Where one bouncing word is in its trip through the pool.</summary>
public struct FlingState
{
    /// <summary>0 free, 1 caught and turning, 2 thrown and slowing back to cruise.</summary>
    public int Phase;
    public double T;
    public double Theta;
    public double R;
    /// <summary>Seconds left in which the pool cannot catch this word again.</summary>
    public double Immune;
    /// <summary>How small to draw the word right now, 0.4..1.</summary>
    public double Scale;
}

/// <summary>
/// The bouncing word against the Whirlpool: it bends toward the pool, gets caught if it comes
/// close, turns around the eye, and is thrown back out faster than it came (the Breakout dome).
/// Pure maths: the caller owns the word's position and velocity and its wall bounces.
/// </summary>
public static class TextFling
{
    /// <summary>Caught inside this share of the pool's reach.</summary>
    public const double CatchShare = 0.3;
    /// <summary>The pool must be at least this big to catch a word.</summary>
    public const double CatchSize = 0.3;
    public const double OrbitSeconds = 1.5;
    public const double Turns = 1.5;
    /// <summary>Speed when thrown, as a multiple of cruise speed.</summary>
    public const double ThrowBoost = 2.6;
    public const double SlowSeconds = 1.3;
    public const double ImmuneSeconds = 3.5;
    /// <summary>Share of the pool's pull that bends a free word.</summary>
    public const double Bend = 0.45;

    /// <summary>
    /// One frame for one word. (x, y) is the word's centre. Returns true on the frame it is thrown.
    /// While caught the caller must NOT run its own motion or wall bounces: this moves the word.
    /// </summary>
    public static bool Step(ref FlingState s, SuperStage stage, ref double x, ref double y, ref double vx, ref double vy, double cruise, double dt)
    {
        if (s.Scale <= 0) s.Scale = 1;
        if (s.Immune > 0) s.Immune -= dt;
        var pool = stage.Pool;
        switch (s.Phase)
        {
            case 1:
            {
                // The pool went, or the player just switched words off: let it go at once.
                if (!stage.PoolTakesWords) { Throw(ref s, pool, x, y, ref vx, ref vy, cruise, 1.2); return true; }
                s.T += dt;
                double u = Math.Clamp(s.T / OrbitSeconds, 0, 1), dip = Math.Sin(Math.PI * u);
                s.Theta += dt * (2 * Math.PI * Turns / OrbitSeconds) * (0.6 + 0.8 * dip);
                double r = s.R * (1 - 0.8 * dip);
                x = pool.X + Math.Cos(s.Theta) * r; y = pool.Y + Math.Sin(s.Theta) * r;
                s.Scale = 1 - 0.6 * dip;
                if (u < 1) return false;
                Throw(ref s, pool, x, y, ref vx, ref vy, cruise, ThrowBoost);
                return true;
            }
            case 2:
            {
                s.T += dt;
                double want = cruise * (1 + (ThrowBoost - 1) * Math.Exp(-s.T * 3 / SlowSeconds));
                SetSpeed(ref vx, ref vy, want);
                s.Scale = 1;
                if (s.T >= SlowSeconds) { s.Phase = 0; SetSpeed(ref vx, ref vy, cruise); }
                return false;
            }
            default:
            {
                s.Scale = 1;
                if (!stage.PoolTakesWords) return false;
                var p = pool.Pull(x, y);
                if (!p.Inside) return false;
                vx += p.Ax * Bend * dt; vy += p.Ay * Bend * dt;
                SetSpeed(ref vx, ref vy, cruise);
                double d = Math.Sqrt((x - pool.X) * (x - pool.X) + (y - pool.Y) * (y - pool.Y));
                if (s.Immune <= 0 && pool.Size >= CatchSize && d < pool.Radius * CatchShare)
                {
                    s.Phase = 1; s.T = 0; s.R = Math.Max(d, 8 * pool.Dip);
                    s.Theta = Math.Atan2(y - pool.Y, x - pool.X);
                }
                return false;
            }
        }
    }

    private static void Throw(ref FlingState s, Whirlpool pool, double x, double y, ref double vx, ref double vy, double cruise, double boost)
    {
        // Out along the turn, leaning away from the eye, so it always leaves.
        double tx = -Math.Sin(s.Theta), ty = Math.Cos(s.Theta), ox = Math.Cos(s.Theta), oy = Math.Sin(s.Theta);
        vx = tx * 0.75 + ox * 0.66; vy = ty * 0.75 + oy * 0.66;
        SetSpeed(ref vx, ref vy, cruise * boost);
        s.Phase = 2; s.T = 0; s.Immune = ImmuneSeconds; s.Scale = 1;
    }

    private static void SetSpeed(ref double vx, ref double vy, double speed)
    {
        double n = Math.Sqrt(vx * vx + vy * vy);
        if (n < 1e-6) { vx = speed; vy = 0; return; }
        vx = vx / n * speed; vy = vy / n * speed;
    }
}
