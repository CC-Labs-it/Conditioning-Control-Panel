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
    private readonly DispatcherTimer _timer;
    internal readonly TextBlock Detail = new() { Foreground = Brushes.Black };
    private DateTimeOffset _connectedAt;
    private DateTimeOffset? _lastStop;
    private string? _lastAction;
    private bool _connected, _disposed;
    internal TimeProvider Clock = TimeProvider.System;
    internal Button MoreButton { get; }
    internal Button StopButton { get; }

    internal RemoteHudWindow(RemoteRelay relay, Action localStop)
    {
        _relay = relay;
        _localStop = localStop;
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
        if (connected && !_connected) { _connectedAt = Clock.GetUtcNow(); _lastStop = null; _lastAction = null; }
        _connected = connected;
        if (!connected) { if (IsVisible) Hide(); return; }
        var elapsed = Clock.GetUtcNow() - _connectedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var seconds = (long)elapsed.TotalSeconds;
        Detail.Text = seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
        if (!string.IsNullOrEmpty(_lastAction)) Detail.Text += "  ·  " + Loc.GetF("remote_hud_last", _lastAction);
        if (!IsVisible) Show();
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
        Detail.Text = kind == "stop" ? Loc.Get(sent ? "remote_hud_stopped" : "remote_hud_stopped_not_sent")
            : sent ? Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_more")) : Loc.Get("remote_hud_not_sent");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        Close();
    }
}
