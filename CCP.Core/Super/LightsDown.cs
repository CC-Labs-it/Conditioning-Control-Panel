namespace ConditioningControlPanel.SuperCore;

/// <summary>How much motion Lights Down may use (mirrors the app's MotionLevel).</summary>
public enum LightsDownMotion { Full, Reduced, Off }

/// <summary>A rectangle in one overlay's own physical pixels.</summary>
public readonly record struct LightsDownRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

/// <summary>One clear hole in the dim: a floating video (with its halo) or a live attention target (no halo).
/// <paramref name="Presence"/> is 0..1, already eased.</summary>
public readonly record struct LightsDownHole(LightsDownRect Rect, double Presence, bool Halo);

/// <summary>
/// Everything one frame of the drawing needs, all already scaled by level, options and motion.
/// The dim follows each pixel's depth from the screen edge (<see cref="LightsDownCanvas"/>):
/// full where the depth is under <c>Front - Feather</c>, nothing past <c>Front</c>.
/// </summary>
public readonly record struct LightsDownLook(double Alpha, double Front, double Feather, uint HaloRgb)
{
    public static readonly LightsDownLook None = new(0, 0, 1, 0);
    public bool Visible => Alpha > 0.001;
}

/// <summary>The small pure rules around the effect.</summary>
public static class LightsDownRules
{
    /// <summary>The mouse moved since the last sample by more than a hand tremor.</summary>
    public const double MoveThresholdPx = 3;

    public static bool Moved(double lastX, double lastY, double x, double y)
    {
        if (double.IsNaN(lastX) || double.IsNaN(lastY)) return false;
        double dx = x - lastX, dy = y - lastY;
        return dx * dx + dy * dy > MoveThresholdPx * MoveThresholdPx;
    }

    /// <summary>
    /// The eyes left the video. Only ever true while the camera is ALREADY running (Lights Down never
    /// switches it on): the face is lost, or the calibrated gaze is fresh and off every video screen.
    /// </summary>
    public static bool LookedAway(bool cameraRunning, bool faceLost, bool gazeLive, bool gazeOnVideo)
        => cameraRunning && (faceLost || (gazeLive && !gazeOnVideo));

    /// <summary>May Lights Down draw at all. A video that is not playing, or is mid teardown, ends it
    /// on the same tick: never more permissive than the video it rides (panic tears that down).</summary>
    public static bool ShouldRun(bool superOn, bool videoPlaying, bool videoCleaningUp, bool hasVideoWindows)
        => superOn && videoPlaying && !videoCleaningUp && hasVideoWindows;

    /// <summary>How far the shapes may travel: 1 Full, 0.5 Reduced, 0 Off (Off only fades).</summary>
    public static double Travel(LightsDownMotion m) => m switch
    {
        LightsDownMotion.Off => 0,
        LightsDownMotion.Reduced => 0.5,
        _ => 1,
    };

    /// <summary>Smoothstep 0..1.</summary>
    public static double Ease(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    public static double SmoothStep(double e0, double e1, double x)
    {
        if (e1 <= e0) return x < e0 ? 0 : 1;
        return Ease((x - e0) / (e1 - e0));
    }
}

/// <summary>
/// Full screen: a small dim at the screen edges. While the mouse rests it eases in; the moment the
/// mouse moves, or the eyes leave, it pulls back toward the edges. Pure and stepped by the caller.
/// </summary>
public sealed class LightsDownFullScreen
{
    /// <summary>The mouse must rest this long before the dim comes back.</summary>
    public const double RestSeconds = 1.2;
    /// <summary>How deep the band reaches at full level, as a share of the screen's short side.</summary>
    public const double Band = 0.15;
    /// <summary>The band's soft inner edge, in bands: wider than the band, so it never reads as a frame.</summary>
    public const double Soft = 1.5;

    private double _rested = RestSeconds;   // the first video starts with a resting mouse

    /// <summary>0..1, linear in time on the way out, exponential on the way in.</summary>
    public double Level { get; private set; }

    /// <summary>Seconds the mouse has rested and the eyes stayed.</summary>
    public double Rested => _rested;

    public void Step(double dt, bool moved, bool away, LightsDownTuning t, double gentleFadeSeconds)
    {
        if (double.IsNaN(dt) || dt < 0) dt = 0;
        dt = Math.Min(dt, 0.25);   // a hitch never jumps the dim
        if (moved || away)
        {
            _rested = 0;
            Level = Math.Max(0, Level - dt * LightsDownOptions.OutRateFor(t, gentleFadeSeconds));
            return;
        }
        _rested += dt;
        if (_rested >= RestSeconds)
            Level += (1 - Level) * (1 - Math.Exp(-dt * t.InRate));
    }

    /// <summary>The look at a level on a screen of short side <paramref name="shortSidePx"/>.</summary>
    public static LightsDownLook Look(double level, LightsDownTuning t, LightsDownMotion motion, double shortSidePx)
    {
        double e = LightsDownRules.Ease(level);
        if (e <= 0 || t.PeakAlpha <= 0) return LightsDownLook.None;
        double travel = LightsDownRules.Travel(motion);
        double band = Band * Math.Max(1, shortSidePx);
        // Retracting = the front slides back into the edge while the soft edge keeps its width,
        // so the dim sinks into the screen edge instead of tightening into a frame. Motion off:
        // the front stays put and only the alpha fades.
        double front = 1.25 * band * (1 - travel * (1 - e));
        return new LightsDownLook(t.PeakAlpha * e, front, Soft * band, 0);
    }
}

/// <summary>
/// Floating videos: the whole screen dims slowly, from the edges in, like a vignette that grows.
/// <see cref="Grow"/> runs 0..1 over <see cref="LightsDownOptions.GrowSeconds"/>; at the run's end
/// <see cref="BeginEnd"/> fades everything out.
/// </summary>
public sealed class LightsDownFloating
{
    /// <summary>The fade out when the last floating video has gone.</summary>
    public const double EndSeconds = 0.6;
    /// <summary>The soft front of the growing dim, as a share of the screen's short side.</summary>
    public const double FeatherShare = 0.45;

    public double Grow { get; private set; }
    public double Fade { get; private set; } = 1;
    public bool Ending { get; private set; }
    public bool Done => Ending && Fade <= 0;

    public void BeginEnd() => Ending = true;

    public void Step(double dt, LightsDownTuning t, double gentleFadeSeconds)
    {
        if (double.IsNaN(dt) || dt < 0) dt = 0;
        dt = Math.Min(dt, 0.25);
        if (Ending)
        {
            double len = gentleFadeSeconds > 0 ? Math.Max(EndSeconds, gentleFadeSeconds) : EndSeconds;
            Fade = Math.Max(0, Fade - dt / len);
            return;
        }
        Grow = Math.Min(1, Grow + dt / LightsDownOptions.GrowSeconds(t));
    }

    /// <summary>
    /// The look at <paramref name="grow"/>: the front walks from the edges (0) past the deepest point
    /// of the screen (half the short side) so the whole screen ends under the dim.
    /// </summary>
    public static LightsDownLook Look(double grow, double fade, LightsDownTuning t, LightsDownMotion motion,
        double shortSidePx, uint haloRgb)
    {
        double g = LightsDownRules.Ease(grow);
        double alpha = t.PeakAlpha * g * LightsDownRules.Ease(fade);
        if (alpha <= 0) return LightsDownLook.None with { HaloRgb = haloRgb };
        double s = Math.Max(1, shortSidePx);
        double feather = FeatherShare * s;
        double travel = LightsDownRules.Travel(motion);
        // Off: the shape is the grown one from the start and only the alpha rises.
        double geo = 1 - travel * (1 - g);
        double front = geo * (s / 2 + feather);
        return new LightsDownLook(alpha, front, feather, haloRgb);
    }
}

/// <summary>
/// The clear holes, keyed by window: a new window's hole eases open, a gone window's hole eases
/// shut where it last was, a moved window's hole follows at once.
/// </summary>
public sealed class LightsDownHoles
{
    public const double InSeconds = 0.45;
    public const double OutSeconds = 0.4;

    private sealed class Slot
    {
        public LightsDownRect Rect;
        public double P;
        public bool Live;
        public bool Halo;
    }

    private readonly Dictionary<long, Slot> _slots = new();
    private readonly List<LightsDownHole> _now = new();

    /// <summary>The holes after the last <see cref="Update"/>, presence eased.</summary>
    public IReadOnlyList<LightsDownHole> Current => _now;

    public int Count => _slots.Count;

    /// <summary>The holes moved, opened, shut or changed in the last <see cref="Update"/>.</summary>
    public bool Changed { get; private set; }

    private readonly List<LightsDownHole> _was = new();

    public void Clear() { _slots.Clear(); _now.Clear(); }

    public void Update(double dt, IReadOnlyList<(long Key, LightsDownRect Rect, bool Halo)> live, double gentleFadeSeconds)
    {
        if (double.IsNaN(dt) || dt < 0) dt = 0;
        dt = Math.Min(dt, 0.25);
        double inLen = gentleFadeSeconds > 0 ? Math.Max(InSeconds, gentleFadeSeconds) : InSeconds;
        double outLen = gentleFadeSeconds > 0 ? Math.Max(OutSeconds, gentleFadeSeconds) : OutSeconds;
        foreach (var s in _slots.Values) s.Live = false;
        foreach (var (key, rect, halo) in live)
        {
            if (rect.W <= 0 || rect.H <= 0) continue;
            if (!_slots.TryGetValue(key, out var s)) _slots[key] = s = new Slot();
            s.Rect = rect;
            s.Live = true;
            s.Halo = halo;
        }
        List<long>? gone = null;
        foreach (var (key, s) in _slots)
        {
            // An attention target (no halo) is cleared at once: the dim never sits on a check.
            s.P = s.Live ? (s.Halo ? Math.Min(1, s.P + dt / inLen) : 1) : Math.Max(0, s.P - dt / outLen);
            if (!s.Live && s.P <= 0) (gone ??= new()).Add(key);
        }
        if (gone != null) foreach (var k in gone) _slots.Remove(k);
        _was.Clear();
        _was.AddRange(_now);
        _now.Clear();
        foreach (var s in _slots.Values)
            if (s.P > 0) _now.Add(new LightsDownHole(s.Rect, LightsDownRules.Ease(s.P), s.Halo));
        bool changed = _was.Count != _now.Count;
        for (int i = 0; !changed && i < _now.Count; i++) changed = _was[i] != _now[i];
        Changed = changed;
    }
}

/// <summary>
/// Draws one frame into a SMALL premultiplied BGRA buffer, one cell per <see cref="Cell"/> physical
/// pixels; the overlay stretches it over the screen with a bilinear filter, which is also what
/// makes every edge fuzzy. Per pixel: the vignette from the depth to the screen edge (cached once),
/// times the union of the holes' clear zones, with the halos (the mod accent) laid over it. Every
/// term is smooth, so where a halo meets the dim there is no seam. Pure (no WPF, no Skia).
/// </summary>
public sealed class LightsDownCanvas
{
    /// <summary>The dim's own colour: nearly black, a breath of violet.</summary>
    public const byte DimR = 3, DimG = 1, DimB = 9;
    /// <summary>The hole's soft edge, from clear (at the window) to the full dim, in DIPs.</summary>
    public const double HoleFeatherDip = 30;
    /// <summary>The halo's falloff outside a window (DIPs, gaussian sigma) and inside it (kept short:
    /// the window covers it, and a glow never sits on the picture if the order ever slips).</summary>
    public const double HaloOutDip = 22, HaloInDip = 10;
    /// <summary>The halo reads as this share of the dim around it, never above <see cref="HaloMax"/>.</summary>
    public const double HaloGain = 0.55, HaloMax = 0.38;   // owner: a SMALL hue; it tints the dim, it never clears it
    /// <summary>The corner radius of a floating window (FloatingFrameLook.CornerDip).</summary>
    public const double CornerDip = 10;
    /// <summary>Attention targets: inflated and feathered less, no halo.</summary>
    public const double TargetPadDip = 14, TargetFeatherDip = 26;
    /// <summary>How soft the depth from the screen edge is, share of the short side (see <see cref="EdgeDepth"/>).</summary>
    public const double EdgeSoftShare = 0.12;
    /// <summary>The small buffer is never wider than about this many cells.</summary>
    public const int TargetCells = 320;

    public int Width { get; }
    public int Height { get; }
    public double Cell { get; }
    public double ScreenW { get; }
    public double ScreenH { get; }
    public double Dpi { get; }
    /// <summary>Premultiplied BGRA, one uint per cell (B in the low byte).</summary>
    public uint[] Pixels { get; }

    private readonly float[] _depth;

    public LightsDownCanvas(double screenWPx, double screenHPx, double dpi)
    {
        ScreenW = Math.Max(1, screenWPx);
        ScreenH = Math.Max(1, screenHPx);
        Dpi = dpi > 0.25 && dpi < 8 ? dpi : 1;
        Cell = CellFor(ScreenW);
        Width = (int)Math.Ceiling(ScreenW / Cell);
        Height = (int)Math.Ceiling(ScreenH / Cell);
        Pixels = new uint[Width * Height];
        _depth = new float[Width * Height];
        for (int j = 0; j < Height; j++)
            for (int i = 0; i < Width; i++)
                _depth[j * Width + i] = (float)EdgeDepth((i + 0.5) * Cell, (j + 0.5) * Cell, ScreenW, ScreenH);
    }

    /// <summary>Physical pixels per cell: 4 at least, so a 4K screen is still about 320 cells wide.</summary>
    public static double CellFor(double screenWPx) => Math.Max(4, Math.Ceiling(screenWPx / TargetCells));

    /// <summary>
    /// How far inside the screen a point is from its edges: a SOFT minimum of the four edge
    /// distances, so it is smooth everywhere. A true distance has creases along the diagonals and
    /// the middle line, and a slow shade over a crease shows as a box. Near an edge it is that
    /// edge's distance; the corners read shallower (negative right in them), so they dim first and
    /// the dim curves around them.
    /// </summary>
    public static double EdgeDepth(double x, double y, double w, double h)
    {
        double k = Math.Max(1, EdgeSoftShare * Math.Min(w, h));
        double sum = Math.Exp(-x / k) + Math.Exp(-(w - x) / k) + Math.Exp(-y / k) + Math.Exp(-(h - y) / k);
        return -k * Math.Log(sum);
    }

    /// <summary>Signed distance to a rounded rectangle: negative inside.</summary>
    public static double RoundedRectDistance(double x, double y, LightsDownRect rc, double radius)
    {
        double hw = rc.W / 2, hh = rc.H / 2;
        radius = Math.Clamp(radius, 0, Math.Min(hw, hh));
        double qx = Math.Abs(x - (rc.X + hw)) - (hw - radius);
        double qy = Math.Abs(y - (rc.Y + hh)) - (hh - radius);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    /// <summary>The vignette's share at a depth: 1 under <c>front - feather</c>, 0 past <c>front</c>.</summary>
    public static double Vignette(double depth, double front, double feather)
        => 1 - LightsDownRules.SmoothStep(front - feather, front, depth);

    /// <summary>How clear a hole leaves a point at signed distance <paramref name="d"/>: 1 inside, 0 past the feather.</summary>
    public static double HoleClear(double d, double featherPx) => 1 - LightsDownRules.SmoothStep(0, featherPx, d);

    /// <summary>The halo's shape at signed distance <paramref name="d"/>: 1 on the window edge, gaussian either side.</summary>
    public static double HaloShape(double d, double outPx, double inPx)
    {
        double s = d >= 0 ? outPx : inPx;
        if (s <= 0) return 0;
        double k = d / s;
        return Math.Exp(-k * k);
    }

    /// <summary>A hole with its padding, feather and reach worked out once per frame.</summary>
    private readonly struct Prep
    {
        public readonly LightsDownRect Rc;
        public readonly double Presence, Feather, Reach, Corner, HaloOut, HaloIn;
        public readonly bool Halo;

        public Prep(in LightsDownHole h, double dpi)
        {
            Halo = h.Halo;
            double pad = Halo ? 0 : TargetPadDip * dpi;
            Rc = new LightsDownRect(h.Rect.X - pad, h.Rect.Y - pad, h.Rect.W + 2 * pad, h.Rect.H + 2 * pad);
            Feather = (Halo ? HoleFeatherDip : TargetFeatherDip) * dpi;
            HaloOut = HaloOutDip * dpi;
            HaloIn = HaloInDip * dpi;
            Reach = Halo ? Math.Max(Feather, 3 * HaloOut) : Feather;
            Corner = CornerDip * dpi;
            Presence = h.Presence;
        }

        public bool Near(double x, double y)
            => x >= Rc.X - Reach && x <= Rc.Right + Reach && y >= Rc.Y - Reach && y <= Rc.Bottom + Reach;
    }

    private Prep[] _prep = Array.Empty<Prep>();
    private int[] _rowHoles = Array.Empty<int>();

    /// <summary>
    /// One pixel, premultiplied: the vignette alpha <paramref name="vignette"/> (already times the
    /// look's alpha) under the holes, with the halos over it.
    /// </summary>
    public static (double A, double R, double G, double B) Shade(double x, double y, double vignette,
        IReadOnlyList<LightsDownHole> holes, uint haloRgb, double dpi)
    {
        var prep = new Prep[holes.Count];
        var all = new int[holes.Count];
        for (int k = 0; k < prep.Length; k++) { prep[k] = new Prep(holes[k], dpi); all[k] = k; }
        return Shade(x, y, vignette, prep, all, all.Length, haloRgb);
    }

    private static (double A, double R, double G, double B) Shade(double x, double y, double vignette,
        Prep[] prep, int[] pick, int count, uint haloRgb)
    {
        double keep = 1, haloKeep = 1;
        for (int n = 0; n < count; n++)
        {
            ref readonly var h = ref prep[pick[n]];
            if (!h.Near(x, y)) continue;
            double d = RoundedRectDistance(x, y, h.Rc, h.Corner);
            // The clear zone ends AT the window (its soft edge runs inward, under the window, which sits above this
            // overlay): a feather outside it let a bright app shine round every video like a lit sign (stills, 2026-11-18).
            if (d < 0) keep *= 1 - h.Presence * HoleClear(d + h.Feather, h.Feather);
            if (h.Halo)
            {
                double s = d >= 0 ? h.HaloOut : h.HaloIn;
                if (d < 3 * s && d > -3 * s) haloKeep *= 1 - h.Presence * HaloShape(d, h.HaloOut, h.HaloIn);
            }
        }
        double a = vignette * keep;
        double ha = Math.Min(HaloMax, HaloGain * vignette) * (1 - haloKeep);
        double hr = ((haloRgb >> 16) & 0xFF) / 255.0, hg = ((haloRgb >> 8) & 0xFF) / 255.0, hb = (haloRgb & 0xFF) / 255.0;
        double under = a * (1 - ha);
        return (ha + under,
            hr * ha + DimR / 255.0 * under,
            hg * ha + DimG / 255.0 * under,
            hb * ha + DimB / 255.0 * under);
    }

    /// <summary>Draw one frame into <see cref="Pixels"/>. Each row only looks at the holes near it.</summary>
    public void Draw(in LightsDownLook look, IReadOnlyList<LightsDownHole> holes)
    {
        var px = Pixels;
        if (!look.Visible) { Array.Clear(px); return; }
        double feather = Math.Max(1, look.Feather);
        int hc = holes.Count;
        if (_prep.Length < hc) { _prep = new Prep[hc]; _rowHoles = new int[hc]; }
        for (int k = 0; k < hc; k++) _prep[k] = new Prep(holes[k], Dpi);
        const double dimR = DimR / 255.0, dimG = DimG / 255.0, dimB = DimB / 255.0;
        for (int j = 0; j < Height; j++)
        {
            double y = (j + 0.5) * Cell;
            int row = j * Width;
            int near = 0;
            for (int k = 0; k < hc; k++)
            {
                ref readonly var h = ref _prep[k];
                if (y >= h.Rc.Y - h.Reach && y <= h.Rc.Bottom + h.Reach) _rowHoles[near++] = k;
            }
            for (int i = 0; i < Width; i++)
            {
                double v = look.Alpha * Vignette(_depth[row + i], look.Front, feather);
                if (v <= 0.0005) { px[row + i] = 0; continue; }
                var (a, r, g, b) = near == 0
                    ? (v, dimR * v, dimG * v, dimB * v)
                    : Shade((i + 0.5) * Cell, y, v, _prep, _rowHoles, near, look.HaloRgb);
                px[row + i] = Pack(a, r, g, b);
            }
        }
    }

    private static uint Pack(double a, double r, double g, double b)
    {
        static uint B8(double v) => v <= 0 ? 0u : v >= 1 ? 255u : (uint)(v * 255 + 0.5);
        uint aa = B8(a);
        uint rr = Math.Min(B8(r), aa), gg = Math.Min(B8(g), aa), bb = Math.Min(B8(b), aa);
        return (aa << 24) | (rr << 16) | (gg << 8) | bb;
    }

    /// <summary>The premultiplied pixel at a cell (tests).</summary>
    public (byte A, byte R, byte G, byte B) At(int i, int j)
    {
        uint p = Pixels[j * Width + i];
        return ((byte)(p >> 24), (byte)(p >> 16), (byte)(p >> 8), (byte)p);
    }

    /// <summary>The cell over a physical pixel.</summary>
    public (int I, int J) CellAt(double x, double y)
        => (Math.Clamp((int)(x / Cell), 0, Width - 1), Math.Clamp((int)(y / Cell), 0, Height - 1));

    /// <summary>
    /// Is a new frame worth drawing: the holes moved or changed, or the look moved by more than a
    /// visible step. A slow growth then repaints a few times a second, not thirty.
    /// </summary>
    public static bool Worth(in LightsDownLook last, in LightsDownLook next, bool holesChanged)
    {
        if (holesChanged) return true;
        if (last.Visible != next.Visible) return true;
        if (!next.Visible) return false;
        return Math.Abs(last.Alpha - next.Alpha) > 0.004
            || Math.Abs(last.Front - next.Front) > 1.5
            || Math.Abs(last.Feather - next.Feather) > 1.5
            || last.HaloRgb != next.HaloRgb;
    }
}
