// WPF MainWindow.Browser.cs:2814-2859, 2889-2936, 2953-3136.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private Window? _browserFullscreenWindow;
    private WindowState _browserPreviousState;
    private WindowDecorations _browserPreviousDecorations;
    private bool _browserPreviousTopmost, _browserPreviousCanResize;
    private WebHost? _browserFullscreenHost;
    private long _browserFullscreenSession;
    private long? _browserNavigationFullscreenSession;
    // Embedded mode (WPF :2995-3055): the page moves into a window CCP owns and closes again on exit.
    private Window? _browserTakeoverWindow;
    internal Window? BrowserTakeoverWindow => _browserTakeoverWindow;
    private bool _browserTakeoverClosing;
    private double? _browserPreFullscreenZoom;
    private DisplayMirror? _browserMirror;

    // A page may report exit; native signals and the guarded takeover fallback can enter.
    private const string BrowserFullscreenExitScript = @"(function(){
        if(window.__ccpBrowserFsExit)return;
        function changed(){if(document.fullscreenElement||document.webkitFullscreenElement)return;
            var w=window.chrome&&window.chrome.webview;
            if(w&&w.postMessage)w.postMessage('ccp_browser_fullscreen_off');
            else if(window.invokeCSharpAction)window.invokeCSharpAction('ccp_browser_fullscreen_off');}
        function dbl(e){e.preventDefault();e.stopImmediatePropagation();
            var w=window.chrome&&window.chrome.webview;
            if(w&&w.postMessage)w.postMessage('ccp_browser_fullscreen_off');
            else if(window.invokeCSharpAction)window.invokeCSharpAction('ccp_browser_fullscreen_off');}
        window.__ccpBrowserFsExit={changed:changed,dbl:dbl};
        document.addEventListener('fullscreenchange',changed);
        document.addEventListener('webkitfullscreenchange',changed);
        window.addEventListener('dblclick',dbl,true);
        document.addEventListener('dblclick',dbl,true);
    })();";

    private const string BrowserFullscreenCleanupScript = "try { if (window.__ccpTakeoverFallbackExit) window.__ccpTakeoverFallbackExit(); var f=window.__ccpBrowserFsExit; if(f){ document.removeEventListener('fullscreenchange',f.changed); document.removeEventListener('webkitfullscreenchange',f.changed); window.removeEventListener('dblclick',f.dbl,true); document.removeEventListener('dblclick',f.dbl,true); delete window.__ccpBrowserFsExit; } if (document.fullscreenElement) document.exitFullscreen(); else if (document.webkitFullscreenElement) document.webkitExitFullscreen(); } catch (_) {}";

    // An engine may complete a queued install after panic, native exit, or navigation.
    private async Task InstallBrowserFullscreenExitScriptAsync(WebHost host)
    {
        await host.InvokeScriptAsync(BrowserFullscreenExitScript);
        if (_browserFullscreenWindow == null) await host.InvokeScriptAsync(BrowserFullscreenCleanupScript);
    }

    private void InitializeBrowserFullscreen()
    {
        _browserFullscreenHost = BrowserView;
        if (_browserFullscreenHost == null) return;
        _browserFullscreenHost.NativeFullscreenEntered += EnterBrowserFullscreen;
        _browserFullscreenHost.NativeFullscreenExited += ExitBrowserPopoutFullscreen;
        _browserFullscreenHost.NavigationBeginning += BrowserFullscreenNavigationStarted;
        _browserFullscreenHost.NavigationCompleted += BrowserFullscreenNavigation;
        _browserFullscreenHost.WebMessage += BrowserFullscreenMessage;
    }

    private void BrowserFullscreenNavigationStarted()
    {
        CancelTakeoverGestureRetry();
        _browserNavigationFullscreenSession = _browserFullscreenWindow == null ? null : _browserFullscreenSession;
        ExitBrowserPopoutFullscreen();
    }

    private void BrowserFullscreenNavigation(Uri url)
    {
        var session = _browserNavigationFullscreenSession;
        _browserNavigationFullscreenSession = null;
        // A later fullscreen entry belongs to the loading page, not the navigation being completed.
        if (session != null && session == _browserFullscreenSession) ExitBrowserPopoutFullscreen();
    }
    private void BrowserFullscreenMessage(string message)
    {
        if (message.Trim().Trim('"') == "ccp_browser_fullscreen_off") ExitBrowserPopoutFullscreen();
    }

    private void EnterBrowserFullscreen()
    {
        if (_browserFullscreenWindow != null || _browserFullscreenHost is not { } host) return;
        CancelTakeoverGestureRetry();
        // WPF :2821-2828 RemoteBrowserStopRule.HideBrowser: the remote card refuses page fullscreen unless the
        // page is the controller's own video (#4423).
        if (Named<Border>("RemoteControlOverlay")?.IsVisible == true && !_remoteBrowserVideoActive)
        {
            _ = host.InvokeScriptAsync(BrowserFullscreenCleanupScript);
            return;
        }
        var window = _browserPopoutWindow;
        var page = BrowserPage;
        if (window == null)
        {
            if (page == null || !page.BrowserContainer.Children.Contains(host)) return;
            var screen = Screens.ScreenFromWindow(this)?.Bounds ?? default;
            // WPF :3011-3024: borderless, topmost, off the taskbar, shown small and then sized to the screen.
            window = new Window
            {
                WindowDecorations = WindowDecorations.None, CanResize = false, ShowInTaskbar = false, Topmost = true,
                Background = Brushes.Black, WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new global::Avalonia.PixelPoint(screen.X + 100, screen.Y + 100), Width = 400, Height = 300,
            };
            _browserTakeoverWindow = window;
            window.Closing += BrowserTakeoverClosing;
        }
        _browserFullscreenWindow = window;
        _browserFullscreenSession++;
        _ = InstallBrowserFullscreenExitScriptAsync(host);
        _browserPreviousState = window.WindowState;
        _browserPreviousDecorations = window.WindowDecorations;
        _browserPreviousTopmost = window.Topmost;
        _browserPreviousCanResize = window.CanResize;
        window.Activated += BrowserFullscreenActivated;
        window.Deactivated += BrowserFullscreenDeactivated;
        window.AddHandler(KeyDownEvent, BrowserFullscreenKeyDown, RoutingStrategies.Tunnel);
        try
        {
            // WPF :2831-2842: clone the screens only when "Show content on" covers more than one.
            if (GlobalScreenCount() > 1 && _browserMirror == null && ScreenMirrorHead.For(this) is { } mirror && mirror.EnableMirror())
                _browserMirror = mirror;
            // WPF :2963-2964: the page runs at 100 % while it fills the screen.
            _browserPreFullscreenZoom = host.BrowserZoom;
            if (_browserPreFullscreenZoom != null) host.SetBrowserZoom(1.0);
            if (window == _browserTakeoverWindow && page != null)
            {
                using (host.BeginReparenting())
                {
                    page.BrowserContainer.Children.Remove(host);
                    window.Content = host;
                    window.Show();
                }
                page.SyncBrowserPrompt();
            }
            else
            {
                window.WindowDecorations = WindowDecorations.None;
                window.CanResize = false;
                window.Topmost = true;
            }
            window.WindowState = WindowState.FullScreen;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Browser fullscreen entry failed");
            ExitBrowserPopoutFullscreen();
        }
    }

    // WPF App.GetGlobalScreens (:2833): the screens the "Show content on" pick covers.
    private int GlobalScreenCount()
    {
        var screens = ScreenList.Enumerate(this);
        var s = CoreSettings.Current;
        return Overlays.PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor,
            s.DualMonitorEnabled, screens.Count, screens.ToList().FindIndex(x => x.IsPrimary)).Length;
    }

    // Alt+F4 or the OS closing the takeover window is an exit too (WPF :3029-3048).
    private void BrowserTakeoverClosing(object? sender, WindowClosingEventArgs e)
    {
        if (sender != _browserTakeoverWindow) return;
        _browserTakeoverClosing = true;
        try { ExitBrowserPopoutFullscreen(); }
        finally { _browserTakeoverClosing = false; }
    }

    private void BrowserFullscreenActivated(object? sender, EventArgs e)
    {
        if (sender == _browserFullscreenWindow) _browserFullscreenWindow.Topmost = true;
    }
    private void BrowserFullscreenDeactivated(object? sender, EventArgs e)
    {
        if (sender == _browserFullscreenWindow) _browserFullscreenWindow.Topmost = false;
    }
    private void BrowserFullscreenKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape && e.Key != Key.F11) return;
        e.Handled = true;
        ExitBrowserPopoutFullscreen();
    }

    internal void ExitBrowserPopoutFullscreen()
    {
        // WPF :3085-3089: mirroring goes first, and a failed restore is retried by the next stop.
        if (_browserMirror is { } mirror && mirror.DisableMirror()) _browserMirror = null;
        var window = _browserFullscreenWindow;
        if (window == null) return;
        _browserFullscreenWindow = null;
        window.Activated -= BrowserFullscreenActivated;
        window.Deactivated -= BrowserFullscreenDeactivated;
        window.RemoveHandler(KeyDownEvent, BrowserFullscreenKeyDown);
        var host = _browserFullscreenHost;
        _ = host?.InvokeScriptAsync(BrowserFullscreenCleanupScript);
        if (_browserPreFullscreenZoom is { } zoom) host?.SetBrowserZoom(zoom);
        _browserPreFullscreenZoom = null;
        if (window == _browserTakeoverWindow)
        {
            _browserTakeoverWindow = null;
            window.Closing -= BrowserTakeoverClosing;
            window.Topmost = false;
            if (BrowserPage is { } page && host != null)
            {
                using (host.BeginReparenting())
                {
                    window.Content = null;
                    if (!page.BrowserContainer.Children.Contains(host)) page.BrowserContainer.Children.Add(host);
                }
                page.SyncBrowserPrompt();
            }
            if (!_browserTakeoverClosing) window.Close();
            return;
        }
        window.WindowState = _browserPreviousState;
        window.WindowDecorations = _browserPreviousDecorations;
        window.CanResize = _browserPreviousCanResize;
        window.Topmost = _browserPreviousTopmost;
    }

    private void DisposeBrowserFullscreen()
    {
        CancelTakeoverGestureRetry();
        ExitBrowserPopoutFullscreen();
        if (_browserFullscreenHost is not { } host) return;
        host.NativeFullscreenEntered -= EnterBrowserFullscreen;
        host.NativeFullscreenExited -= ExitBrowserPopoutFullscreen;
        host.NavigationBeginning -= BrowserFullscreenNavigationStarted;
        host.NavigationCompleted -= BrowserFullscreenNavigation;
        host.WebMessage -= BrowserFullscreenMessage;
        _browserFullscreenHost = null;
    }
}
