// WPF MainWindow.Browser.cs:2814-2860, 2889-3135; BrowserService.cs:255,562.
using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF BrowserService.cs:255: exit a forced host on double-click even if reparenting
    // already ended the document's native fullscreen. Do not intercept normal media keys.
    internal const string BrowserFullscreenScript = @"(function(){
if(window.__ccpShellFs)return;window.__ccpShellFs=true;
function post(m){var w=window.chrome&&window.chrome.webview;if(w&&w.postMessage)w.postMessage(m);else if(window.invokeCSharpAction)window.invokeCSharpAction(m);}
function fs(){return document.fullscreenElement||document.webkitFullscreenElement;}
function change(){post(fs()?'ccp_fullscreen_on':'ccp_fullscreen_off');}
document.addEventListener('fullscreenchange',change);document.addEventListener('webkitfullscreenchange',change);
document.addEventListener('dblclick',function(e){if(window._ccpForcedFs){e.preventDefault();e.stopImmediatePropagation();post('ccp_exit_fullscreen');}},true);
window.addEventListener('keydown',function(e){if(window._ccpForcedFs&&(e.key==='Escape'||e.key==='F11')){e.preventDefault();post('ccp_exit_fullscreen');}},true);
})();";
    private Window? _browserFullscreenWindow;
    private Window? _browserFullscreenPopout;
    private DisplayMirror? _browserFullscreenMirror;
    private DispatcherTimer? _browserEffectTimer;
    internal Func<IEnumerable<Window>> BrowserEffectWindows = () => (global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Windows ?? Array.Empty<Window>();
    internal Action<Window> BrowserRaiseEffect = w => X11Overlay.Raise(w);
    private int _browserStopGeneration;
    private bool _browserStopPending, _browserEntering;
    private bool _browserFullscreenTransition, _browserFullscreenStopped, _browserFullscreenClosed, _browserFullscreenHostClosed, _browserFullscreenActive;
    internal Window? BrowserFullscreenWindow => _browserFullscreenWindow;

    internal void BrowserFullscreenNavigation()
    {
        if (_browserFullscreenClosed) return;
        _browserFullscreenStopped = false;
        if (BrowserView is { } browser)
        {
            _ = browser.InvokeScriptAsync(BrowserFullscreenScript);
            if (_browserFullscreenWindow != null) _ = browser.InvokeScriptAsync("window._ccpForcedFs = true;");
        }
    }

    internal void BrowserFullscreenMessage(string message)
    {
        if (_browserFullscreenClosed) return;
        message = message.Trim().Trim('"');
        if (message == "ccp_fullscreen_on")
        {
            // WPF Browser.cs:2819: remote browser playback is not ported on this head,
            // so no remote video can currently exempt the visible controller card.
            if (!_browserFullscreenStopped && Named<Border>("RemoteControlOverlay")?.IsVisible != true) EnterBrowserFullscreen();
            else if (BrowserView is { } browser) _ = browser.InvokeScriptAsync("try { document.exitFullscreen(); } catch (_) {}");
        }
        else if (message == "ccp_fullscreen_off" || message == "ccp_exit_fullscreen" ||
                 message.StartsWith("ccp_key:Escape:", StringComparison.Ordinal) || message.StartsWith("ccp_key:F11:", StringComparison.Ordinal))
            ExitBrowserFullscreen();
    }

    private void EnterBrowserFullscreen()
    {
        var browser = BrowserView;
        var container = BrowserPage?.FindControl<Grid>("BrowserContainer");
        if (_browserFullscreenTransition || _browserFullscreenWindow != null || browser == null || container == null) return;
        _browserFullscreenTransition = true;
        _browserEntering = true;
        bool entered = false;
        try
        {
            // WPF MainWindow.Browser.cs:2829-2836: only All selects multiple screens.
            var screens = ScreenList.Enumerate(this);
            var settings = CoreSettings.Current;
            var primary = screens.ToList().FindIndex(s => s.IsPrimary);
            var selected = Overlays.PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal,
                settings.GlobalTargetMonitor, settings.DualMonitorEnabled, screens.Count, primary);
            if (selected.Count() > 1)
            {
                var mirror = _browserFullscreenMirror ?? ScreenMirrorHead.For(this);
                if (mirror?.EnableMirror() == true) _browserFullscreenMirror = mirror;
            }
            var screen = Screens.ScreenFromWindow(_browserPopoutWindow ?? this)?.Bounds ?? default;
            var host = new Window { WindowDecorations = WindowDecorations.None, CanResize = false,
                ShowInTaskbar = false, Topmost = true, Background = Brushes.Black,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new global::Avalonia.PixelPoint(screen.X + 100, screen.Y + 100), Width = 400, Height = 300 };
            _browserFullscreenWindow = host;
            _browserFullscreenActive = true;
            _browserFullscreenHostClosed = false;
            host.Closed += (_, _) => { _browserFullscreenHostClosed = true; if (!_browserFullscreenTransition) ExitBrowserFullscreen(false); };
            _browserFullscreenPopout = _browserPopoutWindow;
            host.Closing += BrowserFullscreenClosing;
            host.Activated += BrowserFullscreenActivated;
            host.Deactivated += BrowserFullscreenDeactivated;
            host.AddHandler(KeyDownEvent, BrowserFullscreenKey, RoutingStrategies.Tunnel);
            using (browser.BeginReparenting())
            {
                if (_browserFullscreenPopout != null) _browserFullscreenPopout.Content = null;
                else container.Children.Remove(browser);
                host.Content = browser;
                host.Show();
                host.WindowState = WindowState.FullScreen;
            }
            _ = browser.InvokeScriptAsync("window._ccpForcedFs = true;");
            _browserEffectTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _browserEffectTimer.Tick -= BrowserEffectTimerTick;
            _browserEffectTimer.Tick += BrowserEffectTimerTick;
            _browserEffectTimer.Start();
            TickBrowserFullscreenEffects();
            entered = true;
        }
        catch (Exception ex) { Log.Warning(ex, "Browser fullscreen entry failed"); }
        finally { _browserFullscreenTransition = false; _browserEntering = false; }
        if (!entered || _browserStopPending) { _browserStopPending = false; ExitBrowserFullscreen(); }
    }

    private void BrowserEffectTimerTick(object? sender, EventArgs e) => TickBrowserFullscreenEffects();
    internal void TickBrowserFullscreenEffects()
    {
        if (_browserFullscreenActive) EnhancementPlayerWindow.ReassertFullscreenEffects(BrowserEffectWindows, BrowserRaiseEffect);
    }
    private void BrowserFullscreenActivated(object? sender, EventArgs e)
    {
        if (_browserFullscreenActive && sender == _browserFullscreenWindow) { ((Window)sender).Topmost = true; TickBrowserFullscreenEffects(); }
    }
    private void BrowserFullscreenDeactivated(object? sender, EventArgs e) { if (sender is Window w) w.Topmost = false; }
    private void BrowserFullscreenKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape && e.Key != Key.F11) return;
        e.Handled = true;
        ExitBrowserFullscreen();
    }
    private void BrowserFullscreenClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_browserFullscreenTransition)
        {
            ExitBrowserFullscreen(false);
            if (BrowserView?.Parent == null || BrowserView.Parent == sender) { e.Cancel = true; ((Window)sender!).Hide(); }
        }
    }
    internal void ExitBrowserFullscreen(bool closeHost = true)
    {
        if (_browserFullscreenTransition) return;
        _browserEffectTimer?.Stop();
        if (_browserFullscreenMirror?.DisableMirror() == true) _browserFullscreenMirror = null;
        var host = _browserFullscreenWindow;
        var browser = BrowserView;
        var container = BrowserPage?.FindControl<Grid>("BrowserContainer");
        if (_browserFullscreenTransition || host == null || browser == null || container == null) return;
        _browserFullscreenTransition = true;
        _browserFullscreenActive = false;
        try
        {
            host.Topmost = false;
            _ = browser.InvokeScriptAsync("window._ccpForcedFs = false; try { if(document.fullscreenElement) document.exitFullscreen(); } catch (_) {}");
            using (browser.BeginReparenting())
            {
                if (host.Content == browser) host.Content = null;
                if (browser.Parent == null)
                {
                    if (_browserFullscreenPopout != null && _browserFullscreenPopout == _browserPopoutWindow)
                        _browserFullscreenPopout.Content = browser;
                    else if (!container.Children.Contains(browser)) container.Children.Insert(0, browser);
                }
            }
            if (browser.Parent != null)
            {
                if (closeHost && !_browserFullscreenHostClosed) host.Close();
                if (closeHost && !_browserFullscreenHostClosed && host.IsVisible) host.Hide();
                if (_browserFullscreenHostClosed)
                {
                    host.Closing -= BrowserFullscreenClosing;
                    host.Activated -= BrowserFullscreenActivated;
                    host.Deactivated -= BrowserFullscreenDeactivated;
                    host.RemoveHandler(KeyDownEvent, BrowserFullscreenKey);
                    _browserFullscreenWindow = null;
                    _browserFullscreenPopout = null;
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Browser fullscreen return failed"); host.Topmost = false; host.Hide(); }
        finally { _browserFullscreenTransition = false; }
    }
    internal void StopBrowserFullscreen()
    {
        _browserFullscreenStopped = true;
        var generation = ++_browserStopGeneration;
        if (_browserFullscreenTransition) { if (_browserEntering) _browserStopPending = true; }
        else ExitBrowserFullscreen();
        // Drain late page events from this teardown without blocking the next user entry.
        Dispatcher.UIThread.Post(() => { if (generation == _browserStopGeneration) _browserFullscreenStopped = false; }, DispatcherPriority.Background);
    }
}
