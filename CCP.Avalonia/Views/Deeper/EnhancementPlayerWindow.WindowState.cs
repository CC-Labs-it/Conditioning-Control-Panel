using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    public partial class EnhancementPlayerWindow
    {
        // WPF stores coordinates in DIPs. Avalonia Position uses pixels.
        internal static PixelPoint BoundsPosition(double left, double top, double systemScale) =>
            new((int)Math.Round(left * systemScale), (int)Math.Round(top * systemScale));

        internal static bool BoundsVisible(PixelPoint position, double width, double height,
            double scale, PixelRect screen)
        {
            double x = Math.Max(position.X, screen.X), y = Math.Max(position.Y, screen.Y);
            double right = Math.Min(position.X + width * scale, screen.Right);
            double bottom = Math.Min(position.Y + height * scale, screen.Bottom);
            return right - x >= 200 * scale && bottom - y >= 120 * scale;
        }

        internal static PixelPoint? SelectBoundsPosition(double left, double top, double width, double height,
            IEnumerable<(PixelRect Bounds, double Scale)> screens, double[]? hint = null)
        {
            var displays = screens.ToArray();
            // A hint is valid only while the WPF-compatible coordinates are unchanged.
            if (hint is { Length: 3 } && hint[0] == left && hint[1] == top
                && double.IsFinite(hint[2]) && hint[2] > 0)
            {
                var position = BoundsPosition(left, top, hint[2]);
                if (displays.Any(s => s.Scale == hint[2] && BoundsVisible(position, width, height, s.Scale, s.Bounds)))
                    return position;
            }
            var candidates = displays.Select(s => (s.Bounds, Scale: s.Scale > 0 ? s.Scale : 1))
                .Select(s => (s.Bounds, s.Scale, Position: BoundsPosition(left, top, s.Scale)))
                .Where(s => BoundsVisible(s.Position, width, height, s.Scale, s.Bounds)).ToArray();
            foreach (var s in candidates)
                if (s.Bounds.Contains(s.Position)) return s.Position;
            return candidates.Length > 0 ? candidates[0].Position : null;
        }

        internal static void SaveWindowPosition(AppSettings settings, PixelPoint position, double scale)
        {
            if (!double.IsFinite(scale) || scale <= 0) scale = 1;
            settings.DeeperPlayerWindowLeft = position.X / scale;
            settings.DeeperPlayerWindowTop = position.Y / scale;
            settings.DeeperPlayerWindowPositionHint = new[] { settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop, scale };
        }

        private void InitializeWindowBounds()
        {
            var settings = CoreSettings.Current;
            if (double.IsFinite(settings.DeeperPlayerWindowWidth) && double.IsFinite(settings.DeeperPlayerWindowHeight)
                && settings.DeeperPlayerWindowWidth >= MinWidth && settings.DeeperPlayerWindowHeight >= MinHeight)
            {
                Width = settings.DeeperPlayerWindowWidth;
                Height = settings.DeeperPlayerWindowHeight;
            }
            var boundsWindowOpen = false;
            Closed += (_, _) => boundsWindowOpen = false;
            Opened += (_, _) =>
            {
                boundsWindowOpen = true;
                if (settings.DeeperPlayerWindowWidth <= 0 || !double.IsFinite(settings.DeeperPlayerWindowLeft)
                    || !double.IsFinite(settings.DeeperPlayerWindowTop)) return;
                // Prefer the primary when mixed-DPI interpretations overlap.
                var position = SelectBoundsPosition(settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop,
                    Width, Height, Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => (s.Bounds, s.Scaling)),
                    settings.DeeperPlayerWindowPositionHint);
                if (position is { } point)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Position = point;
                }
            };
            Closing += (_, _) =>
            {
                if (!boundsWindowOpen || WindowState != WindowState.Normal || !double.IsFinite(Width) || !double.IsFinite(Height)
                    || Width < MinWidth || Height < MinHeight) return;
                double scale;
                try { scale = Screens.ScreenFromWindow(this)?.Scaling ?? Screens.Primary?.Scaling ?? 1; }
                catch (ObjectDisposedException) { return; }
                if (scale <= 0) scale = 1;
                SaveWindowPosition(settings, Position, scale);
                settings.DeeperPlayerWindowWidth = Width;
                settings.DeeperPlayerWindowHeight = Height;
                CoreSettings.Save();
            };
        }
    }
}
