using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Video;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// The small "via Scrolller" mark (owner, 2026-10-17: Scrolller media on screen is credited).
    /// Code-only so any surface can drop one in. 11 px, ~75% opacity, no glow, never takes input.
    /// Every appearance fades in and out (~200 ms; Gentle visuals only ever lengthen the fade).
    /// </summary>
    internal sealed class ScrolllerCreditBadge : Border
    {
        internal const double ShownOpacity = 0.75;
        internal const int FadeMs = 200;
        private bool _shown;

        public ScrolllerCreditBadge()
        {
            IsHitTestVisible = false;
            Focusable = false;
            Opacity = 0;
            Visibility = Visibility.Collapsed;
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(6, 2, 6, 2);
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x14));
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Child = new TextBlock
            {
                Text = ScrolllerCredit.Mark,
                FontSize = 11,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                IsHitTestVisible = false,
            };
            AutomationProperties.SetName(this, ScrolllerCredit.Mark);
        }

        public bool Shown => _shown;

        /// <summary>Fade the mark in or out. Idempotent; call from the UI thread.</summary>
        public void SetShown(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            var ms = GentleVisuals.FadeMs(FadeMs);
            if (shown) Visibility = Visibility.Visible;
            var anim = new DoubleAnimation(shown ? ShownOpacity : 0, TimeSpan.FromMilliseconds(ms));
            if (!shown)
                anim.Completed += (_, _) => { if (!_shown) Visibility = Visibility.Collapsed; };
            BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>Drop the mark at once (panic, close).</summary>
        public void HideNow()
        {
            _shown = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// One tiny click-through window in the primary screen's bottom-right corner that carries the
    /// mark for surfaces made of many windows or layers (flashes in all three render paths, the
    /// Chaos wash and GIF cascade). Each surface HOLDS it while a Scrolller item of its own is up and
    /// releases it when that item goes; the chip fades out when nobody holds it. UI thread only.
    /// </summary>
    internal static class ScrolllerCreditChip
    {
        private static readonly HashSet<object> Holders = new(ReferenceEqualityComparer.Instance);
        private static Window? _window;
        private static ScrolllerCreditBadge? _badge;

        public static int HolderCount => Holders.Count;

        /// <summary>True when the call was re-posted to the UI thread (callers may be on a timer).</summary>
        private static bool OffUi(Action again)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null) return true;
            if (d.CheckAccess()) return false;
            d.BeginInvoke(again);
            return true;
        }

        public static void Hold(object owner)
        {
            if (OffUi(() => Hold(owner))) return;
            try
            {
                if (!Holders.Add(owner) || Holders.Count != 1) return;
                EnsureWindow();
                Place();
                if (_window != null && !_window.IsVisible) _window.Show();
                _badge?.SetShown(true);
            }
            catch (Exception ex) { App.Logger?.Debug("ScrolllerCreditChip: hold failed: {E}", ex.Message); }
        }

        public static void Release(object? owner)
        {
            if (owner == null || OffUi(() => Release(owner))) return;
            if (!Holders.Remove(owner) || Holders.Count > 0) return;
            _badge?.SetShown(false);
        }

        /// <summary>Every holder of one kind goes at once (a surface's teardown / panic).</summary>
        public static void ReleaseWhere(Func<object, bool> match)
        {
            if (OffUi(() => ReleaseWhere(match))) return;
            if (Holders.RemoveWhere(o => match(o)) > 0 && Holders.Count == 0) _badge?.SetShown(false);
        }

        /// <summary>App shutdown: an unowned visible window would block OnLastWindowClose.</summary>
        public static void Dispose()
        {
            Holders.Clear();
            try { _window?.Close(); } catch { }
            _window = null;
            _badge = null;
        }

        private static void EnsureWindow()
        {
            if (_window != null) return;
            _badge = new ScrolllerCreditBadge { Margin = new Thickness(0) };
            _window = new Window
            {
                Title = "CCP - via Scrolller",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Focusable = false,
                IsHitTestVisible = false,
                Topmost = true,
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = _badge,
            };
            _window.SourceInitialized += (_, _) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(_window).Handle;
                    var ex = GetWindowLong(hwnd, GwlExStyle);
                    SetWindowLong(hwnd, GwlExStyle, ex | WsExTransparent | WsExNoActivate | WsExToolWindow | WsExLayered);
                }
                catch { }
            };
            _window.SizeChanged += (_, _) => Place();
        }

        private static void Place()
        {
            if (_window == null) return;
            var area = SystemParameters.WorkArea;
            var w = double.IsNaN(_window.ActualWidth) || _window.ActualWidth <= 0 ? 90 : _window.ActualWidth;
            var h = double.IsNaN(_window.ActualHeight) || _window.ActualHeight <= 0 ? 20 : _window.ActualHeight;
            _window.Left = area.Right - w - 12;
            _window.Top = area.Bottom - h - 10;
        }

        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExLayered = 0x80000;
        private const int WsExNoActivate = 0x08000000;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
