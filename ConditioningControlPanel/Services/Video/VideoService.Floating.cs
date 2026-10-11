using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ConditioningControlPanel.Services.Video;
using Screen = System.Windows.Forms.Screen;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Floating mandatory videos (AppSettings.MandatoryVideoFloating), the single-window path:
    /// the SAME player in one small topmost window instead of a window covering every targeted
    /// monitor. It is what a floating video falls back to when the browser engine cannot take the
    /// clip (LibVLC route); otherwise floating videos are the equal instances of
    /// VideoService.FloatingInstances.cs. Only the rectangle and the screen list change, and there
    /// are no attention checks. The decision itself is <see cref="MandatoryVideoPresentation.ShouldFloat"/>.
    /// </summary>
    public partial class VideoService
    {
        /// <summary>The floating window's rectangle in PHYSICAL pixels; null = full screen.</summary>
        private System.Drawing.Rectangle? _floatBounds;
        private Screen? _floatScreen;
        /// <summary>Set by the attention-check message so the replay comes back where the clip was.</summary>
        private bool _floatReuse;

        /// <summary>The clip on screen (or the last one) is the floating window, not full screen.</summary>
        internal bool IsFloating => _floatBounds != null && _floatScreen != null;

        /// <summary>Read once per clip, before any window exists. A LibVLC fallback of the same clip
        /// does not come back through here, so it keeps the mode and the spot.</summary>
        private void DecidePresentation(bool strict)
        {
            var reuse = _floatReuse && IsFloating;
            _floatReuse = false;
            bool floating;
            try
            {
                floating = MandatoryVideoPresentation.ShouldFloat(
                    App.Settings?.Current?.MandatoryVideoFloating == true,
                    strict || IsStrictActive,
                    App.Lockdown?.IsActive == true,
                    App.Chaos?.IsRunning == true);
            }
            catch (Exception ex) { Diag.Swallowed(ex); floating = false; }

            if (floating && reuse) return;
            ClearFloating();
            if (!floating) return;

            var screens = VideoScreens();
            if (screens == null || screens.Length == 0) return;
            var screen = screens[_random.Next(screens.Length)];
            _floatScreen = screen;
            _floatBounds = FloatRectOn(screen, null);
            VideoDiag.Log("VIDEO", $"floating window {_floatBounds} on {screen.DeviceName}");
        }

        /// <summary>A floating rectangle on this screen's work area, in PHYSICAL pixels: the
        /// player's size (AppSettings.MandatoryVideoFloatingSizePercent of the work-area width),
        /// a random spot, overlap allowed but never exactly on a window already up.</summary>
        private System.Drawing.Rectangle FloatRectOn(Screen screen, System.Collections.Generic.IEnumerable<System.Drawing.Rectangle>? takenPx)
        {
            var dpi = BubbleCountWindow.GetDpiForScreen(screen);
            if (dpi <= 0) dpi = 1;
            var wa = screen.WorkingArea;
            var taken = new System.Collections.Generic.List<(int X, int Y, int W, int H)>();
            if (takenPx != null)
                foreach (var t in takenPx)
                    taken.Add(((int)((t.X - wa.X) / dpi), (int)((t.Y - wa.Y) / dpi), (int)(t.Width / dpi), (int)(t.Height / dpi)));
            var (x, y, w, h) = MandatoryVideoPresentation.FloatingRect((int)(wa.Width / dpi), (int)(wa.Height / dpi), _random,
                App.Settings?.Current?.MandatoryVideoFloatingSizePercent ?? MandatoryVideoPresentation.DefaultSizePercent, taken);
            return new System.Drawing.Rectangle(wa.X + (int)(x * dpi), wa.Y + (int)(y * dpi), (int)(w * dpi), (int)(h * dpi));
        }

        private void ClearFloating()
        {
            _floatBounds = null;
            _floatScreen = null;
        }

        /// <summary>Where this clip's windows, targets and messages go: the one floating screen, or
        /// every targeted screen.</summary>
        private Screen[] PlaybackScreens() => IsFloating ? new[] { _floatScreen! } : VideoScreens();

        private System.Drawing.Rectangle BoundsFor(Screen screen) => _floatBounds ?? screen.Bounds;

        /// <summary>The player may have dragged the window: follow it, so the attention-check
        /// message and the replay land where the clip was.</summary>
        private void CaptureFloatBounds()
        {
            try
            {
                if (!IsFloating || _primaryVideoWindow == null) return;
                var hwnd = new WindowInteropHelper(_primaryVideoWindow).Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
                if (r.Right - r.Left < 100 || r.Bottom - r.Top < 60) return;
                _floatBounds = new System.Drawing.Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                _floatScreen = Screen.FromHandle(hwnd);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>Move the floating window with the mouse. A native caption drag, so it works over
        /// the WebView2 surface too (the page reports the press, WPF never sees it) and never
        /// activates the window. Full screen ignores it.</summary>
        private void BeginFloatingDrag(Window? win)
        {
            try
            {
                if (!LegacyCaptionDrag || !IsFloating || win == null || _gracePaused) return;
                var hwnd = new WindowInteropHelper(win).Handle;
                if (hwnd == IntPtr.Zero) return;
                ReleaseCapture();
                SendMessage(hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch (Exception ex) { App.Logger?.Debug("VideoService: floating drag failed - {Error}", ex.Message); }
        }

        /// <summary>The first cut's drag (a native caption move). Off: FloatingVideoFrame owns
        /// motion (fling and bounce) and refuses a caption move on a window it is attached to.</summary>
        private static readonly bool LegacyCaptionDrag = false;

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct FloatRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out FloatRect rect);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
