using System;
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

        private void RestoreWindowBounds()
        {
            var s = CoreSettings.Current;
            if (s.DeeperPlayerWindowWidth >= MinWidth && s.DeeperPlayerWindowHeight >= MinHeight &&
                double.IsFinite(s.DeeperPlayerWindowWidth) && double.IsFinite(s.DeeperPlayerWindowHeight))
            {
                Width = s.DeeperPlayerWindowWidth;
                Height = s.DeeperPlayerWindowHeight;
            }
            var x = s.DeeperPlayerWindowLeft;
            var y = s.DeeperPlayerWindowTop;
            if (s.DeeperPlayerWindowWidth > 0 && double.IsFinite(x) && double.IsFinite(y) &&
                Screens.All.Any(screen => {
                    var b = screen.Bounds;
                    var scale = screen.Scaling;
                    var left = x;
                    var top = y;
                    return Math.Min(left + Width * scale, b.Right) - Math.Max(left, b.X) >= 200 * scale &&
                           Math.Min(top + Height * scale, b.Bottom) - Math.Max(top, b.Y) >= 120 * scale;
                }))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = new PixelPoint((int)x, (int)y);
            }
        }

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
            s.DeeperPlayerWindowLeft = Position.X;
            s.DeeperPlayerWindowTop = Position.Y;
            s.DeeperPlayerWindowWidth = Width;
            s.DeeperPlayerWindowHeight = Height;
            CoreSettings.Save();
        }
    }
}
