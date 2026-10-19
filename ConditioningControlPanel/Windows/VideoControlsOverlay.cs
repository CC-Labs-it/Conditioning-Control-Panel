using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Video;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The mute and close buttons in the top-right corner of one mandatory video window
    /// (owner, 2026-10-19). Two small black rounds with a light ring and a white glyph.
    ///
    /// A SEPARATE WINDOW for the same reason as <see cref="GracePauseOverlayWindow"/>: the LibVLC
    /// VideoView and the browser engine's WebView2 are child HWNDs, and WPF content laid over them
    /// is never drawn (airspace). It is OWNED by the video window, like the gentle curtain: an owned
    /// window always sits above its owner, follows it through every z-order heal, and dies with it,
    /// so every teardown door (end, Esc, panic, Stop, a floating window closing) takes the buttons
    /// down without a line of its own. Never activated, never focused: the strict window keeps its
    /// keyboard (the Esc pause door), and a floating window keeps the player's current app.
    /// It follows the window when it moves (floating windows fly and get dragged).
    /// </summary>
    internal sealed class VideoControlsOverlay
    {
        private const string SpeakerData = "M0.5,4.5 L3,4.5 L6.5,1 L6.5,11 L3,7.5 L0.5,7.5 Z";
        private const string WavesData = "M8.6,4 Q9.8,6 8.6,8 M10.2,2.4 Q12.6,6 10.2,9.6";
        private const string MutedData = "M8.4,4 L11.6,8 M11.6,4 L8.4,8";
        private const string CloseData = "M2,2 L10,10 M10,2 L2,10";

        private readonly Window _host;
        private readonly Window _win;
        private readonly Border _closeButton;
        private readonly Border _muteButton;
        private readonly Path _muteMark;
        private readonly DispatcherTimer _waitTimer;
        private readonly int _notBeforeTick;
        private IntPtr _hwnd;
        private bool _closed;

        /// <summary>Raised once, whichever way the buttons went away.</summary>
        public event Action<VideoControlsOverlay>? Closed;

        public Window Host => _host;

        /// <param name="host">The video window, already shown.</param>
        /// <param name="delayMs">Wait at least this long before showing (a page fading in from black).</param>
        public VideoControlsOverlay(Window host, bool showClose, bool muted, Action onMute, Action onClose, int delayMs = 0)
        {
            _host = host;
            _notBeforeTick = Environment.TickCount + Math.Max(0, delayMs);

            _muteMark = Glyph(muted ? MutedData : WavesData, fill: false);
            var speaker = new Grid { Width = 12, Height = 12 };
            speaker.Children.Add(Glyph(SpeakerData, fill: true));
            speaker.Children.Add(_muteMark);
            _muteButton = RoundButton(speaker, onMute);
            _closeButton = RoundButton(Glyph(CloseData, fill: false), onClose);
            _closeButton.Margin = new Thickness(MandatoryVideoControls.GapDip, 0, 0, 0);
            _closeButton.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;
            SetMuted(muted);
            _closeButton.ToolTip = Loc.Get("video_btn_close");

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_muteButton);
            row.Children.Add(_closeButton);

            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                Topmost = true,
                Title = "",
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = host.Left + Math.Max(0, host.ActualWidth - 80),
                Top = host.Top + MandatoryVideoControls.MarginDip,
                Content = row,
            };
            _win.SourceInitialized += (_, _) =>
            {
                _hwnd = new WindowInteropHelper(_win).Handle;
                if (_hwnd == IntPtr.Zero) return;
                int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
                SetWindowLong(_hwnd, GWL_EXSTYLE, (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW);
                HwndSource.FromHwnd(_hwnd)?.AddHook(NoActivate);
            };
            _win.SizeChanged += (_, _) => Place();   // a DPI change re-sizes it
            _win.Closed += (_, _) => Finish();

            host.LocationChanged += OnHostMoved;
            host.SizeChanged += OnHostMoved;
            host.IsVisibleChanged += OnHostVisibility;
            host.Closed += OnHostClosed;

            // Shown once the clip can be seen: the host is up, no gentle curtain still covers it,
            // and the page's own fade (delayMs) has run.
            _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _waitTimer.Tick += (_, _) => TryShow();
            _waitTimer.Start();
            TryShow();
        }

        public void SetMuted(bool muted)
        {
            if (_closed) return;
            _muteMark.Data = Geometry.Parse(muted ? MutedData : WavesData);
            _muteButton.ToolTip = Loc.Get(muted ? "video_btn_unmute" : "video_btn_mute");
        }

        public void Close()
        {
            if (_closed) return;
            try { _win.Close(); } catch (Exception ex) { Diag.Swallowed(ex); }
            Finish();
        }

        private bool ReadyToShow()
            => _host.IsVisible && !GentleCurtain.Covers(_host) && Environment.TickCount - _notBeforeTick >= 0;

        private void TryShow()
        {
            if (_closed) { _waitTimer.Stop(); return; }
            if (!ReadyToShow()) return;
            _waitTimer.Stop();
            try
            {
                if (_win.Owner == null) _win.Owner = _host;
                _win.Show();
                Place();
            }
            catch (Exception ex) { App.Logger?.Debug("VideoControlsOverlay: show failed - {Error}", ex.Message); Close(); }
        }

        /// <summary>Top-right corner of the host, in physical pixels (mixed-DPI safe).</summary>
        private void Place()
        {
            if (_closed || _hwnd == IntPtr.Zero) return;
            try
            {
                var host = new WindowInteropHelper(_host).Handle;
                if (host == IntPtr.Zero || !GetWindowRect(host, out var h) || !GetWindowRect(_hwnd, out var me)) return;
                var margin = (int)Math.Round(MandatoryVideoControls.MarginDip * VisualTreeHelper.GetDpi(_host).DpiScaleX);
                SetWindowPos(_hwnd, IntPtr.Zero, h.Right - (me.Right - me.Left) - margin, h.Top + margin, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnHostMoved(object? sender, EventArgs e) => Place();

        private void OnHostVisibility(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_closed) return;
            try
            {
                if (_host.IsVisible) { if (!_win.IsVisible) _waitTimer.Start(); }
                else { _waitTimer.Stop(); _win.Hide(); }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private void OnHostClosed(object? sender, EventArgs e) => Close();

        private void Finish()
        {
            if (_closed) return;
            _closed = true;
            _waitTimer.Stop();
            _host.LocationChanged -= OnHostMoved;
            _host.SizeChanged -= OnHostMoved;
            _host.IsVisibleChanged -= OnHostVisibility;
            _host.Closed -= OnHostClosed;
            try { Closed?.Invoke(this); } catch (Exception ex) { Diag.Swallowed(ex); }
            Closed = null;
        }

        private static Path Glyph(string data, bool fill)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Width = 12,
                Height = 12,
                Stretch = Stretch.None,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            if (fill) p.Fill = Brushes.White; else p.Stroke = Brushes.White;
            return p;
        }

        /// <summary>A black round with a soft light ring: visible on a dark clip and a bright one.</summary>
        private static Border RoundButton(UIElement glyph, Action onClick)
        {
            var size = MandatoryVideoControls.ButtonDip;
            var b = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0, 0, 0)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                Opacity = MandatoryVideoControls.RestOpacity,
                Cursor = Cursors.Hand,
                Focusable = false,
                Child = glyph,
            };
            b.MouseEnter += (_, _) => b.Opacity = 1.0;
            b.MouseLeave += (_, _) => b.Opacity = MandatoryVideoControls.RestOpacity;
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; b.CaptureMouse(); };
            b.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                var inside = b.IsMouseCaptured && b.IsMouseOver;
                b.ReleaseMouseCapture();
                if (!inside) return;
                try { onClick(); }
                catch (Exception ex) { App.Logger?.Debug("VideoControlsOverlay: button handler threw - {Error}", ex.Message); }
            };
            return b;
        }

        private static IntPtr NoActivate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_MOUSEACTIVATE) return IntPtr.Zero;
            handled = true;
            return (IntPtr)MA_NOACTIVATE;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    }
}
