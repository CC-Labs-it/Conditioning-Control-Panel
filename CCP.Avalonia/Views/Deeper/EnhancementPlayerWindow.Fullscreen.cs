// WPF EnhancementPlayerWindow.xaml.cs:2001-2553: the same WebView moves to a fullscreen host,
// clone precedes entry when DualMonitorEnabled, and every exit restores extend.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

public partial class EnhancementPlayerWindow
{
    private static readonly HashSet<EnhancementPlayerWindow> FullscreenPlayers = new();
    private Window? _videoFullscreenWindow;
    private DisplayMirror? _videoMirror;
    private bool _videoTransition, _videoClosing, _videoExitPending;
    internal Window? VideoFullscreenWindow => _videoFullscreenWindow;

    private void OnVideoNavigationCompleted(Uri uri)
    {
        if (_videoClosing) return;
        // ponytail: NativeWebView has no document-created hook. Reuse the browser's post-navigation
        // signal/double-click exit bridge; add the player's WPF double-click entry gesture.
        _ = _videoBrowser.InvokeScriptAsync(MainShellWindow.BrowserFullscreenScript + @"
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
        // WPF :1842-1853 (BUG-J9PPPJT274): a video rolling into the next page keeps the host;
        // the new document only needs the forced flag back so its escape hatches still work.
        if (_videoFullscreenWindow != null) _ = _videoBrowser.InvokeScriptAsync("window._ccpForcedFs = true;");
    }

    private void OnVideoWebMessage(string message)
    {
        if (_videoClosing) return;
        switch (message.Trim().Trim('"'))
        {
            case MainShellWindow.BrowserFullscreenOnMessage: EnterVideoFullscreen(); break;
            case MainShellWindow.BrowserFullscreenOffMessage:
                // WPF unsubscribes the native FS signal during reparent: losing HTML5 fullscreen
                // in the move must not undo the forced host. Explicit exit/panic still defer.
                if (!_videoTransition) ExitVideoFullscreen();
                break;
            case MainShellWindow.BrowserExitFullscreenMessage: ExitVideoFullscreen(); break;
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
            host.AddHandler(KeyDownEvent, OnVideoHostKeyDown, RoutingStrategies.Tunnel);
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

    // WPF :2163-2182 (#905): Topmost is rented, so a dialog raised over the video can surface.
    private void OnVideoHostDeactivated(object? sender, EventArgs e) => ((Window)sender!).Topmost = false;

    private void OnVideoHostActivated(object? sender, EventArgs e) => ((Window)sender!).Topmost = true;

    private void OnVideoHostClosing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        ExitVideoFullscreen(closeHost: false);

    private void ExitVideoFullscreen(bool closeHost = true)
    {
        if (_videoTransition) { _videoExitPending = true; return; }
        var host = _videoFullscreenWindow;
        // Restore extend even if entry failed before allocating a host.
        try { _videoMirror?.DisableMirror(); }
        finally { _videoMirror = null; }
        if (host == null) return;
        _videoTransition = true;
        _videoFullscreenWindow = null;
        FullscreenPlayers.Remove(this);
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
