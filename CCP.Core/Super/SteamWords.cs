namespace ConditioningControlPanel.SuperCore;

/// <summary>Where a fog word is in its life.</summary>
public enum SteamWordPhase { Hidden, In, Hold, Out }

/// <summary>
/// One place a word can show in the fog. The place is fixed (shares of the monitor); the word in
/// it changes every time it comes back. The head reads <see cref="Alpha"/>, <see cref="Write"/>
/// and <see cref="Melt"/> and draws; it decides nothing.
/// </summary>
public sealed class SteamWordSlot
{
    /// <summary>Centre, as a share of the monitor's width and height.</summary>
    public double X, Y;
    /// <summary>The box the word is fitted into, as shares of the monitor's width and height
    /// (already turned: a side slot is tall and narrow).</summary>
    public double Wide, High;
    /// <summary>Degrees the word is turned. The side slots read up and down.</summary>
    public double Turn;
    /// <summary>True for the middle slot, which carries the finished Afterglow line.</summary>
    public bool IsLine;

    public string Text { get; internal set; } = "";
    /// <summary>Goes up every time the slot takes a word, so the head knows to lay it out again.</summary>
    public int Serial { get; internal set; }
    public SteamWordPhase Phase { get; internal set; }
    /// <summary>Seconds into the phase.</summary>
    public double Age { get; internal set; }
    /// <summary>How visible the word is, 0..1. Rises through In, 1 in Hold, falls through Out.</summary>
    public double Alpha { get; internal set; }
    /// <summary>The write-on, 0..1: how far across the word the finger has got. 1 once it is in.</summary>
    public double Write { get; internal set; }
    /// <summary>The dissolve, 0..1: 0 until Out, then how far the word has melted back into the fog.</summary>
    public double Melt { get; internal set; }
    internal double Rest, From = 1;
}

/// <summary>
/// The words in the Steam fog, for ONE monitor: which place shows which word, and for how long.
/// A place wakes when enough of it has been wiped and the fog has come back over it
/// (<see cref="SteamField.Mem"/> times <see cref="SteamField.Fog"/>). Its word writes in, holds a
/// few seconds, melts away, and the place is forgotten: it shows nothing until it is wiped and
/// fogged over again, and then it takes a new word. No word stays on screen.
/// No WPF, no Skia. Reads the field; writes only into <see cref="SteamField.Mem"/>, to forget.
/// </summary>
public sealed class SteamWords
{
    /// <summary>Seconds a word takes to write in.</summary>
    public const double InSeconds = 1.2;
    /// <summary>Seconds a word stays once it is in.</summary>
    public const double HoldSeconds = 4.5;
    /// <summary>Seconds a word takes to melt away.</summary>
    public const double OutSeconds = 2.6;
    /// <summary>Seconds a place stays empty after its word has gone.</summary>
    public const double RestSeconds = 2.5;
    /// <summary>How much of a place must have been wiped before it can wake (0..1).</summary>
    public const double WipedAt = 0.25;
    /// <summary>How far the fog must be back over the wiped part before it wakes (0..1): the
    /// word writes into fog that has closed again, after the finger has gone.</summary>
    public const double FogReturned = 0.8;
    /// <summary>Gentle Visuals stretch the in and the out by this much; the hold is the same.</summary>
    public const double GentleStretch = 1.6;
    /// <summary>The fog counts as back over a spot at this density (matches the old words).</summary>
    public const double FogBack = 1.7;

    private readonly SteamWordSlot[] _slots =
    {
        // Four around the edges, a little off level, the way a finger writes on a steamed mirror.
        new() { X = 0.20, Y = 0.135, Wide = 0.30, High = 0.15, Turn = -3 },
        new() { X = 0.78, Y = 0.875, Wide = 0.30, High = 0.15, Turn = 2 },
        new() { X = 0.82, Y = 0.165, Wide = 0.28, High = 0.15, Turn = 3 },
        new() { X = 0.19, Y = 0.855, Wide = 0.28, High = 0.15, Turn = -2 },
        // Two up the sides.
        new() { X = 0.045, Y = 0.50, Wide = 0.07, High = 0.42, Turn = -90 },
        new() { X = 0.955, Y = 0.52, Wide = 0.07, High = 0.42, Turn = 90 },
        // The middle: the line Afterglow finished.
        new() { X = 0.50, Y = 0.52, Wide = 0.74, High = 0.16, IsLine = true },
    };
    private readonly Random _rng;
    private readonly List<string> _pool = new();
    private SteamField? _field;

    public SteamWords(int? seed = null) => _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    public IReadOnlyList<SteamWordSlot> Slots => _slots;
    /// <summary>The line for the middle slot, or null for none.</summary>
    public string? Line { get; set; }
    /// <summary>True while any word is at all visible.</summary>
    public bool Any { get; private set; }

    /// <summary>The words the edge slots draw from. A word on screen keeps its text.</summary>
    public void SetPool(IEnumerable<string> words)
    {
        _pool.Clear();
        foreach (var w in words)
            if (!string.IsNullOrWhiteSpace(w) && !_pool.Contains(w)) _pool.Add(w);
    }

    public static double InFor(bool gentle) => gentle ? InSeconds * GentleStretch : InSeconds;
    public static double OutFor(bool gentle) => gentle ? OutSeconds * GentleStretch : OutSeconds;

    /// <summary>
    /// Once a frame. <paramref name="open"/> false (the switch is off) wakes no place and sends
    /// any word on screen out the soft way; nothing cuts.
    /// </summary>
    public void Step(SteamField field, double dt, bool gentle, bool open = true)
    {
        if (!ReferenceEquals(field, _field))
        {
            // A new field starts with nothing wiped: nothing can be showing.
            _field = field;
            foreach (var s in _slots) Hide(s, rest: 0);
        }
        if (dt < 0) dt = 0;
        double tin = InFor(gentle), tout = OutFor(gentle);
        bool any = false;
        foreach (var s in _slots)
        {
            switch (s.Phase)
            {
                case SteamWordPhase.Hidden:
                    s.Rest -= dt;
                    if (open && s.Rest <= 0 && Awake(field, s))
                    {
                        string? text = s.IsLine ? Line : Pick(s);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            s.Text = text!; s.Serial++;
                            s.Phase = SteamWordPhase.In; s.Age = 0;
                        }
                    }
                    break;
                case SteamWordPhase.In:
                    s.Age += dt;
                    if (!open) Leave(s);
                    else if (s.Age >= tin) { s.Phase = SteamWordPhase.Hold; s.Age = 0; }
                    break;
                case SteamWordPhase.Hold:
                    s.Age += dt;
                    if (!open || s.Age >= HoldSeconds) Leave(s);
                    break;
                case SteamWordPhase.Out:
                    s.Age += dt;
                    if (s.Age >= tout) { Forget(field, s); Hide(s, RestSeconds); }
                    break;
            }
            switch (s.Phase)
            {
                case SteamWordPhase.In:
                    s.Write = Math.Clamp(s.Age / tin, 0, 1);
                    s.Alpha = Smooth(s.Write); s.Melt = 0;
                    break;
                case SteamWordPhase.Hold:
                    s.Write = 1; s.Alpha = 1; s.Melt = 0;
                    break;
                case SteamWordPhase.Out:
                    s.Melt = Math.Clamp(s.Age / tout, 0, 1);
                    s.Alpha = s.From * (1 - Smooth(s.Melt));
                    break;
                default:
                    s.Alpha = 0; s.Write = 0; s.Melt = 0;
                    break;
            }
            if (s.Alpha > 0.002) any = true;
        }
        Any = any;
    }

    /// <summary>How much of a place has been wiped (0..1), and how much of it is wiped AND fogged over again.</summary>
    public static (double Wiped, double Revealed) Measure(SteamField f, SteamWordSlot s)
    {
        Box(f, s, 0, out int x0, out int y0, out int x1, out int y1);
        double wiped = 0, shown = 0; int n = 0;
        for (int gy = y0; gy <= y1; gy++)
            for (int gx = x0, i = gy * f.W + x0; gx <= x1; gx++, i++, n++)
            {
                wiped += f.Mem[i];
                shown += f.Mem[i] * Math.Clamp(f.Fog[i] * FogBack, 0, 1);
            }
        return n == 0 ? (0, 0) : (wiped / n, shown / n);
    }

    private static bool Awake(SteamField f, SteamWordSlot s)
    {
        var (wiped, shown) = Measure(f, s);
        return wiped >= WipedAt && shown >= wiped * FogReturned;
    }

    private static void Leave(SteamWordSlot s)
    {
        s.From = s.Alpha;   // from wherever it had got to, so a word caught mid-rise never jumps
        s.Phase = SteamWordPhase.Out; s.Age = 0;
    }

    private static void Hide(SteamWordSlot s, double rest)
    {
        s.Phase = SteamWordPhase.Hidden; s.Age = 0; s.Rest = rest; s.From = 1;
        s.Alpha = 0; s.Write = 0; s.Melt = 0;
    }

    /// <summary>The wipe marks under a word go with it, so the place must be wiped again to speak again.</summary>
    private static void Forget(SteamField f, SteamWordSlot s)
    {
        Box(f, s, 2, out int x0, out int y0, out int x1, out int y1);
        for (int gy = y0; gy <= y1; gy++)
            Array.Clear(f.Mem, gy * f.W + x0, x1 - x0 + 1);
    }

    private static void Box(SteamField f, SteamWordSlot s, int pad, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = Math.Clamp((int)((s.X - s.Wide / 2) * f.W) - pad, 0, f.W - 1);
        x1 = Math.Clamp((int)((s.X + s.Wide / 2) * f.W) + pad, 0, f.W - 1);
        y0 = Math.Clamp((int)((s.Y - s.High / 2) * f.H) - pad, 0, f.H - 1);
        y1 = Math.Clamp((int)((s.Y + s.High / 2) * f.H) + pad, 0, f.H - 1);
    }

    /// <summary>A word no other place is showing, and not the one this place showed last, when the pool allows.</summary>
    private string? Pick(SteamWordSlot me)
    {
        if (_pool.Count == 0) return null;
        var free = new List<string>(_pool.Count);
        foreach (var w in _pool)
        {
            if (w == me.Text) continue;
            bool shown = false;
            foreach (var o in _slots)
                if (!ReferenceEquals(o, me) && o.Phase != SteamWordPhase.Hidden && o.Text == w) { shown = true; break; }
            if (!shown) free.Add(w);
        }
        if (free.Count == 0) return _pool[_rng.Next(_pool.Count)];
        return free[_rng.Next(free.Count)];
    }

    private static double Smooth(double x) => x * x * (3 - 2 * x);
}
