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
    internal readonly TextBlock Detail = new() { Foreground = Brushes.Black };
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
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = Loc.Get("remote_hud_someone"), Foreground = Brushes.Black, FontWeight = FontWeight.Bold });
        text.Children.Add(Detail);
        panel.Children.Add(text);
        MoreButton = new Button { Content = Loc.Get("remote_hud_more") };
        StopButton = new Button { Content = Loc.Get("remote_hud_stop") };
        MoreButton.Click += async (_, _) => await SignalAsync("more");
        StopButton.Click += async (_, _) => await SignalAsync("stop");
        panel.Children.Add(MoreButton);
        panel.Children.Add(StopButton);
        Content = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(16), Child = panel };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    internal void Refresh()
    {
        if (_disposed) return;
        var connected = _relay.IsActive && _relay.ControllerConnected;
        if (connected && !_connected) { _connectedAt = Clock.GetUtcNow(); _lastStop = null; _lastAction = null; _confirm = null; }
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
