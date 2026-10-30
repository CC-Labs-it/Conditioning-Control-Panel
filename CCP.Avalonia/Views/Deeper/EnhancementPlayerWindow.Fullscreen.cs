// WPF EnhancementPlayerWindow.xaml.cs:1530-1744, 2020-2558, 1333, 1852, 2167-2182, 2659.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;
namespace ConditioningControlPanel.Avalonia.Views.Deeper;
public partial class EnhancementPlayerWindow
{
        internal const string BrowserFullscreenOnMessage = "ccp_fullscreen_on";
        internal const string BrowserFullscreenOffMessage = "ccp_fullscreen_off";
        internal const string BrowserExitFullscreenMessage = "ccp_exit_fullscreen";
        internal const string BrowserFullscreenScript = @"(function() {
    if (window.__ccpFsBound) return;
    window.__ccpFsBound = true;
    function post(m) {
        try {
            var w = window.chrome && window.chrome.webview;
            if (w && w.postMessage) w.postMessage(m);
            else if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(m);
        } catch (_) {}
    }
    function fsElement() { return document.fullscreenElement || document.webkitFullscreenElement; }
    function inAnyFs() { return !!(fsElement() || window._ccpForcedFs); }
    function exitLoop(remaining) {
        if (remaining <= 0 || !fsElement()) return;
        try {
            var p = document.exitFullscreen ? document.exitFullscreen()
                  : (document.webkitExitFullscreen ? document.webkitExitFullscreen() : null);
            if (p && p.then) p.then(function(){ exitLoop(remaining - 1); }, function(){ setTimeout(function(){ exitLoop(remaining - 1); }, 30); });
            else setTimeout(function(){ exitLoop(remaining - 1); }, 30);
        } catch (_) { setTimeout(function(){ exitLoop(remaining - 1); }, 30); }
    }
    function dblHandler(e) {
        if (!inAnyFs()) return;
        if (e) {
            try { e.stopImmediatePropagation(); } catch (_) {}
            try { e.preventDefault(); } catch (_) {}
        }
        exitLoop(5);
        post('ccp_exit_fullscreen');
    }
    document.addEventListener('dblclick', dblHandler, true);
    document.addEventListener('dblclick', dblHandler, false);
    window.addEventListener('dblclick', dblHandler, true);
    function bindOnVideo() {
        try {
            var vids = document.querySelectorAll('video');
            for (var i = 0; i < vids.length; i++) {
                var v = vids[i];
                if (v._ccpBound) continue;
                v._ccpBound = true;
                v.addEventListener('dblclick', dblHandler, true);
                v.addEventListener('dblclick', dblHandler, false);
            }
        } catch (_) {}
    }
    bindOnVideo();
    setInterval(function() { if (inAnyFs()) bindOnVideo(); }, 1000);
    var last = !!fsElement();
    function onChange() {
        var fs = !!fsElement();
        if (fs !== last) { last = fs; post(fs ? 'ccp_fullscreen_on' : 'ccp_fullscreen_off'); }
        if (!fs && window._ccpForcedFs) post('ccp_exit_fullscreen');
    }
    document.addEventListener('fullscreenchange', onChange);
    document.addEventListener('webkitfullscreenchange', onChange);
})();";
    private static readonly HashSet<EnhancementPlayerWindow> FullscreenPlayers = new();
    private Window? _videoFullscreenWindow;
    private DisplayMirror? _videoMirror;
    private bool _videoTransition, _videoClosing, _videoExitPending;
    internal Window? VideoFullscreenWindow => _videoFullscreenWindow;
    private void OnVideoNavigationCompleted(Uri uri)
    {
        if (_videoClosing) return;
        _ = _videoBrowser.InvokeScriptAsync(BrowserFullscreenScript + @"
(function() {
    if (window.__ccpPlayerFsBound) return;
    window.__ccpPlayerFsBound = true;
    document.addEventListener('dblclick', function(e) {
        if (document.fullscreenElement || document.webkitFullscreenElement || window._ccpForcedFs) return;
        var videos = document.querySelectorAll('video'), v = null, area = 0;
        for (var i = 0; i < videos.length; i++) {
            var r = videos[i].getBoundingClientRect(), a = r.width * r.height;
            if (a > area) { v = videos[i]; area = a; }
        }
        if (!v) return;
        e.stopImmediatePropagation();
        e.preventDefault();
        try {
            var p = v.requestFullscreen ? v.requestFullscreen() : v.webkitRequestFullscreen();
            if (p && p.catch) p.catch(function(){});
        } catch (_) {}
    }, true);
    document.addEventListener('keydown', function(e) {
        if (!(window._ccpForcedFs || document.fullscreenElement)) return;
        if (e.key !== 'Escape' && e.key !== 'F11') return;
        e.preventDefault();
        var w = window.chrome && window.chrome.webview;
        if (w && w.postMessage) w.postMessage('ccp_exit_fullscreen');
        else if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction('ccp_exit_fullscreen');
    }, true);
})();");
        if (_videoFullscreenWindow != null) _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = true;");
    }
    private void OnVideoWebMessage(string message)
    {
        if (_videoClosing) return;
        switch (message.Trim().Trim('"'))
        {
            case BrowserFullscreenOnMessage: EnterVideoFullscreen(); break;
            case BrowserFullscreenOffMessage:
                if (!_videoTransition) ExitVideoFullscreen();
                break;
            case BrowserExitFullscreenMessage: ExitVideoFullscreen(); break;
        }
    }
    private void EnterVideoFullscreen()
    {
        if (_videoClosing || _videoTransition || _videoFullscreenWindow != null) return;
        _videoTransition = true;
        try
        {
            if (CoreSettings.Current.DualMonitorEnabled && ScreenList.Enumerate(this).Count > 1)
            {
                var mirror = ScreenMirrorHead.For(this);
                if (mirror?.EnableMirror() == true) _videoMirror = mirror;
            }
            var screen = Screens.ScreenFromWindow(this)?.Bounds ?? default;
            var host = new Window
            {
                WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false,
                Topmost = true, Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new global::Avalonia.PixelPoint(screen.X + 100, screen.Y + 100),
                Width = 400, Height = 300,
            };
            _videoFullscreenWindow = host;
            FullscreenPlayers.Add(this);
            host.Closing += OnVideoHostClosing;
            host.Deactivated += OnVideoHostDeactivated;
            host.Activated += OnVideoHostActivated;
            host.AddHandler(KeyDownEvent, OnVideoHostKeyDown, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            using var moving = _videoBrowser.BeginReparenting();
            ((Grid)_videoPane.Child!).Children.Remove(_videoBrowser);
            host.Content = _videoBrowser;
            host.Show();
            host.WindowState = WindowState.FullScreen;
            _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = true;");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Deeper fullscreen entry failed");
            _videoExitPending = true;
        }
        finally
        {
            _videoTransition = false;
            if (_videoExitPending) { _videoExitPending = false; ExitVideoFullscreen(); }
        }
    }
    private void OnVideoHostKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.F11)) return;
        e.Handled = true;
        ExitVideoFullscreen();
    }
    private void OnVideoHostDeactivated(object? sender, EventArgs e) => ((Window)sender!).Topmost = false;
    private void OnVideoHostActivated(object? sender, EventArgs e) => ((Window)sender!).Topmost = true;
    private void OnVideoHostClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        ExitVideoFullscreen(closeHost: false);
    private void ExitVideoFullscreen(bool closeHost = true)
    {
        if (_videoTransition) { _videoExitPending = true; return; }
        var host = _videoFullscreenWindow;
        try { _videoMirror?.DisableMirror(); }
        finally { if (_videoMirror?.IsMirroring != true) _videoMirror = null; }
        if (host == null) { if (_videoMirror == null) FullscreenPlayers.Remove(this); return; }
        _videoTransition = true;
        _videoFullscreenWindow = null;
        if (_videoMirror == null) FullscreenPlayers.Remove(this);
        try
        {
            host.Closing -= OnVideoHostClosing;
            host.Deactivated -= OnVideoHostDeactivated;
            host.Activated -= OnVideoHostActivated;
            host.RemoveHandler(KeyDownEvent, OnVideoHostKeyDown);
            _ = _videoBrowser.InvokeScriptAsync(
                "window._ccpForcedFs = false; try { if(document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
            using var moving = _videoBrowser.BeginReparenting();
            host.Content = null;
            var pane = (Grid)_videoPane.Child!;
            if (!pane.Children.Contains(_videoBrowser)) pane.Children.Insert(0, _videoBrowser);
            host.Topmost = false;
            if (closeHost) host.Close();
        }
        finally { _videoTransition = false; _videoExitPending = false; }
    }
    internal static void ForceExitFullscreenAll()
    {
        foreach (var player in FullscreenPlayers.ToArray()) player.ExitVideoFullscreen();
    }
}
