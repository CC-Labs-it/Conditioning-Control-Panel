using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF MainWindow.Enhancements.cs:2109-2258: one reused proc toast, hidden between procs.</summary>
internal sealed class LuckyToastWindow : Window
{
    internal static LuckyToastWindow? Current { get; private set; }
    internal static TimeProvider Clock = TimeProvider.System;
    private static int _generation;
    private readonly Border _card;
    internal readonly TextBlock Multiplier;
    private readonly TextBlock _title;
    private readonly BeatLoop _beat;
    private long _phaseAt;
    private bool _fading, _entering;

    private LuckyToastWindow()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        _title = new TextBlock { Text = Loc.Get("lucky_toast_title"), FontWeight = FontWeight.Bold,
            FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center };
        Multiplier = new TextBlock { FontSize = 14, Foreground = Brushes.LightPink,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        var stack = new StackPanel();
        stack.Children.Add(_title);
        stack.Children.Add(Multiplier);
        _card = new Border { Background = new SolidColorBrush(Color.FromArgb(224, 21, 21, 48)),
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2),
            Padding = new Thickness(20, 12), Child = stack };
        Content = _card;
        SizeChanged += (_, _) => Place();
        // WPF MainWindow.Enhancements.cs:2147,2220 keeps the same fade timing; Avalonia uses the shared window beat.
        _beat = new BeatLoop(_card, _ => Tick());
    }

    internal static void Post(bool flash, int multiplier)
    {
        if (Application.Current == null || CoreSettings.Current.SuppressPerkNotifications) return;
        var generation = _generation;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _generation) return;
            try { ShowProc(flash, multiplier); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Lucky toast could not be shown"); }
        });
    }

    internal static void ShowProc(bool flash, int multiplier)
    {
        // WPF :2124 checks again on the UI thread, and suppresses while a game owns the screen.
        var decision = LuckyToastRule.Decide(CoreSettings.Current.SuppressPerkNotifications,
            PanicSurfaces.AnyOwnsTheScreen(), Current?.IsVisible == true);
        if (decision == LuckyToastRule.Decision.Skip) return;
        var w = Current ??= new LuckyToastWindow();
        var color = flash ? Colors.Gold : Colors.HotPink;
        w._title.Foreground = w._card.BorderBrush = new SolidColorBrush(color);
        w._card.BoxShadow = new BoxShadows(new BoxShadow { Color = Color.FromArgb(204, color.R, color.G, color.B), Blur = 30 });
        w.Multiplier.Text = string.Format(Loc.Get("lucky_toast_multiplier"), multiplier);
        w._fading = false;
        w._entering = decision == LuckyToastRule.Decision.Show;
        w._phaseAt = Clock.GetTimestamp();
        w.Opacity = w._entering ? 0 : 1;
        if (w._entering) w.Show();
        w.Place();
        w._beat.Start();
    }

    private void Place()
    {
        if (Screens.Primary is not { } screen) return;
        // WPF :2240-2247 top centre, 40 DIPs below the primary work area's top.
        Position = new PixelPoint(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(Bounds.Width * screen.Scaling)) / 2,
            screen.WorkingArea.Y + (int)(40 * screen.Scaling));
    }

    internal void Tick()
    {
        var ms = Clock.GetElapsedTime(_phaseAt).TotalMilliseconds;
        if (_fading)
        {
            Opacity = Math.Clamp(1 - ms / 300, 0, 1);
            if (ms >= 300) { _beat.Stop(); Hide(); }
            return;
        }
        if (_entering) Opacity = Math.Clamp(ms / 200, 0, 1);
        if (ms < 3000) return;
        _entering = false;
        _fading = true;
        _phaseAt = Clock.GetTimestamp();
        Opacity = 1;
    }

    internal static void CloseToast()
    {
        _generation++;
        Current?._beat.Stop();
        Current?.Close();
        Current = null;
    }
}
