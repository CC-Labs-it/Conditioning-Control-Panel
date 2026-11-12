using System;
using System.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    private CancellationTokenSource? _zoomPending;
    private double? _zoom;
    private bool _zoomEnabled;
    private bool _zoomNeedsCompletion;
    private int _zoomGeneration;
    internal Action<double, CancellationToken>? ZoomInvoker { get; set; }
    internal double? BrowserZoom => _zoom;
    internal bool BrowserZoomReady { get; private set; }

    private void BrowserZoomNavigationCompleted()
    {
        if (_zoom != null) BrowserZoomReady = true;
        if (!_zoomEnabled || !_zoomNeedsCompletion) return;
        _zoomNeedsCompletion = false;
        ApplyBrowserZoom();
    }

    internal void SetBrowserZoom(double zoom)
    {
        _zoomGeneration++;
        _zoom = zoom;
        _zoomEnabled = true;
        _zoomNeedsCompletion = true;
        ApplyBrowserZoom();
    }

    internal void CancelBrowserZoom()
    {
        _zoomGeneration++;
        _zoomEnabled = false;
        _zoomNeedsCompletion = false;
        CancelPendingBrowserZoom();
    }

    internal void BrowserZoomAdapterDestroyed()
    {
        if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            var generation = Volatile.Read(ref _zoomGeneration);
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (generation == _zoomGeneration) BrowserZoomAdapterDestroyed();
            });
            return;
        }
        if (_zoomEnabled) _zoomNeedsCompletion = true;
        CancelPendingBrowserZoom();
    }

    private void CancelPendingBrowserZoom()
    {
        _zoomPending?.Cancel();
        _zoomPending?.Dispose();
        _zoomPending = null;
    }

    private void ApplyBrowserZoom()
    {
        CancelPendingBrowserZoom();
        if (!_zoomEnabled || _zoom is not { } zoom) return;
        _zoomPending = new CancellationTokenSource();
        if (ZoomInvoker is { } invoke) invoke(zoom, _zoomPending.Token);
        else
        {
            try { WebViewZoom.Set(_web?.TryGetPlatformHandle(), zoom, _zoomPending.Token); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "WebHost: zoom failed"); }
        }
    }
}
