using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.FrameCore;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Draws the Prize Counter profile frames (CCP.Core/Frames/ProfileFrames.cs) for WPF. The rules live in Core;
    /// this only turns the steps into geometry. Two ways in:
    /// <list type="bullet">
    /// <item><see cref="Still"/>: one frozen picture per frame and detail (list rows, lobby cards).</item>
    /// <item><see cref="Attach"/>: a living frame on an Image (profile card, the customize picker). The steps that never
    /// move are drawn once into frozen groups; only the moving ones are redrawn, by ONE shared 30 fps clock for every
    /// frame on screen, and only while the frame is visible in an active window.</item>
    /// </list>
    /// The canvas is drawn at <see cref="Unit"/> units square and clipped to it, so the picture keeps the one frame
    /// geometry (ShopCosmetics.FrameWindowRatio) at any size.
    /// </summary>
    public static class ProfileFrameArt
    {
        public const double Unit = 100;

        private static readonly Dictionary<string, Brush> _brushes = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, DrawingImage> _stills = new(StringComparer.Ordinal);
        private static readonly Rect Box = new(0, 0, Unit, Unit);
        private static readonly Geometry BoxClip = Freeze(new RectangleGeometry(Box));

        private static T Freeze<T>(T f) where T : Freezable { if (f.CanFreeze) f.Freeze(); return f; }

        private static Brush BrushOf(string hex)
        {
            lock (_brushes)
            {
                if (_brushes.TryGetValue(hex, out var b)) return b;
                var c = (Color)ColorConverter.ConvertFromString(hex);
                b = Freeze(new SolidColorBrush(c));
                _brushes[hex] = b;
                return b;
            }
        }

        private static readonly Dictionary<(string, double), Pen> _pens = new();

        private static Pen PenOf(string hex, double width)
        {
            var key = (hex, Math.Round(width, 3));
            lock (_pens)
            {
                if (_pens.TryGetValue(key, out var p)) return p;
                p = Freeze(new Pen(BrushOf(hex), key.Item2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
                _pens[key] = p;
                return p;
            }
        }

        /// <summary>The geometry of one step, in units (canvas 0..<see cref="Unit"/>).</summary>
        internal static Geometry? GeometryOf(FramePrim p)
        {
            const double s = Unit;
            if (p.Kind == FramePrimKind.Ellipse)
            {
                if (p.Rx <= 0 || p.Ry <= 0) return null;
                var e = new EllipseGeometry(new Point(p.X * s, p.Y * s), p.Rx * s, p.Ry * s);
                if (Math.Abs(p.Rot) > 0.01) e.Transform = new RotateTransform(p.Rot, p.X * s, p.Y * s);
                return Freeze(e);
            }
            var q = p.Pts;
            if (q == null || q.Length < 4) return null;
            int n = q.Length / 2;
            Point P(int i) => new(q[2 * i] * s, q[2 * i + 1] * s);
            static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var g = new StreamGeometry { FillRule = p.Hole > 0 ? FillRule.EvenOdd : FillRule.Nonzero };
            using (var c = g.Open())
            {
                var filled = p.Fill != null;
                if (p.Smooth && n > 2)
                {
                    if (p.Closed)
                    {
                        c.BeginFigure(Mid(P(n - 1), P(0)), filled, true);
                        for (var i = 0; i < n; i++) c.QuadraticBezierTo(P(i), Mid(P(i), P((i + 1) % n)), true, true);
                    }
                    else
                    {
                        c.BeginFigure(P(0), filled, false);
                        for (var i = 1; i < n - 1; i++) c.QuadraticBezierTo(P(i), Mid(P(i), P(i + 1)), true, true);
                        c.LineTo(P(n - 1), true, true);
                    }
                }
                else
                {
                    c.BeginFigure(P(0), filled, p.Closed);
                    for (var i = 1; i < n; i++) c.LineTo(P(i), true, true);
                }
                if (p.Hole > 0)
                {
                    var r = p.Hole * s;
                    c.BeginFigure(new Point(s / 2 + r, s / 2), true, true);
                    c.ArcTo(new Point(s / 2 - r, s / 2), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                    c.ArcTo(new Point(s / 2 + r, s / 2), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                }
            }
            return Freeze(g);
        }

        /// <summary>Draws steps [from, to) in units. <paramref name="minStroke"/> keeps hairlines visible when the
        /// picture is shown small (units per 0.75 screen pixel).</summary>
        internal static void Draw(DrawingContext dc, IReadOnlyList<FramePrim> prims, int from, int to, double minStroke)
        {
            for (var i = from; i < to; i++)
            {
                var p = prims[i];
                if (p.Alpha <= 0.002) continue;
                var geo = GeometryOf(p);
                if (geo == null) continue;
                Brush? fill = null;
                if (p.Fill != null)
                {
                    fill = p.Fill2 == null ? BrushOf(p.Fill) : Freeze(new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString(p.Fill), (Color)ColorConverter.ConvertFromString(p.Fill2),
                        new Point(0, p.G0 * Unit), new Point(0, p.G1 * Unit)) { MappingMode = BrushMappingMode.Absolute });
                }
                var pen = p.Stroke != null && p.Width > 0 ? PenOf(p.Stroke, Math.Max(minStroke, p.Width * Unit)) : null;
                var alpha = Math.Min(1, p.Alpha);
                if (alpha < 0.999) dc.PushOpacity(alpha);
                dc.DrawGeometry(fill, pen, geo);
                if (alpha < 0.999) dc.Pop();
            }
        }

        private static double MinStrokeFor(double canvasPx) => canvasPx > 0 ? 0.75 * Unit / canvasPx : 0;

        /// <summary>A frozen picture of the frame at rest (motion off), drawn for a canvas about
        /// <paramref name="canvasPx"/> pixels wide. Null for an id that is not a drawn frame.</summary>
        public static DrawingImage? Still(string? id, double canvasPx = 133)
        {
            if (!ProfileFrames.Knows(id)) return null;
            var detail = ProfileFrames.DetailFor(canvasPx);
            var key = id + "|" + detail;
            lock (_stills)
            {
                if (_stills.TryGetValue(key, out var hit)) return hit;
            }
            var prims = ProfileFrames.Build(id, 0, FrameMotion.Still, detail);
            var group = new DrawingGroup();
            using (var dc = group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent, null, Box);
                dc.PushClip(BoxClip);
                Draw(dc, prims, 0, prims.Count, MinStrokeFor(detail == 0 ? 32 : detail == 1 ? 133 : 260));
                dc.Pop();
            }
            var img = Freeze(new DrawingImage(Freeze(group)));
            lock (_stills) _stills[key] = img;
            return img;
        }

        // ---------------------------------------------------------------- living frames

        /// <summary>The motion a frame gets right now: the app's motion level (Gentle already caps it at Reduced), and
        /// none on a performance tier that allows no ambient motion.</summary>
        public static FrameMotion CurrentMotion()
        {
            try
            {
                if (!PerformanceProfile.AllowAmbientMotion(PerformanceProfile.CurrentTier)) return FrameMotion.Still;
                var level = MotionFx.Level;
                return FrameMotion.Of(level == MotionLevel.Off ? 2 : level == MotionLevel.Reduced ? 1 : 0, GentleVisuals.On);
            }
            catch { return FrameMotion.Still; }
        }

        private static readonly ConditionalWeakTable<Image, Live> _attached = new();

        /// <summary>Puts a living frame on <paramref name="host"/> (its Source is set here), drawn for a canvas
        /// <paramref name="canvasPx"/> pixels wide. Ids that are not drawn frames clear it and return false.</summary>
        public static bool Attach(Image host, string? id, double canvasPx)
        {
            if (_attached.TryGetValue(host, out var old)) { old.Detach(); _attached.Remove(host); }
            if (!ProfileFrames.Knows(id)) { host.Source = null; return false; }
            var live = new Live(host, id!, canvasPx);
            _attached.Add(host, live);
            return true;
        }

        /// <summary>Takes a living frame off <paramref name="host"/> (it keeps whatever Source it shows).</summary>
        public static void Detach(Image host)
        {
            if (_attached.TryGetValue(host, out var old)) { old.Detach(); _attached.Remove(host); }
        }

        /// <summary>One living frame: frozen groups for the still runs, open groups for the moving runs.</summary>
        internal sealed class Live
        {
            private readonly Image _host;
            private readonly string _id;
            private readonly int _detail;
            private readonly double _minStroke;
            private readonly List<(bool live, int from, int to, DrawingGroup group)> _runs = new();
            private bool _registered, _detached;

            public DrawingImage Source { get; }

            internal Live(Image host, string id, double canvasPx)
            {
                _host = host; _id = id;
                _detail = ProfileFrames.DetailFor(canvasPx);
                _minStroke = MinStrokeFor(canvasPx);
                var motion = CurrentMotion();
                var prims = ProfileFrames.Build(id, Ticker.Now, motion, _detail);
                var root = new DrawingGroup { ClipGeometry = BoxClip };
                using (var dc = root.Open()) dc.DrawRectangle(Brushes.Transparent, null, Box);
                var start = 0;
                for (var i = 1; i <= prims.Count; i++)
                {
                    if (i < prims.Count && prims[i].Live == prims[start].Live) continue;
                    var g = new DrawingGroup();
                    using (var dc = g.Open()) Draw(dc, prims, start, i, _minStroke);
                    if (!prims[start].Live) Freeze(g);
                    _runs.Add((prims[start].Live, start, i, g));
                    root.Children.Add(g);
                    start = i;
                }
                Source = new DrawingImage(root);
                host.Source = Source;
                host.Loaded += OnLoaded;
                host.Unloaded += OnUnloaded;
                host.IsVisibleChanged += OnVisible;
                if (host.IsLoaded) Register();
            }

            internal string Id => _id;
            internal int Detail => _detail;
            internal IReadOnlyList<(bool live, int from, int to, DrawingGroup group)> Runs => _runs;

            private void OnLoaded(object? s, RoutedEventArgs e) => Register();
            private void OnUnloaded(object? s, RoutedEventArgs e) => Unregister();
            private void OnVisible(object? s, DependencyPropertyChangedEventArgs e) { if (_host.IsVisible) Ticker.Kick(); }

            private Window? _window;

            private void Register()
            {
                if (_detached || _registered) return;
                _registered = true;
                // The clock sleeps while no frame is showing; the window coming back to the front wakes it.
                _window = Window.GetWindow(_host);
                if (_window != null) _window.Activated += OnActivated;
                Ticker.Add(this);
            }

            private void Unregister()
            {
                if (!_registered) return;
                _registered = false;
                if (_window != null) _window.Activated -= OnActivated;
                _window = null;
                Ticker.Remove(this);
            }

            private void OnActivated(object? s, EventArgs e) => Ticker.Kick();

            internal void Detach()
            {
                _detached = true;
                Unregister();
                _host.Loaded -= OnLoaded;
                _host.Unloaded -= OnUnloaded;
                _host.IsVisibleChanged -= OnVisible;
            }

            /// <summary>Worth drawing now: on screen, in a window the user is looking at.</summary>
            internal bool Showing
            {
                get
                {
                    if (!_host.IsVisible) return false;
                    var w = Window.GetWindow(_host);
                    return w == null || (w.IsActive && w.WindowState != WindowState.Minimized);
                }
            }

            /// <summary>Redraws the moving runs for time <paramref name="t"/>.</summary>
            internal void Render(double t, FrameMotion m)
            {
                var prims = ProfileFrames.Build(_id, t, m, _detail);
                foreach (var run in _runs)
                {
                    if (!run.live) continue;
                    using var dc = run.group.Open();
                    Draw(dc, prims, run.from, run.to, _minStroke);
                }
            }
        }

        /// <summary>The one clock every living frame shares (30 fps, frame-locked). It runs only while some frame is
        /// registered and moving, and draws only the frames that are showing.</summary>
        internal static class Ticker
        {
            private static readonly List<Live> _items = new();
            private static readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();
            private static FrameClock? _clock;

            internal static double Now => _watch.Elapsed.TotalSeconds;
            internal static int Count => _items.Count;

            internal static void Add(Live l) { if (!_items.Contains(l)) _items.Add(l); Kick(); }
            internal static void Remove(Live l) { _items.Remove(l); if (_items.Count == 0) _clock?.Stop(); }

            internal static void Kick()
            {
                var m = CurrentMotion();
                if (_items.Count == 0) return;
                if (!m.Moves)
                {
                    // Motion off: one still pose, no clock.
                    _clock?.Stop();
                    foreach (var l in _items.ToArray()) l.Render(0, m);
                    return;
                }
                _clock ??= CreateClock();
                _clock.Start();
            }

            private static FrameClock CreateClock()
            {
                var c = new FrameClock { Interval = TimeSpan.FromMilliseconds(33) };
                c.Tick += (_, _) => TickAll();
                return c;
            }

            private static void TickAll()
            {
                var m = CurrentMotion();
                if (!m.Moves) { Kick(); return; }
                var t = Now;
                var any = false;
                foreach (var l in _items.ToArray())
                {
                    if (!l.Showing) continue;
                    any = true;
                    try { l.Render(t, m); } catch (Exception ex) { App.Logger?.Debug("ProfileFrameArt: draw failed: {E}", ex.Message); }
                }
                // Nothing on screen: stop until a frame shows again (IsVisibleChanged / Loaded kick it).
                if (!any) _clock?.Stop();
            }
        }
    }
}
