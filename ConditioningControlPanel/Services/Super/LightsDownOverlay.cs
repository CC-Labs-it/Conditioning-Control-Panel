using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// Super Lights Down's window: one click-through, non-activating, layered window over one screen.
    /// It draws ONE small picture (<see cref="LightsDownCanvas"/>, about 320 cells across) and lets
    /// WPF stretch it over the screen with a bilinear filter: no gradient brush, no Effect, no
    /// per-frame geometry, and the stretch itself is what makes every edge soft. A frame is written
    /// only when <see cref="LightsDownCanvas.Worth"/> says it changed, so a dim at rest never repaints.
    ///
    /// Full screen it is OWNED by the video window, so it sits just above that video and closes with
    /// it (the dim lies over the screen edges of the picture). Floating it has NO owner (an owned
    /// window always sits above its owner, which would put the dim over the video, and an owner chain
    /// can lift itself over the user's app); the service keeps it just BELOW the floating windows
    /// with <see cref="KeepBelow"/>, so the videos are never dimmed even when a hole lags a frame.
    /// </summary>
    internal sealed class LightsDownOverlay : Window
    {
        private readonly IntPtr _ownerHwnd;
        private readonly System.Drawing.Rectangle _bounds;
        private readonly double _dpi;
        private readonly LightsDownCanvas _canvas;
        private readonly WriteableBitmap _bitmap;
        private readonly Canvas _root = new();
        private LightsDownLook _drawn = LightsDownLook.None;
        private bool _blank = true;

        public IntPtr OwnerHwnd => _ownerHwnd;
        public System.Drawing.Rectangle BoundsPx => _bounds;
        public double Dpi => _dpi;
        public bool Floating => _ownerHwnd == IntPtr.Zero;

        /// <summary>The short side of the screen in physical pixels (the look scales with it).</summary>
        public double ShortSidePx => Math.Min(_bounds.Width, _bounds.Height);

        /// <summary>The holes this screen tracks (floating windows or attention targets on it).</summary>
        public LightsDownHoles Holes { get; } = new();

        /// <summary>An unowned overlay for a screen at the origin, for the offscreen render test.</summary>
        internal static LightsDownOverlay ForTest(int widthPx, int heightPx, double dpi = 1)
            => new(IntPtr.Zero, new System.Drawing.Rectangle(0, 0, widthPx, heightPx), dpi);

        /// <summary>The drawing root, for the offscreen render test.</summary>
        internal FrameworkElement RootForTest => _root;

        /// <summary>The small picture, for tests.</summary>
        internal LightsDownCanvas CanvasForTest => _canvas;

        public LightsDownOverlay(IntPtr ownerHwnd, System.Drawing.Rectangle screenBoundsPx, double dpi)
        {
            _ownerHwnd = ownerHwnd;
            _bounds = screenBoundsPx;
            _dpi = dpi is > 0.25 and < 8 ? dpi : 1;
            double w = screenBoundsPx.Width / _dpi, h = screenBoundsPx.Height / _dpi;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            // Same placement formula as the video window it rides (VideoService.CreateLibVLCVideoWindow).
            Left = screenBoundsPx.X / _dpi;
            Top = screenBoundsPx.Y / _dpi;
            Width = w;
            Height = h;
            Title = "CCP Lights Down";

            _canvas = new LightsDownCanvas(screenBoundsPx.Width, screenBoundsPx.Height, _dpi);
            _bitmap = new WriteableBitmap(_canvas.Width, _canvas.Height, 96, 96, PixelFormats.Pbgra32, null);
            // Cell centres land on the screen pixels they were sampled at: the picture is a whole
            // number of cells, a hair larger than the screen, and the window clips the rest.
            var image = new Image
            {
                Source = _bitmap,
                Stretch = Stretch.Fill,
                Width = _canvas.Width * _canvas.Cell / _dpi,
                Height = _canvas.Height * _canvas.Cell / _dpi,
                IsHitTestVisible = false,
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.Linear);
            _root.Children.Add(image);
            _root.Width = w;
            _root.Height = h;
            _root.ClipToBounds = true;
            _root.IsHitTestVisible = false;
            // A fixed size in this screen's DIPs, pinned top-left: Fit() rescales it when the window's
            // own scale is not this screen's (a mixed-DPI desk), so a cell always lands on its pixels.
            _root.HorizontalAlignment = HorizontalAlignment.Left;
            _root.VerticalAlignment = VerticalAlignment.Top;
            Content = _root;

            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_LAYERED);
                Fit();
            };
            Loaded += (_, _) => Fit();
            if (ownerHwnd != IntPtr.Zero) new WindowInteropHelper(this).Owner = ownerHwnd;
        }

        /// <summary>
        /// Put the window on its screen in PHYSICAL pixels and match the picture to the window's real
        /// scale. Left/Top/Width/Height are DIPs at the scale WPF picked for the window, which on a
        /// desk with two different scales is not this screen's: the dim then sat offset and too small,
        /// with every hole beside its video (owner, 2026-11-18, 125% + 100%).
        /// </summary>
        private void Fit()
        {
            if (_fitting) return;
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            _fitting = true;
            try
            {
                // Twice: a move onto another screen changes the window's scale, and WPF answers
                // that change with a resize of its own.
                for (int i = 0; i < 2; i++)
                    SetWindowPos(hwnd, IntPtr.Zero, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height,
                        SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
                double actual = VisualTreeHelper.GetDpi(this).DpiScaleX;
                double k = actual > 0.25 ? _dpi / actual : 1;
                _root.LayoutTransform = Math.Abs(k - 1) < 0.001 ? Transform.Identity : new ScaleTransform(k, k);
            }
            finally { _fitting = false; }
        }

        private bool _fitting;

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Fit), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>A physical-pixel rectangle in this overlay's own pixels.</summary>
        public LightsDownRect ToLocal(System.Drawing.Rectangle px)
            => new(px.X - _bounds.X, px.Y - _bounds.Y, px.Width, px.Height);

        /// <summary>A desktop DIP rectangle (this screen's scale, the attention bounds) in this overlay's pixels.</summary>
        public LightsDownRect DipToLocal(Rect dip)
            => new(dip.X * _dpi - _bounds.X, dip.Y * _dpi - _bounds.Y, dip.Width * _dpi, dip.Height * _dpi);

        /// <summary>Is a physical-pixel point on this overlay's screen.</summary>
        public bool ContainsPx(double x, double y)
            => x >= _bounds.Left && x < _bounds.Right && y >= _bounds.Top && y < _bounds.Bottom;

        /// <summary>Does a physical-pixel rectangle come close enough to this screen for its hole or halo to show here.</summary>
        public bool Touches(System.Drawing.Rectangle px, double marginPx)
            => px.Right + marginPx > _bounds.Left && px.Left - marginPx < _bounds.Right
            && px.Bottom + marginPx > _bounds.Top && px.Top - marginPx < _bounds.Bottom;

        /// <summary>Draw a frame if it differs visibly from the last one. Returns true when it wrote.</summary>
        public bool Render(in LightsDownLook look, IReadOnlyList<LightsDownHole> holes, bool holesChanged)
        {
            if (!look.Visible)
            {
                if (_blank) return false;
                Array.Clear(_canvas.Pixels);
                Write();
                _blank = true;
                _drawn = look;
                return true;
            }
            if (!_blank && !LightsDownCanvas.Worth(_drawn, look, holesChanged)) return false;
            _canvas.Draw(look, holes);
            Write();
            _blank = false;
            _drawn = look;
            return true;
        }

        private void Write()
            => _bitmap.WritePixels(new Int32Rect(0, 0, _canvas.Width, _canvas.Height), _canvas.Pixels, _canvas.Width * 4, 0);

        /// <summary>
        /// Floating only: keep this window just under the lowest of <paramref name="windows"/> in the
        /// z-order (all of them topmost), so every video sits over the dim. Walks the z-order from
        /// the top and moves only when something sits wrong; called a couple of times a second.
        /// </summary>
        public void KeepBelow(IReadOnlyList<IntPtr> windows)
        {
            if (!Floating || windows.Count == 0) return;
            var me = new WindowInteropHelper(this).Handle;
            if (me == IntPtr.Zero) return;
            var want = new HashSet<IntPtr>();
            foreach (var w in windows) if (w != IntPtr.Zero && IsWindow(w)) want.Add(w);
            if (want.Count == 0) return;
            int meAt = -1, lowestAt = -1, seen = 0;
            IntPtr lowest = IntPtr.Zero;
            int n = 0;
            for (var h = GetTopWindow(IntPtr.Zero); h != IntPtr.Zero && n < 4096; h = GetWindow(h, GW_HWNDNEXT), n++)
            {
                if (h == me) meAt = n;
                else if (want.Contains(h)) { lowest = h; lowestAt = n; seen++; }
                if (meAt >= 0 && seen == want.Count) break;
            }
            if (lowest == IntPtr.Zero || (meAt > lowestAt && seen == want.Count)) return;
            SetWindowPos(me, lowest, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint GW_HWNDNEXT = 2;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    }
}
