// WPF MainWindow.Browser.cs:2727-3055 (HandleBrowserFullscreenChanged, the rented Topmost + Esc/F11 escape hatch #905/#952,
// Enter/ExitBrowserFullscreen, ExitBrowserFullscreenForTeardown) and BrowserService.cs:249-330 (the forced-fullscreen exit
// detector). NativeWebView has no ContainsFullScreenElementChanged and no document-created script seam, so the page's own
// fullscreenchange is reported over the WebHost string channel by a script injected after each completed navigation.
// ponytail: dual-monitor mirroring (WPF App.ScreenMirror, :2735-2745) and the 0.5/1.0 zoom swap (WebHost has no zoom)
// remain wire-shell-browser-b-next.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal const string BrowserFullscreenOnMessage = "ccp_fullscreen_on";
        internal const string BrowserFullscreenOffMessage = "ccp_fullscreen_off";
        internal const string BrowserExitFullscreenMessage = "ccp_exit_fullscreen";

        /// <summary>BrowserService.cs:259-326 (dblclick exit while forced fullscreen, fullscreenchange exit) plus the
        /// stand-in for CoreWebView2.ContainsFullScreenElementChanged (BrowserService.cs:556-567). Idempotent per document.</summary>
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

        private bool _isBrowserFullscreen, _browserFullscreenWasPopout;
        private (WindowDecorations Decorations, bool CanResize, WindowState State, PixelPoint Position,
            double Width, double Height, bool Topmost) _popoutPreFs;
        private Window? _browserFsEscapeTarget;

        internal bool IsBrowserFullscreen => _isBrowserFullscreen;

        /// <summary>The embedded card handed its host to a fullscreen window (WPF reuses _browserPopoutWindow for it).</summary>
        internal bool IsBrowserFullscreenEmbedded => _isBrowserFullscreen && !_browserFullscreenWasPopout;

        /// <summary>WPF OnBrowserWebMessageReceived's fullscreen branches (:1049-1054) and the FullscreenChanged
        /// subscription (:162-164). A WebView2 adapter may deliver a posted string JSON-quoted, so quotes are dropped.</summary>
        internal void OnBrowserWebMessage(string message)
        {
            switch (message.Trim().Trim('"'))
            {
                case BrowserExitFullscreenMessage:
                    Log.Information("MainShell: ccp_exit_fullscreen received (forced FS active = {Active})", _isBrowserFullscreen);
                    if (_isBrowserFullscreen) ExitBrowserFullscreen();
                    break;
                case BrowserFullscreenOnMessage: _ = EnterIfPageIsFullscreenAsync(); break;
                case BrowserFullscreenOffMessage: ExitBrowserFullscreen(); break;
            }
        }

        /// <summary>WPF reacts only to the engine's ContainsFullScreenElementChanged, which a page cannot fake; any page script
        /// can post the string, so the document is asked whether it really holds a fullscreen element before we go topmost.</summary>
        private async System.Threading.Tasks.Task EnterIfPageIsFullscreenAsync()
        {
            var answer = await (BrowserView?.InvokeScriptAsync("!!(document.fullscreenElement||document.webkitFullscreenElement)")
                ?? System.Threading.Tasks.Task.FromResult<string?>(null));
            if (answer?.Trim().Trim('"') == "true") EnterBrowserFullscreen();
            else Log.Information("MainShell: ccp_fullscreen_on ignored, the page holds no fullscreen element");
        }

        /// <summary>WPF ArmFullscreenEscapes: Topmost is rented (dropped on Deactivated, taken back on Activated while still
        /// fullscreen) and Esc/F11 on the WINDOW always gets out. Tunnel-routed so a focused child cannot eat the key.</summary>
        private void ArmFullscreenEscapes(Window window)
        {
            DisarmFullscreenEscapes();
            _browserFsEscapeTarget = window;
            window.Deactivated += OnBrowserFsDeactivated;
            window.Activated += OnBrowserFsActivated;
            window.AddHandler(KeyDownEvent, OnBrowserFsKeyDown, RoutingStrategies.Tunnel);
        }

        private void DisarmFullscreenEscapes()
        {
            var window = _browserFsEscapeTarget;
            _browserFsEscapeTarget = null;
            if (window == null) return;
            window.Deactivated -= OnBrowserFsDeactivated;
            window.Activated -= OnBrowserFsActivated;
            window.RemoveHandler(KeyDownEvent, OnBrowserFsKeyDown);
        }

        private void OnBrowserFsDeactivated(object? sender, EventArgs e)
        {
            if (_browserFsEscapeTarget is { } w) w.Topmost = false;
        }

        private void OnBrowserFsActivated(object? sender, EventArgs e)
        {
            if (_isBrowserFullscreen && _browserFsEscapeTarget is { } w) w.Topmost = true;
        }

        private void OnBrowserFsKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Escape or Key.F11)) return;
            e.Handled = true;
            ExitBrowserFullscreen();
        }

        /// <summary>WPF StartStop.cs:529 (#952): a session ending behind a chrome-less topmost browser leaves its dialog
        /// invisible. Early-outs unless fullscreen is up.</summary>
        internal void ExitBrowserFullscreenForTeardown()
        {
            if (!_isBrowserFullscreen) return;
            try
            {
                ExitBrowserFullscreen();
                Log.Information("Browser fullscreen closed by session teardown (#952)");
            }
            catch (Exception ex) { Log.Debug("ExitBrowserFullscreenForTeardown failed: {Error}", ex.Message); }
        }

        /// <summary>WPF EnterBrowserFullscreen: a user pop-out goes fullscreen in place (state saved for the way back);
        /// the embedded card hands the SAME host to a chrome-less, taskbar-less topmost window on the shell's screen.</summary>
        public void EnterBrowserFullscreen()
        {
            var host = BrowserView;
            if (host == null || _isBrowserFullscreen) return;
            try
            {
                if (_browserPopoutWindow is { } popout)
                {
                    _isBrowserFullscreen = true;
                    _browserFullscreenWasPopout = true;
                    _popoutPreFs = (popout.WindowDecorations, popout.CanResize, popout.WindowState, popout.Position,
                        popout.Width, popout.Height, popout.Topmost);
                    popout.WindowDecorations = WindowDecorations.None;
                    popout.CanResize = false;
                    popout.Topmost = true;
                    popout.WindowState = WindowState.FullScreen;
                    ArmFullscreenEscapes(popout);
                }
                else
                {
                    var page = BrowserPage;
                    var container = page?.FindControl<Grid>("BrowserContainer");
                    if (page == null || container == null) return;
                    _isBrowserFullscreen = true;
                    _browserFullscreenWasPopout = false;

                    var screen = Screens.ScreenFromWindow(this)?.Bounds ?? default;
                    using var reparent = host.BeginReparenting();
                    var fs = new Window
                    {
                        WindowDecorations = WindowDecorations.None,
                        CanResize = false,
                        ShowInTaskbar = false,
                        Topmost = true,
                        Background = Brushes.Black,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Position = new PixelPoint(screen.X + 100, screen.Y + 100),
                        Width = 400,
                        Height = 300,
                    };
                    _browserPopoutWindow = fs;
                    container.Children.Remove(host);
                    fs.Content = host;
                    fs.Closing += (_, _) =>
                    {
                        if (_isBrowserFullscreen)
                        {
                            _isBrowserFullscreen = false;
                            DisarmFullscreenEscapes();
                        }
                        using var returning = host.BeginReparenting();
                        fs.Content = null;
                        if (!container.Children.Contains(host)) container.Children.Add(host);
                        _browserPopoutWindow = null;
                        page.SyncBrowserPopout();
                    };
                    page.SyncBrowserPopout();
                    fs.Show();   // small first, then fullscreen, as WPF's video windows (:2952-2955)
                    fs.WindowState = WindowState.FullScreen;
                    ArmFullscreenEscapes(fs);
                }

                // The page's dblclick/fullscreenchange handlers fire even if HTML5 fullscreen was lost in the move.
                _ = host.InvokeScriptAsync("window._ccpForcedFs = true;");
                Log.Information("Browser entered fullscreen");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to enter browser fullscreen");
                ExitBrowserFullscreen();
            }
        }

        private void ExitBrowserFullscreen()
        {
            if (!_isBrowserFullscreen) return;
            // Off FIRST, and off the window they went on (#952): a user's pop-out must not keep re-claiming Topmost.
            DisarmFullscreenEscapes();
            try
            {
                _ = BrowserView?.InvokeScriptAsync(
                    "window._ccpForcedFs = false; try { if (document.exitFullscreen && document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");

                if (_browserPopoutWindow is { } window)
                {
                    if (_browserFullscreenWasPopout)
                    {
                        var pre = _popoutPreFs;
                        window.WindowState = WindowState.Normal;
                        window.WindowDecorations = pre.Decorations;
                        window.CanResize = pre.CanResize;
                        window.Topmost = pre.Topmost;
                        window.Position = pre.Position;
                        window.Width = pre.Width;
                        window.Height = pre.Height;
                        window.WindowState = pre.State;
                    }
                    else window.Close();   // its Closing handler returns the host to the card
                }

                _isBrowserFullscreen = false;
                Log.Information("Browser exited fullscreen");
            }
            catch (Exception ex) { Log.Error(ex, "Failed to exit browser fullscreen"); }
        }
    }
}
