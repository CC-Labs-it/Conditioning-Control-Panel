using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// Gentle visuals for the fullscreen video windows. Neither engine can fade its own window:
    /// WPF Window.Opacity never reaches a LibVLC VideoView or a WebView2 child HWND, and
    /// SetLayeredWindowAttributes turns WebView2 solid black (see BrowserVideoWindow). So the fade
    /// rides a separate black, click-through, layered WPF window laid over the video window:
    /// <list type="bullet">
    /// <item><see cref="CoverOwner"/> + <see cref="Lift"/>: a curtain OWNED by the video window (an owned window always
    /// sits above its owner, and dies with it, so a panic close takes it down at once) starts
    /// opaque and fades away: the clip fades in from black.</item>
    /// <item><see cref="Cover"/> + <see cref="Lift"/>: unowned topmost curtains over the rectangles
    /// the video windows had, raised before they close and lifted after: the desktop fades back in
    /// instead of snapping back after a natural end.</item>
    /// </list>
    /// Only ever used while gentle visuals are on. <see cref="DropAll"/> is the panic path.
    /// </summary>
    internal static class GentleCurtain
    {
        private static readonly List<Window> _live = new();

        /// <summary>Opaque black cover owned by <paramref name="owner"/> (shown, not fading yet:
        /// call <see cref="Lift"/>). Null when the owner has no HWND or the build failed.</summary>
        public static Window? CoverOwner(Window owner)
        {
            try
            {
                var hwnd = new WindowInteropHelper(owner).Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
                return Build(r, owner);
            }
            catch (Exception ex) { App.Logger?.Debug("GentleCurtain.CoverOwner failed: {E}", ex.Message); return null; }
        }

        /// <summary>The screen rectangle (physical pixels) a window covers, read while it still has
        /// an HWND. Null when it has none.</summary>
        public static Int32Rect? RectOf(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
                return new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            }
            catch { return null; }
        }

        /// <summary>Opaque black unowned topmost curtains over <paramref name="rects"/>.</summary>
        public static List<Window> Cover(IEnumerable<Int32Rect> rects)
        {
            var list = new List<Window>();
            foreach (var rc in rects)
            {
                try
                {
                    var c = Build(new RECT { Left = rc.X, Top = rc.Y, Right = rc.X + rc.Width, Bottom = rc.Y + rc.Height }, null);
                    if (c != null) list.Add(c);
                }
                catch (Exception ex) { App.Logger?.Debug("GentleCurtain.Cover failed: {E}", ex.Message); }
            }
            return list;
        }

        /// <summary>Fade the curtains out over <paramref name="seconds"/>, then close them.</summary>
        public static void Lift(IEnumerable<Window> curtains, double seconds)
        {
            var dur = TimeSpan.FromSeconds(Math.Max(0.05, seconds));
            foreach (var c in curtains)
            {
                try
                {
                    var anim = new DoubleAnimation(1.0, 0.0, dur);
                    anim.Completed += (_, _) => CloseOne(c);
                    c.BeginAnimation(UIElement.OpacityProperty, anim);
                    // Backstop: an animation on a window that lost its HWND never completes.
                    var guard = new DispatcherTimer { Interval = dur + TimeSpan.FromSeconds(2) };
                    guard.Tick += (_, _) => { guard.Stop(); CloseOne(c); };
                    guard.Start();
                }
                catch { CloseOne(c); }
            }
        }

        /// <summary>Take every curtain down at once (panic, engine stop, forced teardown).</summary>
        public static void DropAll()
        {
            if (_live.Count == 0) return;
            foreach (var c in _live.ToArray()) CloseOne(c);
        }

        private static Window? Build(RECT r, Window? owner)
        {
            var c = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Focusable = false,
                IsHitTestVisible = false,
                Background = Brushes.Black,
                Opacity = 1.0,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Title = "",
            };
            if (owner != null) c.Owner = owner;
            var hwnd = new WindowInteropHelper(c).EnsureHandle();
            // A layered window's painted pixels take input regardless of IsHitTestVisible: make it
            // truly click-through and never activatable.
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
            c.Show();
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
            _live.Add(c);
            c.Closed += (_, _) => _live.Remove(c);
            return c;
        }

        private static void CloseOne(Window c)
        {
            try { if (_live.Contains(c)) c.Close(); } catch { }
            _live.Remove(c);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
