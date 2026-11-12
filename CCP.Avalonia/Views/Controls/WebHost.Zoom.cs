using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    private IPlatformHandle? _zoomHandle;
    private bool _zoomSet;
    private int _zoomGeneration;
    internal Action<double>? ZoomInvoker { get; set; }
    internal Func<Task<double?>>? ZoomReader { get; set; }
    internal double Zoom { get; private set; } = 1.0;

    internal void SetZoom(double zoom)
    {
        Zoom = zoom;
        _zoomSet = true;
        ApplyZoom();
    }

    internal async Task<double> ReadZoomAsync()
    {
        try
        {
            var value = ZoomReader != null ? await ZoomReader() : await WebViewZoom.Read(_zoomHandle).WaitAsync(TimeSpan.FromSeconds(2));
            return value is > 0 ? value.Value : Zoom;
        }
        catch (Exception) { return Zoom; }
    }

    internal void ZoomEngineCreated(IPlatformHandle? handle)
    {
        _zoomHandle = handle;
        if (_zoomSet) ApplyZoom();
    }

    private void ApplyZoom()
    {
        if (ZoomInvoker is { } invoke) invoke(Zoom);
        else WebViewZoom.Set(_zoomHandle, Zoom);
    }

    private void AttachZoom(NativeWebView web)
    {
        static void OnUi(Action action) { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); }
        web.AdapterDestroyed += (_, _) => OnUi(() => { _zoomGeneration++; _zoomHandle = null; });
        web.AdapterCreated += (_, e) =>
        {
            IPlatformHandle? handle = null;
            try { handle = e.TryGetPlatformHandle() ?? web.TryGetPlatformHandle(); } catch { }
            OnUi(() =>
            {
                var generation = ++_zoomGeneration;
                ZoomEngineCreated(handle);
                if (OperatingSystem.IsWindows() && (handle is not IWindowsWebView2PlatformHandle windows || windows.CoreWebView2Controller == IntPtr.Zero))
                    RetryController(web, generation, 50);
            });
        };
    }

    private void RetryController(NativeWebView web, int generation, int attempts)
    {
        if (attempts <= 0) return;
        DispatcherTimer.RunOnce(() =>
        {
            if (generation != _zoomGeneration) return;
            IPlatformHandle? handle = null;
            try { handle = web.TryGetPlatformHandle(); } catch { }
            if (handle is IWindowsWebView2PlatformHandle windows && windows.CoreWebView2Controller != IntPtr.Zero)
                ZoomEngineCreated(handle);
            else RetryController(web, generation, attempts - 1);
        }, TimeSpan.FromMilliseconds(100));
    }
}
