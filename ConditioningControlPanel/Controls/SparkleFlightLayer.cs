using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The surface the Sparkles flight is drawn on: a click-through overlay that fills its host and
/// draws everything into ONE <see cref="DrawingGroup"/>, rebuilt per frame off a 30 fps
/// <see cref="FrameClock"/> that runs only while a star, a ring or a mote is alive. No Effect, no
/// layout per frame, nothing held between flights.
///
/// <para>IN and OUT: stars fade in out of the spot and shrink into the wallet (or fade on the way
/// out of it); a landing ring grows and fades, motes spray and fade. Under gentle visuals nothing
/// rises faster than <see cref="GentleVisuals.RiseFloorMs"/>.</para>
/// </summary>
public sealed class SparkleFlightLayer : FrameworkElement
{
    private static readonly Brush Gold = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x80)));
    private static readonly Pen RingPen = FrozenPen(Color.FromRgb(0xFF, 0xD5, 0x80), 2);
    private static readonly Pen BigRingPen = FrozenPen(Color.FromRgb(0xFF, 0xE8, 0xA9), 2.5);

    private sealed record Star(SparkleStarPath Path, Action<int> Landed, int Index) { public bool Done; }
    private readonly record struct Ring(double X, double Y, double T0, bool Big);
    private readonly record struct Mote(double X, double Y, double Dx, double Dy, double T0, double Life, double R);

    private readonly DrawingGroup _drawing = new();
    private readonly FrameClock _clock = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _time = new();
    private readonly List<Star> _stars = new();
    private readonly List<Ring> _rings = new();
    private readonly List<Mote> _motes = new();
    private readonly Random _rng = new();
    private double _flightStart;

    public SparkleFlightLayer()
    {
        IsHitTestVisible = false;
        Focusable = false;
        _clock.Tick += (_, _) => Frame();
        _time.Start();
        Unloaded += (_, _) => Clear();
    }

    public bool IsBusy => _stars.Count > 0;

    private double Now => _time.Elapsed.TotalMilliseconds;

    /// <summary>Flies <paramref name="paths"/>; <paramref name="landed"/> runs per star as it arrives
    /// (its index), on the UI thread.</summary>
    public void Fly(IReadOnlyList<SparkleStarPath> paths, Action<int> landed)
    {
        _flightStart = Now;
        for (var i = 0; i < paths.Count; i++) _stars.Add(new Star(paths[i], landed, i));
        Wake();
    }

    /// <summary>A landing: a gold ring and a few motes (a dozen for the last one).</summary>
    public void Ping(Point at, bool big)
    {
        var t = Now;
        _rings.Add(new Ring(at.X, at.Y, t, big));
        for (int k = big ? 12 : 4, i = 0; i < k; i++)
        {
            var a = _rng.NextDouble() * Math.PI * 2;
            var d = (big ? 26 : 12) + _rng.NextDouble() * (big ? 30 : 14);
            _motes.Add(new Mote(at.X, at.Y, Math.Cos(a) * d, Math.Sin(a) * d + 6, t, 360 + _rng.NextDouble() * 300,
                1.2 + _rng.NextDouble() * 1.3));
        }
        Wake();
    }

    /// <summary>Drops everything at once without running a landing. The caller puts the number right.</summary>
    public void Clear()
    {
        _stars.Clear(); _rings.Clear(); _motes.Clear();
        _clock.Stop();
        using (_drawing.Open()) { }
    }

    protected override void OnRender(DrawingContext dc) => dc.DrawDrawing(_drawing);

    private void Wake()
    {
        if (!_clock.IsEnabled) _clock.Start();
        Frame();
    }

    private void Frame()
    {
        var now = Now;
        var gentle = GentleVisuals.On;
        var picture = SparkleStar.Shared;
        // Landings first, so a callback that pings is drawn this frame.
        foreach (var s in _stars.ToArray())
        {
            if (s.Done || now - _flightStart < s.Path.DelayMs + s.Path.DurMs) continue;
            s.Done = true;
            try { s.Landed(s.Index); } catch (Exception ex) { App.Logger?.Debug("Sparkle landing: {E}", ex.Message); }
        }
        _stars.RemoveAll(s => s.Done);
        _rings.RemoveAll(r => now - r.T0 > 800);
        _motes.RemoveAll(m => now - m.T0 > m.Life);

        using (var dc = _drawing.Open())
        {
            foreach (var s in _stars)
            {
                if (picture == null || SparkleFlightPlan.Pose(s.Path, now - _flightStart) is not { } p) continue;
                var alpha = gentle ? Math.Min(p.Alpha, (now - _flightStart - s.Path.DelayMs) / GentleVisuals.RiseFloorMs) : p.Alpha;
                if (alpha <= 0.01) continue;
                var half = SparkleFlightPlan.StarPx * p.Scale / 2;
                var m = Matrix.Identity;
                m.Rotate(p.Angle);
                m.Translate(p.X, p.Y);
                dc.PushTransform(new MatrixTransform(m));
                dc.PushOpacity(Math.Clamp(alpha, 0, 1));
                dc.DrawImage(picture, new Rect(-half, -half, half * 2, half * 2));
                dc.Pop(); dc.Pop();
            }
            foreach (var r in _rings)
            {
                var k = (now - r.T0) / 800;
                var e = 1 - Math.Pow(1 - k, 3);
                var alpha = 0.9 * (1 - k) * Rise(now - r.T0, gentle);
                var rad = 4 + e * (r.Big ? 30 : 16);
                dc.PushOpacity(Math.Clamp(alpha, 0, 1));
                dc.DrawEllipse(null, r.Big ? BigRingPen : RingPen, new Point(r.X, r.Y), rad, rad);
                dc.Pop();
            }
            foreach (var m in _motes)
            {
                var k = (now - m.T0) / m.Life;
                var e = SparkleFlightPlan.Ease(k, 0.1, 0.7, 0.3, 1);
                dc.PushOpacity(Math.Clamp((1 - k) * Rise(now - m.T0, gentle), 0, 1));
                var r = m.R * (1 - 0.8 * e);
                dc.DrawEllipse(Gold, null, new Point(m.X + m.Dx * e, m.Y + m.Dy * e), r, r);
                dc.Pop();
            }
        }
        if (_stars.Count == 0 && _rings.Count == 0 && _motes.Count == 0) _clock.Stop();
    }

    private static double Rise(double ageMs, bool gentle) => gentle ? Math.Min(1, ageMs / GentleVisuals.RiseFloorMs) : 1;

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static Pen FrozenPen(Color c, double w)
    {
        var p = new Pen(new SolidColorBrush(c), w);
        p.Freeze();
        return p;
    }
}
