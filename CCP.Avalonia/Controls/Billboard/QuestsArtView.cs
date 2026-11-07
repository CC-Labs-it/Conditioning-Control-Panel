using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

/// <summary>Today's quests as a clipboard. The stamp replays completed progress only.</summary>
public sealed class QuestsArtView : Control, IBillboardArtView
{
    private readonly int _shown, _done;
    private readonly BeatLoop _beat;
    private double _time = 1.6, _held;
    private bool _released;
    private static readonly Color Ink = Color.Parse("#2a1c44"), Mint = Color.Parse("#3ce67d"), Pink = Color.Parse("#ff4fa8"), Accent = Color.Parse(CardHues.Waiting);
    private static readonly Geometry Check = Geometry.Parse("M -0.52,0.02 L -0.14,0.38 L 0.52,-0.38");
    private static readonly Geometry Gem = Geometry.Parse("M 0,-1 L 0.75,0 L 0,1 L -0.75,0 Z");
    private static readonly Geometry SparkShape = Geometry.Parse("M 0,-1 Q 0.12,-0.12 1,0 Q 0.12,0.12 0,1 Q -0.12,0.12 -1,0 Q -0.12,-0.12 0,-1 Z");
    private static readonly Geometry Arrow = Geometry.Parse("M -1,-0.34 L 0.1,-0.34 L 0.1,-0.82 L 1,0 L 0.1,0.82 L 0.1,0.34 L -1,0.34 Z");

    public QuestsArtView(object? data)
    {
        var counts = QuestClipboard.Data(data);
        (_shown, _done) = QuestClipboard.Fit(counts.Done, counts.Total);
        IsHitTestVisible = false;
        ClipToBounds = true;
        _beat = new BeatLoop(this, t => { _time = _held + t; InvalidateVisual(); });
    }

    public int Shown => _shown;
    public int DoneShown => _done;
    internal bool ShowSheen = true, ShowStamp = true; // Isolate effects in render proofs.
    internal double Seconds => _time;
    internal bool Running => _beat.IsRunning;
    internal void Frame(double t) { _time = t; InvalidateVisual(); }
    // WPF BillboardVectorArt.Particles: a still frame keeps its particles.
    private bool Particles => !_beat.IsRunning || Env.AllowParticles;
    public void Run()
    {
        if (_released) return;
        if (!Env.AllowAmbientLoops) { Hold(); InvalidateVisual(); return; }
        if (!_beat.IsRunning) _beat.Start();
    }
    public void Hold() { if (_beat.IsRunning) _held = _time; _beat.Stop(); }
    public void Release() { Hold(); _released = true; }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { Hold(); base.OnDetachedFromVisualTree(e); }

    private static SolidColorBrush Fill(Color c, double a = 1) => new(c, Math.Clamp(a, 0, 1));
    private static double Ease(double k) => 1 - Math.Pow(1 - Math.Clamp(k, 0, 1), 3);
    private static double Back(double k) { k = Math.Clamp(k, 0, 1) - 1; return 1 + 2.70158 * k * k * k + 1.70158 * k * k; }
    private static LinearGradientBrush Face(Color a, Color b) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };
    internal static Pen Bevel(double thickness, bool pressed)
    {
        var g = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative) };
        g.GradientStops.Add(new GradientStop(pressed ? Color.FromArgb(0x80, 0, 0, 0) : Color.FromArgb(0x70, 255, 255, 255), 0));
        g.GradientStops.Add(new GradientStop(pressed ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(0, 255, 255, 255), 0.45));
        g.GradientStops.Add(new GradientStop(pressed ? Color.FromArgb(0, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0), 0.6));
        g.GradientStops.Add(new GradientStop(pressed ? Color.FromArgb(0x38, 255, 255, 255) : Color.FromArgb(0x60, 0, 0, 0), 1));
        return new Pen(g, Math.Max(0.75, thickness));
    }
    internal static void Shadow(DrawingContext d, Rect r, double radius, double lift)
    {
        if (lift <= 0) return;
        var ink = Color.FromRgb(4, 4, 12);
        d.DrawRectangle(Fill(ink, 0.22), null, new Rect(r.X + lift * 0.9, r.Y + lift * 1.4, r.Width + lift * 0.4, r.Height + lift * 0.4), radius + lift * 0.4, radius + lift * 0.4);
        d.DrawRectangle(Fill(ink, 0.32), null, new Rect(r.X + lift * 0.45, r.Y + lift * 0.7, r.Width, r.Height), radius, radius);
    }
    internal static void Plate(DrawingContext d, Rect r, double radius, IBrush fill, double lift = 0, bool pressed = false)
    {
        if (!pressed) Shadow(d, r, radius, lift);
        d.DrawRectangle(fill, null, r, radius, radius);
        double th = Math.Clamp(Math.Min(r.Width, r.Height) * 0.05, 1, 2.6);
        var inner = new Rect(r.X + th / 2, r.Y + th / 2, Math.Max(0, r.Width - th), Math.Max(0, r.Height - th));
        d.DrawRectangle(null, Bevel(th, pressed), inner, Math.Max(0, radius - th / 2), Math.Max(0, radius - th / 2));
    }
    internal static void PaintHalo(DrawingContext d, double sw, double sh, double t) =>
        d.DrawRectangle(null, new Pen(Fill(Accent, 0.25 + 0.35 * (0.5 + 0.5 * Math.Sin(t * 3.1))), sh * 0.05), new Rect(-sw / 2 - sh * 0.06, -sh * 0.56, sw + sh * 0.12, sh * 1.12), sh * 0.2, sh * 0.2);
    private static void Mark(DrawingContext d, Point c, double r, double alpha = 1)
    {
        using var pose = d.PushTransform(new Matrix(r, 0, 0, r, c.X, c.Y));
        d.DrawGeometry(null, new Pen(Fill(Colors.White, alpha), 0.26, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Check);
    }
    private static double Hash(int seed, int i, int salt) { double x = Math.Sin(seed * 12.9898 + i * 78.233 + salt * 37.719) * 43758.5453; return x - Math.Floor(x); }
    private static void Spark(DrawingContext d, Point c, double r, Color hue, double a, double turn)
    {
        if (a <= 0.02 || r <= 0.3) return;
        double cs = Math.Cos(turn) * r, sn = Math.Sin(turn) * r;
        using (d.PushTransform(new Matrix(cs, sn, -sn, cs, c.X, c.Y))) d.DrawGeometry(Fill(hue, a), null, SparkShape);
    }
    private static void Sparks(DrawingContext d, Point box, double br, double sh, double f)
    {
        if (f >= 0.7) return;
        double k = Ease(f / 0.7);
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4 + 0.3, dist = br * (1.3 + 2.4 * k) * (0.8 + 0.4 * Hash(13, i, 2));
            Spark(d, new Point(box.X + Math.Cos(a) * dist, box.Y + Math.Sin(a) * dist * 0.8), sh * 0.13 * (1 - k * 0.6), i % 2 == 0 ? Colors.White : Accent, 1 - k, a);
        }
    }
    private static void Motes(DrawingContext d, double w, double h, double t, Color hue, int count, int seed, Rect area, double speed, double size, double alpha)
    {
        for (int i = 0; i < count; i++)
        {
            double hx = Hash(seed, i, 1), hy = Hash(seed, i, 2), hs = Hash(seed, i, 3), life = (hy + t * speed * (0.6 + 0.8 * hs)) % 1.0;
            double a = alpha * Math.Sin(life * Math.PI) * (0.55 + 0.45 * Math.Sin(t * (2 + hs * 3) + i)), r = size * h * (0.6 + 0.8 * hs);
            if (a > 0.02) d.DrawEllipse(Fill(hue, a), null, new Point((area.X + hx * area.Width + Math.Sin(t * 0.9 + i * 1.7) * 0.012) * w, (area.Bottom - life * area.Height) * h), r, r);
        }
    }
    private static void Twinkles(DrawingContext d, double w, double h, double t, Color hue, int count, int seed, Rect area, double size)
    {
        for (int i = 0; i < count; i++)
        {
            double u = t / (2.2 + Hash(seed, i, 4) * 2.4) + Hash(seed, i, 5), ph = u - Math.Floor(u);
            int at = i * 7 + (int)Math.Floor(u);
            if (ph >= 0.35) continue;
            double life = Math.Sin(ph / 0.35 * Math.PI);
            Spark(d, new Point((area.X + Hash(seed, at, 6) * area.Width) * w, (area.Y + Hash(seed, at, 7) * area.Height) * h), size * h * (0.5 + 0.5 * life), i % 3 == 0 ? Colors.White : hue, life, ph * 1.2);
        }
    }
    private static RadialGradientBrush Radial(double cx, double cy, double rx, double ry, GradientStop a, GradientStop b) => new()
    {
        Center = new RelativePoint(cx, cy, RelativeUnit.Relative), GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(rx, RelativeUnit.Relative), RadiusY = new RelativeScalar(ry, RelativeUnit.Relative), GradientStops = { a, b },
    };

    public override void Render(DrawingContext d)
    {
        base.Render(d);
        double w = Bounds.Width, h = Bounds.Height, t = _time;
        if (w <= 0 || h <= 0) return;
        // WPF Ground(Deep(Accent)) plus CachedGround's vignette; Deep(#ffc94a) = #3b2922.
        d.DrawRectangle(Radial(0.72, 0.45, 0.75, 0.75 * w / Math.Max(1, h), new GradientStop(Color.Parse("#3b2922"), 0), new GradientStop(Color.Parse("#0c0d1a"), 1)), null, new Rect(Bounds.Size));
        d.DrawRectangle(Radial(0.62, 0.48, 0.85, 0.85, new GradientStop(Color.FromArgb(0, 6, 6, 14), 0.55), new GradientStop(Color.FromArgb(0x70, 6, 6, 14), 1)), null, new Rect(Bounds.Size));
        bool fx = Particles;
        if (fx) Motes(d, w, h, t, Accent, 12, 23, new Rect(0.5, 0.08, 0.48, 0.86), 0.05, 0.005, 0.4);
        Board(d, w, h, t, fx);
        if (!fx) return;
        Twinkles(d, w, h, t, Color.Parse("#ffdb89"), 4, 29, new Rect(0.52, 0.1, 0.44, 0.8), 0.028); // Mix(Accent, White, 0.35)
        Motes(d, w, h, t, Colors.White, 5, 31, new Rect(0.55, 0.2, 0.42, 0.8), 0.1, 0.008, 0.3);
    }

    private void Board(DrawingContext d, double w, double h, double t, bool fx)
    {
        double bw = Math.Min(w * 0.36, h * 0.85), bh = QuestClipboard.BoardHeight(h, _shown);
        double intro = Back(t / 0.7), cy = h * 0.52 + Math.Sin(t) * h * 0.008 + (1 - intro) * h * 0.4;
        using var opacity = d.PushOpacity(Math.Clamp(intro * 1.5, 0, 1));
        using var pose = d.PushTransform(Matrix.CreateRotation((-2.5 + Math.Sin(t * 0.7) * 0.8) * Math.PI / 180) * Matrix.CreateTranslation(w * 0.73, cy));
        Plate(d, new Rect(-bw / 2, -bh / 2, bw, bh), bw * 0.07, Face(Color.Parse("#463466"), Color.Parse("#261c3c")), bh * 0.035);
        Plate(d, new Rect(-bw * 0.46, -bh * 0.415, bw * 0.92, bh * 0.875), bw * 0.05, Fill(Color.FromRgb(4, 4, 12), 0.22), pressed: true);
        double ch = Math.Min(bh * 0.11, bw * 0.13);
        Plate(d, new Rect(-bw * 0.19, -bh / 2 - ch / 2, bw * 0.38, ch), ch * 0.22, Face(Color.Parse("#ffeab0"), Color.Parse("#b18a24")), ch * 0.16);
        Plate(d, new Rect(-bw * 0.08, -bh / 2 - ch * 0.2, bw * 0.16, ch * 0.32), ch * 0.16, Fill(Color.FromRgb(4, 4, 12), 0.75), pressed: true);
        double avail = bh - h * 0.155, gap = h * 0.025;
        double sh = Math.Min(h * 0.15, (avail - gap * (_shown - 1)) / _shown), sw = bw * 0.86;
        double y0 = -bh / 2 + h * 0.1 + (avail - (_shown * sh + (_shown - 1) * gap)) / 2;
        double p = t < QuestClipboard.LoopStart ? -1 : (t - QuestClipboard.LoopStart) % QuestClipboard.LoopSeconds;
        int target = QuestClipboard.StampTarget(t, _done);
        for (int i = 0; i < _shown; i++)
        {
            bool done = i < _done, next = i == _done;
            double enter = Back((t - 0.25 - i * 0.11) / 0.45);
            if (enter <= 0) continue;
            double dx = next ? -sh * 0.2 : 0;
            if (next && p >= 3.45) dx += Math.Sin((p - 3.45) * 20) * Math.Exp(-(p - 3.45) * 5) * sh * 0.14;
            double dy = (1 - enter) * -bh * 0.45;
            if (i == target && p >= 1.62) dy += Math.Exp(-(p - 1.62) * 9) * Math.Abs(Math.Cos((p - 1.62) * 22)) * sh * 0.07;
            var c = new Point(dx, y0 + i * (sh + gap) + sh / 2 + dy);
            using (d.PushOpacity(Math.Clamp(enter * 2, 0, 1)))
            using (d.PushTransform(Matrix.CreateRotation((Math.Sin(i * 7.13) * 1.5 + (next ? Math.Sin(t * 1.3) * 0.6 : 0)) * Math.PI / 180) * Matrix.CreateTranslation(c.X, c.Y)))
            {
                Plate(d, new Rect(-sw / 2, -sh / 2, sw, sh), sh * 0.16,
                    done ? Face(Color.Parse("#74677f"), Color.Parse("#4b3d59")) : Face(Color.Parse("#fff0d8"), Color.Parse("#ead39a")), done ? 0 : sh * 0.1, pressed: done);
                var box = new Point(-sw / 2 + sh * 0.5, 0);
                d.DrawEllipse(Fill(done ? Mint : Ink, done ? 0.5 : 0.08), new Pen(Fill(done ? Mint : Ink, 0.5), sh * 0.04), box, sh * 0.27, sh * 0.27);
                if (done) Mark(d, box, sh * 0.24, 0.85);
                double x = -sw / 2 + sh * 0.98, len = sw - sh * 1.93;
                d.DrawRectangle(Fill(done ? Colors.White : Ink, 0.3), null, new Rect(x, -sh * 0.18, len * 0.8, sh * 0.13), sh * 0.06, sh * 0.06);
                d.DrawRectangle(Fill(done ? Colors.White : Ink, 0.16), null, new Rect(x, sh * 0.07, len * 0.5, sh * 0.11), sh * 0.05, sh * 0.05);
                if (done) d.DrawLine(new Pen(Fill(Mint, 0.7), sh * 0.06), new Point(x, -sh * 0.1), new Point(x + len * 0.86, -sh * 0.13));
                using (d.PushTransform(new Matrix(sh * 0.22, 0, 0, sh * 0.22, sw / 2 - sh * 0.45, 0))) d.DrawGeometry(Fill(done ? Colors.White : Pink, done ? 0.14 : 1), null, Gem);
                if (next)
                {
                    PaintHalo(d, sw, sh, t);
                    if (ShowSheen) PaintSheen(d, new Rect(-sw / 2, -sh / 2, sw, sh), t);
                    double poke = p >= 3.25 && p < 3.7 ? Math.Sin((p - 3.25) / 0.45 * Math.PI) * sh * 0.224 : 0;
                    using (d.PushTransform(new Matrix(sh * 0.364, 0, 0, sh * 0.364, -sw / 2 - sh * 0.53 + Math.Sin(t * 3.2) * sh * 0.056 + poke, 0))) d.DrawGeometry(Fill(Pink), null, Arrow);
                }
                if (i == target && p >= 1.62 && p < 2.82)
                {
                    double f = p - 1.62, flare = Math.Exp(-f * 2.6);
                    d.DrawEllipse(Fill(Mint, flare * 0.85), null, box, sh * 0.27 * (1 + 0.25 * flare), sh * 0.27 * (1 + 0.25 * flare));
                    Mark(d, box, sh * 0.27 * (0.9 + 0.4 * Math.Exp(-f * 7)), Math.Min(1, 0.85 + flare));
                    double e = Ease(f / 0.55);
                    d.DrawEllipse(null, new Pen(Fill(Mint, 0.8 * (1 - e)), sh * 0.06 * (1 - e) + 0.6), box, sh * 0.27 * (1.2 + 1.6 * e), sh * 0.27 * (1.2 + 1.6 * e));
                    if (fx) Sparks(d, box, sh * 0.27, sh, f);
                }
            }
        }
        if (ShowStamp) PaintStamp(d, sh, sw, bh, y0, gap, target, p);
    }

    internal static void PaintSheen(DrawingContext d, Rect bounds, double t)
    {
        double u = (t + 0.4) / 2.9, k = (u - Math.Floor(u)) / 0.32;
        if (k <= 0 || k >= 1) return;
        double band = Math.Max(8, bounds.Width * 0.22);
        double x = bounds.Left - band + Ease(k) * (bounds.Width + band * 2 + bounds.Height * 0.5);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(x, bounds.Top), true);
            c.LineTo(new Point(x + band, bounds.Top));
            c.LineTo(new Point(x + band - bounds.Height * 0.5, bounds.Bottom));
            c.LineTo(new Point(x - bounds.Height * 0.5, bounds.Bottom));
            c.EndFigure(true);
        }
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0x60, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
        };
        using var clip = d.PushClip(new RoundedRect(bounds, bounds.Height * 0.16));
        using var opacity = d.PushOpacity(0.95 * Math.Sin(k * Math.PI));
        d.DrawGeometry(brush, null, g);
    }

    internal static Matrix StampTransform(double p, double sh, double bh, double x, double y)
    {
        var f = QuestClipboard.StampFrame(p, sh, bh);
        return Matrix.CreateScale(f.Sx, f.Sy) * Matrix.CreateRotation(f.Tilt * Math.PI / 180) * Matrix.CreateTranslation(x, y + f.Y);
    }

    internal static void PaintStamp(DrawingContext d, double sh, double sw, double bh, double y0, double gap, int target, double p)
    {
        if (target >= 0 && p >= 0.6 && p <= 3.2)
        {
            var f = QuestClipboard.StampFrame(p, sh, bh);
            double s = sh * 1.4;
            using var fade = d.PushOpacity(f.Alpha);
            using var stamp = d.PushTransform(StampTransform(p, sh, bh, -sw / 2 + sh * 0.5, y0 + target * (sh + gap) + sh * 0.62));
            Plate(d, new Rect(-s * 0.4, -s * 0.13, s * 0.8, s * 0.13), s * 0.03, Fill(Ink));
            Plate(d, new Rect(-s * 0.36, -s * 0.5, s * 0.72, s * 0.38), s * 0.08, Face(Pink, Color.Parse("#993366")), s * 0.03);
            Plate(d, new Rect(-s * 0.09, -s * 0.78, s * 0.18, s * 0.3), s * 0.04, Fill(Color.Parse("#8a629c")));
            d.DrawEllipse(Face(Color.Parse("#ffb0d5"), Pink), new Pen(Fill(Colors.White, 0.3), 1), new Point(0, -s * 0.9), s * 0.21, s * 0.18);
        }
    }
}
