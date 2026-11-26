using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Tutorial;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    // Monitor-wide coach mark. WPF's overlay is one window sized to MainWindow's monitor
    // (TutorialOverlay.xaml:8; TutorialOverlay.xaml.cs:360-424), owned by MainWindow (:52-59), so
    // it dims and blocks everything outside CCP on that monitor. The live card stays in the host's
    // OverlayLayer; this satellite draws the rest of the monitor, minus the host's client area, from
    // a non-topmost, non-activating window owned by the host, so it stays above the host's frame
    // (title bar, borders) and blocks it as WPF's monitor-wide window did. Native Wayland and
    // headless have no XID/HWND, so the satellite is never shown there and the tour stays host-only.
    public partial class TutorialOverlay
    {
        private Window? _monitorDim;
        private Path? _monitorDimPath;
        private Window? _monitorDimAnchor;
        private Window? _monitorDimHost;
        private bool _monitorDimFadedIn;

        /// <summary>Test seam: headless windows have no XID/HWND, so tests force the satellite on.</summary>
        internal static bool ForceMonitorDimForTests;

        /// <summary>Last region applied to the satellite, satellite-relative physical px (tests read it).</summary>
        internal PixelRect[]? MonitorDimRegion { get; private set; }
        internal Window? MonitorDimWindow => _monitorDim;

        /// <summary>The host's client area in physical px from its own scaling, so a host on a
        /// different-DPI monitor than the satellite still leaves its real rect clear.</summary>
        internal static PixelRect HostPixelRect(PixelPoint position, Size client, double hostScaling) =>
            new(position, PixelSize.FromSize(client, hostScaling > 0 ? hostScaling : 1));

        /// <summary>Satellite-relative input/draw rects: the monitor minus the host (Core rules).</summary>
        internal static PixelRect[] MonitorDimRects(PixelRect monitor, PixelRect host) =>
            SpotlightInputRegion.Create(monitor.Width, monitor.Height, 1, 1,
                    host.X - monitor.X, host.Y - monitor.Y, host.Width, host.Height)
                .Select(r => new PixelRect(r.X, r.Y, r.Width, r.Height)).ToArray();

        private void UpdateMonitorDim()
        {
            // The monitor comes from the shell while it is shown; a hidden shell (tray, launcher) falls back to the host.
            var anchor = _tutorialMainWindow is { IsVisible: true } shell ? shell : _targetWindow;
            var host = _targetWindow;
            if (anchor == null || host == null) { RemoveMonitorDim(); return; }
            // WPF GetMainWindowMonitorBoundsDip: the monitor MainWindow is on.
            var center = new PixelPoint(anchor.Position.X + (int)(anchor.Bounds.Width * anchor.DesktopScaling / 2),
                anchor.Position.Y + (int)(anchor.Bounds.Height * anchor.DesktopScaling / 2));
            var screens = ScreenList.Enumerate(anchor);
            var screen = screens.FirstOrDefault(s => s.Bounds.Contains(center)) ?? screens.FirstOrDefault(s => s.IsPrimary) ?? screens.FirstOrDefault();
            if (screen == null) { RemoveMonitorDim(); return; }
            var monitor = screen.Bounds;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            // Client area only: the frame stays under the dim (WPF covers it too, :360-396).
            var hostRect = HostPixelRect(host.PointToScreen(default), host.ClientSize, host.DesktopScaling);
            var rects = MonitorDimRects(monitor, hostRect);

            if (_monitorDim == null)
            {
                var w = new Window
                {
                    WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, ShowActivated = false,
                    Topmost = false, CanResize = false, Background = Brushes.Transparent,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    WindowStartupLocation = WindowStartupLocation.Manual, Title = "Tutorial",
                };
                // Native Wayland and headless: host-only. X11 without a compositor would paint the
                // host's hole opaque, so it stays host-only too.
                var backend = X11Overlay.BackendOf(w.TryGetPlatformHandle());
                if (!ForceMonitorDimForTests && (backend == OverlayBackend.None
                    || backend == OverlayBackend.X11 && !X11Overlay.IsCompositing))
                {
                    w.Close();
                    return;
                }
                _monitorDimPath = new Path { Fill = new SolidColorBrush(Color.FromArgb(DimAlpha(), 0, 0, 0)) };
                if (!_monitorDimFadedIn)
                {
                    // WPF fades the whole overlay in over 300 ms on Loaded (:99-109), once per tour.
                    _monitorDimFadedIn = true;
                    _monitorDimPath.Opacity = 0;
                    _monitorDimPath.Transitions = OpacityFade(FadeInDuration);
                }
                w.Content = _monitorDimPath;
                w.KeyDown += OnMonitorDimKeyDown;
                X11Overlay.SetPassive(w);   // Windows: WS_EX_NOACTIVATE, never HWND_TOPMOST
                _monitorDim = w;
                _monitorDimAnchor = anchor;
                _monitorDimHost = host;
                anchor.PositionChanged += OnMonitorDimMoved;
                if (!ReferenceEquals(host, anchor)) host.PositionChanged += OnMonitorDimMoved;
            }
            var win = _monitorDim;
            win.Position = monitor.Position;
            win.Width = monitor.Width / scale;
            win.Height = monitor.Height / scale;
            var geometry = new GeometryGroup();
            foreach (var r in rects)
                geometry.Children.Add(new RectangleGeometry(new Rect(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale)));
            _monitorDimPath!.Data = geometry;
            MonitorDimRegion = rects;
            X11Overlay.SetWindowInputRegion(win, rects);
            if (win.IsVisible) return;
            win.Show(host);   // owned: X11 WM_TRANSIENT_FOR, Win32 owner
            if (_monitorDimPath.Opacity == 0 && !_fadingOut) _monitorDimPath.Opacity = 1;   // runs the fade-in
        }

        private void OnMonitorDimMoved(object? sender, PixelPointEventArgs e)
        {
            if (_monitorDim != null && !_tornDown) UpdateMonitorDim();
        }

        private void OnMonitorDimKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || _closed) return;
            SkipTutorial();
            e.Handled = true;
        }

        /// <summary>Normal end: fade with the card over 200 ms (WPF :1305); Teardown closes it.</summary>
        private void FadeOutMonitorDim()
        {
            if (_monitorDimPath == null) return;
            _monitorDimPath.Transitions = OpacityFade(FadeOutDuration);
            _monitorDimPath.Opacity = 0;
        }

        private void RemoveMonitorDim()
        {
            var w = _monitorDim;
            if (w == null) return;
            _monitorDim = null;
            _monitorDimPath = null;
            MonitorDimRegion = null;
            if (_monitorDimAnchor != null) _monitorDimAnchor.PositionChanged -= OnMonitorDimMoved;
            if (_monitorDimHost != null) _monitorDimHost.PositionChanged -= OnMonitorDimMoved;
            _monitorDimAnchor = null;
            _monitorDimHost = null;
            w.KeyDown -= OnMonitorDimKeyDown;
            try { w.Close(); } catch (Exception ex) { Serilog.Log.Debug("[Tutorial] monitor dim close: {E}", ex.Message); }
        }
    }
}
