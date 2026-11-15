using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos;

// WPF ChaosTunnelService.cs:19-137: the opaque, non-topmost run background.
internal sealed class ChaosTunnelWindow : Window
{
    internal static ChaosTunnelWindow? Active { get; private set; }
    internal static Func<bool> CanOpen = () => WebHost.IsAvailable;
    internal static Func<Window, bool> Sink = X11Overlay.Sink;
    internal static Func<Window?> MainWindow = () => (global::Avalonia.Application.Current?.ApplicationLifetime
        as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    internal static Func<Window, Window, bool> Guard = X11Overlay.RestackBelow;
    internal static Action<string, float> PlaySound = ChaosOverlayWindow.ChaosSfx.Play;
    internal static Func<string, double, LibVlcAudio.IPausableVoice?> PlayAmbient =
        (path, volume) => LibVlcAudio.Instance?.PlayVoice(path, volume, true, () => { }) as LibVlcAudio.IPausableVoice;
    internal static Func<string?> AmbientPath = () => CoreModArt.AudioOverridePath("chaos/tunnel_ambient.mp3")
        ?? ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", "tunnel_ambient.mp3"));
    internal WebHost Web { get; } = new() { InjectCarrier = true, SuppressNewWindows = true };
    private readonly List<string> _pending = new();
    private readonly DispatcherTimer _timeout = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly DispatcherTimer _bridgeProbe = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _zGuard = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _exitWatchdog = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private bool _ready, _runActive, _videoPlaying;
    private LibVlcAudio.IPausableVoice? _ambient;

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
        Web.NavigationFinished += (url, ok) =>
        {
            if (ok && !_ready && Active == this) { ProbeBridge(); _bridgeProbe.Start(); }
        };
        _bridgeProbe.Tick += (_, _) => ProbeBridge();
        _timeout.Tick += (_, _) => OnTimeout();
        _zGuard.Tick += (_, _) => GuardMain();
        _exitWatchdog.Tick += (_, _) => OnExitTimeout();
        Closed += (_, _) => { _timeout.Stop(); _bridgeProbe.Stop(); _zGuard.Stop(); _exitWatchdog.Stop(); StopAmbient(); _pending.Clear(); if (Active == this) Active = null; };
    }

    internal static void Open(bool preload = false)
    {
        if (!CoreSettings.Current.ChaosTunnelEnabled || !CanOpen()) { CloseActive(); return; }
        try
        {
            var w = Active;
            if (w == null)
            {
                w = new ChaosTunnelWindow();
                Active = w;
                var url = new Uri(WebAssetServer.Shared.Url("tunnel/index.html"));
                w.Web.AllowNavigation = u => u.Scheme == url.Scheme && u.Host == url.Host && u.Port == url.Port;
                w.Show();
                // Wayland offers no client-controlled stacking; do not cover unpinned game chrome.
                if (!Sink(w) && !CoreSettings.Current.ChaosPinOnTop) { CloseActive(); return; }
                w._timeout.Start();
                w.Web.Navigate(url);
            }
            w._exitWatchdog.Stop();
            w._runActive = true;
            w._videoPlaying = false;
            if (w._ready) w.StartAmbient();
            if (!preload)
            {
                w.GuardMain();
                w._zGuard.Start();
                w.Post(new { type = "run-start" });
            }
        }
        catch (Exception ex) { Log.Warning("Chaos tunnel: {E}", ex.Message); CloseActive(); }
    }

    // WPF closes on exit-done or after 1.2 seconds; a new run claims the window.
    internal static void EndActive()
    {
        var w = Active;
        if (w == null) return;
        w._runActive = false;
        w._zGuard.Stop();
        w.StopAmbient();
        if (!w._ready) { CloseActive(); return; }
        w.Post(new { type = "run-end" });
        w._exitWatchdog.Stop();
        w._exitWatchdog.Start();
    }

    internal static void CloseActive()
    {
        var w = Active;
        Active = null;
        if (w != null) { try { w.Close(); } catch (Exception ex) { Log.Debug("Chaos tunnel close: {E}", ex.Message); } }
    }

    internal void ProbeBridge() => _ = Web.InvokeScriptAsync(WebHost.CarrierShim + ";window.dispatchEvent(new Event('ccp-tunnel-connect'));");
    internal void GuardMain()
    {
        try { var main = MainWindow(); if (Active == this && _runActive && main is { IsVisible: true, WindowState: not WindowState.Minimized } && main != this) Guard(main, this); }
        catch (Exception ex) { Log.Debug("Chaos tunnel stacking: {E}", ex.Message); }
    }
    internal bool GuardRunning => _zGuard.IsEnabled;
    internal void OnTimeout() { if (!_ready && Active == this) CloseActive(); }
    internal void OnExitTimeout() { if (!_runActive && Active == this) CloseActive(); }

    private void Send(string json) => _ = Web.InvokeScriptAsync(
        "window.dispatchEvent(new MessageEvent('ccp-tunnel-message',{data:" + json + "}));");

    internal void Post(object message)
    {
        var json = JsonConvert.SerializeObject(message);
        if (_ready) Send(json);
        else _pending.Add(json);
    }

    internal void SetVideoPlaying(bool on)
    {
        _videoPlaying = on;
        Post(new { type = "video-playing", on });
        try { _ambient?.SetPaused(on); } catch (Exception ex) { Log.Debug("Chaos tunnel ambient pause: {E}", ex.Message); }
    }

    private void StartAmbient()
    {
        if (_ambient != null) return;
        try
        {
            var path = AmbientPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            _ambient = PlayAmbient(path, Math.Clamp(CoreSettings.Current.MasterVolume / 100.0 * 0.26, 0, 1));
            if (_videoPlaying) _ambient?.SetPaused(true);
        }
        catch (Exception ex) { Log.Debug("Chaos tunnel ambient: {E}", ex.Message); }
    }

    private void StopAmbient()
    {
        try { _ambient?.Dispose(); } catch (Exception ex) { Log.Debug("Chaos tunnel ambient stop: {E}", ex.Message); }
        _ambient = null;
    }

    internal void OnMessage(string json)
    {
        try
        {
            var message = JObject.Parse(json);
            switch ((string?)message["type"])
            {
                case "ready" when !_ready:
                    _ready = true;
                    _timeout.Stop();
                    _bridgeProbe.Stop();
                    foreach (var pending in _pending) Send(pending);
                    _pending.Clear();
                    if (_runActive) StartAmbient();
                    break;
                case "sfx":
                    var name = (string?)message["name"];
                    if (!string.IsNullOrWhiteSpace(name)) PlaySound(name, (float?)message["scale"] ?? 0.6f);
                    break;
                case "exit-done" when !_runActive:
                    if (Active == this) CloseActive();
                    break;
            }
        }
        catch (Exception ex) { Log.Debug("Chaos tunnel message: {E}", ex.Message); }
    }
}
