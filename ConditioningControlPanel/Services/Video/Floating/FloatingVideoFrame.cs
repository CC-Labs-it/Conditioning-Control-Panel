using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Video.Browser;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>
    /// One floating video window as the video service sees it: its frame, its motion and its sound.
    /// The service decides HOW MANY there are and WHICH ONE carries the sound; the frame decides how
    /// a window looks and moves.
    /// </summary>
    internal interface IFloatingVideoSurface
    {
        /// <summary>The player pressed or flung this window. Raised on the UI thread.</summary>
        event Action<IFloatingVideoSurface>? Touched;

        /// <summary>True lights the border: this window carries the sound.</summary>
        bool AudioLit { get; set; }

        /// <summary>Silences or restores this window's own sound. Never touches another window.</summary>
        void SetMuted(bool muted);

        /// <summary>
        /// How fast a thrown window travels: 1 is the flash images' own speed, 0.1 a slow drift.
        /// Scales the throw and the top speed, never the drag under the hand. Live: a window in
        /// flight follows a change.
        /// </summary>
        double MotionSpeed { get; set; }

        /// <summary>
        /// Fling and bounce, starting on the monitor whose work area is given (device pixels). A
        /// held window may cross to any monitor; once let go it bounces inside the work area of
        /// the monitor it is on.
        /// </summary>
        void StartMotion(Rect boundsPx);

        /// <summary>Hide the window (a one-pixel window region: nothing to see, nothing to click) until
        /// <see cref="Reveal"/>. Call before the window is shown (FloatingFirstFrame).</summary>
        void Veil();

        /// <summary>Show a veiled window: the opening animation when <paramref name="animate"/>
        /// (FloatingSpawnLook), else at once. <paramref name="revealed"/> runs once it is fully shown.</summary>
        void Reveal(bool animate, Action? revealed);

        /// <summary>The closing animation; <paramref name="done"/> runs once at its end, or at once
        /// when not animating. Detach runs it too, so it can never be lost.</summary>
        void Vanish(bool animate, Action done);

        /// <summary>Stops moving and lets go of every hook. Safe to call twice, and on a closed window.</summary>
        void Detach();
    }

    /// <summary>
    /// Gives a floating video window its frame (border, rounded corners), its fling and bounce, and
    /// its audio light. Works on any top-level WPF window, so both video engines share it.
    ///
    /// How it is built, and why:
    ///   * BORDER = the window's own BorderThickness / BorderBrush. The stock Window template draws
    ///     them as a margin around the content, so the video (a WebView2 child HWND on the default
    ///     engine, which paints over any WPF content laid on top of it) is simply inset. Nothing is
    ///     drawn over the video, and nothing is reparented.
    ///   * CORNERS = the DWM corner preference (Windows 11), or a rounded window region where DWM
    ///     has none (Windows 10). Both clip child HWNDs too. Never a layered window: a WebView2
    ///     does not paint inside one.
    ///   * LIGHT = the border brush colour, eased in and out and breathing on a 30 fps
    ///     <see cref="FrameClock"/>. No Effect anywhere.
    ///   * MOTION = <see cref="FloatingVideoMotion"/> (the flash images' rules) stepped by the same
    ///     clock; the window is moved with SetWindowPos, never activated.
    ///   * POINTER = <see cref="FloatingVideoPointer"/>, one listening mouse hook for all frames.
    /// </summary>
    internal static class FloatingVideoFrame
    {
        /// <param name="window">The floating video window, already created (shown or not).</param>
        /// <param name="mute">
        /// How to silence a window the frame cannot silence itself (the LibVLC window). Null when
        /// the window knows how (BrowserVideoWindow).
        /// </param>
        public static IFloatingVideoSurface Attach(Window window, Action<bool>? mute = null)
            => new Frame(window, mute);

        private sealed class Frame : IFloatingVideoSurface, IFloatingPointerTarget
        {
            private static readonly Stopwatch Clock = Stopwatch.StartNew();
            private static double NowMs => Clock.Elapsed.TotalMilliseconds;

            private readonly Window? _win;
            private readonly Action<bool>? _mute;
            private readonly FrameClock _clock = new();
            private readonly SolidColorBrush _brush = new();
            private readonly HwndSourceHook _wndProc;
            private readonly (byte R, byte G, byte B) _accent;

            private IntPtr _hwnd;
            private HwndSource? _source;
            private FloatingVideoMotion? _motion;
            private Rect? _bounds;
            private bool _detached;
            private bool _audioLit;
            private double _lit;
            private double _breathSeconds;
            private double _lastTickMs;
            private bool _regionCorners;
            private double _speed = 1.0;
            private bool _forceMove;   // the window was moved or sized behind the motion's back
            private bool _resync;      // ...while it was at rest: read its rectangle again
            private (byte R, byte G, byte B) _painted;
            private bool _veiled;          // the region shows _shown of the window (FloatingSpawnLook)
            private bool _shaped;          // a region is on the window
            private double _shown = 1.0;
            private DispatcherTimer? _anim;
            private Action? _animDone;

            public event Action<IFloatingVideoSurface>? Touched;

            public Frame(Window? window, Action<bool>? mute)
            {
                _win = window;
                _mute = mute;
                _wndProc = WndProc;
                _accent = ResolveAccent();
                _clock.Tick += OnTick;
                if (window == null) { _detached = true; return; }

                try
                {
                    Paint(force: true);
                    window.BorderThickness = new Thickness(FloatingFrameLook.BorderDip);
                    window.BorderBrush = _brush;
                    window.Closed += OnClosed;
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd != IntPtr.Zero) Hook(hwnd);
                    else window.SourceInitialized += OnSourceInitialized;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- contract ----

            public double MotionSpeed
            {
                get => _speed;
                set
                {
                    _speed = double.IsNaN(value) ? 1.0
                        : Math.Clamp(value, FloatingVideoMotion.MinSpeed, FloatingVideoMotion.MaxSpeed);
                    if (_motion != null) _motion.Speed = _speed;   // a window in flight follows
                }
            }

            public bool AudioLit
            {
                get => _audioLit;
                set
                {
                    if (_audioLit == value) return;
                    _audioLit = value;
                    if (value) _breathSeconds = 0;   // every light starts its breath from the bottom
                    RunClock();
                }
            }

            public void SetMuted(bool muted)
            {
                try
                {
                    if (_win is BrowserVideoWindow page) page.SetFloatingMuted(muted);
                    else _mute?.Invoke(muted);
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            public void StartMotion(Rect boundsPx)
            {
                if (_detached || boundsPx.Width <= 0 || boundsPx.Height <= 0) return;
                _bounds = boundsPx;
                SyncMotion(moveIfClamped: true, startBounds: true);
            }

            public void Detach()
            {
                if (_detached) return;
                _detached = true;
                _clock.Stop();
                _clock.Tick -= OnTick;
                FinishAnimation();
                FloatingVideoPointer.Unregister(this);
                _motion?.Drop();
                Touched = null;
                try
                {
                    if (_source != null && !_source.IsDisposed) _source.RemoveHook(_wndProc);
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                _source = null;
                try
                {
                    if (_win != null)
                    {
                        _win.Closed -= OnClosed;
                        _win.SourceInitialized -= OnSourceInitialized;
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- veil, reveal, close (FloatingFirstFrame, FloatingSpawnLook) ----

            public void Veil()
            {
                if (_detached) return;
                _veiled = true;
                _shown = 0;
                ApplyShape();
            }

            public void Reveal(bool animate, Action? revealed)
            {
                if (_detached || !_veiled || _anim != null) { revealed?.Invoke(); return; }
                if (!animate || _hwnd == IntPtr.Zero) { EndVeil(); revealed?.Invoke(); return; }
                Animate(FloatingSpawnLook.RevealMs, FloatingSpawnLook.RevealScale, () => { EndVeil(); revealed?.Invoke(); });
            }

            public void Vanish(bool animate, Action done)
            {
                if (_detached || !animate || _hwnd == IntPtr.Zero || !IsWindow(_hwnd)) { done(); return; }
                FinishAnimation();
                _veiled = true;
                Animate(FloatingSpawnLook.VanishMs, FloatingSpawnLook.VanishScale, done);
            }

            private void EndVeil()
            {
                if (_detached) return;   // closing mid-reveal: never one frame of the full window
                _veiled = false;
                _shown = 1.0;
                ApplyShape();
            }

            /// <summary>Only the region moves, about 60 times a second: no window opacity (the window
            /// is never layered), no Effect, no resize of the video under it.</summary>
            private void Animate(int ms, Func<double, double> shownAt, Action done)
            {
                var sw = Stopwatch.StartNew();
                _animDone = done;
                var anim = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                _anim = anim;
                anim.Tick += (_, _) =>
                {
                    if (!ReferenceEquals(_anim, anim)) { anim.Stop(); return; }
                    var t = sw.Elapsed.TotalMilliseconds;
                    _shown = shownAt(t);
                    ApplyShape();
                    if (t >= ms) FinishAnimation();
                };
                _shown = shownAt(0);
                ApplyShape();
                anim.Start();
            }

            /// <summary>Stops a running animation and runs what was waiting on its end, once.</summary>
            private void FinishAnimation()
            {
                var anim = _anim;
                var done = _animDone;
                _anim = null;
                _animDone = null;
                anim?.Stop();
                if (done == null) return;
                try { done(); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- window ----

            private void OnSourceInitialized(object? sender, EventArgs e)
            {
                if (_detached || _win == null) return;
                try { Hook(new WindowInteropHelper(_win).Handle); }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            private void OnClosed(object? sender, EventArgs e) => Detach();

            private void Hook(IntPtr hwnd)
            {
                if (hwnd == IntPtr.Zero || _hwnd != IntPtr.Zero) return;
                _hwnd = hwnd;
                _source = HwndSource.FromHwnd(hwnd);
                _source?.AddHook(_wndProc);

                // A floating video never takes focus from the player's current app: not when it
                // is shown, not when it is pressed, dragged or thrown.
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                if ((ex & WS_EX_NOACTIVATE) == 0) SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);

                RoundCorners();
                if (_veiled) ApplyShape();
                Paint(force: true);
                FloatingVideoPointer.Register(this);
                if (_bounds != null) SyncMotion(moveIfClamped: true);
                RunClock();
            }

            private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                switch (msg)
                {
                    case WM_MOUSEACTIVATE:
                        handled = true;
                        return (IntPtr)MA_NOACTIVATE;
                    case WM_NCLBUTTONDOWN when wParam == (IntPtr)HTCAPTION:
                        // The older caption drag (a modal move loop). The frame moves the window
                        // itself now; two movers would fight, so this one is refused.
                        handled = true;
                        return IntPtr.Zero;
                    case WM_SIZE:
                        if (_regionCorners || _veiled) ApplyShape();
                        OnResized();
                        break;
                    case WM_DISPLAYCHANGE:
                    case WM_SETTINGCHANGE when wParam == (IntPtr)SPI_SETWORKAREA:
                        // A monitor went away, a resolution or a taskbar changed: once Windows
                        // has settled, pull the window back onto a work area.
                        _resync = true;
                        RunClock();
                        break;
                }
                return IntPtr.Zero;
            }

            private void RoundCorners()
            {
                try
                {
                    int round = DWMWCP_ROUND;
                    if (DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int)) == 0) return;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                _regionCorners = true;   // no DWM corners here (Windows 10): cut them ourselves
                ApplyShape();
            }

            /// <summary>The window's region: rounded corners where DWM has none (Windows 10), and the
            /// visible share during a veil, reveal or close. No region at all when neither applies,
            /// so DWM's own rounding (Windows 11) comes back once a reveal ends.</summary>
            private void ApplyShape()
            {
                try
                {
                    if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;
                    if (!_veiled && !_regionCorners)
                    {
                        if (_shaped && SetWindowRgn(_hwnd, IntPtr.Zero, true) != 0) _shaped = false;
                        return;
                    }
                    if (!GetWindowRect(_hwnd, out var r)) return;
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    if (w <= 0 || h <= 0) return;
                    uint dpi = 96;
                    try { dpi = GetDpiForWindow(_hwnd); } catch (Exception ex) { Diag.Swallowed(ex); }
                    if (dpi == 0) dpi = 96;
                    int d = (int)Math.Round(FloatingFrameLook.CornerDip * 2.0 * dpi / 96.0);
                    var (l, t, rr, b) = _veiled ? FloatingSpawnLook.Visible(w, h, _shown) : (0, 0, w, h);
                    // Fully veiled = ONE pixel at the centre, never an empty region: the window stays on
                    // screen for Chromium, so its page is not treated as hidden while the clip loads and
                    // paints at once when the region opens.
                    var rgn = rr > l && b > t ? CreateRoundRectRgn(l, t, rr + 1, b + 1, d, d) : CreateRectRgn(w / 2, h / 2, w / 2 + 1, h / 2 + 1);
                    if (rgn == IntPtr.Zero) return;
                    if (SetWindowRgn(_hwnd, rgn, true) == 0) DeleteObject(rgn);   // on success Windows owns it
                    else _shaped = true;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            /// <summary>The window changed size by itself: it landed on a monitor with another
            /// scale and WPF resized it to keep its size in DIPs (border and corners follow, they
            /// are DIPs too). The motion learns the new size; the next tick puts the window back
            /// under the hand.</summary>
            private void OnResized()
            {
                if (_detached || _motion == null) return;
                try
                {
                    if (_motion.Active)
                    {
                        if (!GetWindowRect(_hwnd, out var r)) return;
                        _motion.Resize(r.Right - r.Left, r.Bottom - r.Top);
                        _forceMove = true;
                    }
                    else _resync = true;
                    RunClock();
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- pointer (FloatingVideoPointer, UI thread) ----

            IntPtr IFloatingPointerTarget.Hwnd => _hwnd;

            bool IFloatingPointerTarget.PointerDown(Point px)
            {
                if (_detached) return false;
                RaiseTouched();
                if (!SyncMotion(moveIfClamped: false) || _motion == null) return false;
                _motion.Press(px.X, px.Y, NowMs);
                RunClock();
                return true;
            }

            void IFloatingPointerTarget.PointerMove(Point px) => _motion?.Move(px.X, px.Y, NowMs);

            void IFloatingPointerTarget.PointerUp(Point px)
            {
                if (_detached || _motion == null || !_motion.Held) return;
                _motion.Move(px.X, px.Y, NowMs);
                Release();
            }

            private void Release()
            {
                if (_motion == null) return;
                var outcome = _motion.Release(NowMs, MotionLevelNow());
                FloatingVideoPointer.Released(this);
                MoveTo(_motion.X, _motion.Y);
                if (outcome == FlashDragOutcome.Fling) RaiseTouched();
            }

            /// <summary>Queued, never raised inside the mouse hook: the listener posts to a page and
            /// reshuffles the sound, and a hook callback has to return at once.</summary>
            private void RaiseTouched()
            {
                try
                {
                    var d = _win?.Dispatcher;
                    if (d == null || d.HasShutdownStarted) return;
                    d.BeginInvoke(new Action(() =>
                    {
                        if (_detached) return;
                        try { Touched?.Invoke(this); }
                        catch (Exception ex) { Diag.Swallowed(ex); }
                    }));
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- motion ----

            /// <summary>Read the window's real rectangle into the motion (the service may have
            /// placed it since), pulled inside the bounds. False when there is nothing to move.</summary>
            private bool SyncMotion(bool moveIfClamped, bool startBounds = false)
            {
                if (_detached || _bounds == null || _hwnd == IntPtr.Zero) return false;
                if (!IsWindow(_hwnd) || !GetWindowRect(_hwnd, out var r)) return false;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w <= 0 || h <= 0) return false;
                var b = _bounds.Value;
                if (_motion == null)
                {
                    _motion = new FloatingVideoMotion(r.Left, r.Top, w, h, b.X, b.Y, b.Width, b.Height)
                    {
                        Speed = _speed,
                        MonitorAt = WorkAreaAt,
                    };
                }
                else
                {
                    if (_motion.Active) _motion.Drop();
                    // StartMotion names the monitor; any later sync takes the one the window is on.
                    if (startBounds) _motion.SetBounds(b.X, b.Y, b.Width, b.Height);
                    _motion.Resync(r.Left, r.Top, w, h, rehome: !startBounds);
                }
                // Every monitor, read fresh: a held window may be carried to any of them.
                _motion.SetDesktop(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
                    GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
                if (moveIfClamped && ((int)Math.Round(_motion.X) != r.Left || (int)Math.Round(_motion.Y) != r.Top))
                    MoveTo(_motion.X, _motion.Y);
                return true;
            }

            /// <summary>The monitor seam of <see cref="FloatingVideoMotion.MonitorAt"/>: the work
            /// area of the monitor nearest to a point, device pixels.</summary>
            private static (double X, double Y, double W, double H)? WorkAreaAt(double x, double y)
            {
                try
                {
                    var mon = MonitorFromPoint(new POINT { X = (int)Math.Round(x), Y = (int)Math.Round(y) }, MONITOR_DEFAULTTONEAREST);
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return null;
                    return (mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right - mi.rcWork.Left, mi.rcWork.Bottom - mi.rcWork.Top);
                }
                catch (Exception ex) { Diag.Swallowed(ex); return null; }
            }

            private void MoveTo(double x, double y)
            {
                try
                {
                    if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd)) return;
                    SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            // ---- clock ----

            private void RunClock()
            {
                if (_detached || _hwnd == IntPtr.Zero) return;
                // The window follows the hand every frame; a flight and the light keep the 30 fps beat.
                _clock.Interval = TimeSpan.FromMilliseconds(_motion?.Held == true ? 0 : 33);
                if (_clock.IsEnabled) return;
                _lastTickMs = NowMs;
                _clock.Start();
            }

            private void OnTick(object? sender, EventArgs e)
            {
                if (_detached) { _clock.Stop(); return; }
                if (_hwnd != IntPtr.Zero && !IsWindow(_hwnd)) { Detach(); return; }

                var now = NowMs;
                var dt = Math.Clamp((now - _lastTickMs) / 1000.0, 0.0, 0.1);
                _lastTickMs = now;

                var level = MotionLevelNow();
                if (_resync)
                {
                    _resync = false;
                    if (_motion?.Held != true) SyncMotion(moveIfClamped: true);
                }
                if (_forceMove && _motion != null)
                {
                    _forceMove = false;
                    MoveTo(_motion.X, _motion.Y);
                }
                if (_motion != null && _motion.Active)
                {
                    // A button-up the hook never delivered must not leave the window stuck to the hand.
                    if (_motion.Held && (GetAsyncKeyState(GetSystemMetrics(SM_SWAPBUTTON) != 0 ? VK_RBUTTON : VK_LBUTTON) & 0x8000) == 0) Release();
                    if (_motion.Step(dt)) MoveTo(_motion.X, _motion.Y);
                }

                _lit = FloatingFrameLook.Approach(_lit, _audioLit, dt);
                if (_audioLit) _breathSeconds += dt;
                Paint(force: false);

                bool easing = _audioLit ? _lit < 1.0 : _lit > 0.0;
                bool breathing = _audioLit && level != MotionLevel.Off;
                if (_motion?.Active == true || easing || breathing)
                    _clock.Interval = TimeSpan.FromMilliseconds(_motion?.Held == true ? 0 : 33);
                else
                    _clock.Stop();
            }

            private void Paint(bool force)
            {
                var c = FloatingFrameLook.Border(_accent.R, _accent.G, _accent.B, _lit,
                    FloatingFrameLook.Breath(_breathSeconds, MotionLevelNow()));
                if (!force && c == _painted) return;
                _painted = c;
                try
                {
                    _brush.Color = Color.FromRgb(c.R, c.G, c.B);
                    if (_hwnd != IntPtr.Zero && !_regionCorners)
                    {
                        // The hairline DWM draws on a rounded window wears the same colour.
                        int colorRef = c.R | (c.G << 8) | (c.B << 16);
                        DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
                    }
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }

            private static MotionLevel MotionLevelNow()
            {
                try { return MotionFx.Level; }
                catch { return MotionLevel.Full; }
            }

            private static (byte, byte, byte) ResolveAccent()
            {
                try
                {
                    if (Application.Current?.TryFindResource("PinkColor") is Color c) return (c.R, c.G, c.B);
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
                return (FloatingFrameLook.AccentR, FloatingFrameLook.AccentG, FloatingFrameLook.AccentB);
            }

            // ---- native ----

            private const int GWL_EXSTYLE = -20;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WM_SIZE = 0x0005;
            private const int WM_SETTINGCHANGE = 0x001A;
            private const int WM_DISPLAYCHANGE = 0x007E;
            private const int SPI_SETWORKAREA = 0x002F;
            private const int SM_XVIRTUALSCREEN = 76;
            private const int SM_YVIRTUALSCREEN = 77;
            private const int SM_CXVIRTUALSCREEN = 78;
            private const int SM_CYVIRTUALSCREEN = 79;
            private const uint MONITOR_DEFAULTTONEAREST = 2;
            private const int WM_MOUSEACTIVATE = 0x0021;
            private const int WM_NCLBUTTONDOWN = 0x00A1;
            private const int HTCAPTION = 2;
            private const int MA_NOACTIVATE = 3;
            private const int VK_LBUTTON = 0x01;
            private const int VK_RBUTTON = 0x02;
            private const int SM_SWAPBUTTON = 23;
            private const uint SWP_NOSIZE = 0x0001;
            private const uint SWP_NOZORDER = 0x0004;
            private const uint SWP_NOACTIVATE = 0x0010;
            private const uint SWP_NOOWNERZORDER = 0x0200;
            private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            private const int DWMWA_BORDER_COLOR = 34;
            private const int DWMWCP_ROUND = 2;

            [StructLayout(LayoutKind.Sequential)]
            private struct RECT { public int Left, Top, Right, Bottom; }

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT { public int X, Y; }

            [StructLayout(LayoutKind.Sequential)]
            private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

            [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
            [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
            [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
            [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
            [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
            [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
            [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
            [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
            [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
            [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hWnd);
            [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
            [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
            [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
            [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
            [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        }
    }
}
