using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// E-Stim's lightning: jagged tether bolts arcing bubble-to-bubble for a quarter second.
/// One full-virtual-screen click-through window; each strike clears the canvas, draws the
/// bolt segments (violet glow + electric core + an impact flash at every hit point),
/// re-jitters the paths a few times so they flicker like live current, then fades and
/// hides. Keep-alive contract like every chaos overlay: created once at run start
/// (<see cref="EnsureCreated"/>), shown only for the strike's ~0.4s, closed only at
/// teardown (<see cref="CloseActive"/>) — layered-window churn is what deadlocks the
/// render thread.
/// </summary>
public sealed class ChaosEStimOverlay : Window
{
    private const int FLICKER_MS = 40;     // re-jitter cadence while the bolt is hot
    private const int HOT_MS = 120;        // full-brightness window (the requested 0.12s)
    private const int FADE_MS = 80;        // fade-out after the hot window
    private const double JITTER = 14;      // max perpendicular displacement per midpoint (DIPs)
    private const double SEG_LEN = 55;     // approx DIPs between midpoints along a bolt
    private const double FLASH_SIZE = 28;  // impact flash diameter at each struck bubble
    private const double STRIKE_OPACITY = 0.85;   // whole-strike ceiling — the current reads as a whisper, not a flashbang

    private static readonly Color GlowColor = Color.FromRgb(0x9C, 0x5C, 0xFF);   // violet haze
    private static readonly Color CoreColor = Color.FromRgb(0xBF, 0xEC, 0xFF);   // electric white-blue

    private static ChaosEStimOverlay? _active;

    /// <summary>The charge is live: light the cursor halo. Safe from any thread.</summary>
    public static void Arm() => ChaosEStimGlow.Arm();

    /// <summary>Charges spent (or cancelled): drop the cursor halo. Safe from any thread.</summary>
    public static void Disarm() => ChaosEStimGlow.Disarm();

    /// <summary>Create the (hidden) lightning + halo windows ahead of time at run start.</summary>
    public static void EnsureCreated()
    {
        ChaosEStimGlow.EnsureCreated();
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
                        _active = new ChaosEStimOverlay();
                        ((Window)_active).Show();   // realize the hwnd once...
                        _active.Hide();             // ...then idle hidden until a strike
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEStim.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Fire one strike: draw bolts along <paramref name="boltsPx"/> (physical-px
    /// segment endpoints, in strike order), flicker for the hot window, fade, hide.
    /// Safe from any thread.</summary>
    public static void Strike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        if (boltsPx == null || boltsPx.Count == 0) return;
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
                        _active = new ChaosEStimOverlay();
                        ((Window)_active).Show();
                    }
                    else if (!_active.IsVisible) _active.Show();
                    ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
                    _active.BeginStrike(boltsPx);
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEStim.Strike: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Re-stack the bolt + halo windows above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
    public static void RaiseActive()
    {
        ChaosWindowZ.RaiseTopmost(_active);
        ChaosEStimGlow.RaiseActive();
    }

    /// <summary>Instant teardown (run end / shutdown) — the only place these hwnds die.</summary>
    public static void CloseActive()
    {
        ChaosEStimGlow.CloseActive();
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
                    if (w != null) { w._tick.Stop(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private readonly Canvas _canvas;
    private readonly DispatcherTimer _tick;
    private readonly Random _rng = new();
    private readonly List<(Point A, Point B)> _segments = new();   // window-local DIPs
    private readonly List<Polyline> _glows = new();
    private readonly List<Polyline> _cores = new();
    private double _elapsedMs;
    private int _strikeSeq;   // guards a stale lifetime-Completed from wiping a newer strike

    private ChaosEStimOverlay()
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
        // (see ChaosWindowZ.StageBounds). px→DIP mapping follows this window's Left/Top.
        var (sl, st, sw, sh) = ChaosWindowZ.StageBounds();
        Left = sl; Top = st; Width = sw; Height = sh;

        _canvas = new Canvas { IsHitTestVisible = false };
        Content = _canvas;

        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FLICKER_MS) };
        _tick.Tick += StrikeTick;

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void BeginStrike(IReadOnlyList<(Point From, Point To)> boltsPx)
    {
        // Physical px → this window's device-independent units → window-local coords
        // (the window spans the virtual screen, which may not start at 0,0).
        var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        if (t == null) return;
        Point Local(Point px) { var p = t.Value.Transform(px); return new Point(p.X - Left, p.Y - Top); }

        _tick.Stop();
        _canvas.BeginAnimation(OpacityProperty, null);
        _canvas.Opacity = STRIKE_OPACITY;
        _canvas.Children.Clear();
        _segments.Clear();
        _glows.Clear();
        _cores.Clear();
        _elapsedMs = 0;

        var flashBrush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(130, 0xFF, 0xFF, 0xFF), 0.0));
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(70, CoreColor.R, CoreColor.G, CoreColor.B), 0.45));
        flashBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, GlowColor.R, GlowColor.G, GlowColor.B), 1.0));
        if (flashBrush.CanFreeze) flashBrush.Freeze();

        foreach (var (fromPx, toPx) in boltsPx)
        {
            var a = Local(fromPx);
            var b = Local(toPx);
            _segments.Add((a, b));

            var glow = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(80, GlowColor.R, GlowColor.G, GlowColor.B)),
                StrokeThickness = 5.5,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            var core = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(190, CoreColor.R, CoreColor.G, CoreColor.B)),
                StrokeThickness = 1.7,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            _glows.Add(glow);
            _cores.Add(core);
            _canvas.Children.Add(glow);
            _canvas.Children.Add(core);

            // Impact flash at the receiving end (the anchor's own flash is its pop burst).
            var flash = new Ellipse
            {
                Width = FLASH_SIZE, Height = FLASH_SIZE,
                Fill = flashBrush, IsHitTestVisible = false,
            };
            Canvas.SetLeft(flash, b.X - FLASH_SIZE / 2);
            Canvas.SetTop(flash, b.Y - FLASH_SIZE / 2);
            _canvas.Children.Add(flash);
        }

        JitterBolts();
        // Gentle visuals: no re-jitter flicker; the bolt rises, holds still and fades.
        bool gentle = !Services.Safety.GentleVisuals.AllowFlicker;
        if (!gentle) _tick.Start();

        // Lifetime rides the ANIMATION clock, not the tick timer: the pops this strike triggers
        // open payload windows on the same dispatcher, which can stall timers for hundreds of ms.
        // A time-based animation snaps to the right opacity at the next rendered frame, so the
        // bolt never overstays its 120ms + fade no matter how busy the UI thread is.
        int seq = ++_strikeSeq;
        var life = new DoubleAnimationUsingKeyFrames();
        if (gentle)
        {
            foreach (var (v, ms) in Services.Video.GentleWindowRules.RiseHoldFade(STRIKE_OPACITY,
                         Services.Safety.GentleVisuals.RiseMs(0), HOT_MS, Services.Safety.GentleVisuals.FadeMs(FADE_MS)))
                life.KeyFrames.Add(new LinearDoubleKeyFrame(v, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        }
        else
        {
            life.KeyFrames.Add(new DiscreteDoubleKeyFrame(STRIKE_OPACITY, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            life.KeyFrames.Add(new DiscreteDoubleKeyFrame(STRIKE_OPACITY, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HOT_MS))));
            life.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HOT_MS + FADE_MS))));
        }
        life.Completed += (_, _) =>
        {
            if (seq != _strikeSeq) return;   // a newer strike took the canvas — leave it alone
            _tick.Stop();
            _canvas.Children.Clear();
            try { Hide(); } catch { }   // idle hidden — no DWM tax between strikes
        };
        _canvas.BeginAnimation(OpacityProperty, life);
    }

    /// <summary>Rebuild every bolt's jagged path — called per flicker tick so the current dances.</summary>
    private void JitterBolts()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            var (a, b) = _segments[i];
            var pts = new PointCollection { a };
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            int mids = Math.Max(2, (int)(len / SEG_LEN));
            // Unit perpendicular for the sideways jitter.
            double px = len > 0.001 ? -dy / len : 0, py = len > 0.001 ? dx / len : 0;
            for (int m = 1; m <= mids; m++)
            {
                double f = m / (double)(mids + 1);
                double off = (_rng.NextDouble() * 2 - 1) * JITTER;
                pts.Add(new Point(a.X + dx * f + px * off, a.Y + dy * f + py * off));
            }
            pts.Add(b);
            if (pts.CanFreeze) pts.Freeze();
            _glows[i].Points = pts;
            _cores[i].Points = pts;
        }
    }

    /// <summary>Cosmetic flicker only — the strike's lifetime/teardown live on the animation
    /// clock in <see cref="BeginStrike"/> (timer ticks can stall behind payload windows).</summary>
    private void StrikeTick(object? sender, EventArgs e)
    {
        try
        {
            _elapsedMs += FLICKER_MS;
            if (_elapsedMs >= HOT_MS) { _tick.Stop(); return; }
            JitterBolts();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosEStim tick: {E}", ex.Message); }
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

/// <summary>
/// E-Stim's "charged" telegraph: a small violet halo riding the cursor while charges wait
/// to discharge. Its own TINY self-following window — the bolt overlay spans the virtual
/// screen, and a full-screen layered surface left visible for a whole armed window (which
/// can be a long time) taxes DWM composition; this one is ~90px. Click-through + NOACTIVATE,
/// same keep-alive contract as every chaos overlay.
/// </summary>
public sealed class ChaosEStimGlow : Window
{
    private const double HALO_SIZE = 64;
    private const int WIN_SIZE = 92;       // window padding around the halo so the pulse never clips
    private const int FOLLOW_MS = 16;

    private static ChaosEStimGlow? _active;

    /// <summary>Create the (hidden) halo window ahead of time at run start.</summary>
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
                        _active = new ChaosEStimGlow();
                        ((Window)_active).Show();   // realize the hwnd once...
                        _active.Hide();             // ...then idle hidden until armed
                    }
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEStimGlow.EnsureCreated: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Charges armed: show the halo and follow the cursor. Safe from any thread.</summary>
    public static void Arm()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) { _active = new ChaosEStimGlow(); ((Window)_active).Show(); }
                    else if (!_active.IsVisible) _active.Show();
                    ChaosWindowZ.RaiseAboveVideo(_active);   // un-hiding doesn't re-stack — kick over a playing video
                    _active.FollowTick(null, EventArgs.Empty);   // snap to the cursor before the first frame
                    _active._follow.Start();
                }
                catch (Exception ex) { App.Logger?.Debug("ChaosEStimGlow.Arm: {E}", ex.Message); }
            });
        }
        catch { }
    }

    /// <summary>Charges spent or cancelled: stop following and hide (window stays alive).</summary>
    public static void Disarm()
    {
        try
        {
            var disp = Application.Current?.Dispatcher;
            if (disp == null || disp.HasShutdownStarted) return;
            disp.BeginInvoke(() =>
            {
                try
                {
                    if (_active == null) return;
                    _active._follow.Stop();
                    _active.Hide();
                }
                catch { }
            });
        }
        catch { }
    }

    /// <summary>Re-stack the live halo above a mandatory video (see ChaosWindowZ). UI thread only.</summary>
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
                    if (w != null) { w._follow.Stop(); w.Close(); }
                }
                catch { }
            });
        }
        catch { }
    }

    private readonly DispatcherTimer _follow;

    private ChaosEStimGlow()
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
        Width = WIN_SIZE;
        Height = WIN_SIZE;

        // Soft violet halo with an electric core hint — quieter than the Rabbit Caller whistle.
        var brush = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.5), Center = new Point(0.5, 0.5) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xBF, 0xEC, 0xFF), 0.10));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x9C, 0x5C, 0xFF), 0.45));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x9C, 0x5C, 0xFF), 1.0));
        if (brush.CanFreeze) brush.Freeze();

        var scale = new ScaleTransform(1, 1);
        var halo = new System.Windows.Shapes.Ellipse
        {
            Width = HALO_SIZE, Height = HALO_SIZE,
            Fill = brush,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        Content = halo;

        // A quick electric tremble — charged, not breathing.
        var pulse = new DoubleAnimation(0.88, 1.10, TimeSpan.FromMilliseconds(160))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FOLLOW_MS) };
        _follow.Tick += FollowTick;

        SourceInitialized += (_, _) => ApplyExStyles();
    }

    private void FollowTick(object? sender, EventArgs e)
    {
        try
        {
            if (!GetCursorPos(out var px)) return;
            var t = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
            if (t == null) return;
            var p = t.Value.Transform(new Point(px.X, px.Y));
            Left = p.X - WIN_SIZE / 2.0;
            Top = p.Y - WIN_SIZE / 2.0;
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosEStimGlow tick: {E}", ex.Message); }
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

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
