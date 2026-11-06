// The WPF drawing surface the billboard art was written against, over Avalonia's. The scenes
// (ConditioningControlPanel/Controls/Billboard/*Scenes*.cs, WheelArtView, InviteArtView, BuiltInArt,
// ...) are ported line for line: each ported file aliases DrawingContext, Matrix, the Transform
// classes, StreamGeometry and EllipseGeometry to the types below, so its paint code reads as the
// WPF original. Only the semantics the scenes use are carried over:
//  - DrawingContext's Push*/Pop stack (Avalonia returns a disposable per push);
//  - WPF's mutable Matrix (Scale/Rotate/Translate append, RotateAt/ScaleAt about a point);
//  - WPF Transform objects as matrices (RotateTransform/ScaleTransform about a centre);
//  - StreamGeometryContext's BeginFigure(start, filled, closed) / LineTo(p, stroked, smooth) form.
// Freeze() is a no-op here: Avalonia brushes and geometries need no freezing to be shared.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AMatrix = Avalonia.Matrix;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard;

/// <summary>WPF DrawingContext over an Avalonia one: the same calls, a Push/Pop stack.</summary>
public sealed class Wdc
{
    private readonly DrawingContext _c;
    private readonly Stack<(DrawingContext.PushedState State, double Opacity)> _pushed = new();

    private readonly bool _recording;

    /// <param name="recording">True over a DrawingGroup's context: Avalonia's recording context drops
    /// a rectangle's corner radii, so rounded rectangles go down as rounded geometry there.</param>
    public Wdc(DrawingContext c, bool recording = false) { _c = c; _recording = recording; }

    public DrawingContext Inner => _c;

    public void DrawRectangle(IBrush? brush, IPen? pen, Rect r) => _c.DrawRectangle(brush, pen, r);

    public void DrawRoundedRectangle(IBrush? brush, IPen? pen, Rect r, double rx, double ry)
    {
        rx = Math.Max(0, rx); ry = Math.Max(0, ry);
        if (_recording && (rx > 0 || ry > 0)) _c.DrawGeometry(brush, pen, new RectangleGeometry(r, rx, ry));
        else _c.DrawRectangle(brush, pen, r, rx, ry);
    }

    public void DrawEllipse(IBrush? brush, IPen? pen, Point center, double rx, double ry) =>
        _c.DrawEllipse(brush, pen, center, Math.Abs(rx), Math.Abs(ry));

    /// <summary>Geometries whose WPF figures had unstroked segments, to the outline WPF would stroke
    /// (<see cref="NoStroke"/> when none of it is stroked).</summary>
    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Geometry, Geometry> StrokeTwins = new();

    internal static readonly Geometry NoStroke = new StreamGeometry();

    public void DrawGeometry(IBrush? brush, IPen? pen, Geometry g)
    {
        if (pen != null && StrokeTwins.TryGetValue(g, out var stroke))
        {
            if (brush != null) _c.DrawGeometry(brush, null, g);
            if (!ReferenceEquals(stroke, NoStroke)) _c.DrawGeometry(null, pen, stroke);
            return;
        }
        _c.DrawGeometry(brush, pen, g);
    }

    public void DrawLine(IPen pen, Point a, Point b) => _c.DrawLine(pen, a, b);

    /// <summary>Note: WPF fades a pushed opacity as one layer; Avalonia multiplies each primitive's
    /// alpha. The pictures match except where a faded drawing overlaps itself (the wheel's motion
    /// ghosts read a little fainter). Group opacity (RequiresFullOpacityHandling, a save layer per
    /// push) measured 1.4x-7x the CPU frame cost, so the cheap form stays.</summary>
    public void DrawDrawing(Drawing d) => d.Draw(_c);

    public void DrawText(FormattedText text, Point origin) => _c.DrawText(text, origin);

    public void DrawImage(IImage image, Rect r) => _c.DrawImage(image, r);

    public void PushTransform(WTransform t) => _pushed.Push((_c.PushTransform(t.Value), 1));

    public void PushTransform(WMatrix m) => _pushed.Push((_c.PushTransform(m), 1));

    public void PushOpacity(double a)
    {
        a = Math.Clamp(a, 0, 1);
        _pushed.Push((_c.PushOpacity(a), a));
    }

    public void PushClip(Geometry clip) => _pushed.Push((_c.PushGeometryClip(clip), 1));

    public void Pop()
    {
        if (_pushed.Count > 0) _pushed.Pop().State.Dispose();
    }

    /// <summary>Pops whatever a paint left pushed (WPF closes a DrawingGroup the same way).</summary>
    public void Unwind()
    {
        while (_pushed.Count > 0) _pushed.Pop().State.Dispose();
    }
}

/// <summary>WPF System.Windows.Media.Matrix: mutable, row-vector, the operations append.</summary>
public struct WMatrix
{
    public double M11, M12, M21, M22, OffsetX, OffsetY;

    public WMatrix(double m11, double m12, double m21, double m22, double offsetX, double offsetY)
    {
        M11 = m11; M12 = m12; M21 = m21; M22 = m22; OffsetX = offsetX; OffsetY = offsetY;
    }

    public static WMatrix Identity => new(1, 0, 0, 1, 0, 0);

    public static WMatrix operator *(WMatrix a, WMatrix b) => new(
        a.M11 * b.M11 + a.M12 * b.M21, a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21, a.M21 * b.M12 + a.M22 * b.M22,
        a.OffsetX * b.M11 + a.OffsetY * b.M21 + b.OffsetX, a.OffsetX * b.M12 + a.OffsetY * b.M22 + b.OffsetY);

    public static WMatrix Multiply(WMatrix a, WMatrix b) => a * b;

    public void Append(WMatrix m) => this = this * m;

    public void Prepend(WMatrix m) => this = m * this;

    public void Translate(double x, double y) { OffsetX += x; OffsetY += y; }

    public void Scale(double sx, double sy) => this = this * new WMatrix(sx, 0, 0, sy, 0, 0);

    public void ScaleAt(double sx, double sy, double cx, double cy) =>
        this = this * new WMatrix(sx, 0, 0, sy, cx - sx * cx, cy - sy * cy);

    public void ScalePrepend(double sx, double sy) => this = new WMatrix(sx, 0, 0, sy, 0, 0) * this;

    public void TranslatePrepend(double x, double y) => this = new WMatrix(1, 0, 0, 1, x, y) * this;

    public void RotatePrepend(double degrees) => this = Rotation(degrees, 0, 0) * this;

    public readonly double Determinant => M11 * M22 - M12 * M21;

    public readonly bool HasInverse => Math.Abs(Determinant) > double.Epsilon;

    /// <summary>WPF Matrix.Invert (throws on a singular matrix, as WPF).</summary>
    public void Invert()
    {
        double d = Determinant;
        if (Math.Abs(d) <= double.Epsilon) throw new InvalidOperationException("Matrix is not invertible");
        this = new WMatrix(M22 / d, -M12 / d, -M21 / d, M11 / d,
            (M21 * OffsetY - M22 * OffsetX) / d, (M12 * OffsetX - M11 * OffsetY) / d);
    }

    public void Rotate(double degrees) => this = this * Rotation(degrees, 0, 0);

    public void RotateAt(double degrees, double cx, double cy) => this = this * Rotation(degrees, cx, cy);

    public void Skew(double degX, double degY) =>
        this = this * new WMatrix(1, Math.Tan(degY * Math.PI / 180), Math.Tan(degX * Math.PI / 180), 1, 0, 0);

    public readonly Point Transform(Point p) => new(p.X * M11 + p.Y * M21 + OffsetX, p.X * M12 + p.Y * M22 + OffsetY);

    internal static WMatrix Rotation(double degrees, double cx, double cy)
    {
        double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new WMatrix(c, s, -s, c, cx - c * cx + s * cy, cy - s * cx - c * cy);
    }

    public static implicit operator AMatrix(WMatrix m) => new(m.M11, m.M12, m.M21, m.M22, m.OffsetX, m.OffsetY);
}

/// <summary>WPF Transform, as the matrix it stands for.</summary>
public abstract class WTransform
{
    public abstract WMatrix Value { get; }

    public void Freeze() { }

    public static implicit operator AMatrix(WTransform t) => t.Value;
}

public sealed class WMatrixTransform : WTransform
{
    public WMatrix Matrix;

    public WMatrixTransform(WMatrix m) => Matrix = m;

    public WMatrixTransform(double m11, double m12, double m21, double m22, double ox, double oy) => Matrix = new WMatrix(m11, m12, m21, m22, ox, oy);

    public override WMatrix Value => Matrix;
}

public sealed class WTranslateTransform : WTransform
{
    public double X, Y;

    public WTranslateTransform() { }

    public WTranslateTransform(double x, double y) { X = x; Y = y; }

    public override WMatrix Value => new(1, 0, 0, 1, X, Y);
}

public sealed class WScaleTransform : WTransform
{
    public double ScaleX = 1, ScaleY = 1, CenterX, CenterY;

    public WScaleTransform() { }

    public WScaleTransform(double sx, double sy) { ScaleX = sx; ScaleY = sy; }

    public WScaleTransform(double sx, double sy, double cx, double cy) { ScaleX = sx; ScaleY = sy; CenterX = cx; CenterY = cy; }

    public override WMatrix Value => new(ScaleX, 0, 0, ScaleY, CenterX - ScaleX * CenterX, CenterY - ScaleY * CenterY);
}

public sealed class WRotateTransform : WTransform
{
    public double Angle, CenterX, CenterY;

    public WRotateTransform() { }

    public WRotateTransform(double angle) => Angle = angle;

    public WRotateTransform(double angle, double cx, double cy) { Angle = angle; CenterX = cx; CenterY = cy; }

    public override WMatrix Value => WMatrix.Rotation(Angle, CenterX, CenterY);
}

/// <summary>WPF TransformGroup: children applied first to last.</summary>
public sealed class WTransformGroup : WTransform
{
    public List<WTransform> Children { get; } = new();

    public override WMatrix Value
    {
        get
        {
            var m = WMatrix.Identity;
            foreach (var c in Children) m *= c.Value;
            return m;
        }
    }
}

/// <summary>WPF StreamGeometry: Open() hands back the WPF-shaped context. The figures are recorded
/// and built on close, because WPF's per-segment isStroked has no faithful Avalonia twin (Skia
/// leaves round-capped dots at every unstroked gap): a geometry with unstroked segments also gets
/// a stroke-only twin that <see cref="Wdc.DrawGeometry"/> strokes instead.</summary>
public sealed class WStreamGeometry
{
    private StreamGeometry _fill = new();

    public FillRule FillRule { get; set; } = FillRule.EvenOdd;

    public WStreamGeometryContext Open() => new(this);

    public void Freeze() { }

    public Geometry Geometry => _fill;

    public Rect Bounds => _fill.Bounds;

    public static implicit operator Geometry(WStreamGeometry g) => g._fill;

    internal void Build(List<WStreamGeometryContext.Figure> figures)
    {
        _fill = new StreamGeometry();
        bool partial = false;
        using (var c = _fill.Open())
        {
            c.SetFillRule(FillRule);
            foreach (var f in figures)
            {
                c.BeginFigure(f.Start, f.Filled);
                foreach (var s in f.Segments)
                {
                    s.Emit(c);
                    partial |= !s.Stroked;
                }
                c.EndFigure(f.Closed);
            }
        }
        if (!partial) return;
        // The stroke twin: each run of stroked segments as an open figure, plus a closed figure's
        // closing line (WPF always strokes it).
        var stroke = new StreamGeometry();
        bool any = false;
        using (var c = stroke.Open())
        {
            foreach (var f in figures)
            {
                var at = f.Start;
                bool open = false;
                foreach (var s in f.Segments)
                {
                    if (s.Stroked)
                    {
                        if (!open) { c.BeginFigure(at, false); open = true; any = true; }
                        s.Emit(c);
                    }
                    else if (open) { c.EndFigure(false); open = false; }
                    at = s.End;
                }
                if (f.Closed && at != f.Start)
                {
                    if (!open) { c.BeginFigure(at, false); open = true; any = true; }
                    c.LineTo(f.Start);
                }
                if (open) c.EndFigure(false);
            }
        }
        Wdc.StrokeTwins.AddOrUpdate(_fill, any ? stroke : Wdc.NoStroke);
    }
}

/// <summary>WPF StreamGeometryContext: BeginFigure(start, filled, closed), segments with isStroked.</summary>
public sealed class WStreamGeometryContext : IDisposable
{
    internal sealed class Figure
    {
        public Point Start;
        public bool Filled, Closed;
        public readonly List<Segment> Segments = new();
    }

    internal readonly record struct Segment(int Kind, Point A, Point B, Point C, Size Size, double Rotation, bool Large, SweepDirection Sweep, bool Stroked)
    {
        public Point End => Kind switch { 0 => A, 1 => B, 2 => C, _ => A };

        public void Emit(StreamGeometryContext c)
        {
            switch (Kind)
            {
                case 0: c.LineTo(A); break;
                case 1: c.QuadraticBezierTo(A, B); break;
                case 2: c.CubicBezierTo(A, B, C); break;
                default: c.ArcTo(A, Size, Rotation, Large, Sweep); break;
            }
        }
    }

    private readonly WStreamGeometry _owner;
    private readonly List<Figure> _figures = new();
    private Figure? _f;
    private bool _done;

    internal WStreamGeometryContext(WStreamGeometry owner) => _owner = owner;

    public void BeginFigure(Point start, bool isFilled, bool isClosed)
    {
        _f = new Figure { Start = start, Filled = isFilled, Closed = isClosed };
        _figures.Add(_f);
    }

    private void Add(Segment s) => (_f ?? throw new InvalidOperationException("BeginFigure first")).Segments.Add(s);

    public void LineTo(Point p, bool isStroked, bool isSmoothJoin) => Add(new(0, p, default, default, default, 0, false, default, isStroked));

    public void PolyLineTo(IList<Point> points, bool isStroked, bool isSmoothJoin)
    {
        foreach (var p in points) LineTo(p, isStroked, isSmoothJoin);
    }

    public void QuadraticBezierTo(Point control, Point end, bool isStroked, bool isSmoothJoin) =>
        Add(new(1, control, end, default, default, 0, false, default, isStroked));

    public void BezierTo(Point c1, Point c2, Point end, bool isStroked, bool isSmoothJoin) =>
        Add(new(2, c1, c2, end, default, 0, false, default, isStroked));

    public void ArcTo(Point end, Size size, double rotationAngle, bool isLargeArc, SweepDirection sweep, bool isStroked, bool isSmoothJoin) =>
        Add(new(3, end, default, default, size, rotationAngle, isLargeArc, sweep, isStroked));

    public void Close() => Dispose();

    public void Dispose()
    {
        if (_done) return;
        _done = true;
        _owner.Build(_figures);
    }
}

/// <summary>WPF EllipseGeometry(center, rx, ry).</summary>
public sealed class WEllipseGeometry
{
    private readonly EllipseGeometry _g;

    public WEllipseGeometry(Point center, double rx, double ry) => _g = new EllipseGeometry { Center = center, RadiusX = rx, RadiusY = ry };

    public WEllipseGeometry(Rect r) => _g = new EllipseGeometry(r);

    public void Freeze() { }

    public static implicit operator Geometry(WEllipseGeometry g) => g._g;
}

/// <summary>Freeze() is WPF's thread-safety step; Avalonia shares these as they are.</summary>
public static class WpfFreezeExtensions
{
    public static void Freeze(this IBrush _) { }

    public static void Freeze(this Geometry _) { }

    public static void Freeze(this IPen _) { }

    public static void Freeze(this Drawing _) { }

    public static void Freeze(this DashStyle _) { }
}
