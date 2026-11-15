using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosTunnelService.cs:19-137. Only pinned runs are supported until a native
// stacking seam can keep the opaque tunnel below non-topmost game windows.
internal sealed class ChaosTunnelWindow : Window
{
    internal static ChaosTunnelWindow? Active { get; private set; }
    internal static Func<bool> CanOpen = () => WebHost.IsAvailable && OperatingSystem.IsLinux();
    internal WebHost Web { get; } = new() { InjectCarrier = true, SuppressNewWindows = true };
    private readonly List<string> _pending = new();
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _ready;

    private ChaosTunnelWindow()
    {
        SystemDecorations = WindowDecorations.None;
        Background = Brushes.Black;
        Topmost = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = Web;
        var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
        if (screen != null)
        {
            Position = screen.Bounds.Position;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            Width = screen.Bounds.Width / scale;
            Height = screen.Bounds.Height / scale;
        }
        Web.WebMessage += OnMessage;
        _timeout.Tick += (_, _) => OnTimeout();
        Closed += (_, _) => { _timeout.Stop(); _pending.Clear(); if (Active == this) Active = null; };
    }

    internal static void Open()
    {
        CloseActive();
        if (!CoreSettings.Current.ChaosTunnelEnabled || !CoreSettings.Current.ChaosPinOnTop || !CanOpen()) return;
        try
        {
            var w = new ChaosTunnelWindow();
            Active = w;
            var url = new Uri(WebAssetServer.Shared.Url("tunnel/index.html"));
            w.Web.AllowNavigation = u => u.Scheme == url.Scheme && u.Host == url.Host && u.Port == url.Port;
            w.Show();
            w._timeout.Start();
            w.Web.Navigate(url);
            w.Post(new { type = "run-start" });
        }
        catch (Exception ex) { Log.Warning("Chaos tunnel: {E}", ex.Message); CloseActive(); }
    }

    internal static void CloseActive()
    {
        var w = Active;
        Active = null;
        if (w != null) { try { w.Close(); } catch (Exception ex) { Log.Debug("Chaos tunnel close: {E}", ex.Message); } }
    }

    internal void OnTimeout() { if (!_ready && Active == this) CloseActive(); }

    internal void Post(object message)
    {
        var json = JsonConvert.SerializeObject(message);
        if (_ready) _ = Web.PostJsonAsync(json);
        else _pending.Add(json);
    }

    internal void OnMessage(string json)
    {
        try
        {
            var message = JObject.Parse(json);
            if ((string?)message["type"] != "ready" || _ready) return;
            _ready = true;
            _timeout.Stop();
            foreach (var pending in _pending) _ = Web.PostJsonAsync(pending);
            _pending.Clear();
        }
        catch (Exception ex) { Log.Debug("Chaos tunnel message: {E}", ex.Message); }
    }
}
