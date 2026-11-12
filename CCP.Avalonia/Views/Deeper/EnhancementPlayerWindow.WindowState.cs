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
            IEnumerable<(PixelRect Bounds, double Scale)> screens)
        {
            var candidates = screens.Select(s => (s.Bounds, Scale: s.Scale > 0 ? s.Scale : 1))
                .Select(s => (s.Bounds, s.Scale, Position: BoundsPosition(left, top, s.Scale)))
                .Where(s => BoundsVisible(s.Position, width, height, s.Scale, s.Bounds)).ToArray();
            foreach (var s in candidates)
                if (s.Bounds.Contains(s.Position)) return s.Position;
            return candidates.Length > 0 ? candidates[0].Position : null;
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
            Opened += (_, _) =>
            {
                if (settings.DeeperPlayerWindowWidth <= 0 || !double.IsFinite(settings.DeeperPlayerWindowLeft)
                    || !double.IsFinite(settings.DeeperPlayerWindowTop)) return;
                // Prefer the primary when mixed-DPI interpretations overlap.
                var position = SelectBoundsPosition(settings.DeeperPlayerWindowLeft, settings.DeeperPlayerWindowTop,
                    Width, Height, Screens.All.OrderByDescending(s => s.IsPrimary).Select(s => (s.Bounds, s.Scaling)));
                if (position is { } point)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Position = point;
                }
            };
            Closing += (_, _) =>
            {
                if (WindowState != WindowState.Normal || !double.IsFinite(Width) || !double.IsFinite(Height)
                    || Width < MinWidth || Height < MinHeight) return;
                var scale = Screens.ScreenFromWindow(this)?.Scaling ?? Screens.Primary?.Scaling ?? 1;
                if (scale <= 0) scale = 1;
                settings.DeeperPlayerWindowLeft = Position.X / scale;
                settings.DeeperPlayerWindowTop = Position.Y / scale;
                settings.DeeperPlayerWindowWidth = Width;
                settings.DeeperPlayerWindowHeight = Height;
                CoreSettings.Save();
            };
        }
    }
}
