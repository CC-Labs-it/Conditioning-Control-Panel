using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Lights Down, the add-on to the mandatory video (reworked by the owner, 2026-11-17):
    /// <list type="bullet">
    /// <item>FULL SCREEN: a small dim at the screen edges. It eases in while the mouse rests and pulls
    /// back toward the edges the moment the mouse moves, or the eyes leave the video when the
    /// camera is already running (<see cref="LightsDownRules.LookedAway"/>; never switched on here).</item>
    /// <item>FLOATING: every screen the videos can open on dims slowly from the edges in, except a
    /// soft hole around each floating window with a halo in the mod's accent colour. The holes follow
    /// the windows as they drift, open, join and close.</item>
    /// </list>
    /// Rides <see cref="VideoService"/>'s lifecycle: it starts on VideoStarted; a full-screen dim ends
    /// with its video windows (each overlay is owned by one), a floating dim fades out in 0.6 s when
    /// the run ends. Panic (<see cref="Panic"/>), the switch going off and every non-floating teardown
    /// end it on the same frame. Click-through and non-activating: the video keeps every click and key.
    /// The rules, maths and frame step live in CCP.Core/Super/LightsDown.cs and LightsDownRun.cs (shared
    /// with the Avalonia head); this file only reads the world and draws.
    /// </summary>
    internal static class LightsDownService
    {
        private static VideoService? _video;
        private static readonly List<LightsDownOverlay> _overlays = new();
        private static readonly List<LightsDownScreen> _screens = new();
        private static readonly List<Rect> _cut = new();
        private static readonly List<LightsDownRect> _cutRects = new();
        private static readonly List<(IntPtr Hwnd, System.Drawing.Rectangle Rect)> _floatRects = new();
        private static readonly List<(long Key, LightsDownRect Px)> _videos = new();
        private static readonly List<IntPtr> _floatHwnds = new();
        private static readonly Stopwatch _clock = new();
        private static LightsDownRun? _run;
        private static bool _hooked, _gazeHooked, _floating;
        private static double _last;
        private static double _gazeX, _gazeY, _gazeAt = double.NegativeInfinity;
        private static uint _haloRgb = LightsDownRun.FallbackHalo;

        /// <summary>Wire once, after the VideoService exists. Costs nothing until a video starts with the switch on.</summary>
        public static void Attach(VideoService video)
        {
            if (_video != null) return;
            _video = video;
            video.VideoStarted += (_, _) => OnUi(TryStart);
            video.VideoEnded += (_, _) => OnUi(EndOfVideo);
            SuperAccess.Changed += e =>
            {
                if (e != SuperEffect.LightsDown) return;
                OnUi(() => { if (SuperAccess.IsOn(SuperEffect.LightsDown)) TryStart(); else Stop(); });
            };
        }

        private static bool Wanted(VideoService v)
            => LightsDownRules.ShouldRun(SuperAccess.IsOn(SuperEffect.LightsDown), v.IsPlaying, v.IsCleaningUp,
                v.HasOpenWindows || v.HasFloatingInstances);

        private static void TryStart()
        {
            var v = _video;
            if (v == null || !Wanted(v)) return;
            if (_overlays.Count > 0)
            {
                if (_run?.Ending != true && _floating == v.FloatingVideoUp) return;
                Stop();   // a fading floating dim, or the other mode: start clean
            }
            try
            {
                _floating = v.FloatingVideoUp;
                if (_floating)
                {
                    foreach (var screen in VideoService.FloatingVideoScreens())
                        Open(IntPtr.Zero, screen);
                }
                else
                {
                    foreach (var h in v.GetVideoWindowHandles())
                    {
                        if (h == IntPtr.Zero || !IsWindow(h)) continue;
                        Open(h, Screen.FromHandle(h));
                    }
                }
                if (_overlays.Count == 0) return;
                _run = new LightsDownRun(_floating);
                _clock.Restart();
                _last = 0;
                if (App.Webcam != null && !_gazeHooked) { App.Webcam.OnGazeMove += OnGaze; _gazeHooked = true; }
                if (!_hooked) { CompositionTarget.Rendering += OnRendering; _hooked = true; }
                App.Logger?.Information("Super Lights Down: {Mode}, {N} overlay(s)", _floating ? "floating" : "full screen", _overlays.Count);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Super Lights Down: could not start, the video plays without it");
                Stop();
            }
        }

        private static void Open(IntPtr owner, Screen screen)
        {
            double dpi = BubbleCountWindow.GetDpiForScreen(screen);
            var o = new LightsDownOverlay(owner, screen.Bounds, dpi);
            o.Closed += (_, _) => _overlays.Remove(o);
            _overlays.Add(o);
            o.Show();
        }

        /// <summary>Tear everything down at once. Idempotent.</summary>
        public static void Stop()
        {
            if (_hooked) { CompositionTarget.Rendering -= OnRendering; _hooked = false; }
            if (_gazeHooked && App.Webcam != null) { App.Webcam.OnGazeMove -= OnGaze; }
            _gazeHooked = false;
            _gazeAt = double.NegativeInfinity;
            foreach (var o in _overlays.ToArray())
            {
                try { o.Close(); } catch { }
            }
            _overlays.Clear();
            _run = null;
            _clock.Reset();
        }

        /// <summary>The panic key: gone on this frame, whatever the mode, no fade.</summary>
        public static void Panic()
        {
            if (IsActive) OnUi(Stop);
        }

        /// <summary>
        /// Called by VideoService.CloseAll, the one funnel every teardown reaches (panic, emergency
        /// exit, skip, natural end, attention retry). CloseAll can hold the dispatcher in a
        /// non-pumping wait for up to 500 ms, so a full-screen dim goes the moment teardown begins.
        /// A floating dim fades out (the panic key has already ended it through <see cref="Panic"/>).
        /// </summary>
        internal static void OnVideoTeardown()
        {
            if (!IsActive) return;
            OnUi(EndOfVideo);
        }

        private static void EndOfVideo()
        {
            if (!IsActive) return;
            if (_run == null || _overlays.Count == 0 || !_run.EndOfVideo()) Stop();
        }

        /// <summary>True while an overlay is up or the frame hook is armed.</summary>
        internal static bool IsActive => _hooked || _overlays.Count > 0;

        private static void OnRendering(object? sender, EventArgs e)
        {
            var v = _video;
            var run = _run;
            if (v == null || _overlays.Count == 0 || run == null) { Stop(); return; }

            double now = _clock.Elapsed.TotalSeconds;
            double step = LightsDownRun.FrameSeconds(PerformanceProfile.CurrentTier == PerformanceTier.Performance);
            if (now - _last < step) return;
            double dt = now - _last;
            _last = now;

            var tune = LightsDownRun.TuningFor(App.Settings?.Current);
            double gentle = Safety.GentleVisuals.FadeSeconds;   // 0 when off; Gentle only lengthens
            var motion = LightsDownRun.MotionFor(MotionFx.Level);
            _screens.Clear();
            foreach (var o in _overlays) _screens.Add(o.Screen);

            if (run.Ending)
            {
                run.StepEnding(dt, _screens, tune, gentle, motion, _haloRgb);
                RenderAll();
                if (run.Done) Stop();
                return;
            }

            if (!Wanted(v)) { EndOfVideo(); return; }
            if (_floating != v.FloatingVideoUp) { Stop(); TryStart(); return; }

            bool follow = App.Settings?.Current?.LightsDownFollow == true;
            (double, double)? cursor = GetCursorPos(out var cp) ? (cp.X, cp.Y) : null;
            var eyes = Eyes(now);
            if (_floating)
            {
                v.CopyFloatingVideoRects(_floatRects);
                _videos.Clear();
                _floatHwnds.Clear();
                foreach (var (h, r) in _floatRects)
                {
                    _videos.Add(((long)h, new LightsDownRect(r.X, r.Y, r.Width, r.Height)));
                    _floatHwnds.Add(h);
                }
                if (run.AccentDue(now)) _haloRgb = ModAccent();
                run.StepFloating(dt, _screens, _videos, follow, cursor, eyes, tune, gentle, motion, _haloRgb);
                if (run.RestackDue(now))
                    foreach (var o in _overlays) o.KeepBelow(_floatHwnds);
            }
            else
            {
                // The live attention targets are cut out of the dim, so it never covers a check.
                if (run.CutDue(now))
                {
                    v.CopyAttentionBounds(_cut);
                    _cutRects.Clear();
                    foreach (var r in _cut) _cutRects.Add(new LightsDownRect(r.X, r.Y, r.Width, r.Height));
                }
                run.StepFull(dt, _screens, cursor, follow, eyes, _cutRects, tune, gentle, motion);
            }
            RenderAll();
        }

        private static void RenderAll()
        {
            foreach (var o in _overlays) o.Render(o.Screen.Look, o.Holes.Current, o.Holes.Changed);
        }

        /// <summary>The camera as Lights Down reads it: only when it is ALREADY running (never switched on here).</summary>
        private static LightsDownEyes Eyes(double now)
        {
            var cam = App.Webcam;
            if (cam == null || !cam.IsRunning) return LightsDownEyes.None;
            return LightsDownEyes.From(true, cam.State == WebcamTrackingState.FaceLost, cam.Calibration != null,
                now - _gazeAt, _gazeX, _gazeY);
        }

        /// <summary>The active mod's accent (a live event may override it), as 0xRRGGBB.</summary>
        private static uint ModAccent()
        {
            try { return LightsDownRun.HaloFor(App.Mods?.GetAccentColorHex()); }
            catch (Exception ex) { App.Logger?.Debug("Lights Down: mod accent unavailable: {E}", ex.Message); }
            return LightsDownRun.FallbackHalo;
        }

        /// <summary>Gaze arrives in DIPs local to the calibrated monitor; keep it in physical pixels.</summary>
        private static void OnGaze(Point p)
        {
            try
            {
                var webcam = App.Webcam;
                double ox = 0, oy = 0, dpi;
                if (webcam != null && webcam.Calibration?.MonitorBounds is { DeviceName: not null } mb
                    && webcam.TryGetCalibratedBounds(out var physical))
                {
                    dpi = mb.DpiScale is > 0.25 and < 8.0 ? mb.DpiScale : 1.0;
                    ox = physical.X; oy = physical.Y;
                }
                else dpi = Screen.PrimaryScreen is { } ps ? BubbleCountWindow.GetDpiForScreen(ps) : 1.0;
                _gazeX = ox + p.X * dpi;
                _gazeY = oy + p.Y * dpi;
                _gazeAt = _clock.Elapsed.TotalSeconds;
            }
            catch { }
        }

        private static void OnUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) a();
            else d.BeginInvoke(a);
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    }
}
