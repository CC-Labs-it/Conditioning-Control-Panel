using System;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>
/// The transport seam under <see cref="PrizeCounterHostService"/>: whatever shows the counter page.
/// The service keeps the relay, the identity pin, the init frame and every page message; a surface
/// only carries frames both ways and shows or takes itself down. Two of them: the counter WINDOW
/// (<see cref="WindowCounterSurface"/>, the game doors) and the SHEET inside the launcher or the
/// panel (<see cref="SheetCounterSurface"/>, the wallet doors).
/// </summary>
internal interface ICounterSurface
{
    /// <summary>The window a dialog over the counter is owned by (the page's sign-in).</summary>
    Window? Window { get; }

    /// <summary>The page has said <c>ready</c> to this counter.</summary>
    bool IsReady { get; }

    /// <summary>The player is looking at it: its window is the foreground window.</summary>
    bool IsForeground { get; }

    /// <summary>Load the page and hand it to these callbacks (a fresh page answers <c>ready</c> again).</summary>
    void NavigatePage(string url, Action ready, Action<JObject> message);

    /// <summary>A frame to the page; queued until its <c>ready</c>.</summary>
    void Post(object msg);

    /// <summary>Keyboard focus to the page (and its window forward).</summary>
    void FocusWeb();

    /// <summary>Take it down. <paramref name="animate"/> false cuts at once (panic, app exit, a dead page).</summary>
    void Close(bool animate);
}

/// <summary>The counter window: a <see cref="ChaosWebViewHost"/>, exactly as the game doors always had it.</summary>
internal sealed class WindowCounterSurface : ICounterSurface
{
    private readonly ChaosWebViewHost _host;

    public WindowCounterSurface(ChaosWebViewHost host) => _host = host;

    public Window? Window => _host.Window;
    public bool IsReady => _host.IsReady;
    public bool IsForeground => _host.IsForeground;
    public void NavigatePage(string url, Action ready, Action<JObject> message) => _host.NavigatePage(url, ready, message);
    public void Post(object msg) => _host.Post(msg);
    public void FocusWeb() => _host.FocusWeb();
    public void Close(bool animate) => _host.Dispose();
}
