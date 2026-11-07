using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Remote;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// WPF Windows/RemoteHud/RemoteHudWindow.cs:239,449. More and Stop first; Easy and controller name are part B.
internal sealed class RemoteHudWindow : Window, IDisposable
{
    private readonly RemoteRelay _relay;
    private readonly Action _localStop;
    private readonly Func<(PixelRect WorkingArea, double Scaling)?> _anchorScreen;
    private readonly DispatcherTimer _timer;
    // WPF palette (RemoteHudWindow.cs:56-61): ink pill, pink edge, white headline, muted subline.
    private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x2E), Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    internal readonly TextBlock Detail = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xA8, 0xC8)), FontSize = 11 };
    private DateTimeOffset _connectedAt;
    private DateTimeOffset? _lastStop;
    private string? _lastAction, _confirm;
    private DateTimeOffset _confirmUntil;
    private bool _connected, _disposed;
    internal TimeProvider Clock = TimeProvider.System;
    internal Button MoreButton { get; }
    internal Button StopButton { get; }

    internal static readonly TimeSpan ConfirmFor = TimeSpan.FromMilliseconds(1600);   // WPF RemoteHudRules.cs:22

    /// <param name="anchorScreen">Work area and scaling of the monitor the main window is on (WPF Place :345).</param>
    internal RemoteHudWindow(RemoteRelay relay, Action localStop, Func<(PixelRect WorkingArea, double Scaling)?> anchorScreen)
    {
        _relay = relay;
        _localStop = localStop;
        _anchorScreen = anchorScreen;
        // WPF ShowActivated false (:98): showing the pill never pulls focus out of a game or a lock card.
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowDecorations = WindowDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(10) };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Loc.Get("remote_hud_someone"), Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeight.SemiBold });
        text.Children.Add(Detail);
        panel.Children.Add(text);
        MoreButton = RoundButton(Color.FromRgb(0x4F, 0xE0, 0xB5), "remote_hud_more", "remote_hud_more_tip");
        StopButton = RoundButton(Color.FromRgb(0xFF, 0x54, 0x68), "remote_hud_stop", "remote_hud_stop_tip");
        MoreButton.Click += async (_, _) => await SignalAsync("more");
        StopButton.Click += async (_, _) => await SignalAsync("stop");
        panel.Children.Add(MoreButton);
        panel.Children.Add(StopButton);
        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, Ink.R, Ink.G, Ink.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, Pink.R, Pink.G, Pink.B)),
            BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(26), Padding = new Thickness(8, 6), Child = panel,
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        // WPF :213-214: re-centre when the detail line grows, and stop ticking once the window is gone
        // (a window manager close would otherwise leave the tick calling Show on a closed window).
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        Closed += (_, _) => { _disposed = true; _timer.Stop(); };
    }

    /// <summary>WPF MakeSignalButton (:398): a 40 px round, non-focusable button with its tooltip.</summary>
    private static Button RoundButton(Color fill, string labelKey, string tipKey)
    {
        var b = new Button
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(0), Focusable = false,
            Background = new SolidColorBrush(fill), Foreground = new SolidColorBrush(Ink),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Content = new TextBlock { Text = Loc.Get(labelKey), FontSize = 11, FontWeight = FontWeight.Bold },
        };
        ToolTip.SetTip(b, Loc.Get(tipKey));
        return b;
    }

    internal void Refresh()
    {
        if (_disposed) return;
        var connected = _relay.IsActive && _relay.ControllerConnected;
        if (connected && !_connected) { _connectedAt = Clock.GetUtcNow(); _lastStop = null; _confirm = null; }   // WPF keeps the last action until the session ends
        _connected = connected;
        if (!connected) { if (IsVisible) Hide(); return; }
        var now = Clock.GetUtcNow();
        // WPF Paint (:295): a More/Stop confirmation holds until it expires, the clock waits.
        if (_confirm != null && now < _confirmUntil) Detail.Text = _confirm;
        else
        {
            _confirm = null;
            var elapsed = now - _connectedAt;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            var seconds = (long)elapsed.TotalSeconds;
            Detail.Text = seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
            if (!string.IsNullOrEmpty(_lastAction)) Detail.Text += "  ·  " + Loc.GetF("remote_hud_last", _lastAction);
        }
        if (!IsVisible) { Show(); Place(); }
    }

    /// <summary>WPF Place (:345): top centre of the work area, 10 DIP below its top edge.</summary>
    private void Place()
    {
        if (_anchorScreen() is not { } screen) return;
        var width = Bounds.Width > 0 ? Bounds.Width : 360;
        var wa = screen.WorkingArea;
        Position = new PixelPoint((int)(wa.X + (wa.Width - width * screen.Scaling) / 2), (int)(wa.Y + 10 * screen.Scaling));
    }

    internal void RecordAction(string action)
    {
        _lastAction = RemoteActionLabels.For(action);
        Refresh();
    }

    internal async Task SignalAsync(string kind)
    {
        if (_disposed || !_relay.IsActive || !_relay.ControllerConnected) return;
        if (kind == "stop")
        {
            var now = Clock.GetUtcNow();
            if (_lastStop is { } last && now >= last && now - last < TimeSpan.FromMilliseconds(2500)) return;
            _lastStop = now;
            try { _localStop(); } catch (Exception ex) { Serilog.Log.Warning(ex, "Remote HUD local stop failed"); }
        }
        var sent = await _relay.SendSignalAsync(kind);
        if (_disposed) return;
        _confirm = kind == "stop" ? Loc.Get(sent ? "remote_hud_stopped" : "remote_hud_stopped_not_sent")
            : sent ? Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_more")) : Loc.Get("remote_hud_not_sent");
        _confirmUntil = Clock.GetUtcNow() + ConfirmFor;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Close();
    }
}
