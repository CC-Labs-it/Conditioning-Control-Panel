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
    private static TextBlock? _text;
    private static DispatcherTimer? _timer;
    private static int _generation;
    private static int _ignoreStopFeedback;

    internal static void Seed() => Services.Haptics.Core.MockProviderV2.Toast = Post;

    public static void Post(string message)
    {
        if (Application.Current == null) return;
        // Mixer panic retries publish ALL STOP again after the window has closed.
        if (message == "Mock haptics: ALL STOP" && System.Threading.Volatile.Read(ref _ignoreStopFeedback) != 0) return;
        if (message != "Mock haptics: ALL STOP") System.Threading.Interlocked.Exchange(ref _ignoreStopFeedback, 0);
        var generation = System.Threading.Volatile.Read(ref _generation);
        Dispatcher.UIThread.Post(() =>
        {
            // A panic/exit invalidates already queued provider callbacks as well as closing the window.
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
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => Close();
        }
        _timer.Stop();
        _timer.Start();
    }

    internal static void StopForPanic()
    {
        System.Threading.Interlocked.Exchange(ref _ignoreStopFeedback, 1);
        Close();
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
