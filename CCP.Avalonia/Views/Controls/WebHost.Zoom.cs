using System;
using System.Threading;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    private CancellationTokenSource? _zoomPending;
    private double? _zoom;
    private bool _zoomEnabled;
    private bool _zoomAdapterReady;
    private int _zoomGeneration;
    internal Action<double, CancellationToken>? ZoomInvoker { get; set; }
    internal double? BrowserZoom => _zoom;
    internal bool BrowserZoomReady { get; private set; }

    internal void BrowserZoomAdapterReady(IPlatformHandle? handle = null)
    {
        if (_zoomAdapterReady) return;
        _zoomAdapterReady = true;
        // The adapter may exist before the first user request.
        if (_zoom == null) return;
        BrowserZoomReady = true;
        if (_zoomEnabled) ApplyBrowserZoom(handle);
    }

    // NativeWebView.AdapterCreated on the UI thread: the event's own handle first, else ask the view.
    private void ZoomAdapterCreated(global::Avalonia.Controls.NativeWebView native, Func<IPlatformHandle?> eventHandle)
    {
        if (!ReferenceEquals(_web, native)) return;
        try
        {
            var handle = eventHandle();
            if (WebViewZoom.IsReady(handle)) BrowserZoomAdapterReady(handle);
            else TryBrowserZoomAdapterReady();
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "WebHost: zoom adapter readiness failed"); TryBrowserZoomAdapterReady(); }
    }

    private void TryBrowserZoomAdapterReady()
    {
        try { var handle = _web?.TryGetPlatformHandle(); if (WebViewZoom.IsReady(handle)) BrowserZoomAdapterReady(handle); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "WebHost: zoom readiness failed"); }
    }

    internal void SetBrowserZoom(double zoom)
    {
        _zoomGeneration++;
        _zoom = zoom;
        _zoomEnabled = true;
        if (_zoomAdapterReady)
        {
            ApplyBrowserZoom();
            BrowserZoomReady = _zoomAdapterReady;
        }
    }

    internal void CancelBrowserZoom()
    {
        _zoomGeneration++;
        _zoomEnabled = false;
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
        _zoomAdapterReady = false;
        BrowserZoomReady = false;
        CancelPendingBrowserZoom();
    }

    private void CancelPendingBrowserZoom()
    {
        _zoomPending?.Cancel();
        _zoomPending?.Dispose();
        _zoomPending = null;
    }

    private void ApplyBrowserZoom(IPlatformHandle? handle = null)
    {
        CancelPendingBrowserZoom();
        if (!_zoomEnabled || _zoom is not { } zoom) return;
        try
        {
            if (ZoomInvoker is null && _web is not null)
            {
                handle ??= _web.TryGetPlatformHandle();
                if (!WebViewZoom.IsReady(handle))
                {
                    _zoomAdapterReady = false;
                    BrowserZoomReady = false;
                    return;
                }
            }
            _zoomPending = new CancellationTokenSource();
            if (ZoomInvoker is { } invoke) invoke(zoom, _zoomPending.Token);
            else WebViewZoom.Set(handle, zoom, _zoomPending.Token);
        }
        catch (Exception ex)
        {
            _zoomAdapterReady = false;
            BrowserZoomReady = false;
            Serilog.Log.Debug(ex, "WebHost: zoom failed");
        }
    }
}
