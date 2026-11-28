using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Browser;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private WebHost? _takeoverRetryHost;
    private int _takeoverRetryGeneration;
    private bool _takeoverAsked, _takeoverRetried;
    private long _takeoverAskUntil;
    internal Func<Task> TakeoverFocusDelay = () => Task.Delay(TakeoverFullscreenRetry.FocusSettleMs);
    internal Action<WebHost>? TakeoverFocusForTest;

    internal void ArmTakeoverGestureRetry(WebHost host)
    {
        CancelTakeoverGestureRetry();
        _takeoverRetryHost = host;
    }

    internal void CancelTakeoverGestureRetry()
    {
        ++_takeoverRetryGeneration;
        _takeoverRetryHost = null;
        _takeoverAsked = _takeoverRetried = false;
    }

    private void NoteTakeoverFullscreenAsk()
    {
        if (_takeoverRetryHost == null) return;
        ++_takeoverRetryGeneration;
        _takeoverAsked = true;
        _takeoverRetried = false;
        _takeoverAskUntil = Environment.TickCount64 + 5000; // WPF BrowserFullscreenGate.TakeoverAskMs
    }

    private void HandleTakeoverFullscreenDenied(string message)
    {
        if (!_takeoverAsked || Environment.TickCount64 > _takeoverAskUntil || _takeoverRetryHost is not { } host ||
            !TakeoverFullscreenRetry.ShouldRetryWithGesture(message, _takeoverRetried)) return;
        _takeoverRetried = true;
        _ = RetryTakeoverFullscreenAsync(host, _takeoverRetryGeneration);
    }

    private async Task RetryTakeoverFullscreenAsync(WebHost host, int generation)
    {
        try
        {
            bool gesture = host.CanRetryFullscreenGesture;
            if (!gesture && !host.CanUseTakeoverFullscreenFallback) { Log.Debug("Takeover fullscreen retry unavailable"); return; }
            if (TakeoverFocusForTest is { } focus) focus(host);
            else
            {
                if (TopLevel.GetTopLevel(host) is Window window &&
                    TakeoverFullscreenRetry.ShouldRaiseHost(window.IsVisible, window.WindowState == WindowState.Minimized, window.IsActive))
                {
                    if (OperatingSystem.IsWindows()) Platform.Win32Windows.FocusWindow(window);
                    else window.Activate();
                }
                host.FocusPage();
            }
            await TakeoverFocusDelay();
            if (generation != _takeoverRetryGeneration || !ReferenceEquals(host, _takeoverRetryHost) ||
                !_takeoverAsked || Environment.TickCount64 > _takeoverAskUntil) return;
            if (gesture)
            {
                await host.RetryFullscreenWithGesture(TakeoverFullscreenRetry.EvaluateParams(WebHostTakeover.FullscreenRecheckScript));
                return;
            }
            if (_browserFullscreenWindow != null || !ReferenceEquals(host, _browserFullscreenHost)) return;
            var result = await host.InvokeScriptAsync(WebHostTakeover.FullscreenFallbackScript);
            if (!string.Equals(result?.Trim().Trim('"'), "true", StringComparison.OrdinalIgnoreCase)) return;
            if (generation != _takeoverRetryGeneration || !ReferenceEquals(host, _takeoverRetryHost) ||
                !_takeoverAsked || Environment.TickCount64 > _takeoverAskUntil || _browserFullscreenWindow != null)
            {
                await host.InvokeScriptAsync(WebHostTakeover.FullscreenFallbackCleanupScript);
                return;
            }
            EnterBrowserFullscreen();
            if (_browserFullscreenWindow == null) await host.InvokeScriptAsync(WebHostTakeover.FullscreenFallbackCleanupScript);
        }
        catch (Exception ex) { Log.Debug("Takeover fullscreen gesture retry failed: {Error}", ex.Message); }
    }
}
