using System;
using System.Globalization;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Deeper;

// Adapted from parity/trunk d8a24e93a9e17eae95e075bd2c8254caeb3a1a7f.
// The editor owns the WPF-style reparent; this bridge carries page state and zoom.
internal sealed class DeeperPageBridge : IDisposable
{
    internal const string ZoomIn = "ccp_zoom_in", ZoomOut = "ccp_zoom_out";
    internal const string FullscreenOn = "ccp_fullscreen_on", FullscreenOff = "ccp_fullscreen_off";
    internal const string ExitFullscreen = "ccp_exit_fullscreen";
    private readonly WebHost _host;
    private readonly Action<bool> _fullscreen;
    private bool _disposed;
    internal Func<string, Task<string?>> Invoke { get; set; }
    internal double Zoom { get; private set; } = 1;
    internal bool PageFullscreen { get; private set; }

    internal DeeperPageBridge(WebHost host, Action<bool> fullscreen)
    {
        _host = host;
        _fullscreen = fullscreen;
        Invoke = host.InvokeScriptAsync;
        host.NavigationCompleted += OnNavigationCompleted;
        host.WebMessage += OnWebMessage;
    }

    internal static double Next(double zoom, double delta) => Math.Clamp(Math.Round(zoom + delta, 2), 0.25, 5);

    internal void Adjust(double delta)
    {
        if (_disposed) return;
        Zoom = Next(Zoom, delta);
        _ = Invoke(Script(Zoom));
    }

    // ponytail: no document-created hook or native zoom. Reapply CSS zoom and install listeners
    // after each navigation. WPF's native dblclick exit (no click-pair detector, WPF :576) also
    // survives lost HTML5 state.
    internal static string Script(double zoom) =>
        "(function(){document.documentElement.style.zoom='" + zoom.ToString("0.##", CultureInfo.InvariantCulture) + "';"
        + "if(window.__ccpDeeperBridge)return;window.__ccpDeeperBridge=1;"
        + "var post=function(m){try{window.invokeCSharpAction(m);}catch(e){}};"
        + "window.addEventListener('wheel',function(e){if(!e.ctrlKey||!e.deltaY)return;e.preventDefault();"
        + "post(e.deltaY<0?'" + ZoomIn + "':'" + ZoomOut + "');},{passive:false,capture:true});"
        + "var fs=function(){post((document.fullscreenElement||document.webkitFullscreenElement)?'" + FullscreenOn + "':'" + FullscreenOff + "');};"
        + "document.addEventListener('fullscreenchange',fs);document.addEventListener('webkitfullscreenchange',fs);"
        + "var exit=function(){if(!document.fullscreenElement&&!document.webkitFullscreenElement&&!window._ccpForcedFs)return;"
        + "try{if(document.exitFullscreen)document.exitFullscreen();else if(document.webkitExitFullscreen)document.webkitExitFullscreen();}catch(e){}"
        + "post('" + ExitFullscreen + "');};"
        + "document.addEventListener('dblclick',exit,true);"
        + "document.addEventListener('keydown',function(e){if(e.key==='Escape'||e.key==='F11')exit();},true);fs();})()";

    internal const string ExitScript = "window._ccpForcedFs=false;try{if(document.fullscreenElement)document.exitFullscreen();"
        + "else if(document.webkitFullscreenElement)document.webkitExitFullscreen();}catch(e){}";

    private void OnNavigationCompleted(Uri url)
    {
        if (_disposed) return;
        SetFullscreen(false);
        if (url.Scheme != "about") _ = Invoke(Script(Zoom));
    }

    private void OnWebMessage(string body)
    {
        if (_disposed) return;
        switch (DeeperPreview.Unquote(body))
        {
            case ZoomIn: Adjust(+0.10); break;
            case ZoomOut: Adjust(-0.10); break;
            case FullscreenOn: SetFullscreen(true); break;
            case FullscreenOff: SetFullscreen(false); break;
            case ExitFullscreen: LeaveFullscreen(); break;
        }
    }

    private void SetFullscreen(bool on)
    {
        if (on == PageFullscreen) return;
        PageFullscreen = on;
        _fullscreen(on);
    }

    internal void LeaveFullscreen()
    {
        if (_disposed) return;
        _ = Invoke(ExitScript);
        // Repair a partial enter/exit independently of the page flag (WPF's forced exit).
        PageFullscreen = false;
        _fullscreen(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        LeaveFullscreen();
        _disposed = true;
        _host.NavigationCompleted -= OnNavigationCompleted;
        _host.WebMessage -= OnWebMessage;
    }
}
