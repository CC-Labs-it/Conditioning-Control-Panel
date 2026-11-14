// WPF MainWindow.Browser.cs:2814-2859, 2889-2936, 2953-2990, 3081-3136.
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Controls;

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

    // A page may report exit, but only the native engine signal can enter fullscreen.
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

    private const string BrowserFullscreenCleanupScript = "try { var f=window.__ccpBrowserFsExit; if(f){ document.removeEventListener('fullscreenchange',f.changed); document.removeEventListener('webkitfullscreenchange',f.changed); window.removeEventListener('dblclick',f.dbl,true); document.removeEventListener('dblclick',f.dbl,true); delete window.__ccpBrowserFsExit; } if (document.fullscreenElement) document.exitFullscreen(); else if (document.webkitFullscreenElement) document.webkitExitFullscreen(); } catch (_) {}";

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
        _browserFullscreenHost.NativeFullscreenEntered += EnterBrowserPopoutFullscreen;
        _browserFullscreenHost.NativeFullscreenExited += ExitBrowserPopoutFullscreen;
        _browserFullscreenHost.NavigationBeginning += BrowserFullscreenNavigationStarted;
        _browserFullscreenHost.NavigationCompleted += BrowserFullscreenNavigation;
        _browserFullscreenHost.WebMessage += BrowserFullscreenMessage;
    }

    private void BrowserFullscreenNavigationStarted()
    {
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

    private void EnterBrowserPopoutFullscreen()
    {
        var window = _browserPopoutWindow;
        if (window == null || _browserFullscreenWindow != null) return; // embedded takeover is part B
        _browserFullscreenWindow = window;
        _browserFullscreenSession++;
        if (_browserFullscreenHost is { } host) _ = InstallBrowserFullscreenExitScriptAsync(host);
        _browserPreviousState = window.WindowState;
        _browserPreviousDecorations = window.WindowDecorations;
        _browserPreviousTopmost = window.Topmost;
        _browserPreviousCanResize = window.CanResize;
        window.Activated += BrowserFullscreenActivated;
        window.Deactivated += BrowserFullscreenDeactivated;
        window.AddHandler(KeyDownEvent, BrowserFullscreenKeyDown, RoutingStrategies.Tunnel);
        try
        {
            window.WindowDecorations = WindowDecorations.None;
            window.CanResize = false;
            window.Topmost = true;
            window.WindowState = WindowState.FullScreen;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Browser fullscreen entry failed");
            ExitBrowserPopoutFullscreen();
        }
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
        var window = _browserFullscreenWindow;
        if (window == null) return;
        _browserFullscreenWindow = null;
        window.Activated -= BrowserFullscreenActivated;
        window.Deactivated -= BrowserFullscreenDeactivated;
        window.RemoveHandler(KeyDownEvent, BrowserFullscreenKeyDown);
        _ = _browserFullscreenHost?.InvokeScriptAsync(BrowserFullscreenCleanupScript);
        window.WindowState = _browserPreviousState;
        window.WindowDecorations = _browserPreviousDecorations;
        window.CanResize = _browserPreviousCanResize;
        window.Topmost = _browserPreviousTopmost;
    }

    private void DisposeBrowserFullscreen()
    {
        ExitBrowserPopoutFullscreen();
        if (_browserFullscreenHost is not { } host) return;
        host.NativeFullscreenEntered -= EnterBrowserPopoutFullscreen;
        host.NativeFullscreenExited -= ExitBrowserPopoutFullscreen;
        host.NavigationBeginning -= BrowserFullscreenNavigationStarted;
        host.NavigationCompleted -= BrowserFullscreenNavigation;
        host.WebMessage -= BrowserFullscreenMessage;
        _browserFullscreenHost = null;
    }
}
