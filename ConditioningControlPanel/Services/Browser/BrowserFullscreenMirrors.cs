using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace ConditioningControlPanel.Services.Browser
{
    /// <summary>
    /// Which screens get a copy of a fullscreen browser video (ccp-bugs #1639). Pure, so it is
    /// testable: every "Show content on" screen except the one the real fullscreen host sits on.
    /// Only when the picker covers two screens or more, the same condition the 7.2.3 clone had:
    /// content pinned to one screen never spreads to the others.
    /// </summary>
    internal static class BrowserFullscreenMirrorRule
    {
        public static IReadOnlyList<string> ExtraScreens(string? hostDevice, IEnumerable<string?> contentDevices)
        {
            var content = contentDevices
                .Where(d => !string.IsNullOrEmpty(d))
                .Select(d => d!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (content.Count < 2) return Array.Empty<string>();
            return content.Where(d => !string.Equals(d, hostDevice, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>The source picture scaled to fit inside the destination, centred (letterbox,
        /// never crop: the copy shows the whole video the main screen shows).</summary>
        public static (int Left, int Top, int Right, int Bottom) FitRect(int srcW, int srcH, int destW, int destH)
        {
            destW = Math.Max(1, destW);
            destH = Math.Max(1, destH);
            if (srcW <= 0 || srcH <= 0) return (0, 0, destW, destH);
            double scale = Math.Min((double)destW / srcW, (double)destH / srcH);
            int w = Math.Clamp((int)Math.Round(srcW * scale), 1, destW);
            int h = Math.Clamp((int)Math.Round(srcH * scale), 1, destH);
            int left = (destW - w) / 2;
            int top = (destH - h) / 2;
            return (left, top, left + w, top + h);
        }
    }

    /// <summary>
    /// The fullscreen browser video on every other content screen, WITHOUT the Windows display
    /// clone that froze the app (#1565 #1570 #1585) and WITHOUT a second player: each extra screen
    /// gets a plain black window carrying a live DWM thumbnail of the real fullscreen host (the
    /// technique <see cref="Fyp.FypGhostOverlay"/> already ships for a WebView2 source). DWM draws
    /// the copy on the GPU from the surface it is composing anyway, so there is no decoder, no
    /// download, no audio and no drift; the real host keeps the sound and every control.
    ///
    /// The copies are GDI (WinForms) windows, so they ask the WPF render thread for nothing
    /// (#1504 #1510 #1513), and they are OWNED by the host: Windows keeps them above it, takes them
    /// out of topmost when the host rents its topmost claim back (#905 #952), hides them with it
    /// and destroys them with it, so a copy can never outlive the video as a black topmost screen.
    /// A copy never takes focus; a click hands focus to the host (Esc and F11 work there) and a
    /// double-click leaves fullscreen, like the video's own double-click.
    /// </summary>
    internal sealed class BrowserFullscreenMirrors
    {
        private readonly IntPtr _source;
        private readonly Action _focusHost;
        private readonly Action _exitFullscreen;
        private readonly List<Mirror> _mirrors = new();
        private readonly WF.Timer _watch = new() { Interval = 400 };
        private int _srcW, _srcH;
        private bool _closed;

        public BrowserFullscreenMirrors(IntPtr sourceHwnd, Action focusHost, Action exitFullscreen)
        {
            _source = sourceHwnd;
            _focusHost = focusHost;
            _exitFullscreen = exitFullscreen;
            _watch.Tick += (_, _) => Watch();
        }

        public int Count => _mirrors.Count;

        /// <summary>One line per screen for the log: the DWM answers, never anything personal.</summary>
        public string Diag { get; private set; } = "";

        /// <summary>Open a copy on each screen. A copy whose thumbnail DWM refuses, or whose source
        /// reports no size, is closed at once: a black screen is worse than no copy.</summary>
        public void Show(IEnumerable<WF.Screen> screens, bool topmost)
        {
            if (_closed || _source == IntPtr.Zero || !IsWindow(_source)) return;
            var diag = new List<string>();
            foreach (var screen in screens)
            {
                var mirror = new Mirror(this, _source, screen.Bounds, topmost);
                try
                {
                    mirror.Show();
                    int hr = DwmRegisterThumbnail(mirror.Handle, _source, out var thumb);
                    if (hr == 0 && thumb == IntPtr.Zero) hr = unchecked((int)0x80004005);
                    SIZE size = default;
                    int sizeHr = hr == 0 ? DwmQueryThumbnailSourceSize(thumb, out size) : hr;
                    bool ok = hr == 0 && sizeHr == 0 && size.cx > 0 && size.cy > 0;
                    diag.Add($"{screen.Bounds.Width}x{screen.Bounds.Height} register=0x{hr:X8} source=0x{sizeHr:X8} {size.cx}x{size.cy}");
                    if (!ok)
                    {
                        if (thumb != IntPtr.Zero) try { DwmUnregisterThumbnail(thumb); } catch { }
                        mirror.Close();
                        continue;
                    }
                    mirror.Thumb = thumb;
                    _mirrors.Add(mirror);
                }
                catch (Exception ex)
                {
                    diag.Add($"threw: {ex.Message}");
                    try { mirror.Close(); } catch { }
                }
            }
            Diag = string.Join("; ", diag);
            _srcW = _srcH = -1;   // force the first point
            Watch();
            if (_mirrors.Count > 0) _watch.Start();
        }

        /// <summary>Closes every copy. Safe to call twice.</summary>
        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _watch.Stop(); _watch.Dispose(); } catch { }
            foreach (var m in _mirrors.ToArray())
            {
                try { m.Close(); } catch (Exception ex) { App.Logger?.Debug("Browser mirror close: {E}", ex.Message); }
            }
            _mirrors.Clear();
        }

        /// <summary>Follows the host: it is shown small and maximized a turn later, so the copy is
        /// re-pointed whenever its size changes. A host that is gone or hidden takes the copies with it.</summary>
        private void Watch()
        {
            if (_closed) return;
            try
            {
                if (!IsWindow(_source) || !IsWindowVisible(_source)) { Close(); return; }
                if (!GetClientRect(_source, out var src)) return;
                if (src.Right == _srcW && src.Bottom == _srcH) return;
                _srcW = src.Right;
                _srcH = src.Bottom;
                foreach (var m in _mirrors) m.Point(_srcW, _srcH);
            }
            catch (Exception ex) { App.Logger?.Debug("Browser mirror watch: {E}", ex.Message); }
        }

        private sealed class Mirror : WF.Form
        {
            private readonly BrowserFullscreenMirrors _owner;
            private readonly IntPtr _ownerHwnd;
            private readonly SD.Rectangle _target;
            public IntPtr Thumb;

            public Mirror(BrowserFullscreenMirrors owner, IntPtr ownerHwnd, SD.Rectangle target, bool topmost)
            {
                _owner = owner;
                _ownerHwnd = ownerHwnd;
                _target = target;
                FormBorderStyle = WF.FormBorderStyle.None;
                StartPosition = WF.FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = topmost;
                Text = "Fullscreen copy";
                BackColor = SD.Color.Black;
                Bounds = target;   // physical px (PerMonitorV2), the same units as Screen.Bounds
                FormClosed += (_, _) => Unregister();
            }

            protected override bool ShowWithoutActivation => true;

            protected override WF.CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    cp.Parent = _ownerHwnd;   // a top-level window's "parent" is its OWNER
                    return cp;
                }
            }

            /// <summary>The copy stays exactly on its monitor: no DPI rescale moves or shrinks it.</summary>
            protected override void OnDpiChanged(WF.DpiChangedEventArgs e)
            {
                e.Cancel = true;
                base.OnDpiChanged(e);
                Bounds = _target;
            }

            protected override void OnMouseDown(WF.MouseEventArgs e)
            {
                base.OnMouseDown(e);
                try { _owner._focusHost(); } catch { }
            }

            protected override void OnDoubleClick(EventArgs e)
            {
                base.OnDoubleClick(e);
                try { _owner._exitFullscreen(); } catch { }
            }

            public void Point(int srcW, int srcH)
            {
                if (Thumb == IntPtr.Zero || IsDisposed) return;
                if (Bounds != _target) Bounds = _target;
                var (l, t, r, b) = BrowserFullscreenMirrorRule.FitRect(srcW, srcH, ClientSize.Width, ClientSize.Height);
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_OPACITY,
                    rcDestination = new RECT { Left = l, Top = t, Right = r, Bottom = b },
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = true,
                };
                DwmUpdateThumbnailProperties(Thumb, ref props);
            }

            private void Unregister()
            {
                var t = Thumb;
                Thumb = IntPtr.Zero;
                if (t != IntPtr.Zero) try { DwmUnregisterThumbnail(t); } catch { }
            }
        }

        // ------------------------------- native -------------------------------

        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int DWM_TNP_RECTDESTINATION = 0x00000001;
        private const int DWM_TNP_OPACITY = 0x00000004;
        private const int DWM_TNP_VISIBLE = 0x00000008;
        private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)] public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)] public bool fSourceClientAreaOnly;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmUpdateThumbnailProperties(IntPtr thumb, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    }
}
