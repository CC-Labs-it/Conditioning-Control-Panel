// PORTED from ConditioningControlPanel/Controls/Billboard/BillboardVectorArt.cs: the base of the
// deck's drawn art (house scenes, tip scenes, wheel, invite, tables, spiral, calendar). WPF kept the
// picture in one retained DrawingGroup re-opened per tick; here a tick invalidates this one control
// (never the window) and Render paints the frame through the WPF-shaped Wdc. The clock is the
// window's shared 30 fps BeatLoop (WPF FrameClock at 30 fps), running only between Run and Hold and
// only when ambient loops are allowed; otherwise the art holds one still frame. Static layers are
// cached as DrawingGroups until the size or hue changes, exactly as WPF's Layer().

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

/// <summary>WPF IAccentedArt: a view the host can tint with the card's hue after it is made.</summary>
internal interface IAccentedArt
{
    Color Accent { set; }
}

/// <summary>WPF BillboardVectorArt.</summary>
public abstract class BillboardVectorArt : Control, IBillboardArtView, IAccentedArt
{
    /// <summary>The frame a still card shows (Motion Off, or never played): far enough in that
    /// every entrance has landed.</summary>
    public const double StillSeconds = 1.6;

    private readonly BeatLoop _beat;
    private double _offset, _now;
    private bool _released;

    protected BillboardVectorArt()
    {
        IsHitTestVisible = false;
        _beat = new BeatLoop(this, t => { _now = _offset + t; InvalidateVisual(); });
    }

    /// <summary>The card's hue.</summary>
    public Color Accent { get; set; } = Color.FromRgb(0xff, 0x4f, 0xa8);

    /// <summary>Seconds of animation shown so far.</summary>
    protected double Seconds => SecondsForTests ?? (_beat.IsRunning ? _now : (_offset > 0 ? _offset : StillSeconds));

    /// <summary>Tests and shot harnesses pin the clock here to paint a chosen moment.</summary>
    internal double? SecondsForTests { get; set; }

    /// <summary>The moment on screen (tests).</summary>
    internal double ShownSeconds => Seconds;

    /// <summary>True while the art's clock runs (tests).</summary>
    internal bool Playing => _beat.IsRunning;

    void IBillboardArtView.Run() => Play();

    void IBillboardArtView.Hold() => Pause();

    public void Play()
    {
        if (_released) return;
        if (!Env.AllowAmbientLoops) { Pause(); Redraw(); return; }
        if (_beat.IsRunning) return;
        if (_offset <= 0) _offset = 0.0001;
        _now = _offset;
        _beat.Start();
    }

    public void Pause()
    {
        if (!_beat.IsRunning) return;
        _offset = _now;
        _beat.Stop();
    }

    public void Release()
    {
        Pause();
        _released = true;
        _layers.Clear();
        _brushes.Clear();
        InvalidateVisual();
    }

    /// <summary>A pointer press on the art, in 0..1 coordinates of the view. Most views ignore it.</summary>
    public virtual void Touch(Point normalized) { }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Pause();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Redraws the picture at the current time.</summary>
    public void Redraw() => InvalidateVisual();

    public sealed override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (_released || w <= 0 || h <= 0 || double.IsNaN(w) || double.IsNaN(h)) return;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        var dc = new Wdc(context);
        try { Paint(dc, w, h, Seconds); }
        catch (Exception ex) { Log.Debug("Billboard art paint failed: {E}", ex.Message); }
        finally { dc.Unwind(); }
    }

    /// <summary>Draws one frame at time <paramref name="t"/> seconds.</summary>
    protected abstract void Paint(Wdc dc, double w, double h, double t);

    // ---- shared paint helpers ---------------------------------------------------------------

    /// <summary>A relative brush point (WPF brush points are relative by default).</summary>
    protected static RelativePoint Rel(double x, double y) => new(x, y, RelativeUnit.Relative);

    /// <summary>WPF new RadialGradientBrush(inner, outer).</summary>
    protected static RadialGradientBrush Radial(Color inner, Color outer) => new()
    {
        GradientStops = { new GradientStop(inner, 0), new GradientStop(outer, 1) },
    };

    /// <summary>WPF RadialGradientBrush's initialiser (Center, GradientOrigin, RadiusX/Y, MappingMode).
    /// WPF points and radii are relative to the shape's box unless the mapping is Absolute.</summary>
    protected static RadialGradientBrush RadialAt(RadialGradientBrush b, Point? center = null, Point? origin = null,
        double? rx = null, double? ry = null, bool absolute = false)
    {
        var unit = absolute ? RelativeUnit.Absolute : RelativeUnit.Relative;
        // WPF defaults: centre and origin (0.5, 0.5), radii 0.5 (relative).
        if (absolute || center != null) b.Center = new RelativePoint(center?.X ?? 0.5, center?.Y ?? 0.5, unit);
        if (absolute || origin != null) b.GradientOrigin = new RelativePoint(origin?.X ?? 0.5, origin?.Y ?? 0.5, unit);
        if (absolute || rx != null) b.RadiusX = new RelativeScalar(rx ?? 0.5, unit);
        if (absolute || ry != null) b.RadiusY = new RelativeScalar(ry ?? 0.5, unit);
        return b;
    }

    /// <summary>WPF new LinearGradientBrush(a, b, start, end) (relative points).</summary>
    protected static LinearGradientBrush LinearBrush(Color a, Color b, Point start, Point end) => new()
    {
        StartPoint = Rel(start.X, start.Y), EndPoint = Rel(end.X, end.Y),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };

    /// <summary>WPF new LinearGradientBrush(a, b, angleDegrees): from (0,0) toward the angle.</summary>
    protected static LinearGradientBrush LinearBrush(Color a, Color b, double angle)
    {
        double r = angle * Math.PI / 180;
        return LinearBrush(a, b, new Point(0, 0), new Point(Math.Cos(r), Math.Sin(r)));
    }

    /// <summary>The card ground: a radial pool of the hue's dark tone, off to the right where
    /// the art sits, fading to the well floor.</summary>
    protected static void Ground(Wdc dc, double w, double h, Color inner)
    {
        var g = Radial(inner, Color.FromRgb(0x0c, 0x0d, 0x1a));
        g.Center = g.GradientOrigin = Rel(0.72, 0.45);
        g.RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative);
        g.RadiusY = new RelativeScalar(0.75 * w / Math.Max(1, h), RelativeUnit.Relative);
        dc.DrawRectangle(g, null, new Rect(0, 0, w, h));
    }

    /// <summary>A dark tone of a hue for the ground (about a fifth of its brightness).</summary>
    protected static Color Deep(Color c) => Color.FromRgb((byte)(c.R * 0.2 + 8), (byte)(c.G * 0.16 + 9), (byte)(c.B * 0.22 + 18));

    protected static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    protected static SolidColorBrush Solid(Color c, double a = 1) => new(WithAlpha(c, a));

    protected static Pen Stroke(Color c, double thickness, double a = 1) =>
        new(Solid(c, a), Math.Max(0.5, thickness), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    /// <summary>Ease-out cubic, clamped.</summary>
    protected static double EaseOut(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    /// <summary>A soft round glow (no Effect): a radial brush that fades to clear.</summary>
    protected static void Glow(Wdc dc, Point c, double r, Color hue, double a) =>
        dc.DrawEllipse(Radial(WithAlpha(hue, a), WithAlpha(hue, 0)), null, c, r, r);

    // ---- cached layers and the shared juice (one lamp, top left) ------------------------------

    private readonly Dictionary<string, Drawing> _layers = new();
    private readonly Dictionary<long, IBrush> _brushes = new();
    private readonly Dictionary<int, Pen> _bevels = new();
    private Size _layerSize;
    private Color _layerAccent;

    /// <summary>A static layer painted once and kept until the size or hue changes, so a frame only
    /// re-draws what moves. Draw it straight or under a transform.</summary>
    protected Drawing Layer(string key, double w, double h, Action<Wdc> paint)
    {
        if (_layerSize.Width != w || _layerSize.Height != h || _layerAccent != Accent)
        {
            _layers.Clear();
            _layerSize = new Size(w, h);
            _layerAccent = Accent;
        }
        if (_layers.TryGetValue(key, out var cached)) return cached;
        var g = new DrawingGroup();
        using (var ctx = g.Open())
        {
            var d = new Wdc(ctx, recording: true);
            try { paint(d); }
            finally { d.Unwind(); }
        }
        _layers[key] = g;
        return g;
    }

    /// <summary>The ground plus a soft vignette, cached.</summary>
    protected void CachedGround(Wdc dc, double w, double h, Color inner) =>
        dc.DrawDrawing(Layer("ground", w, h, d =>
        {
            Ground(d, w, h, inner);
            var v = new RadialGradientBrush
            {
                Center = Rel(0.62, 0.48), GradientOrigin = Rel(0.62, 0.48),
                RadiusX = new RelativeScalar(0.85, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.85, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 6, 6, 14), 0.55), new GradientStop(Color.FromArgb(0x70, 6, 6, 14), 1) },
            };
            d.DrawRectangle(v, null, new Rect(0, 0, w, h));
        }));

    /// <summary>A solid brush from a small per-view cache (alpha in 1/64 steps).</summary>
    protected IBrush Fill(Color c, double a = 1)
    {
        int q = (int)Math.Round(Math.Clamp(a, 0, 1) * 64);
        long key = ((long)c.R << 24) | ((long)c.G << 16) | ((long)c.B << 8) | (long)q;
        if (_brushes.TryGetValue(key, out var b)) return b;
        b = new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(WithAlpha(c, q / 64.0));
        if (_brushes.Count < 256) _brushes[key] = b;
        return b;
    }

    /// <summary>True while particles may run (perf tier allows them), or on a still frame.</summary>
    protected bool Particles
    {
        get
        {
            if (!_beat.IsRunning) return true;
            try { return Env.AllowParticles; } catch { return true; }
        }
    }

    /// <summary>A fixed pseudo random in [0, 1) for slot <paramref name="i"/>, never a live rng.</summary>
    protected static double Hash(int seed, int i, int salt)
    {
        double x = Math.Sin(seed * 12.9898 + i * 78.233 + salt * 37.719) * 43758.5453;
        return x - Math.Floor(x);
    }

    /// <summary>A small idle bob of about <paramref name="amp"/> px, never in step with its neighbours.</summary>
    protected static double Bob(double t, double amp, double phase = 0) =>
        Math.Sin(t * 1.35 + phase) * amp + Math.Sin(t * 0.61 + phase * 1.7) * amp * 0.35;

    /// <summary>Motes rising through an area (normalised to the card). Two calls with different speeds
    /// and sizes make the parallax: far ones small, dim and slow, near ones larger and quicker.</summary>
    protected void Motes(Wdc dc, double w, double h, double t, Color hue, int count, int seed,
        Rect area, double speed, double size, double alpha)
    {
        if (!Particles) return;
        var brush = Fill(hue);
        for (int i = 0; i < count; i++)
        {
            double hx = Hash(seed, i, 1), hy = Hash(seed, i, 2), hs = Hash(seed, i, 3);
            double life = (hy + t * speed * (0.6 + 0.8 * hs)) % 1.0;
            double x = (area.X + hx * area.Width + Math.Sin(t * 0.9 + i * 1.7) * 0.012) * w;
            double y = (area.Bottom - life * area.Height) * h;
            double a = alpha * Math.Sin(life * Math.PI) * (0.55 + 0.45 * Math.Sin(t * (2 + hs * 3) + i));
            if (a <= 0.02) continue;
            double r = size * h * (0.6 + 0.8 * hs);
            dc.PushOpacity(a);
            dc.DrawEllipse(brush, null, new Point(x, y), r, r);
            dc.Pop();
        }
    }

    private static readonly Geometry SparkShape = Geometry.Parse("M 0,-1 Q 0.12,-0.12 1,0 Q 0.12,0.12 0,1 Q -0.12,0.12 -1,0 Q -0.12,-0.12 0,-1 Z");

    /// <summary>A four point glint, the shape a sparkle leaves (no glow ball).</summary>
    protected void Spark(Wdc dc, Point c, double r, Color hue, double a, double turn = 0)
    {
        if (a <= 0.02 || r <= 0.3) return;
        double cs = Math.Cos(turn) * r, sn = Math.Sin(turn) * r;
        dc.PushTransform(new WMatrix(cs, sn, -sn, cs, c.X, c.Y));
        dc.PushOpacity(Math.Min(1, a));
        dc.DrawGeometry(Fill(hue), null, SparkShape);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Sparkles that pop in, hold a beat and fade, each on its own slow cycle and spot.</summary>
    protected void Twinkles(Wdc dc, double w, double h, double t, Color hue, int count, int seed, Rect area, double size)
    {
        if (!Particles) return;
        for (int i = 0; i < count; i++)
        {
            double period = 2.2 + Hash(seed, i, 4) * 2.4;
            double u = t / period + Hash(seed, i, 5);
            double ph = u - Math.Floor(u);
            int cycle = (int)Math.Floor(u);
            double life = ph < 0.35 ? Math.Sin(ph / 0.35 * Math.PI) : 0;
            if (life <= 0) continue;
            double x = (area.X + Hash(seed, i * 7 + cycle, 6) * area.Width) * w;
            double y = (area.Y + Hash(seed, i * 7 + cycle, 7) * area.Height) * h;
            Spark(dc, new Point(x, y), size * h * (0.5 + 0.5 * life), i % 3 == 0 ? Colors.White : hue, life, ph * 1.2);
        }
    }

    /// <summary>The lamp sits top left: a raised plate has a lit top-left edge and a shaded
    /// bottom-right; a pressed one the reverse.</summary>
    protected Pen Bevel(double thickness, bool pressed = false)
    {
        int key = (int)Math.Round(thickness * 4) * 2 + (pressed ? 1 : 0);
        if (_bevels.TryGetValue(key, out var pen)) return pen;
        var g = new LinearGradientBrush { StartPoint = Rel(0, 0), EndPoint = Rel(1, 1) };
        if (!pressed)
        {
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.45));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.6));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x60, 0, 0, 0), 1));
        }
        else
        {
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0, 0, 0), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0.45));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 255, 255, 255), 0.6));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0x38, 255, 255, 255), 1));
        }
        pen = new Pen(g, Math.Max(0.75, thickness));
        if (_bevels.Count < 32) _bevels[key] = pen;
        return pen;
    }

    /// <summary>A soft drop shadow down and right of a rounded rect (two stacked shades, no Effect).</summary>
    protected void Shadow(Wdc dc, Rect r, double radius, double lift)
    {
        if (lift <= 0) return;
        var ink = Color.FromRgb(4, 4, 12);
        dc.DrawRoundedRectangle(Fill(ink, 0.22), null, new Rect(r.X + lift * 0.9, r.Y + lift * 1.4, r.Width + lift * 0.4, r.Height + lift * 0.4), radius + lift * 0.4, radius + lift * 0.4);
        dc.DrawRoundedRectangle(Fill(ink, 0.32), null, new Rect(r.X + lift * 0.45, r.Y + lift * 0.7, r.Width, r.Height), radius, radius);
    }

    /// <summary>A rounded plate with depth: shadow when raised, the lamp's bevel either way.</summary>
    protected void Plate(Wdc dc, Rect r, double radius, IBrush fill, double lift, bool pressed = false)
    {
        if (!pressed) Shadow(dc, r, radius, lift);
        dc.DrawRoundedRectangle(fill, null, r, radius, radius);
        double th = Math.Clamp(Math.Min(r.Width, r.Height) * 0.05, 1, 2.6);
        var inner = new Rect(r.X + th / 2, r.Y + th / 2, Math.Max(0, r.Width - th), Math.Max(0, r.Height - th));
        dc.DrawRoundedRectangle(null, Bevel(th, pressed), inner, Math.Max(0, radius - th / 2), Math.Max(0, radius - th / 2));
    }

    private static readonly IBrush SheenBrush = new LinearGradientBrush
    {
        StartPoint = Rel(0, 0.5), EndPoint = Rel(1, 0.5),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0),
            new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0.5),
            new GradientStop(Color.FromArgb(0, 255, 255, 255), 1),
        },
    }.ToImmutable();

    /// <summary>A slanted highlight band crossing a shape once per <paramref name="period"/> seconds,
    /// clipped to it. It is off the shape most of the period, so it reads as a glint, not a strobe.</summary>
    protected static void Sheen(Wdc dc, Geometry clip, Rect bounds, double t, double period, double offset = 0, double strength = 1)
    {
        double u = (t + offset) / period;
        double k = (u - Math.Floor(u)) / 0.32;
        if (k <= 0 || k >= 1) return;
        double band = Math.Max(8, bounds.Width * 0.22);
        double x = bounds.Left - band + EaseOut(k) * (bounds.Width + band * 2 + bounds.Height * 0.5);
        var g = new WStreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(x, bounds.Top), true, true);
            ctx.LineTo(new Point(x + band, bounds.Top), false, false);
            ctx.LineTo(new Point(x + band - bounds.Height * 0.5, bounds.Bottom), false, false);
            ctx.LineTo(new Point(x - bounds.Height * 0.5, bounds.Bottom), false, false);
        }
        dc.PushClip(clip);
        dc.PushOpacity(Math.Clamp(strength, 0, 1) * Math.Sin(k * Math.PI));
        dc.DrawGeometry(SheenBrush, null, g);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Back-out ease (a small overshoot), clamped at 0 and settling on 1.</summary>
    protected static double BackOut(double t, double s = 1.6)
    {
        t = Math.Clamp(t, 0, 1);
        double u = t - 1;
        return 1 + (s + 1) * u * u * u + s * u * u;
    }

    /// <summary>Two hues mixed: toward white for a lit face, toward ink for shade.</summary>
    protected static Color Mix(Color a, Color b, double k)
    {
        k = Math.Clamp(k, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
    }

    public static Color ParseHue(string? hex, Color fallback) =>
        !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c) ? c : fallback;
}
