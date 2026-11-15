using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF KeywordHighlightService.cs:34-57, 194-269: per-monitor boxes and lifetime.
internal static class KeywordHighlightOverlay
{
    internal sealed record Target(PixelRect Bounds, double Scaling);
    internal static Func<IReadOnlyList<Target>> Screens = () => MainShellWindow.Current is { } host
        ? ScreenList.Enumerate(host).Select(s => new Target(s.Bounds, s.Scaling)).ToArray() : [];
    internal static Func<Window, PixelRect, bool> Configure = (w, b) => X11Overlay.IsCompositing &&
        X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, b);
    private static readonly Dictionary<PixelRect, (Window Window, Canvas Canvas, IDisposable? Capture)> Windows = new();
    internal static int WindowCount => Windows.Count;
    internal static IReadOnlyList<Canvas> Canvases => Windows.Values.Select(x => x.Canvas).ToArray();
    internal static bool IsHighlightWindow(Window window) => Windows.Values.Any(x => x.Window == window);
    internal static Rect LocalRect(System.Drawing.Rectangle word, Target screen)
    {
        var k = screen.Scaling > 0 ? screen.Scaling : 1;
        return new Rect((word.X - screen.Bounds.X) / k - 10, (word.Y - screen.Bounds.Y) / k - 10,
            word.Width / k + 20, word.Height / k + 20);
    }
    internal static void Show(IReadOnlyList<ScreenOcrService.Word>? words)
    {
        if (words == null || words.Count == 0 || !CoreSettings.Current.KeywordHighlightEnabled) return;
        foreach (var screen in Screens())
        {
            var hits = words.Where(w => screen.Bounds.Intersects(new PixelRect(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height))).ToArray();
            if (hits.Length == 0) continue;
            if (!Windows.TryGetValue(screen.Bounds, out var entry))
            {
                var canvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
                var window = new Window { WindowDecorations = WindowDecorations.None, TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                    Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                    CanResize = false, Focusable = false, IsHitTestVisible = false, Content = canvas };
                try
                {
                    if (!Configure(window, screen.Bounds)) { window.Close(); continue; }
                    window.Show();
                    var capture = CoreSettings.Current.OcrHighlightVisibleInCapture ? null : Win32Overlay.CaptureLayer(window);
                    entry = (window, canvas, capture); Windows.Add(screen.Bounds, entry);
                }
                catch { window.Close(); continue; }
            }
            X11Overlay.Raise(entry.Window);
            foreach (var word in hits) Add(entry.Canvas, LocalRect(word.Bounds, screen));
        }
    }
    private static void Add(Canvas canvas, Rect bounds)
    {
        if (!Color.TryParse(CoreSettings.Current.KeywordHighlightColor, out var color)) color = Color.FromRgb(255, 105, 180);
        var box = new global::Avalonia.Controls.Shapes.Rectangle { Width = bounds.Width, Height = bounds.Height,
            RadiusX = 6, RadiusY = 6, StrokeThickness = 4, Stroke = new SolidColorBrush(color),
            Fill = new SolidColorBrush(Color.FromArgb(128, color.R, color.G, color.B)) };
        Canvas.SetLeft(box, bounds.X); Canvas.SetTop(box, bounds.Y); canvas.Children.Add(box);
        _ = Fade();
        async Task Fade()
        {
            var duration = Math.Max(1, CoreSettings.Current.KeywordHighlightDurationMs);
            var hold = (int)(duration * .6);
            await Task.Delay(hold);
            var start = DateTime.UtcNow;
            while (canvas.Children.Contains(box))
            {
                var remaining = 1 - (DateTime.UtcNow - start).TotalMilliseconds / Math.Max(1, duration - hold);
                if (remaining <= 0) break;
                box.Opacity = remaining;
                await Task.Delay(16);
            }
            canvas.Children.Remove(box);
        }
    }
    internal static void CloseAll()
    {
        foreach (var entry in Windows.Values) { entry.Canvas.Children.Clear(); entry.Capture?.Dispose(); entry.Window.Close(); }
        Windows.Clear();
    }
}
