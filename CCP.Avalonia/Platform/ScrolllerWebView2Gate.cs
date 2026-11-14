using System;
using ConditioningControlPanel.Services.Fyp.Online;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The Scrolller gate on this head's Windows web views: WPF's own <see cref="ScrolllerWebGate"/> (moved to Core, parity
/// with WPF 7.1.14), hooked on the CoreWebView2 behind every <see cref="Views.Controls.WebHost"/>. Same behaviour as
/// WPF: honest user-agent and our Referer, a local 403 while the switch is off or the share is spent, cached media answered
/// from the device cache, pictures and GIFs fetched whole by the gate's own client (no remote-media consent needed, served
/// even when the cache cannot take the copy), clips and ranged requests passed to the network and charged by the REAL
/// bytes that arrive (DevTools Network domain), and the core's disk cache cleared under the day limit and on the off
/// switch. Avalonia.Controls.WebView exposes no response seam, so the core comes from the adapter's platform handle
/// (<c>IWindowsWebView2PlatformHandle.CoreWebView2</c>) through the WebView2 SDK's managed wrapper. UI thread only.
/// </summary>
internal static class ScrolllerWebView2Gate
{
    // WPF BrowserService.cs:665-675 reads the live core, not the control's cached Source.
    public static string? CurrentDocumentUrl(IntPtr core) => core == IntPtr.Zero ? null
        : CoreWebView2.CreateFromComICoreWebView2(core).Source;

    // WPF WebAudioWatch (#1540 / #1549): a page that starts playing audio names itself in the recent-sounds ring.
    private static readonly WebAudioTracker Audio = new(
        ConditioningControlPanel.Services.Audio.RecentSounds.Shared, () => CoreEngine.IsRunning || CoreSession.IsSessionRunning);   // WPF EngineOffAudioLog.EngineRunning: engine or session

    internal static void WatchAudio(CoreWebView2 core) => Audio.Attach(core,
        changed => core.IsDocumentPlayingAudioChanged += (_, _) => changed(),
        () => (core.IsDocumentPlayingAudio, core.Source, core.IsMuted));

    // WPF BrowserService.cs:580-606: answer matching requests with an empty 204.
    public static bool AttachAds(IntPtr pointer, Func<string?, string?, bool> shouldBlockAds)
    {
        try
        {
            var core = CoreWebView2.CreateFromComICoreWebView2(pointer);
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                try
                {
                    if (shouldBlockAds(core.Source, e.Request.Uri))
                        e.Response = core.Environment.CreateWebResourceResponse(null, 204, "No Content", "");
                }
                catch (Exception ex) { Log.Debug("Browser ad request check failed: {E}", ex.Message); }
            };
            return true;
        }
        catch (Exception ex) { Log.Warning("Browser ad filter attach failed: {E}", ex.Message); return false; }
    }

    /// <summary>Hooks one raw ICoreWebView2. False when there is none (the caller retries until the core exists).</summary>
    public static bool Attach(IntPtr coreWebView2)
    {
        if (coreWebView2 == IntPtr.Zero)
        {
            Log.Warning("ScrolllerWebView2Gate: the web view has no CoreWebView2 yet; the gate waits for it");
            return false;
        }
        try
        {
            ScrolllerWebGate.AttachHook = WatchAudio;
            ScrolllerWebGate.Attach(CoreWebView2.CreateFromComICoreWebView2(coreWebView2));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning("ScrolllerWebView2Gate: attach failed: {E}", ex.Message);
            return false;
        }
    }
}
