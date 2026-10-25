using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF Services/Haptics/Core/MockToast: one shared window, refreshed for one second.
/// Providers call from their mixer thread; only the dispatcher owns controls and the timer.</summary>
internal static class HapticMockToast
{
    internal static Window? Window { get; private set; }
    /// <summary>Test seam: expiry is measured on this clock, so a test steps it instead of sleeping.</summary>
    internal static TimeProvider Time { get; set; } = TimeProvider.System;
    private static readonly TimeSpan Expiry = TimeSpan.FromSeconds(1);
    private static TextBlock? _text;
    private static DispatcherTimer? _timer;
    private static long _shownAt;
    private static int _generation;

    internal static void Seed() => Services.Haptics.Core.MockProviderV2.Toast = Post;

    public static void Post(string message)
    {
        if (Application.Current == null) return;
        var generation = System.Threading.Volatile.Read(ref _generation);
        Dispatcher.UIThread.Post(() =>
        {
            // Exit invalidates already queued provider callbacks as well as closing the window.
            if (generation != _generation) return;
            try { Show(message); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Mock haptics feedback could not be shown"); }
        });
    }

    private static void Show(string message)
    {
        if (Window == null)
        {
            var window = new HapticMockToastWindow();
            _text = (TextBlock)window.Content!;
            Window = window;
            window.Closed += (_, _) =>
            {
                _timer?.Stop();
                _timer = null;
                Window = null;
                _text = null;
            };
            var screen = window.Screens.Primary; // WPF SystemParameters.WorkArea is the primary work area.
            if (screen != null)
            {
                var work = screen.WorkingArea;
                var scale = screen.Scaling;
                window.Position = new PixelPoint(work.Right - (int)(260 * scale), work.Bottom - (int)(112 * scale));
            }
            window.Show();
        }
        _text!.Text = message;
        _shownAt = Time.GetTimestamp();
        if (_timer == null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += (_, _) => Tick();
        }
        _timer.Stop();
        _timer.Interval = Expiry;
        _timer.Start();
    }

    /// <summary>WPF's 1 s timer tick: close once a full second has passed since the last message.</summary>
    internal static void Tick()
    {
        if (Window == null) return;
        var left = Expiry - Time.GetElapsedTime(_shownAt);
        if (left <= TimeSpan.Zero) { Window.Close(); return; }
        if (_timer == null) return;
        _timer.Stop();
        _timer.Interval = left;
        _timer.Start();
    }

    internal static void Close()
    {
        System.Threading.Interlocked.Increment(ref _generation);
        _timer?.Stop();
        Window?.Close();
    }
}

/// <summary>The WPF mock toast surface; also constructed by --render-all for visual proof.</summary>
internal sealed class HapticMockToastWindow : Window
{
    internal HapticMockToastWindow()
    {
        Width = 240;
        Height = 92;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = new SolidColorBrush(Color.FromArgb(230, 255, 105, 180));
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Content = new TextBlock
        {
            Text = "Mock haptics connected\nLush / Edge / Solace",
            Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = 12,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 4),
        };
    }
}
