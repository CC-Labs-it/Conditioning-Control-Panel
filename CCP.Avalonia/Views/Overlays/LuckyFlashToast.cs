using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>WPF MainWindow.Enhancements.OnLuckyProc: one reusable toast, top-centre of the
/// primary work area, 200 ms in, held 3 s after the last proc, 300 ms out. No game interruption.
/// WPF MainWindow/MainWindow.Enhancements.cs:2109-2279. Labels are mod-aware and shared by flash and bubble procs.</summary>
internal sealed class LuckyFlashToast : Window
{
    internal static LuckyFlashToast? Active { get; private set; }
    internal static Func<bool> ScreenOwned = PanicSurfaces.AnyOwnsTheScreen;
    internal static Func<Window, PixelRect, bool> Prepare = (w, r) => X11Overlay.IsAvailable
        && X11Overlay.SetClickThrough(w, true) && X11Overlay.SetOpacity(w, 0) && X11Overlay.SetOverrideRedirect(w, r);
    private readonly TextBlock _multiplier, _title;
    private readonly Border _body, _shadow;
    private readonly Action<TimeSpan> _frame;
    private long _lastProc;
    private bool _refresh;
    internal double Alpha { get; private set; }

    internal LuckyFlashToast()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = ShowActivated = CanResize = Focusable = IsHitTestVisible = false;
        var gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        _multiplier = new TextBlock
        {
            Text = "", FontSize = 14, Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xB6, 0xC1)), HorizontalAlignment = HorizontalAlignment.Center,
        };
        // P82: shadow is a sibling, never an Effect on the content. Gold under the see-through body,
        // as WPF's DropShadowEffect glow shows through it.
        var shadow = _shadow = new Border
        {
            CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(204, 0xFF, 0xD7, 0x00)),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(204, 0xFF, 0xD7, 0x00) }),
        };
        _title = new TextBlock { Text = "", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = gold, HorizontalAlignment = HorizontalAlignment.Center };
        var body = _body = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x15, 0x15, 0x30)),
            BorderBrush = gold, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 12),
            Child = new StackPanel { Children =
            {
                _title,
                _multiplier,
            } },
        };
        Content = new Panel
        {
            Margin = new Thickness(15), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Children = { shadow, body },
        };
        _frame = _ => Tick();
        Closed += (_, _) => { DispatcherFrameSource.Shared.Unsubscribe(_frame); if (Active == this) Active = null; };
    }

    internal static void ShowLucky(string procType, int multiplier)
    {
        try { ShowCore(procType, multiplier); }
        catch (Exception ex)
        {
            CloseActive();
            Serilog.Log.Debug("Lucky toast failed: {Error}", ex.Message);
        }
    }

    private static void ShowCore(string procType, int multiplier)
    {
        var decision = LuckyToastRule.Decide(CoreSettings.Current.SuppressPerkNotifications, ScreenOwned(), Active?.IsVisible == true);
        if (decision == LuckyToastRule.Decision.Skip) return;
        var w = Active ?? new LuckyFlashToast();
        var color = procType.Contains("Flash") ? Color.FromRgb(0xFF, 0xD7, 0x00) : Color.FromRgb(0xFF, 0x69, 0xB4);
        w._title.Text = procType;
        w._title.Foreground = w._body.BorderBrush = new SolidColorBrush(color);
        var glow = Color.FromArgb(204, color.R, color.G, color.B);
        w._shadow.Background = new SolidColorBrush(glow);
        w._shadow.BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = glow });
        w._multiplier.Text = ConditioningControlPanel.Localization.Loc.GetF("lucky_xp_multiplier", multiplier);
        w._lastProc = FlashOverlay.Clock.GetTimestamp();
        w._refresh = decision == LuckyToastRule.Decision.Refresh;
        var content = (Control)w.Content!;
        content.Measure(Size.Infinity);
        var size = content.DesiredSize;
        w.Width = size.Width;
        w.Height = size.Height;
        var screen = w.Screens.Primary ?? w.Screens.All[0];
        var scale = screen.Scaling;
        var rect = new PixelRect(screen.WorkingArea.X + (screen.WorkingArea.Width - (int)(size.Width * scale)) / 2,
            screen.WorkingArea.Y + (int)(40 * scale), (int)(size.Width * scale), (int)(size.Height * scale));
        w.Position = rect.Position;
        if (decision == LuckyToastRule.Decision.Show)
        {
            if (!Prepare(w, rect)) { w.Close(); return; }
            Active = w;
            w.Show();
            DispatcherFrameSource.Shared.Subscribe(w._frame);
        }
        w.Tick();
    }

    internal string MultiplierText => _multiplier.Text ?? "";
    internal IBrush? Accent => _body.BorderBrush;

    internal void Tick()
    {
        var t = FlashOverlay.Clock.GetElapsedTime(_lastProc).TotalSeconds;
        Alpha = Math.Clamp(t < 3 ? (_refresh ? 1 : t / 0.2) : 1 - (t - 3) / 0.3, 0, 1);
        X11Overlay.SetOpacity(this, Alpha);
        if (t < 3.3) return;
        DispatcherFrameSource.Shared.Unsubscribe(_frame);
        Hide(); // WPF reuses the hidden window; CloseActive releases it on Stop/panic/shutdown.
    }

    internal static void CloseActive()
    {
        if (Active is not { } w) return;
        Active = null;
        DispatcherFrameSource.Shared.Unsubscribe(w._frame);
        try { w.Close(); }
        catch (Exception ex) { Serilog.Log.Debug("Lucky toast close failed: {Error}", ex.Message); }
    }
}
