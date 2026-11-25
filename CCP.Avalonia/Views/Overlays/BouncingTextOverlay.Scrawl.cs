using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF BouncingTextService.cs:390-430,533-780 and ScrawlScene.cs; the state is Core ScrawlField.
internal static partial class BouncingTextOverlay
{
    internal static Func<bool> ScrawlOn = () => SuperAccess.IsOn(SuperEffect.Scrawl);
    internal static Func<Action<int, int>, IDisposable?> ScrawlListen = GazeDriftMouseHook.Start;
    internal static bool ScrawlListenWhenTesting;
    private static ScrawlField? _scrawl;
    private static IDisposable? _scrawlListener;
    private static string _scrawlFont = "";
    private static double _scrawlSize;
    internal static ScrawlField? Scrawl => _scrawl;
    private static readonly Random ScrawlRandom = new();

    private static void StartScrawl()
    {
        SuperAccess.Changed -= ScrawlChanged;
        SuperAccess.Changed += ScrawlChanged;
        SyncScrawl();
    }

    private static void ScrawlChanged(SuperEffect effect)
    {
        if (effect != SuperEffect.Scrawl) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ScrawlChanged(effect)); return; }
        SyncScrawl();
    }

    /// <summary>WPF ScrawlStampFontSize: the words' size times the Stamp size option.</summary>
    private static double ScrawlStampFontSize() =>
        Engine.FontSize * ScrawlOptions.ClampStampSize(CoreSettings.Current.ScrawlStampSize) / 100.0;

    private static void SyncScrawl()
    {
        if (!_running) return;
        if (!ScrawlOn()) { DisableScrawl(); return; }
        if (_scrawl != null) return;
        var face = new Typeface(Family(), FontStyle.Normal, FontWeight.Bold);
        _scrawlSize = ScrawlStampFontSize();
        _scrawlFont = Engine.Font;
        _scrawl = new ScrawlField(_scrawlSize, (word, size) =>
        {
            var ft = new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, Brushes.White);
            return (ft.WidthIncludingTrailingWhitespace, ft.Height);
        }, () => global::ConditioningControlPanel.Localization.Loc.Get("super_scrawl_corner"));
        foreach (var window in Windows) window.AttachScrawl(_scrawl, face);
        var scene = _scrawl;
        if (!SkipPlatformChecksForTests || ScrawlListenWhenTesting)
            _scrawlListener = ScrawlListen((x, y) => Dispatcher.UIThread.Post(() =>
            {
                if (_running && ReferenceEquals(scene, _scrawl)) ScrawlClick(x, y);
            }));
    }

    private static void StopScrawl()
    {
        SuperAccess.Changed -= ScrawlChanged;
        DisableScrawl();
    }

    private static void DisableScrawl()
    {
        var listener = _scrawlListener; _scrawlListener = null;
        if (listener != null) ThreadPool.QueueUserWorkItem(_ => { try { listener.Dispose(); } catch { } });
        if (_scrawl == null) return;
        _scrawl.Clear(); _scrawl = null;
        foreach (var w in Windows) w.DetachScrawl();
        foreach (var l in Engine.Logos)
        {
            l.SlamT = -1; l.SlamLanded = false;
            double speed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            if (speed > l.Cruise && speed > 0) { l.VelX *= l.Cruise / speed; l.VelY *= l.Cruise / speed; }
        }
    }

    /// <summary>Stamps are built in the words' font and size: the walls start over when either moves.</summary>
    private static void ResetScrawlForFont()
    {
        if (_scrawl == null || (_scrawlSize == ScrawlStampFontSize() && _scrawlFont == Engine.Font)) return;
        DisableScrawl(); SyncScrawl();
    }

    private static MotionLevel ScrawlMotion => AmbientFxCanvas.Env.Level;

    /// <summary>WPF TickScrawl: the option box read live, then the clock, the shake and the repaint.</summary>
    private static void TickScrawl(double dt)
    {
        if (_scrawl == null) return;
        ResetScrawlForFont();
        if (_scrawl is not { } field) return;
        var s = CoreSettings.Current;
        int slam = ScrawlOptions.ClampSlam(s.ScrawlSlam);
        field.Configure(ScrawlOptions.ClampStampSeconds(s.ScrawlStampSeconds),
            ScrawlOptions.EffectiveCap(s.ScrawlStampCap, _scrawlSize / BouncingTextEngine.BaseFontSize * 100.0, Engine.Logos.Count),
            ScrawlOptions.ShakeFactor(slam, Services.Safety.GentleVisuals.On), cornerLabel: s.BouncingTextFxCornerBurst);
        field.Tick(dt, ScrawlMotion, s.LockdownPhotosafe);
        foreach (var w in Windows) w.PaintScrawl(field.ShakeX, field.ShakeY, field.IsLive);
    }

    private static void RelaxScrawl(BouncingTextEngine.Logo l, double dt)
    {
        if (_scrawl == null) return;
        var speed = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
        var factor = ScrawlRules.RelaxFactor(speed, l.Cruise, dt);
        l.VelX *= factor; l.VelY *= factor;
    }

    private static void ScrawlBounce(BouncingTextEngine.Logo l, BounceStep step)
    {
        if (_scrawl == null) return;
        if (step.Bounced)
        {
            var wall = ScrawlRules.WallOf(step.BouncedX && l.VelX > 0, step.BouncedX && l.VelX < 0,
                step.BouncedY && l.VelY > 0, step.BouncedY && l.VelY < 0);
            var (mx, my, _) = MonitorCentreAt(l.PosX + l.TextWidth / 2, l.PosY + l.TextHeight / 2);
            _scrawl.WallHit(l.Text, wall, l.PosX, l.PosY, l.PosX + l.TextWidth, l.PosY + l.TextHeight,
                new ScrawlBounds(Engine.MinX, Engine.MinY, Engine.MaxX, Engine.MaxY), mx, my);
        }
        if (!step.CornerHit) return;
        // WPF :419: a gold corner blows a hole in the fog. It knocks nothing: reach 0.
        double d = _screenScale;
        try
        {
            lock (SteamOverlay.Gate)
                SuperStageHost.Stage.Blast(new Blast((l.PosX + l.TextWidth / 2) * d, (l.PosY + l.TextHeight / 2) * d, 0, 230 * d, BlastKind.GoldCorner));
        }
        catch (Exception ex) { Log.Debug("Scrawl: corner blast failed: {E}", ex.Message); }
    }

    /// <summary>
    /// WPF StepStage (:541): the word against the shared stage. The pool bends it, catches it near the eye,
    /// turns it and throws it back out (TextFling); its box is swept (bubbles pop, deck cards are knocked) and
    /// it cuts a trail through the fog. Words are in DIPs, the stage in device pixels. True while the pool
    /// holds the word: the caller skips its own move and wall bounces. Under <see cref="SteamOverlay.Gate"/>.
    /// </summary>
    private static bool StepScrawlStage(BouncingTextEngine.Logo l, double dt)
    {
        if (_scrawl == null) return false;
        var stage = SuperStageHost.Stage;
        double d = _screenScale <= 0 ? 1 : _screenScale;
        lock (SteamOverlay.Gate)
        {
            double cx = (l.PosX + l.TextWidth / 2) * d, cy = (l.PosY + l.TextHeight / 2) * d;
            double vx = l.VelX * d, vy = l.VelY * d;
            TextFling.Step(ref l.Fling, stage, ref cx, ref cy, ref vx, ref vy, Math.Max(1, l.Cruise) * d, dt);
            l.VelX = vx / d; l.VelY = vy / d;
            bool held = l.Fling.Phase == 1;
            if (held)
            {
                l.PosX = Math.Clamp(cx / d - l.TextWidth / 2, Engine.MinX, Math.Max(Engine.MinX, Engine.MaxX - l.TextWidth));
                l.PosY = Math.Clamp(cy / d - l.TextHeight / 2, Engine.MinY, Math.Max(Engine.MinY, Engine.MaxY - l.TextHeight));
            }
            double left = l.PosX * d, top = l.PosY * d, w = l.TextWidth * d, h = l.TextHeight * d;
            // A caught word is small and in the eye: it pops and knocks nothing on the way round.
            stage.MovingPops = CoreSettings.Current.MovingEffectsPop;
            if (!held) stage.Sweep(left, top, left + w, top + h);
            if (stage.SteamOn)
                for (int i = -1; i <= 1; i++)
                    stage.Wipe(left + w / 2 + i * w * 0.32, top + h / 2, Math.Max(h * 0.7, 26 * d), dt * 1.5, mark: false);
            return held;
        }
    }

    /// <summary>Centre and width (engine DIP) of the screen under a point; the whole field if none.</summary>
    private static (double X, double Y, double W) MonitorCentreAt(double x, double y)
    {
        var screens = _host == null ? null : ScreenList.Enumerate(_host);
        var b = screens?.FirstOrDefault(s => s.Bounds.Contains(new PixelPoint((int)(x * _screenScale), (int)(y * _screenScale))))?.Bounds;
        return b is { } r
            ? ((r.X + r.Width / 2.0) / _screenScale, (r.Y + r.Height / 2.0) / _screenScale, r.Width / _screenScale)
            : ((Engine.MinX + Engine.MaxX) / 2, (Engine.MinY + Engine.MaxY) / 2, Engine.MaxX - Engine.MinX);
    }

    // The hook observes, never consumes, a click. Keep CCP controls usable even where a word overlaps them.
    internal static void ScrawlClick(int px, int py)
    {
        if (!_running || _scrawl == null || !ScrawlOptions.SlamOn(CoreSettings.Current.ScrawlSlam) || ClickOverOwnWindow(px, py)) return;
        double x = px / _screenScale, y = py / _screenScale;
        for (int i = Engine.Logos.Count - 1; i >= 0; i--)
        {
            var l = Engine.Logos[i];
            if (x < l.PosX - 8 || x > l.PosX + l.TextWidth + 8 || y < l.PosY - 8 || y > l.PosY + l.TextHeight + 8 || l.SlamT >= 0) continue;
            (l.SlamX, l.SlamY, l.SlamW) = MonitorCentreAt(l.PosX + l.TextWidth / 2, l.PosY + l.TextHeight / 2);
            // Motion off: the word stays put, only the still stamp lands at the centre and fades.
            if (ScrawlMotion == MotionLevel.Off) _scrawl.SlamLanded(l.Text, l.SlamX, l.SlamY, l.SlamW);
            else { l.SlamT = 0; l.SlamLanded = false; }
            return;
        }
    }

    internal static Func<int, int, bool> ClickOverOwnWindow = (x, y) =>
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return false;
        return desktop.Windows.Any(w => w is not BouncingTextOverlayWindow && w.IsVisible &&
            x >= w.Position.X && y >= w.Position.Y && x < w.Position.X + w.Bounds.Width * w.DesktopScaling && y < w.Position.Y + w.Bounds.Height * w.DesktopScaling);
    };

    private static bool StepScrawlSlam(BouncingTextEngine.Logo l, double dt)
    {
        if (_scrawl == null || l.SlamT < 0) return false;
        l.SlamT += dt;
        double x = l.SlamX - l.TextWidth / 2, y = l.SlamY - l.TextHeight / 2;
        if (l.SlamT <= ScrawlRules.SlamTravel)
        {
            double f = Math.Min(1, dt * ScrawlRules.SlamPull);
            l.PosX += (x - l.PosX) * f; l.PosY += (y - l.PosY) * f;
            return true;
        }
        l.PosX = x; l.PosY = y;
        if (!l.SlamLanded)
        {
            l.SlamLanded = true; _scrawl.SlamLanded(l.Text, l.SlamX, l.SlamY, l.SlamW);
            var gain = ScrawlOptions.KnockFactor(CoreSettings.Current.ScrawlSlam);
            foreach (var other in Engine.Logos)
            {
                if (other == l || other.SlamT >= 0) continue;
                var (vx, vy) = ScrawlRules.Knock(other.PosX + other.TextWidth / 2, other.PosY + other.TextHeight / 2, l.SlamX, l.SlamY, other.Cruise, ScrawlMotion);
                other.VelX += vx * gain; other.VelY += vy * gain;
            }
            double d = l.Cruise / Math.Sqrt(2);
            l.VelX = ScrawlRandom.Next(2) == 0 ? d : -d; l.VelY = ScrawlRandom.Next(2) == 0 ? d : -d;
        }
        if (l.SlamT >= ScrawlRules.SlamHold) { l.SlamT = -1; l.SlamLanded = false; }
        return true;
    }

    /// <summary>A word the pool has caught shrinks toward the eye; a slammed word swells at the screen centre.</summary>
    private static double ScrawlScale(BouncingTextEngine.Logo l)
    {
        if (_scrawl == null) return 1;
        double fling = l.Fling.Scale > 0 && l.Fling.Scale < 1 ? l.Fling.Scale : 1;
        return fling * (l.SlamT >= 0 ? ScrawlRules.SlamWordScale(l.SlamT, ScrawlMotion) : 1);
    }

    /// <summary>The stamps' boxes in physical pixels, for the OCR skip list (the stamps are the player's text too).</summary>
    private static IEnumerable<System.Drawing.Rectangle> ScrawlRects()
    {
        if (_scrawl == null) return Array.Empty<System.Drawing.Rectangle>();
        var boxes = new List<(double X, double Y, double W, double H)>();
        _scrawl.AppendRects(boxes);
        return boxes.Select(r => System.Drawing.Rectangle.FromLTRB((int)Math.Floor(r.X * _screenScale), (int)Math.Floor(r.Y * _screenScale),
            (int)Math.Ceiling((r.X + r.W) * _screenScale), (int)Math.Ceiling((r.Y + r.H) * _screenScale)));
    }
}

internal sealed partial class BouncingTextOverlayWindow
{
    private ScrawlLayer? _scrawlLayer;
    private TranslateTransform? _shake;
    private ITransform? _canvasTransform;
    private bool _scrawlWasLive;

    /// <summary>The stamp layer sits first in the canvas (under every word); the shake moves the whole canvas.</summary>
    internal void AttachScrawl(ScrawlField field, Typeface face)
    {
        DetachScrawl();
        _scrawlLayer = new ScrawlLayer(field, face, _screen.X / _k, _screen.Y / _k) { Width = _screen.Width / _k, Height = _screen.Height / _k };
        Canvas.Children.Insert(0, _scrawlLayer);
        _canvasTransform = Canvas.RenderTransform;
        _shake = new TranslateTransform();
        var group = new TransformGroup { Children = { _shake } };
        Canvas.RenderTransform = group;
        if (_canvasTransform is Transform t) group.Children.Add(t);
    }

    internal void DetachScrawl()
    {
        if (_scrawlLayer != null) Canvas.Children.Remove(_scrawlLayer);
        if (_shake != null) Canvas.RenderTransform = _canvasTransform;
        (_scrawlLayer, _shake, _canvasTransform, _scrawlWasLive) = (null, null, null, false);
    }

    /// <summary>Per frame: apply the shake and redraw the stamps while anything is alive (plus one frame to clear).</summary>
    internal void PaintScrawl(double shakeX, double shakeY, bool live)
    {
        if (_shake != null) { _shake.X = shakeX; _shake.Y = shakeY; }
        if (_scrawlLayer != null && (live || _scrawlWasLive)) _scrawlLayer.InvalidateVisual();
        _scrawlWasLive = live;
    }

    internal ScrawlLayer? ScrawlLayerForTests => _scrawlLayer;
    internal TranslateTransform? ShakeForTests => _shake;
}

/// <summary>WPF ScrawlLayer + ScrawlScene.Render: one bouncing-text window's view of the shared Core field,
/// in engine DIPs moved into the window by its origin. Every fade is baked into a cached brush or pen
/// (alpha in 64 steps) instead of a pushed opacity: on Skia each pushed opacity is a save-layer over the
/// whole full-screen window (AGENTS.md Skia traps).</summary>
internal sealed class ScrawlLayer : Control
{
    private static readonly Color[] Inks =
    {
        Color.FromRgb(255, 111, 181), Color.FromRgb(95, 255, 208), Color.FromRgb(190, 150, 255),
        Color.FromRgb(255, 207, 107), Colors.White,
    };
    private static readonly Color LabelInk = Color.FromRgb(255, 226, 154), Bead = Colors.White;
    private static readonly Dictionary<uint, IBrush> Solids = new(), Glows = new();
    private static readonly Dictionary<(uint, double), IPen> Strokes = new();

    private readonly ScrawlField _f;
    private readonly Typeface _face;
    private readonly double _ox, _oy;
    private Geometry? _label;
    private int _labelSerial = -1;

    internal ScrawlLayer(ScrawlField field, Typeface face, double originX, double originY)
    {
        (_f, _face, _ox, _oy) = (field, face, originX, originY);
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>--render-all only (RenderProof builds every view with no arguments): a sample field with wall
    /// stamps, a corner stamp and a slam, rested a little. Internal, so no production caller ships the sample.</summary>
    internal ScrawlLayer() : this(Sample(), SampleFace, 0, 0) => (Width, Height) = (1280, 720);

    private static readonly Typeface SampleFace = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private static ScrawlField Sample()
    {
        var f = new ScrawlField(48, (w, s) =>
        {
            var ft = new FormattedText(w, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SampleFace, s, Brushes.White);
            return (ft.WidthIncludingTrailingWhitespace, ft.Height);
        }, () => "CORNER", new Random(5));
        var b = new ScrawlBounds(140, 60, 1140, 660);   // inside the proof's 1100-wide view
        f.WallHit("DEEPER", ScrawlWall.Top, 500, 60, 700, 120, b, 640, 360);
        f.WallHit("OBEY", ScrawlWall.Left, 140, 300, 280, 360, b, 640, 360);
        f.WallHit("SINK", ScrawlWall.Right, 1000, 600, 1140, 660, b, 640, 360);
        f.SlamLanded("DROP", 640, 360, 1280);
        f.Tick(0.3, MotionLevel.Full);
        return f;
    }

    /// <summary>The colour at an alpha, snapped to 64 steps so the caches stay small. Null when invisible.</summary>
    private static uint? Argb(Color c, double alpha)
    {
        int a = (int)Math.Round(Math.Clamp(alpha, 0, 1) * 63) * 255 / 63;
        return a <= 0 ? null : (uint)a << 24 | (uint)c.R << 16 | (uint)c.G << 8 | c.B;
    }

    private static IBrush? Solid(Color c, double alpha)
    {
        if (Argb(c, alpha) is not { } key) return null;
        if (!Solids.TryGetValue(key, out var b)) Solids[key] = b = new ImmutableSolidColorBrush(Color.FromUInt32(key));
        return b;
    }

    private static IPen? Pen(Color c, double alpha, double width)
    {
        if (Argb(c, alpha) is not { } key) return null;
        if (!Strokes.TryGetValue((key, width), out var p))
            Strokes[(key, width)] = p = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromUInt32(key)), width, lineJoin: PenLineJoin.Round);
        return p;
    }

    /// <summary>WPF RadialGradientBrush(inner, transparent) at the glow's fade.</summary>
    private static IBrush? Glow(Color c, double alpha)
    {
        if (Argb(c, ScrawlRules.GlowAlpha * alpha) is not { } key) return null;
        if (!Glows.TryGetValue(key, out var b))
            Glows[key] = b = new RadialGradientBrush
            {
                GradientStops = { new GradientStop(Color.FromUInt32(key), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
            }.ToImmutable();
        return b;
    }

    /// <summary>The stamp's glyphs, centred on its origin, built once and kept on the stamp (every window shares it).</summary>
    private Geometry? Glyphs(ScrawlStamp s)
    {
        if (s.Look is Geometry g) return g;
        var ft = new FormattedText(s.Word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _face, s.Size, Brushes.White);
        var built = ft.BuildGeometry(new Point(-ft.WidthIncludingTrailingWhitespace / 2, -ft.Height / 2));
        s.Look = built;
        return built;
    }

    public override void Render(DrawingContext dc)
    {
        base.Render(dc);
        var motion = _f.Motion;
        double fs = _f.FontSize, k = _f.K;
        double stroke = ScrawlRules.StrokeWidth * k, slamStroke = ScrawlRules.SlamStrokeWidth * k, halo = slamStroke * 3;
        using var origin = dc.PushTransform(Matrix.CreateTranslation(-_ox, -_oy));
        foreach (var s in _f.Stamps)
        {
            if (Glyphs(s) is not { } text) continue;
            var ink = Inks[s.Ink];
            double life = s.Slam ? ScrawlRules.SlamStampLife : _f.StampLife;
            bool leftover = !s.Slam;
            // Leftovers hold, then let go (ease-in); the short-lived slam stamp keeps the mockup's linear fade.
            double al = leftover ? ScrawlRules.LeftoverAlpha(s.Age, life, s.Pulse) : ScrawlRules.StampAlpha(s.Age, life, s.Pulse);
            if (al <= 0.003) continue;
            double melt = leftover ? ScrawlRules.MeltAmount(s.Age, life, motion) : 0;
            var (along, across) = ScrawlRules.LandPunch(s.Age, s.Slam, motion);
            double breath = leftover ? ScrawlRules.IdleBreath(s.Age, s.Phase, motion) : 1;
            var place = Matrix.CreateRotation(s.Rot) * Matrix.CreateTranslation(s.X, s.Y);
            if (leftover && ScrawlRules.EchoAt(s.Age, motion, out double es, out double ea))
                // The one-off ghost echo, out from where the stamp landed.
                using (dc.PushTransform(place * Matrix.CreateTranslation(-s.X, -s.Y) * Matrix.CreateScale(es, es) * Matrix.CreateTranslation(s.X, s.Y)))
                    dc.DrawGeometry(null, Pen(ink, ScrawlRules.StrokeAlpha * al * ea, stroke), text);
            using (dc.PushTransform(place * Body(s, along * breath, across * breath, melt, fs)))
            {
                // Afterglow: a halo that settles into a slow breath, phased per stamp.
                if (leftover) dc.DrawGeometry(null, Pen(ink, 0.22 * al * ScrawlRules.AfterglowStrength(s.Age, life, motion, s.Phase), halo), text);
                if (s.Slam || s.Gold) dc.DrawGeometry(null, Pen(ink, 0.22 * al, halo), text);
                dc.DrawGeometry(Solid(ink, ScrawlRules.FillAlpha * al), Pen(ink, ScrawlRules.StrokeAlpha * al, s.Slam ? slamStroke : stroke), text);
                if (Solid(ink, ScrawlRules.DotAlpha * al) is { } dot)
                    foreach (var q in s.Dots) dc.DrawEllipse(dot, null, new Point(q.X, q.Y), q.R, q.R);
            }
            if (leftover && melt > 0.001) DrawDrips(dc, s, ink, life, al, fs, motion);
        }
        double gr = ScrawlRules.GlowRadius * fs;
        foreach (var g in _f.Glows)
            if (Glow(Inks[g.Ink], ScrawlRules.GlowAlphaAt(g.Age)) is { } glow)
                dc.DrawEllipse(glow, null, new Point(g.X, g.Y), gr, gr);
        foreach (var w in _f.Waves)
        {
            double u = w.Age / ScrawlRules.WaveLife;
            for (int j = 0; j < 2; j++)
            {
                double uu = Math.Clamp(u - j * ScrawlRules.WaveLag, 0, 1);
                if (uu <= 0 || Pen(Inks[1], 0.6 * (1 - uu), (j == 0 ? 3 : 2) * k) is not { } ring) continue;
                dc.DrawEllipse(null, ring, new Point(w.X, w.Y), uu * w.Reach, uu * w.Reach);
            }
        }
        foreach (var p in _f.Sparks)
        {
            double a = 1 - p.T / p.Life, r = (1 + 2.2 * a) * k;
            if (Solid(Inks[p.Ink], a) is { } spark) dc.DrawEllipse(spark, null, new Point(p.X, p.Y), r, r);
        }
        if (_f.LabelText is not { } word || _f.LabelAge < 0) return;
        if (_label == null || _labelSerial != _f.LabelSerial)
        {
            var ft = new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _face, _f.LabelSize, Brushes.White);
            _label = ft.BuildGeometry(new Point(-ft.WidthIncludingTrailingWhitespace / 2, -ft.Height / 2));
            _labelSerial = _f.LabelSerial;
        }
        if (_label == null) return;
        double lu = _f.LabelAge / ScrawlRules.FloatLife, ls = ScrawlRules.LabelScale(_f.LabelAge, motion);
        double la = Math.Min(1, _f.LabelAge / ScrawlRules.LabelAlphaIn) * (1 - lu * lu);
        using (dc.PushTransform(Matrix.CreateScale(ls, ls) * Matrix.CreateTranslation(_f.LabelX, _f.LabelY - lu * fs * 2)))
        {
            dc.DrawGeometry(null, Pen(Inks[ScrawlField.Gold], 0.22 * la, halo), _label);   // the mockup's gold glow
            dc.DrawGeometry(Solid(LabelInk, la), null, _label);
        }
    }

    /// <summary>WPF SetBody, in world space after the stamp's placement: melt (stretch, then sag), then the landing
    /// punch and breath in stamp space (the text runs along local X, the wall normal is local Y).</summary>
    private static Matrix Body(ScrawlStamp s, double along, double across, double melt, double fs)
    {
        var m = Matrix.Identity;
        if (melt > 0.001)
            m = Matrix.CreateTranslation(-s.X, -(s.Y - s.HalfH)) * Matrix.CreateScale(1, 1 + ScrawlRules.MeltStretch * melt)
                * Matrix.CreateTranslation(s.X, s.Y - s.HalfH + melt * ScrawlRules.MeltSag * fs);
        if (Math.Abs(along - 1) > 1e-4 || Math.Abs(across - 1) > 1e-4)
            m *= Matrix.CreateTranslation(-s.X, -s.Y) * Matrix.CreateRotation(-s.Rot) * Matrix.CreateScale(across, along)
                * Matrix.CreateRotation(s.Rot) * Matrix.CreateTranslation(s.X, s.Y);
        return m;
    }

    private static void DrawDrips(DrawingContext dc, ScrawlStamp s, Color ink, double life, double al, double fs, MotionLevel motion)
    {
        double k = Math.Max(1, fs / ScrawlRules.MockupFont);
        for (int i = 0; i + 2 < s.Drips.Length; i += 3)
        {
            double t = ScrawlRules.DripProgress(s.Age, life, s.Drips[i + 1], motion);
            if (t <= 0) continue;
            double fall = t * t * ScrawlRules.DripReach * fs * s.Drips[i + 2];
            double x0 = s.X + s.Drips[i] * s.HalfW, y0 = s.Y + s.HalfH * 0.55;
            double x = x0 + ScrawlRules.DripSway(s.Age, s.Phase + i, t, motion) * fs, y = y0 + fall;
            double rx = 0.07 * fs * (1 - 0.5 * t), ry = rx * (1 + 1.8 * t), a = al * (1 - t * 0.6);
            // The thin strand the drop still hangs from, thinning out as it lets go.
            if (t < 0.6 && fall > ry && Pen(ink, 0.5 * a * (1 - t / 0.6), 0.9 * k) is { } strand)
                dc.DrawLine(strand, new Point(x0, y0), new Point(x, y - ry));
            dc.DrawEllipse(Solid(ink, ScrawlRules.DotAlpha * a), null, new Point(x, y), rx, ry);
            dc.DrawEllipse(Solid(Bead, 150 / 255.0 * a), null, new Point(x - rx * 0.35, y - ry * 0.35), rx * 0.32, ry * 0.26);   // bead highlight
        }
    }
}
