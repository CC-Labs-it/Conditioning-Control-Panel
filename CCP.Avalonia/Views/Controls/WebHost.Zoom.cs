using System;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    private IPlatformHandle? _zoomHandle;
    private bool _zoomSet;
    internal Action<double>? ZoomInvoker { get; set; }
    internal double Zoom { get; private set; } = 1.0;

    internal void SetZoom(double zoom)
    {
        Zoom = zoom;
        _zoomSet = true;
        ApplyZoom();
    }

    internal void ZoomEngineCreated(IPlatformHandle? handle)
    {
        _zoomHandle = handle;
        if (_zoomSet) ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (ZoomInvoker is { } invoke) invoke(Zoom);
        else if (_zoomHandle is IGtkWebViewPlatformHandle gtk) WebViewZoom.SetGtk(gtk.WebKitWebView, Zoom);
    }

    private void AttachZoom(NativeWebView web)
    {
        static void OnUi(Action action) { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); }
        web.AdapterDestroyed += (_, _) => OnUi(() => _zoomHandle = null);
        web.AdapterCreated += (_, e) =>
        {
            IPlatformHandle? handle = null;
            try { handle = e.TryGetPlatformHandle() ?? web.TryGetPlatformHandle(); } catch { }
            OnUi(() => ZoomEngineCreated(handle));
        };
    }
}
