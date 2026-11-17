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
    /// The rules and maths live in CCP.Core/Super/LightsDown.cs; this file only reads the world and draws.
    /// </summary>
    internal static class LightsDownService
    {
        private static VideoService? _video;
        private static readonly List<LightsDownOverlay> _overlays = new();
        private static readonly List<Rect> _cut = new();
        private static readonly List<(IntPtr Hwnd, System.Drawing.Rectangle Rect)> _floatRects = new();
        private static readonly List<(long Key, LightsDownRect Rect, bool Halo)> _live = new();
        private static readonly List<IntPtr> _floatHwnds = new();
        private static readonly Stopwatch _clock = new();
        private static LightsDownFullScreen? _full;
        private static LightsDownFloating? _float;
        private static bool _hooked, _gazeHooked, _floating;
        private static double _last, _nextCut, _nextZ, _nextAccent;
        private static double _mouseX = double.NaN, _mouseY = double.NaN;
        private static double _gazeX, _gazeY, _gazeAt = double.NegativeInfinity;
        private static uint _haloRgb = FallbackHalo;

        /// <summary>The floating frame's own pink, when no mod colour can be read.</summary>
        private const uint FallbackHalo = 0xFF69B4;

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
                if (_float?.Ending != true && _floating == v.FloatingVideoUp) return;
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
                _full = new LightsDownFullScreen();
                _float = new LightsDownFloating();
                _clock.Restart();
                _last = 0; _nextCut = 0; _nextZ = 0; _nextAccent = 0;
                _mouseX = _mouseY = double.NaN;
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
            _full = null;
            _float = null;
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
            if (_floating && _float != null && _overlays.Count > 0) _float.BeginEnd();
            else Stop();
        }

        /// <summary>True while an overlay is up or the frame hook is armed.</summary>
        internal static bool IsActive => _hooked || _overlays.Count > 0;

        private static void OnRendering(object? sender, EventArgs e)
        {
            var v = _video;
            if (v == null || _overlays.Count == 0 || _full == null || _float == null) { Stop(); return; }

            double now = _clock.Elapsed.TotalSeconds;
            // 30 fps is plenty for slow light and halves what a fullscreen layered window costs;
            // the Performance tier drops to 20.
            double step = PerformanceProfile.CurrentTier == PerformanceTier.Performance ? 1.0 / 20 : 1.0 / 30;
            if (now - _last < step) return;
            double dt = now - _last;
            _last = now;

            var opt = App.Settings?.Current;
            // The player's options, read live so a slider moves the dim at once.
            var tune = opt == null ? LightsDownTuning.Stock
                : LightsDownOptions.Tuning(opt.LightsDownDarkness, opt.LightsDownDimSpeed, opt.LightsDownReturnSpeed);
            double gentle = Safety.GentleVisuals.FadeSeconds;   // 0 when off; Gentle only lengthens
            var motion = MotionFx.Level switch
            {
                MotionLevel.Off => LightsDownMotion.Off,
                MotionLevel.Reduced => LightsDownMotion.Reduced,
                _ => LightsDownMotion.Full,
            };

            if (_float.Ending)
            {
                _float.Step(dt, tune, gentle);
                _live.Clear();
                foreach (var o in _overlays)
                {
                    o.Holes.Update(dt, _live, gentle);
                    o.Render(LightsDownFloating.Look(_float.Grow, _float.Fade, tune, motion, o.ShortSidePx, _haloRgb),
                        o.Holes.Current, o.Holes.Changed);
                }
                if (_float.Done) Stop();
                return;
            }

            if (!Wanted(v)) { EndOfVideo(); return; }
            if (_floating != v.FloatingVideoUp) { Stop(); TryStart(); return; }

            if (_floating) FloatingFrame(v, dt, now, tune, gentle, motion);
            else FullScreenFrame(v, dt, now, tune, gentle, motion);
        }

        private static void FloatingFrame(VideoService v, double dt, double now, LightsDownTuning tune, double gentle, LightsDownMotion motion)
        {
            var f = _float!;
            f.Step(dt, tune, gentle);
            if (now >= _nextAccent) { _nextAccent = now + 1; _haloRgb = ModAccent(); }

            v.CopyFloatingVideoRects(_floatRects);
            _floatHwnds.Clear();
            foreach (var (h, _) in _floatRects) _floatHwnds.Add(h);
            bool restack = now >= _nextZ;
            if (restack) _nextZ = now + 0.5;

            foreach (var o in _overlays)
            {
                double margin = 3 * LightsDownCanvas.HaloOutDip * o.Dpi + LightsDownCanvas.HoleFeatherDip * o.Dpi;
                _live.Clear();
                foreach (var (h, r) in _floatRects)
                    if (o.Touches(r, margin)) _live.Add(((long)h, o.ToLocal(r), true));
                o.Holes.Update(dt, _live, gentle);
                if (restack) o.KeepBelow(_floatHwnds);
                o.Render(LightsDownFloating.Look(f.Grow, f.Fade, tune, motion, o.ShortSidePx, _haloRgb),
                    o.Holes.Current, o.Holes.Changed);
            }
        }

        private static void FullScreenFrame(VideoService v, double dt, double now, LightsDownTuning tune, double gentle, LightsDownMotion motion)
        {
            var s = _full!;
            bool moved = false;
            if (GetCursorPos(out var cp))
            {
                moved = LightsDownRules.Moved(_mouseX, _mouseY, cp.X, cp.Y);
                _mouseX = cp.X; _mouseY = cp.Y;
            }
            s.Step(dt, moved, LookedAway(now), tune, gentle);

            // The live attention targets are cut out of the dim, so it never covers a check.
            bool cutNow = now >= _nextCut;
            if (cutNow)
            {
                _nextCut = now + 0.1;
                v.CopyAttentionBounds(_cut);
            }
            foreach (var o in _overlays)
            {
                _live.Clear();
                for (int i = 0; i < _cut.Count; i++) _live.Add((i, o.DipToLocal(_cut[i]), false));
                o.Holes.Update(dt, _live, gentle);
                o.Render(LightsDownFullScreen.Look(s.Level, tune, motion, o.ShortSidePx), o.Holes.Current, o.Holes.Changed);
            }
        }

        /// <summary>Only with the camera already running: the face is gone, or the calibrated gaze is
        /// fresh and on none of the video screens.</summary>
        private static bool LookedAway(double now)
        {
            var cam = App.Webcam;
            if (cam == null || !cam.IsRunning) return false;
            bool faceLost = cam.State == WebcamTrackingState.FaceLost;
            bool gazeLive = cam.Calibration != null && now - _gazeAt < 0.5;
            bool onVideo = false;
            if (gazeLive)
                foreach (var o in _overlays)
                    if (o.ContainsPx(_gazeX, _gazeY)) { onVideo = true; break; }
            return LightsDownRules.LookedAway(true, faceLost, gazeLive, onVideo);
        }

        /// <summary>The active mod's accent (a live event may override it), as 0xRRGGBB.</summary>
        private static uint ModAccent()
        {
            try
            {
                var hex = App.Mods?.GetAccentColorHex();
                if (BubbleCore.DiscoPalette.TryParseHex(hex, out var rgb)) return rgb & 0xFFFFFF;
            }
            catch (Exception ex) { App.Logger?.Debug("Lights Down: mod accent unavailable: {E}", ex.Message); }
            return FallbackHalo;
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
