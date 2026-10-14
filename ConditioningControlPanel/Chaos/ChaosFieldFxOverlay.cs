using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ConditioningControlPanel;

/// <summary>
/// Field FX for the run-boon pool: Size Queen's expanding pop-ring, Aftermath's crackling
/// residue zone, and Tail-Plug's rabbit sparkle trail all draw on this ONE full-virtual-screen
/// click-through canvas. The popping itself lives in BubbleService.TickFieldHazards — this
/// window is purely the visible half. Keep-alive contract like every chaos overlay: created
/// once at run start (<see cref="EnsureCreated"/>), shown only while something is drawn
/// (idle visible layered surfaces tax DWM), closed only at teardown (<see cref="CloseActive"/>)
/// — layered-window churn is what deadlocks the render thread.
/// </summary>
public sealed class ChaosFieldFxOverlay : Window
{
    private const int TRAIL_DOT_POOL = 90;     // recycled spark ring buffer (rabbits emit ~1 dot / 40 DIPs)
    private const double TRAIL_DOT_SIZE = 16;

    private static readonly Color RingColor = Color.FromRgb(0x7A, 0xE0, 0xFF);      // Size Queen — snap-cyan
    private static readonly Color ResidueColor = Color.FromRgb(0x9C, 0x5C, 0xFF);   // Aftermath — E-Stim violet
    private static readonly Color TrailColor = Color.FromRgb(0xFF, 0x4D, 0xC4);     // Tail-Plug — rabbit pink
    private static readonly Color WarmTrailColor = Color.FromRgb(0xFF, 0x8A, 0x14); // GG sweeper rabbit — amber

    private static ChaosFieldFxOverlay? _active;

    /// <summary>Create the (hidden) field-FX window ahead of time at run start.</summary>
    public static void EnsureCreated()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((Window)_active).Show();   // realize the hwnd once...
                        _active.Hide();             // ...then idle hidden until something draws
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosFieldFx.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Size Queen: draw one expanding ring at <paramref name="centerPx"/> (physical px).</summary>
    public static void Ripple(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawRipple(centerPx, radiusPx, lifeMs));

    /// <summary>the Ripple (right-click): the player's cast — a linear cyan wavefront that IS
    /// the kill front, a pink echo a beat behind, and eight radial shards flying with it.</summary>
    public static void SnapRipple(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawSnapRipple(centerPx, radiusPx, lifeMs));

    /// <summary>Aftermath: draw one crackling residue zone for <paramref name="lifeMs"/>.</summary>
    public static void Residue(Point centerPx, double radiusPx, double lifeMs) =>
        OnUi(w => w.DrawResidue(centerPx, radiusPx, lifeMs));

    /// <summary>Drop one fading sparkle at a rabbit's trail point. <paramref name="warm"/> picks
    /// the amber GG-sweeper spark over the default Tail-Plug pink.</summary>
    public static void TrailDot(Point centerPx, double lifeSec, bool warm = false) =>
        OnUi(w => w.DrawTrailDot(centerPx, lifeSec, warm));

    /// <summary>The Bound: create/update the elastic thread between a tethered pair (keyed by
    /// PairId; endpoints in physical px). Updated every anim tick while both halves live.</summary>
    public static void SetTether(int key, Point aPx, Point bPx) =>
        OnUi(w => w.UpdateTether(key, aPx, bPx));

    /// <summary>The Bound: the pair resolved (or one half did) — drop its thread.</summary>
    public static void ClearTether(int key) =>
        OnUi(w => w.RemoveTether(key));

    /// <summary>Re-stack the live window above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive() => ChaosWindowZ.RaiseTopmost(_active);

    /// <summary>Instant teardown (run end / shutdown) — the only place this hwnd dies.</summary>
    public static void CloseActive()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) { _active = null; return; }
            disp.BeginInvoke(() =>
            {
                try
                {
                    var w = _active;
                    _active = null;
                    w?.Close();
                }
                catch { }
            });
        }
        catch { }
    }

    private static void OnUi(Action<ChaosFieldFxOverlay> act)
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null)
                    {
                        _active = new ChaosFieldFxOverlay();
                        ((Window)_active).Show();
                        ChaosWindowZ.RaiseAboveVideo(_active);
                    }
                    else if (!_active.IsVisible)
                    {
                        _active.Show();
                        ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
                    }
                    act(_active);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosFieldFx: {E}", ex.Message); }
            });
        }
        catch { }
    }

    private readonly Canvas _canvas;
    private readonly Ellipse[] _trailDots = new Ellipse[TRAIL_DOT_POOL];
    private readonly ScaleTransform[] _trailScales = new ScaleTransform[TRAIL_DOT_POOL];
    private int _trailIndex;
    private int _transientCount;   // live ripples/residues — the window hides when everything faded
    private readonly Brush _trailBrushCool;   // Tail-Plug rabbit-pink spark
    private readonly Brush _trailBrushWarm;   // GG sweeper amber spark

    /// <summary>A frozen radial spark brush (gold core → <paramref name="edge"/> falloff).</summary>
    private static Brush BuildTrailBrush(Color edge)
    {
        var b = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(200, 0xFF, 0xE9, 0xA0), 0.0));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(120, edge.R, edge.G, edge.B), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, edge.R, edge.G, edge.B), 1.0));
        if (b.CanFreeze) b.Freeze();
        return b;
    }

    private ChaosFieldFxOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = ChaosWindowZ.BornTopmost;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // Single-monitor by default: confine to the primary screen unless multi-monitor is on
        // (ripples/residue/rabbit trails shouldn't span every screen — see ChaosWindowZ.StageBounds).
        // Local() subtracts this window's Left/Top, so px→DIP mapping follows the new origin.
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        // Pre-build the recycled trail-spark pool (rabbit-pink, gold core) plus a warm amber
        // variant for GG sweeper rabbits, so their trail reads as a different rabbit at a glance.
        var dotBrush = _trailBrushCool = BuildTrailBrush(TrailColor);
        _trailBrushWarm = BuildTrailBrush(WarmTrailColor);
        for (int i = 0; i < TRAIL_DOT_POOL; i++)
        {
            var sc = new ScaleTransform(1, 1);
            var dot = new Ellipse
            {
                Width = TRAIL_DOT_SIZE, Height = TRAIL_DOT_SIZE,
                Fill = dotBrush,
                IsHitTestVisible = false,
                Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = sc,
            };
            _trailDots[i] = dot;
            _trailScales[i] = sc;
            _canvas.Children.Add(dot);
        }

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    /// <summary>Physical px → this window's local DIPs (it spans the virtual screen).</summary>
    private Point? Local(Point px)
    {
        var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (t == null) return null;
        var p = t.Value.Transform(px);
        return new Point(p.X - Left, p.Y - Top);
    }

    private void DrawRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        // px radius → DIPs via the same device transform (uniform scale assumed per ring).
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double r = radiusPx * scale;

        var st = new ScaleTransform(0.05, 0.05);
        var ring = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(190, RingColor.R, RingColor.G, RingColor.B)),
            StrokeThickness = 6,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = st,
        };
        Canvas.SetLeft(ring, c.X - r);
        Canvas.SetTop(ring, c.Y - r);
        _canvas.Children.Add(ring);
        _transientCount++;

        var dur = TimeSpan.FromMilliseconds(lifeMs);
        var grow = new DoubleAnimation(0.05, 1.0, dur) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var fade = new DoubleAnimation(0.95, 0, dur);
        fade.Completed += (_, _) => RemoveTransient(ring);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private static readonly Color CastFrontColor = Color.FromRgb(0x7A, 0xE0, 0xFF);   // the water — cyan
    private static readonly Color CastEchoColor = Color.FromRgb(0xFF, 0x69, 0xB4);    // the hand — pink

    private void DrawSnapRipple(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double r = radiusPx * scale;
        var dur = TimeSpan.FromMilliseconds(lifeMs);

        // The cyan front grows LINEARLY so the drawn ring matches the linear kill front in
        // TickFieldHazards exactly — what the ring touches is what pops. The pink echo rides
        // eased behind it, purely for the look.
        AddCastRing(c, r, dur, CastFrontColor, 6, 0.95, eased: false);
        AddCastRing(c, r * 0.82, dur, CastEchoColor, 3.5, 0.65, eased: true);

        // Eight radial shards fly out with the front and die before it lands.
        var spokeBrush = Frozen(Color.FromArgb(220, CastFrontColor.R, CastFrontColor.G, CastFrontColor.B));
        var flyDur = TimeSpan.FromMilliseconds(lifeMs * 0.7);
        for (int i = 0; i < 8; i++)
        {
            double ang = Math.PI * 2 * i / 8 + 0.39;   // offset so no shard sits on an axis
            double ux = Math.Cos(ang), uy = Math.Sin(ang);
            var tt = new TranslateTransform(ux * r * 0.2, uy * r * 0.2);
            var shard = new Line
            {
                X1 = c.X, Y1 = c.Y,
                X2 = c.X + ux * r * 0.16, Y2 = c.Y + uy * r * 0.16,
                Stroke = spokeBrush,
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0.9,
                RenderTransform = tt,
            };
            _canvas.Children.Add(shard);
            _transientCount++;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(ux * r * 0.2, ux * r * 0.85, flyDur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(uy * r * 0.2, uy * r * 0.85, flyDur) { EasingFunction = ease });
            var fade = new DoubleAnimation(0.9, 0, flyDur);
            fade.Completed += (_, _) => RemoveTransient(shard);
            shard.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void AddCastRing(Point c, double r, TimeSpan dur, Color color, double thickness, double fromOpacity, bool eased)
    {
        var st = new ScaleTransform(0.05, 0.05);
        var ring = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = st,
        };
        Canvas.SetLeft(ring, c.X - r);
        Canvas.SetTop(ring, c.Y - r);
        _canvas.Children.Add(ring);
        _transientCount++;

        var grow = new DoubleAnimation(0.05, 1.0, dur);
        if (eased) grow.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(fromOpacity, 0, dur);
        fade.Completed += (_, _) => RemoveTransient(ring);
        st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ring.BeginAnimation(OpacityProperty, fade);
    }

    private void DrawResidue(Point centerPx, double radiusPx, double lifeMs)
    {
        if (Local(centerPx) is not Point c) return;
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double r = radiusPx * scale;

        var brush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 0xBF, 0xEC, 0xFF), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(90, ResidueColor.R, ResidueColor.G, ResidueColor.B), 0.55));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, ResidueColor.R, ResidueColor.G, ResidueColor.B), 1.0));
        if (brush.CanFreeze) brush.Freeze();
        var zone = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(zone, c.X - r);
        Canvas.SetTop(zone, c.Y - r);
        _canvas.Children.Add(zone);
        _transientCount++;

        // A crackling flicker for its whole life, then a quick fade-away at the end.
        var flicker = new DoubleAnimationUsingKeyFrames();
        if (!Services.Safety.GentleVisuals.AllowFlicker)
        {
            // Gentle visuals: no crackle; a steady glow that rises, holds and fades over its life.
            int rise = Services.Safety.GentleVisuals.RiseMs(0), fade = Services.Safety.GentleVisuals.FadeMs(90);
            foreach (var (v, ms) in Services.Video.GentleWindowRules.RiseHoldFade(0.8, rise, (int)lifeMs - rise - fade, fade))
                flicker.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        }
        else
        {
            int steps = Math.Max(2, (int)(lifeMs / 90));
            var rng = new Random();
            for (int i = 0; i < steps; i++)
                flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.55 + rng.NextDouble() * 0.45,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(i * 90))));
            flicker.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(lifeMs))));
        }
        flicker.Completed += (_, _) => RemoveTransient(zone);
        zone.BeginAnimation(OpacityProperty, flicker);
    }

    // ---- The Bound: elastic tether lines, one per live pair ----
    private readonly Dictionary<int, Line> _tethers = new();
    private static readonly Brush TetherBrush = Frozen(Color.FromArgb(150, 0xFF, 0x69, 0xB4));

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void UpdateTether(int key, Point aPx, Point bPx)
    {
        if (Local(aPx) is not Point a || Local(bPx) is not Point b) return;
        if (!_tethers.TryGetValue(key, out var line))
        {
            line = new Line
            {
                Stroke = TetherBrush,
                StrokeThickness = 3.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                StrokeDashCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0.55,
            };
            _tethers[key] = line;
            _canvas.Children.Add(line);
        }
        line.X1 = a.X; line.Y1 = a.Y;
        line.X2 = b.X; line.Y2 = b.Y;
        // Elastic read: the further it stretches, the thinner the thread.
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        line.StrokeThickness = Math.Clamp(5.0 - dist / 250.0, 1.5, 5.0);
    }

    private void RemoveTether(int key)
    {
        if (_tethers.TryGetValue(key, out var line))
        {
            try { _canvas.Children.Remove(line); } catch { }
            _tethers.Remove(key);
        }
        if (_transientCount == 0 && _tethers.Count == 0
            && Services.BubbleService.ChaosRabbitTrailSecNow <= 0)
        {
            try { Hide(); } catch { }
        }
    }

    private void DrawTrailDot(Point centerPx, double lifeSec, bool warm = false)
    {
        if (Local(centerPx) is not Point c) return;
        var dot = _trailDots[_trailIndex];
        var sc = _trailScales[_trailIndex];
        _trailIndex = (_trailIndex + 1) % TRAIL_DOT_POOL;
        dot.Fill = warm ? _trailBrushWarm : _trailBrushCool;   // amber for GG sweepers, pink for Tail-Plug

        Canvas.SetLeft(dot, c.X - TRAIL_DOT_SIZE / 2);
        Canvas.SetTop(dot, c.Y - TRAIL_DOT_SIZE / 2);
        var dur = TimeSpan.FromSeconds(Math.Max(0.3, lifeSec));
        dot.BeginAnimation(OpacityProperty, null);
        dot.Opacity = 0.65;
        var fade = new DoubleAnimation(0.65, 0, dur);
        fade.Completed += (_, _) =>
        {
            if (_transientCount == 0 && _tethers.Count == 0 && Services.BubbleService.ChaosRabbitTrailSecNow <= 0)
            {
                try { Hide(); } catch { }   // boon gone / run over — don't idle visible
            }
        };
        dot.BeginAnimation(OpacityProperty, fade);
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.1, 0.3, dur));
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.1, 0.3, dur));
    }

    /// <summary>A ripple/residue finished: drop its element; hide the window once idle.
    /// (Trail dots are pooled + opacity-0 when faded, so they don't hold the window open —
    /// but while rabbits are dragging trails the steady dot stream keeps it visible anyway.)</summary>
    private void RemoveTransient(UIElement el)
    {
        try { _canvas.Children.Remove(el); } catch { }
        _transientCount = Math.Max(0, _transientCount - 1);
        if (_transientCount == 0 && _tethers.Count == 0 && Services.BubbleService.ChaosRabbitTrailSecNow <= 0)
        {
            try { Hide(); } catch { }
        }
    }

    private void ApplyExStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
