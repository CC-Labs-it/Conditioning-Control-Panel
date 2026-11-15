using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
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
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF KeywordHighlightService.cs:34-57, 194-269: per-monitor boxes and lifetime.
internal static class KeywordHighlightOverlay
{
    internal sealed record Target(PixelRect Bounds, double Scaling, string? Id = null);
    internal static Func<IReadOnlyList<Target>> Screens = () => MainShellWindow.Current is { } host
        ? ScreenList.Enumerate(host).Select(s => new Target(s.Bounds, s.Scaling, ScreenId(s))).ToArray() : [];
    // WinForms OCR uses MONITORINFOEX.szDevice; Avalonia's DisplayName may be a friendly name.
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, IntPtr info);
    private static string? ScreenId(Screen screen)
    {
        if (!OperatingSystem.IsWindows() || screen.TryGetPlatformHandle() is not { } handle) return screen.DisplayName;
        var info = Marshal.AllocHGlobal(104); // MONITORINFOEXW: 40-byte header, 32 UTF-16 chars.
        try { Marshal.WriteInt32(info, 104); return GetMonitorInfoW(handle.Handle, info) ? Marshal.PtrToStringUni(info + 40) : screen.DisplayName; }
        finally { Marshal.FreeHGlobal(info); }
    }
    internal static Func<Window, PixelRect, bool> Configure = (w, b) => X11Overlay.IsCompositing &&
        X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOverrideRedirect(w, b);
    private sealed class Entry(Window window, Canvas canvas)
    {
        internal Window Window { get; } = window;
        internal Canvas Canvas { get; } = canvas;
        internal IDisposable? Capture;
    }
    private static readonly Dictionary<PixelRect, Entry> Windows = new();
    internal static Func<Window, IDisposable?> ExcludeFromCapture = Win32Overlay.CaptureLayer;
    private static AppSettings? _settings;
    private static SettingsService? _settingsService;
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
        var screens = Screens();
        foreach (var screen in screens)
        {
            var hits = words.Where(w => !string.IsNullOrEmpty(w.Screen)
                ? string.Equals(w.Screen, screen.Id, StringComparison.OrdinalIgnoreCase)
                : ReferenceEquals(screen, screens.FirstOrDefault(s => s.Bounds.Intersects(new PixelRect(w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height))))).ToArray();
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
                    entry = new Entry(window, canvas); Windows.Add(screen.Bounds, entry);
                    WatchSettings();
                    RefreshCaptureVisibility();
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
    // WPF MainWindow.Awareness.cs:749-755 and KeywordHighlightService.cs:290-308.
    // Follow all writers, including a cloud restore which replaces the settings object.
    private static void WatchSettings()
    {
        if (_settings != null) return;
        _settingsService = CoreSettings.Service;
        if (_settingsService != null) _settingsService.CurrentReplaced += SettingsReplaced;
        BindSettings();
    }
    private static void BindSettings()
    {
        if (_settings != null) _settings.PropertyChanged -= SettingsChanged;
        _settings = CoreSettings.Current;
        _settings.PropertyChanged += SettingsChanged;
    }
    private static void SettingsReplaced() => OnUi(() => { if (_settings == null) return; BindSettings(); RefreshCaptureVisibility(); });
    private static void SettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(AppSettings.OcrHighlightVisibleInCapture))
            OnUi(RefreshCaptureVisibility);
    }
    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
    internal static void RefreshCaptureVisibility()
    {
        var visible = CoreSettings.Current.OcrHighlightVisibleInCapture;
        foreach (var entry in Windows.Values)
        {
            try
            {
                if (visible) { entry.Capture?.Dispose(); entry.Capture = null; }
                else if (entry.Capture == null) entry.Capture = ExcludeFromCapture(entry.Window);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Keyword highlight capture affinity failed"); }
        }
    }
    internal static void CloseAll()
    {
        if (_settings != null) _settings.PropertyChanged -= SettingsChanged;
        if (_settingsService != null) _settingsService.CurrentReplaced -= SettingsReplaced;
        _settings = null; _settingsService = null;
        foreach (var entry in Windows.Values) { entry.Canvas.Children.Clear(); entry.Capture?.Dispose(); entry.Window.Close(); }
        Windows.Clear();
    }
}
