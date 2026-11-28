using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// One screen Lights Down dims, in desktop PHYSICAL pixels, with the holes it tracks and the look the
/// last <see cref="LightsDownRun"/> step gave it. Both heads keep one per overlay (WPF: a layered window
/// per screen; Avalonia: a layer inside a full-screen video window, or a dim window under the floating
/// videos) and only draw <see cref="Look"/> with <see cref="Holes"/>.
/// </summary>
public sealed class LightsDownScreen
{
    public LightsDownScreen(double x, double y, double widthPx, double heightPx, double dpi)
    {
        X = x; Y = y;
        W = Math.Max(1, widthPx);
        H = Math.Max(1, heightPx);
        Dpi = dpi is > 0.25 and < 8 ? dpi : 1;
    }

    public double X { get; }
    public double Y { get; }
    public double W { get; }
    public double H { get; }
    public double Dpi { get; }

    /// <summary>The short side in physical pixels (the look scales with it).</summary>
    public double ShortSidePx => Math.Min(W, H);

    public LightsDownHoles Holes { get; } = new();

    /// <summary>What the last step asks this screen to draw.</summary>
    public LightsDownLook Look { get; set; } = LightsDownLook.None;

    /// <summary>A desktop physical-pixel rectangle in this screen's own pixels.</summary>
    public LightsDownRect ToLocal(in LightsDownRect px) => new(px.X - X, px.Y - Y, px.W, px.H);

    /// <summary>A desktop DIP rectangle (this screen's scale; the attention bounds) in this screen's pixels.</summary>
    public LightsDownRect DipToLocal(in LightsDownRect dip) => new(dip.X * Dpi - X, dip.Y * Dpi - Y, dip.W * Dpi, dip.H * Dpi);

    public bool ContainsPx(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;

    /// <summary>Does a physical-pixel rectangle come close enough for its hole or halo to show here.</summary>
    public bool Touches(in LightsDownRect px, double marginPx)
        => px.Right + marginPx > X && px.X - marginPx < X + W && px.Bottom + marginPx > Y && px.Y - marginPx < Y + H;

    /// <summary>How far outside a floating window its halo and hole still reach on this screen.</summary>
    public double HaloMarginPx => 3 * LightsDownCanvas.HaloOutDip * Dpi + LightsDownCanvas.HoleFeatherDip * Dpi;
}

/// <summary>
/// The camera as Lights Down reads it. Never switches a camera on: <see cref="Running"/> false means
/// nothing is known. <see cref="Live"/> = calibrated and a gaze sample younger than half a second;
/// <see cref="X"/>/<see cref="Y"/> are that gaze in desktop physical pixels.
/// </summary>
public readonly record struct LightsDownEyes(bool Running, bool FaceLost, bool Live, double X, double Y)
{
    public const double FreshSeconds = 0.5;
    public static readonly LightsDownEyes None = default;

    public static LightsDownEyes From(bool running, bool faceLost, bool calibrated, double gazeAgeSeconds, double x, double y)
        => running ? new(true, faceLost, calibrated && gazeAgeSeconds < FreshSeconds, x, y) : None;
}

/// <summary>
/// One Lights Down run, the frame step both heads share (moved out of WPF LightsDownService, 2026-11-25):
/// the full-screen edge dim or the floating vignette, the holes on every screen, "Follow eyes or mouse".
/// The head reads the world (cursor, camera, video rectangles, options) and draws each screen's
/// <see cref="LightsDownScreen.Look"/>; nothing here knows a window.
/// </summary>
public sealed class LightsDownRun
{
    /// <summary>How often the floating dims re-check their order under the videos.</summary>
    public const double RestackSeconds = 0.5;
    /// <summary>How often the attention targets are re-read for the full-screen cut-outs.</summary>
    public const double CutSeconds = 0.1;
    /// <summary>How often the mod accent is re-read for the halos.</summary>
    public const double AccentSeconds = 1;
    /// <summary>The floating frame's own pink, when no mod colour can be read.</summary>
    public const uint FallbackHalo = 0xFF69B4;

    private static readonly (long, LightsDownRect, bool)[] NoHoles = Array.Empty<(long, LightsDownRect, bool)>();
    private readonly List<(long Key, LightsDownRect Rect, bool Halo)> _live = new();
    private double _mouseX = double.NaN, _mouseY = double.NaN;
    private double _nextCut, _nextZ, _nextAccent;

    public LightsDownRun(bool floating) => Floating = floating;

    public bool Floating { get; }
    public LightsDownFullScreen Full { get; } = new();
    public LightsDownFloating Float { get; } = new();
    public bool Ending => Float.Ending;
    public bool Done => Float.Done;

    /// <summary>30 fps is plenty for slow light; the Performance tier drops to 20.</summary>
    public static double FrameSeconds(bool performanceTier) => performanceTier ? 1.0 / 20 : 1.0 / 30;

    public static LightsDownMotion MotionFor(MotionLevel level) => level switch
    {
        MotionLevel.Off => LightsDownMotion.Off,
        MotionLevel.Reduced => LightsDownMotion.Reduced,
        _ => LightsDownMotion.Full,
    };

    /// <summary>The player's options, read live so a slider moves the dim at once.</summary>
    public static LightsDownTuning TuningFor(AppSettings? s) => s == null ? LightsDownTuning.Stock
        : LightsDownOptions.Tuning(s.LightsDownDarkness, s.LightsDownDimSpeed, s.LightsDownReturnSpeed);

    /// <summary>The mod accent as 0xRRGGBB, or the frame pink.</summary>
    public static uint HaloFor(string? accentHex)
        => BubbleCore.DiscoPalette.TryParseHex(accentHex, out var rgb) ? rgb & 0xFFFFFF : FallbackHalo;

    public bool CutDue(double now) => Due(ref _nextCut, now, CutSeconds);
    public bool RestackDue(double now) => Due(ref _nextZ, now, RestackSeconds);
    public bool AccentDue(double now) => Due(ref _nextAccent, now, AccentSeconds);

    private static bool Due(ref double next, double now, double every)
    {
        if (now < next) return false;
        next = now + every;
        return true;
    }

    /// <summary>The video's run ended: a floating dim fades out (true = keep stepping), anything else stops now.</summary>
    public bool EndOfVideo()
    {
        if (!Floating) return false;
        Float.BeginEnd();
        return true;
    }

    /// <summary>The floating fade after the last video: holes ease shut, the dim fades.</summary>
    public void StepEnding(double dt, IReadOnlyList<LightsDownScreen> screens, LightsDownTuning t, double gentle,
        LightsDownMotion motion, uint halo)
    {
        Float.Step(dt, t, gentle);
        foreach (var s in screens)
        {
            s.Holes.Update(dt, NoHoles, gentle);
            s.Look = LightsDownFloating.Look(Float.Grow, Float.Fade, t, motion, s.ShortSidePx, halo);
        }
    }

    /// <summary>
    /// Floating: the dim grows (or, with Follow on and nothing watched, pulls back) and each screen
    /// cuts a haloed hole for every floating window near it. <paramref name="videos"/> are the windows
    /// a player can see, in desktop physical pixels, keyed by window.
    /// </summary>
    public void StepFloating(double dt, IReadOnlyList<LightsDownScreen> screens, IReadOnlyList<(long Key, LightsDownRect Px)> videos,
        bool follow, (double X, double Y)? cursor, in LightsDownEyes eyes, LightsDownTuning t, double gentle,
        LightsDownMotion motion, uint halo)
    {
        bool watched = true;
        if (follow)
        {
            bool cursorOn = false, gazeOn = false;
            foreach (var (_, r) in videos)
            {
                if (cursor is { } c && Inside(r, c.X, c.Y)) cursorOn = true;
                if (eyes.Live && Inside(r, eyes.X, eyes.Y)) gazeOn = true;
            }
            watched = LightsDownRules.Watched(eyes.FaceLost, eyes.Live, gazeOn, cursorOn);
        }
        Float.Step(dt, t, gentle, watched);
        foreach (var s in screens)
        {
            double margin = s.HaloMarginPx;
            _live.Clear();
            foreach (var (key, r) in videos)
                if (s.Touches(r, margin)) _live.Add((key, s.ToLocal(r), true));
            s.Holes.Update(dt, _live, gentle);
            s.Look = LightsDownFloating.Look(Float.Grow, Float.Fade, t, motion, s.ShortSidePx, halo);
        }
    }

    /// <summary>
    /// Full screen: the edge dim eases in while the mouse rests and pulls back when it moves or the
    /// eyes leave (Follow on: while no video screen is watched). <paramref name="cutDip"/> are the live
    /// attention targets in desktop DIPs, cut out so the dim never sits on a check.
    /// </summary>
    public void StepFull(double dt, IReadOnlyList<LightsDownScreen> screens, (double X, double Y)? cursor, bool follow,
        in LightsDownEyes eyes, IReadOnlyList<LightsDownRect> cutDip, LightsDownTuning t, double gentle, LightsDownMotion motion)
    {
        bool moved = false;
        if (cursor is { } c)
        {
            moved = LightsDownRules.Moved(_mouseX, _mouseY, c.X, c.Y);
            _mouseX = c.X; _mouseY = c.Y;
        }
        bool cursorOn = false, gazeOn = false;
        foreach (var s in screens)
        {
            if (cursor is { } p && s.ContainsPx(p.X, p.Y)) cursorOn = true;
            if (eyes.Live && s.ContainsPx(eyes.X, eyes.Y)) gazeOn = true;
        }
        if (follow) Full.Step(dt, false, !LightsDownRules.Watched(eyes.FaceLost, eyes.Live, gazeOn, cursorOn), t, gentle);
        else Full.Step(dt, moved, LightsDownRules.LookedAway(eyes.Running, eyes.FaceLost, eyes.Live, gazeOn), t, gentle);
        foreach (var s in screens)
        {
            _live.Clear();
            for (int i = 0; i < cutDip.Count; i++) _live.Add((i, s.DipToLocal(cutDip[i]), false));
            s.Holes.Update(dt, _live, gentle);
            s.Look = LightsDownFullScreen.Look(Full.Level, t, motion, s.ShortSidePx);
        }
    }

    private static bool Inside(in LightsDownRect r, double x, double y) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}

/// <summary>Where a floating dim stands in the window order against the videos it must stay under.</summary>
public enum LightsDownStack { Below, Wrong, Unknown }

/// <summary>
/// The floating dim's promise: every video sits over it. A head that cannot keep that promise
/// refuses Lights Down for the run rather than dim a video (Avalonia: a window manager that ignores
/// the restack, or one whose order cannot be read).
/// </summary>
public sealed class LightsDownStackGuard
{
    /// <summary>Two checks in a row (half a second apart) with the dim still over a video, after a restack was asked.</summary>
    public const int WrongLimit = 2;
    /// <summary>Two seconds without a readable order.</summary>
    public const int UnknownLimit = 4;

    private int _wrong, _unknown;

    /// <summary>
    /// Order from a bottom-to-top stacking list: <paramref name="me"/> is the dim's index, <paramref name="videos"/>
    /// the videos' indexes (-1 = not listed). <paramref name="lowest"/> is the index of the video to restack under.
    /// </summary>
    public static LightsDownStack Order(int me, IReadOnlyList<int> videos, out int lowest)
    {
        lowest = -1;
        if (me < 0) return LightsDownStack.Unknown;
        foreach (var v in videos)
        {
            if (v < 0) return LightsDownStack.Unknown;
            if (lowest < 0 || v < lowest) lowest = v;
        }
        return lowest < 0 || me < lowest ? LightsDownStack.Below : LightsDownStack.Wrong;
    }

    /// <summary>Feed one check; true = refuse now.</summary>
    public bool Refuse(LightsDownStack s)
    {
        switch (s)
        {
            case LightsDownStack.Below: _wrong = _unknown = 0; return false;
            case LightsDownStack.Wrong: _unknown = 0; return ++_wrong >= WrongLimit;
            default: _wrong = 0; return ++_unknown >= UnknownLimit;
        }
    }
}
