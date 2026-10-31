using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

// WPF Controls/Billboard/BillboardVectorArt.cs:115-126,191-407; BuiltInArt.cs:298-347.
public sealed partial class CalendarArtView
{
    internal static Func<bool> ParticlesAllowed = () => Env.AllowParticles;
    internal double? SecondsForTests { get; set; }
    private readonly Dictionary<(Color, int), IBrush> _fills = new();
    private readonly Dictionary<(double, bool), IPen> _bevels = new();
    private IBrush? _ground, _vignette, _progress;
    private Size _groundSize;
    private static readonly Lazy<Geometry> SparkShape = new(() => Geometry.Parse("M0,-1 Q.12,-.12 1,0 Q.12,.12 0,1 Q-.12,.12 -1,0 Q-.12,-.12 0,-1 Z"));
    internal static double Bob(double t, double amp) => Math.Sin(t * 1.35) * amp + Math.Sin(t * .61) * amp * .35;
    private static Color Mix(Color a, Color b, double k) => Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
    private IBrush Fill(Color c, double a = 1)
    {
        var key = (c, (int)Math.Round(Math.Clamp(a, 0, 1) * 64));
        if (!_fills.TryGetValue(key, out var b)) { b = new ImmutableSolidColorBrush(c, key.Item2 / 64.0); if (_fills.Count < 256) _fills[key] = b; }
        return b;
    }
    private static IBrush Linear(Point start, Point end, params GradientStop[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(start, RelativeUnit.Relative), EndPoint = new RelativePoint(end, RelativeUnit.Relative) };
        foreach (var stop in stops) brush.GradientStops.Add(stop);
        return brush.ToImmutable();
    }
    internal void PaintGround(DrawingContext dc, double w, double h)
    {
        if (_ground == null || _groundSize != new Size(w, h))
        {
            _groundSize = new Size(w, h);
            _ground = new RadialGradientBrush { Center = new RelativePoint(.72, .45, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.72, .45, RelativeUnit.Relative), RadiusX = new RelativeScalar(.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(.75 * w / Math.Max(1, h), RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.FromRgb(0x33, 0x1a, 0x2e), 0), new(Color.FromRgb(12, 13, 26), 1) } }.ToImmutable();
            _vignette = new RadialGradientBrush { Center = new RelativePoint(.62, .48, RelativeUnit.Relative), GradientOrigin = new RelativePoint(.62, .48, RelativeUnit.Relative), RadiusX = new RelativeScalar(.85, RelativeUnit.Relative), RadiusY = new RelativeScalar(.85, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.FromArgb(0, 6, 6, 14), .55), new(Color.FromArgb(0x70, 6, 6, 14), 1) } }.ToImmutable();
        }
        dc.DrawRectangle(_ground, null, new Rect(0, 0, w, h));
        dc.DrawRectangle(_vignette, null, new Rect(0, 0, w, h));
    }
    private static double Hash(int seed, int i, int salt) { double x = Math.Sin(seed * 12.9898 + i * 78.233 + salt * 37.719) * 43758.5453; return x - Math.Floor(x); }
    internal void PaintParticles(DrawingContext dc, double w, double h, double t, bool twinkles)
    {
        if (Running && !ParticlesAllowed()) return; // WPF still frames retain particles.
        for (int i = 0; i < (twinkles ? 3 : 10); i++)
        {
            if (twinkles)
            {
                double u = t / (2.2 + Hash(67, i, 4) * 2.4) + Hash(67, i, 5), ph = u - Math.Floor(u);
                double life = ph < .35 ? Math.Sin(ph / .35 * Math.PI) : 0;
                int cycle = (int)Math.Floor(u);
                Spark(dc, new Point((.48 + Hash(67, i * 7 + cycle, 6) * .5) * w, (.08 + Hash(67, i * 7 + cycle, 7) * .84) * h), .024 * h * (.5 + .5 * life), i % 3 == 0 ? Colors.White : Mix(Accent, Colors.White, .4), life, ph * 1.2);
            }
            else
            {
                double hs = Hash(61, i, 3), life = (Hash(61, i, 2) + t * .05 * (.6 + .8 * hs)) % 1;
                double a = .35 * Math.Sin(life * Math.PI) * (.55 + .45 * Math.Sin(t * (2 + hs * 3) + i));
                if (a <= .02) continue;
                using var opacity = dc.PushOpacity(a);
                double r = .005 * h * (.6 + .8 * hs);
                dc.DrawEllipse(Fill(Accent), null, new Point((.46 + Hash(61, i, 1) * .52 + Math.Sin(t * .9 + i * 1.7) * .012) * w, (.95 - life * .9) * h), r, r);
            }
        }
    }
    internal void Spark(DrawingContext dc, Point c, double r, Color hue, double a, double turn = 0)
    {
        if (a <= .02 || r <= .3) return;
        double cs = Math.Cos(turn) * r, sn = Math.Sin(turn) * r;
        using var transform = dc.PushTransform(new Matrix(cs, sn, -sn, cs, c.X, c.Y));
        using var opacity = dc.PushOpacity(Math.Min(1, a));
        dc.DrawGeometry(Fill(hue), null, SparkShape.Value);
    }
    internal void TodayGlint(DrawingContext dc, Rect rect, double cell, double t)
    {
        double gp = (t % 3.2) / 3.2;
        if (gp < .3) Spark(dc, new Point(rect.Right - cell * .05, rect.Top + cell * .05), cell * .32 * Math.Sin(gp / .3 * Math.PI), Colors.White, .95, gp * 2);
    }
    internal void PaintProgress(DrawingContext dc, Rect bar, double bh, double t)
    {
        _progress ??= Linear(new Point(.5, 0), new Point(.5, 1), new(Mix(Accent, Colors.White, .3), 0), new(Mix(Accent, Color.FromRgb(20, 10, 20), .15), 1));
        dc.DrawRectangle(_progress, null, bar, bh / 2, bh / 2);
        Sheen(dc, bar, bh / 2, t);
        Spark(dc, new Point(bar.Right - bh * .4, bar.Y + bh / 2), bh * (.7 + .25 * Math.Sin(t * 5)), Colors.White, .8, t * .8);
    }
    private static readonly IBrush SheenBrush = Linear(new Point(0, .5), new Point(1, .5), new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(0x60, 255, 255, 255), .5), new(Color.FromArgb(0, 255, 255, 255), 1));
    internal static void Sheen(DrawingContext dc, Rect bounds, double radius, double t)
    {
        double u = (t + .6) / 3.4, k = (u - Math.Floor(u)) / .32;
        if (k <= 0 || k >= 1) return;
        double band = Math.Max(8, bounds.Width * .22), x = bounds.Left - band + (1 - Math.Pow(1 - k, 3)) * (bounds.Width + band * 2 + bounds.Height * .5);
        var g = new StreamGeometry();
        using (var ctx = g.Open()) { ctx.BeginFigure(new Point(x, bounds.Top), true); ctx.LineTo(new Point(x + band, bounds.Top)); ctx.LineTo(new Point(x + band - bounds.Height * .5, bounds.Bottom)); ctx.LineTo(new Point(x - bounds.Height * .5, bounds.Bottom)); ctx.EndFigure(true); }
        using var clip = dc.PushClip(new RoundedRect(bounds, radius));
        using var opacity = dc.PushOpacity(.8 * Math.Sin(k * Math.PI));
        dc.DrawGeometry(SheenBrush, null, g);
    }
    private static readonly IBrush RaisedBevel = Linear(new Point(0, 0), new Point(1, 1), new(Color.FromArgb(0x70, 255, 255, 255), 0), new(Color.FromArgb(0, 255, 255, 255), .45), new(Color.FromArgb(0, 0, 0, 0), .6), new(Color.FromArgb(0x60, 0, 0, 0), 1));
    private static readonly IBrush PressedBevel = Linear(new Point(0, 0), new Point(1, 1), new(Color.FromArgb(0x80, 0, 0, 0), 0), new(Color.FromArgb(0, 0, 0, 0), .45), new(Color.FromArgb(0, 255, 255, 255), .6), new(Color.FromArgb(0x38, 255, 255, 255), 1));
    internal void Plate(DrawingContext dc, Rect rect, double radius, IBrush fill, double lift, bool pressed)
    {
        if (!pressed && lift > 0)
        {
            var ink = Color.FromRgb(4, 4, 12);
            dc.DrawRectangle(Fill(ink, .22), null, new Rect(rect.X + lift * .9, rect.Y + lift * 1.4, rect.Width + lift * .4, rect.Height + lift * .4), radius + lift * .4, radius + lift * .4);
            dc.DrawRectangle(Fill(ink, .32), null, rect.Translate(new Vector(lift * .45, lift * .7)), radius, radius);
        }
        dc.DrawRectangle(fill, null, rect, radius, radius);
        double th = Math.Clamp(Math.Min(rect.Width, rect.Height) * .05, 1, 2.6);
        var inner = new Rect(rect.X + th / 2, rect.Y + th / 2, Math.Max(0, rect.Width - th), Math.Max(0, rect.Height - th));
        var key = (Math.Round(th * 4), pressed);
        if (!_bevels.TryGetValue(key, out var bevel)) { bevel = new Pen(pressed ? PressedBevel : RaisedBevel, th); if (_bevels.Count < 32) _bevels[key] = bevel; }
        dc.DrawRectangle(null, bevel, inner, Math.Max(0, radius - th / 2), Math.Max(0, radius - th / 2));
    }
}
