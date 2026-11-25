using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// Super Lights Down on this head, the twin of WPF <c>Services/Super/LightsDownService.cs</c> (7.4.1), riding
/// <see cref="MandatoryVideoOverlay"/>. The frame step is Core <see cref="LightsDownRun"/>, shared with WPF;
/// this file reads the world and places the pictures (<see cref="LightsDownCanvasView"/>).
/// <list type="bullet">
/// <item>FULL SCREEN: the edge dim is a layer INSIDE each full-screen video window, over the picture and
/// under the attention targets, the grace card and the buttons (WPF owns its dim window by the video, so
/// it lies over the picture's edges the same way). It needs no window manager, so it draws wherever the
/// video does.</item>
/// <item>FLOATING: one refusing dim window per video screen (<see cref="TryOpen"/>: click-through set
/// before mapping, a real transparent visual, the video restacked over it, never under WSLg), kept under
/// every floating window and grace card twice a second. A WM that does not keep that order, or whose
/// order cannot be read, ends Lights Down for the run (<see cref="LightsDownStackGuard"/>): it refuses
/// rather than dim a video.</item>
/// </list>
/// Panic (the <c>lights-down</c> surface) and the switch going off close everything on the spot, before
/// the decoder teardown; a floating run's natural end fades it in 0.6 s (Gentle only lengthens). UI thread.
/// </summary>
internal static class LightsDownOverlay
{
    private static readonly List<Window> Windows = new();
    internal static int Count => Windows.Count;

    private sealed record Dim(LightsDownScreen Screen, LightsDownCanvasView View, Window? Window, Panel? Host);

    private static readonly List<Dim> Dims = new();
    private static readonly List<LightsDownScreen> Screens = new();
    private static readonly List<(Window Window, PixelRect Px)> Floats = new();
    private static readonly List<(long Key, LightsDownRect Px)> Videos = new();
    private static readonly List<TopLevel> Above = new();
    private static readonly List<LightsDownRect> Cut = new();
    private static readonly Stopwatch Clock = new();
    private static LightsDownStackGuard _guard = new();
    private static MandatoryVideoOverlay? _video;
    private static LightsDownRun? _run;
    private static DispatcherTimer? _timer;
    private static double _last, _gazeX, _gazeY, _gazeAt = double.NegativeInfinity;
    private static bool _gazeHooked, _accessHooked;
    private static uint _halo = LightsDownRun.FallbackHalo;

    /// <summary>Test seams: the switch (Core SuperAccess), the cursor (desktop px), opening a floating dim,
    /// reading its order and the floating screens.</summary>
    internal static Func<bool> EffectOn = () => SuperAccess.IsOn(SuperEffect.LightsDown);
    internal static Func<(int X, int Y)?> Cursor = SuperPointer.Cursor;
    internal static Func<Window, PixelRect, double, Control, bool> OpenDim = (v, b, s, c) => TryOpen(v, b, s, content: c);
    internal static Func<Window, IReadOnlyList<TopLevel>, LightsDownStack> Stack = X11Overlay.StackUnder;
    internal static Func<MandatoryVideoOverlay, Window, IReadOnlyList<(PixelRect Bounds, double Scale)>> FloatScreens =
        (v, reference) => v.LightsDownScreens(reference);

    internal static bool Running => _run != null;
    internal static LightsDownRun? RunForTest => _run;
    internal static string? Refused { get; private set; }
    internal static IEnumerable<LightsDownCanvasView> Layers { get { foreach (var d in Dims) yield return d.View; } }

    // The optional probes let headless tests exercise refusal without touching a desktop.
    internal static bool TryOpen(Window video, PixelRect bounds, double scale,
        Func<Window, bool>? eligible = null, Func<Window, bool>? clickThrough = null,
        Func<Window, bool>? transparent = null, Func<Window, Window, bool>? below = null, Control? content = null)
    {
        eligible ??= w => X11Overlay.BackendOf(w.TryGetPlatformHandle()) == OverlayBackend.X11
            && X11Overlay.IsAvailable && X11Overlay.IsCompositing;
        clickThrough ??= w => X11Overlay.SetClickThrough(w, true);
        transparent ??= w => w.ActualTransparencyLevel != WindowTransparencyLevel.None;
        below ??= (w, v) => X11Overlay.RestackAbove(v, w);
        // A WM that ignores restacking (WSLg) leaves the last-mapped dim window over the video.
        if (!eligible(video) || X11Overlay.WmCannotKeepAbove) return false;

        var w = new Window
        {
            SystemDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, CanResize = false, Topmost = true,
            Focusable = false, IsHitTestVisible = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Title = "CCP Lights Down",
            // The picture starts blank (fully transparent): never an opacity-0 window (KWin stalls those).
            Content = content ?? new Panel(),
        };
        try
        {
            // Set the empty XFixes input region before mapping. Refuse if this XID cannot
            // be made click-through; no mapped full-screen window may receive input.
            if (!clickThrough(w)) { w.Close(); return false; }
            scale = scale > 0 ? scale : 1;
            w.Position = new PixelPoint(bounds.X, bounds.Y);
            w.Width = bounds.Width / scale;
            w.Height = bounds.Height / scale;
            w.Show();
            if (!transparent(w) || !below(w, video)) { w.Close(); return false; }
            Windows.Add(w);
            w.Closed += (_, _) => Windows.Remove(w);
            return true;
        }
        catch
        {
            w.Close();
            return false;
        }
    }

    private static bool Wanted(MandatoryVideoOverlay v)
        => LightsDownRules.ShouldRun(EffectOn(), v.Scheduler.IsPlaying, v.LightsDownClosing, v.LightsDownHasWindows);

    /// <summary>WPF TryStart (VideoStarted, posted): costs nothing unless the switch is on and a video plays.</summary>
    internal static void Start(MandatoryVideoOverlay video)
    {
        HookAccess();
        if (!Wanted(video)) { if (_run == null) _video = video; return; }
        bool floating = video.LightsDownFloating;
        if (_run != null)
        {
            if (!_run.Ending && _run.Floating == floating) return;
            CloseAll();   // a fading floating dim, or the other mode: start clean
        }
        _video = video;
        _run = new LightsDownRun(floating);
        _guard = new LightsDownStackGuard();
        Refused = null;
        if (!floating && !OpenLayers(video)) { Stop(); return; }
        Clock.Restart();
        _last = 0;
        if (!_gazeHooked) { WebcamTracker.Instance.OnGazeMove += OnGaze; _gazeHooked = true; }
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(15) };
        _timer.Tick += Tick;
        _timer.Start();
        Serilog.Log.Information("Super Lights Down: {Mode}", floating ? "floating" : $"full screen, {Dims.Count} layer(s)");
    }

    private static void HookAccess()
    {
        if (_accessHooked) return;
        _accessHooked = true;
        SuperAccess.Changed += e =>
        {
            if (e != SuperEffect.LightsDown) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!EffectOn()) CloseAll();
                else if (_video is { } v) Start(v);
            });
        };
    }

    /// <summary>Full screen: a layer over each video window's picture, under its targets, card and buttons.</summary>
    private static bool OpenLayers(MandatoryVideoOverlay video)
    {
        foreach (var s in video.Surfaces)
        {
            if (s.Window.Content is not Panel host || !host.Children.Contains(s.Picture)) continue;
            var screen = s.Screen ?? s.Window.Screens.ScreenFromWindow(s.Window);
            var scale = screen is { Scaling: > 0 } ? screen.Scaling : s.Window.RenderScaling;
            var px = screen?.Bounds ?? new PixelRect(s.Window.Position, PixelSize.FromSize(s.Window.ClientSize, scale));
            var ld = new LightsDownScreen(px.X, px.Y, px.Width, px.Height, scale);
            var view = new LightsDownCanvasView(ld);
            host.Children.Insert(host.Children.IndexOf(s.Picture) + 1, view);
            Dims.Add(new Dim(ld, view, null, host));
        }
        return Dims.Count > 0;
    }

    /// <summary>Floating: one dim window per screen a floating video may open on, all or none.</summary>
    private static bool OpenFloating(MandatoryVideoOverlay video, Window reference)
    {
        foreach (var (bounds, scale) in FloatScreens(video, reference))
        {
            var ld = new LightsDownScreen(bounds.X, bounds.Y, bounds.Width, bounds.Height, scale);
            var view = new LightsDownCanvasView(ld);
            int before = Windows.Count;
            if (!OpenDim(reference, bounds, scale, view) || Windows.Count == before) return false;
            Dims.Add(new Dim(ld, view, Windows[^1], null));
        }
        return Dims.Count > 0;
    }

    private static void Refuse(string why)
    {
        Serilog.Log.Information("Super Lights Down: refused ({Why}); the video plays without it", why);
        CloseAll();
        Refused = why;
    }

    /// <summary>Everything down on this call (panic, the switch off, a refusal). Idempotent.</summary>
    internal static void CloseAll()
    {
        Stop();
        foreach (var w in Windows.ToArray())
            try { w.Close(); } catch (Exception ex) { Serilog.Log.Debug(ex, "Lights Down close failed"); }
        Windows.Clear();
    }

    private static void Stop()
    {
        if (_timer != null) { _timer.Stop(); _timer.Tick -= Tick; _timer = null; }
        if (_gazeHooked) { WebcamTracker.Instance.OnGazeMove -= OnGaze; _gazeHooked = false; }
        _gazeAt = double.NegativeInfinity;
        foreach (var d in Dims)
        {
            try
            {
                d.Host?.Children.Remove(d.View);
                d.Window?.Close();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Lights Down close failed"); }
        }
        Dims.Clear();
        _run = null;
        Clock.Reset();
    }

    /// <summary>WPF OnVideoTeardown, from <see cref="MandatoryVideoOverlay.CloseAll"/> before the decoder goes:
    /// a floating dim fades out, anything else goes now.</summary>
    internal static void EndOfVideo()
    {
        if (_run == null || Dims.Count == 0 || !_run.EndOfVideo()) CloseAll();
    }

    private static void Tick(object? sender, EventArgs e)
    {
        try { Frame(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Super Lights Down: stopped after a frame failed"); CloseAll(); }
    }

    /// <summary>One frame, at 30 fps (20 on the Performance tier). Tests pass <paramref name="forcedDt"/>.</summary>
    internal static void Frame(double? forcedDt = null)
    {
        var v = _video;
        var run = _run;
        if (v == null || run == null) { CloseAll(); return; }
        double now = forcedDt is { } f ? _last + f : Clock.Elapsed.TotalSeconds;
        if (forcedDt == null && now - _last < LightsDownRun.FrameSeconds(AmbientFxCanvas.Env.CurrentTier == PerformanceTier.Performance)) return;
        double dt = now - _last;
        _last = now;

        var tune = LightsDownRun.TuningFor(CoreSettings.Current);
        double gentle = GentleVisuals.FadeSeconds;   // 0 when off; Gentle only lengthens
        var motion = LightsDownRun.MotionFor(AmbientFxCanvas.Env.Level);
        Screens.Clear();
        foreach (var d in Dims) Screens.Add(d.Screen);

        if (run.Ending)
        {
            run.StepEnding(dt, Screens, tune, gentle, motion, _halo);
            PaintAll();
            if (run.Done) CloseAll();
            return;
        }
        if (!Wanted(v)) { EndOfVideo(); return; }
        if (run.Floating != v.LightsDownFloating) { CloseAll(); Start(v); return; }

        bool follow = CoreSettings.Current.LightsDownFollow;
        (double, double)? cursor = Cursor() is { } c ? (c.X, c.Y) : null;
        var eyes = Eyes(now);
        if (run.Floating)
        {
            v.CopyLightsDownFloats(Floats);
            if (Dims.Count == 0 && Floats.Count > 0)
            {
                if (!OpenFloating(v, Floats[0].Window)) { Refuse("the dim window could not open safely"); return; }
                Screens.Clear();
                foreach (var d in Dims) Screens.Add(d.Screen);
            }
            Videos.Clear();
            foreach (var (w, px) in Floats)
                Videos.Add((RuntimeHelpers.GetHashCode(w), new LightsDownRect(px.X, px.Y, px.Width, px.Height)));
            if (run.AccentDue(now)) _halo = LightsDownRun.HaloFor(CoreMods.AccentColorHex);
            run.StepFloating(dt, Screens, Videos, follow, cursor, eyes, tune, gentle, motion, _halo);
            if (Dims.Count > 0 && run.RestackDue(now) && !KeepUnder(v)) return;
        }
        else
        {
            if (run.CutDue(now)) v.CopyLightsDownCuts(Cut);
            run.StepFull(dt, Screens, cursor, follow, eyes, Cut, tune, gentle, motion);
        }
        PaintAll();
    }

    /// <summary>Every floating window and grace card over every dim; false = refused.</summary>
    private static bool KeepUnder(MandatoryVideoOverlay v)
    {
        v.CopyLightsDownAbove(Above);
        var worst = LightsDownStack.Below;
        foreach (var d in Dims)
        {
            if (d.Window == null) continue;
            var s = Stack(d.Window, Above);
            if (s == LightsDownStack.Wrong || (s == LightsDownStack.Unknown && worst == LightsDownStack.Below)) worst = s;
        }
        if (!_guard.Refuse(worst)) return true;
        Refuse(worst == LightsDownStack.Wrong ? "the window manager keeps the dim over a video" : "the window order cannot be read");
        return false;
    }

    private static void PaintAll()
    {
        foreach (var d in Dims) d.View.Paint(d.Screen.Look, d.Screen.Holes.Current, d.Screen.Holes.Changed);
    }

    /// <summary>The camera as Lights Down reads it: only when it is ALREADY running (never switched on here).</summary>
    private static LightsDownEyes Eyes(double now)
    {
        var cam = WebcamTracker.Instance;
        if (!cam.IsRunning) return LightsDownEyes.None;
        return LightsDownEyes.From(true, cam.State == WebcamTrackingState.FaceLost, cam.Calibration != null,
            now - _gazeAt, _gazeX, _gazeY);
    }

    /// <summary>Gaze arrives in DIPs local to the calibrated monitor; keep it in desktop physical pixels.</summary>
    private static void OnGaze(Point p)
    {
        try
        {
            var dpi = WebcamTracker.Instance.Calibration?.MonitorBounds?.DpiScale ?? 1;
            if (dpi <= 0.25 || dpi >= 8) dpi = 1;
            var desk = GazeFocus.GazeToDesktop(p);
            _gazeX = desk.X * dpi;
            _gazeY = desk.Y * dpi;
            _gazeAt = Clock.Elapsed.TotalSeconds;
        }
        catch (Exception ex) { Serilog.Log.Debug("Lights Down: gaze sample dropped: {E}", ex.Message); }
    }
}
