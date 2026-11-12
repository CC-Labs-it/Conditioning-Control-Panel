using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    public partial class EnhancementPlayerWindow
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        internal bool? TitleBarWindowsOverride { get; set; }
        internal Func<EnhancementPlayerWindow, IntPtr>? TitleBarHandleOverride { get; set; }
        internal Action<IntPtr, int, int>? TitleBarAttributeOverride { get; set; }

        // Headless tests can simulate a display scale for the window lifecycle.
        internal static double? WindowStateScaleOverride { get; set; }

        private void RestoreWindowBounds()
        {
            var s = CoreSettings.Current;
            if (s.DeeperPlayerWindowWidth >= MinWidth && s.DeeperPlayerWindowHeight >= MinHeight &&
                double.IsFinite(s.DeeperPlayerWindowWidth) && double.IsFinite(s.DeeperPlayerWindowHeight))
            {
                Width = s.DeeperPlayerWindowWidth;
                Height = s.DeeperPlayerWindowHeight;
            }
            if (s.DeeperPlayerWindowWidth > 0 && TryRestorePosition(
                s.DeeperPlayerWindowLeft, s.DeeperPlayerWindowTop, Width, Height,
                Screens.All.Select(screen => (screen.Bounds, WindowStateScaleOverride ?? screen.Scaling)), out var position))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = position;
            }
        }

        // WPF's Left and Top are DIPs; Avalonia's Position and screen bounds are pixels.
        // Try each monitor's scale and keep the candidate with the largest visible area.
        internal static bool TryRestorePosition(double x, double y, double width, double height,
            IEnumerable<(PixelRect Bounds, double Scale)> screens, out PixelPoint position)
        {
            position = default;
            double bestArea = 0;
            if (!double.IsFinite(x) || !double.IsFinite(y)) return false;
            foreach (var (bounds, scale) in screens)
            {
                if (!double.IsFinite(scale) || scale <= 0) continue;
                double left = x * scale, top = y * scale;
                if (left < int.MinValue || left > int.MaxValue || top < int.MinValue || top > int.MaxValue) continue;
                double visibleWidth = (Math.Min(left + width * scale, bounds.Right) - Math.Max(left, bounds.X)) / scale;
                double visibleHeight = (Math.Min(top + height * scale, bounds.Bottom) - Math.Max(top, bounds.Y)) / scale;
                if (visibleWidth < 200 || visibleHeight < 120) continue;
                var area = visibleWidth * visibleHeight;
                if (area <= bestArea) continue;
                bestArea = area;
                position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
            }
            return bestArea > 0;
        }

        internal static Point ToSavedPosition(PixelPoint position, double scale) =>
            new Point(position.X / scale, position.Y / scale);

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            DeeperEditorWindow.ApplyDarkTitleBar(TitleBarWindowsOverride ?? OperatingSystem.IsWindows(),
                TitleBarHandleOverride?.Invoke(this) ?? TryGetPlatformHandle()?.Handle ?? IntPtr.Zero,
                TitleBarAttributeOverride ?? ((h, a, v) => DwmSetWindowAttribute(h, a, ref v, sizeof(int))),
                CoreMods.AccentColorHexProvider is null ? null : CoreMods.AccentColorHex);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            base.OnClosing(e);
            if (e.Cancel || WindowState != WindowState.Normal || Width < MinWidth || Height < MinHeight ||
                !double.IsFinite(Width) || !double.IsFinite(Height)) return;
            var s = CoreSettings.Current;
            var scale = WindowStateScaleOverride ?? Screens.ScreenFromWindow(this)?.Scaling ?? DesktopScaling;
            var saved = ToSavedPosition(Position, scale);
            s.DeeperPlayerWindowLeft = saved.X;
            s.DeeperPlayerWindowTop = saved.Y;
            s.DeeperPlayerWindowWidth = Width;
            s.DeeperPlayerWindowHeight = Height;
            CoreSettings.Save();
        }
    }
}
