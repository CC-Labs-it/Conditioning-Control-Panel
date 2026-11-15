namespace ConditioningControlPanel.SuperCore;

/// <summary>Who caused a blast. Effects use it to ignore their own echo where they must.</summary>
public enum BlastKind { CardBreak, BubblePop, RipePop, GoldCorner }

/// <summary>
/// Something went off at a point of the virtual desktop (device pixels). <c>ReachPx</c> is how far
/// its ring travels and knocks things (0 = it knocks nothing); <c>HolePx</c> is the hole it leaves
/// in the fog.
/// </summary>
public readonly record struct Blast(double X, double Y, double ReachPx, double HolePx, BlastKind Kind);

/// <summary>
/// The one place the Super effects meet. Each effect keeps its own things (pictures, bubbles, the
/// bouncing word) and asks the stage two questions: what is the pool doing to me, and did anything
/// go off near me. Each effect tells the stage one thing: something of mine went off here.
/// The fog lives here because every effect marks it.
/// One instance per app. UI thread only. No WPF, no Skia.
/// </summary>
public sealed class SuperStage
{
    public Whirlpool Pool { get; } = new();
    /// <summary>The Whirlpool star is on and its base (the spiral) is running.</summary>
    public bool PoolOn { get; set; }
    /// <summary>The Steam star is on and its base (the pink filter) is running.</summary>
    public bool SteamOn { get; set; }

    /// <summary>Raised on the UI thread for every blast, after the fog took its hole.</summary>
    public event Action<Blast>? Blasted;

    /// <summary>
    /// Raised when a moving thing (the bouncing word) covers a box of the desktop this frame:
    /// left, top, right, bottom in device pixels. Bubbles under it pop; pictures under it are knocked.
    /// </summary>
    public event Action<double, double, double, double>? Swept;

    /// <summary>The bouncing word is here this frame. Cheap when nobody listens.</summary>
    public void Sweep(double left, double top, double right, double bottom) => Swept?.Invoke(left, top, right, bottom);

    private sealed class Mon { public double X, Y, W, H; public SteamField Field = null!; }
    private readonly Dictionary<string, Mon> _mons = new(StringComparer.Ordinal);

    /// <summary>True when the pool is on and big enough to act.</summary>
    public bool PoolLive => PoolOn && Pool.Live;

    /// <summary>What the pool does to a thing at a world point. Nothing while it is off.</summary>
    public PoolPull Pull(double x, double y, double mass = 1)
        => PoolLive ? Pool.Pull(x, y, mass) : new PoolPull(0, 0, 1, false, false);

    /// <summary>
    /// The fog field of one monitor, made (or remade after a resolution change) on first ask.
    /// Bounds are that monitor's rectangle in virtual-desktop device pixels.
    /// </summary>
    public SteamField Field(string monitorKey, double x, double y, double w, double h)
    {
        if (_mons.TryGetValue(monitorKey, out var m) && m.W == w && m.H == h)
        {
            m.X = x; m.Y = y;
            return m.Field;
        }
        m = new Mon { X = x, Y = y, W = w, H = h, Field = new SteamField(w, h) };
        _mons[monitorKey] = m;
        return m.Field;
    }

    /// <summary>Forget every field (Steam switched off, panic). The next ask starts clean.</summary>
    public void DropFields() => _mons.Clear();

    /// <summary>Something went off. The fog takes the hole, then every listener hears it.</summary>
    public void Blast(Blast b)
    {
        if (SteamOn && b.HolePx > 0)
            foreach (var m in _mons.Values) m.Field.Hole(b.X - m.X, b.Y - m.Y, b.HolePx);
        Blasted?.Invoke(b);
    }

    /// <summary>A slow wipe at a world point (the cursor, the bouncing word).</summary>
    public void Wipe(double x, double y, double radiusPx, double dt, bool mark)
    {
        if (!SteamOn) return;
        foreach (var m in _mons.Values)
            if (x > m.X - radiusPx && x < m.X + m.W + radiusPx && y > m.Y - radiusPx && y < m.Y + m.H + radiusPx)
                m.Field.Wipe(x - m.X, y - m.Y, radiusPx, dt, mark);
    }

    /// <summary>A band of the screen the fog should write words into when it next covers it.</summary>
    public void Remember(double x0, double y0, double x1, double y1)
    {
        if (!SteamOn) return;
        foreach (var m in _mons.Values) m.Field.Remember(x0 - m.X, y0 - m.Y, x1 - m.X, y1 - m.Y);
    }

    /// <summary>One frame of fog on every monitor: the creep, the heal, and the pool's swirl.</summary>
    public void StepSteam(double dt, double level, double cap)
    {
        if (!SteamOn) return;
        foreach (var m in _mons.Values)
        {
            m.Field.Step(dt, level, cap);
            if (PoolLive)
                m.Field.Swirl(Pool.X - m.X, Pool.Y - m.Y, Pool.Radius, Pool.Eased, Pool.Angle, Pool.Spin, Pool.Turns, dt, cap);
        }
    }

    /// <summary>Everything off at once (panic, sign-out, the gate closing).</summary>
    public void Reset()
    {
        Pool.Reset();
        DropFields();
    }
}
