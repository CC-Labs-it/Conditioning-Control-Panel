using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The WebHost media seam. Mute IMPORTED-AND-ADAPTED from trunk e3109afdf; playback shares the WPF script.
    /// WPF mutes the Home browser with CoreWebView2.IsMuted (MainWindow.Browser.cs:2603); NativeWebView has no engine
    /// mute on either adapter, so this rides the one channel WebHost does have, <see cref="WebHost.InvokeScriptAsync"/>,
    /// and reads its answer as a plain string (both adapters; no window.chrome.webview on the page side).
    ///
    /// <para>The script keeps a flag on the page, mutes every media element it can reach (the document and same-origin
    /// frames) and keeps new ones muted with a capturing play / volumechange listener and a MutationObserver. A new
    /// document has no flag, so the Home card re-runs it after every navigation. Limit, stated rather than hidden: a
    /// player inside a CROSS-ORIGIN iframe is out of reach; BambiCloud and HypnoTube play first-party video.</para>
    /// </summary>
    internal static class WebHostMedia
    {
        /// <summary>The script for a mute state. Idempotent: running it twice installs one observer.</summary>
        internal static string MuteScript(bool muted) =>
            "(function(m){try{" +
            "window.__ccpMuted=m;" +
            "var each=function(d){try{d.querySelectorAll('video,audio').forEach(function(e){e.muted=window.__ccpMuted;});" +
            "d.querySelectorAll('iframe').forEach(function(f){try{if(f.contentDocument)each(f.contentDocument);}catch(x){}});}catch(x){}};" +
            "each(document);" +
            "if(!window.__ccpMuteHooked){window.__ccpMuteHooked=true;" +
            "var keep=function(ev){var t=ev.target;if(t&&window.__ccpMuted&&t.muted===false){t.muted=true;}};" +
            "document.addEventListener('play',keep,true);" +
            "document.addEventListener('volumechange',keep,true);" +
            "try{new MutationObserver(function(){if(window.__ccpMuted)each(document);})" +
            ".observe(document.documentElement,{childList:true,subtree:true});}catch(x){}" +
            "}return m?'muted':'unmuted';}catch(e){return 'error';}})(" + (muted ? "true" : "false") + ");";

        /// <summary>Applies the mute state to the live page. False when there is no engine or the script did not run.
        /// The answer arrives JSON-quoted (WebView2) or raw (WebKitGTK); the quotes are trimmed to read both.</summary>
        internal static async Task<bool> SetMutedAsync(this WebHost host, bool muted)
        {
            if (!host.HasEngine) return false;
            var result = await host.InvokeScriptAsync(MuteScript(muted));
            return result?.Trim('"') == (muted ? "muted" : "unmuted");
        }

        /// <summary>WPF's live pause switch mutes, then pauses/resumes top-document media.
        /// It does not alter the saved Home mute preference, and resuming restores that preference rather than
        /// unmuting outright (WPF unmutes, leaving the header icon saying "muted" over audible sound).</summary>
        internal static async Task<bool> SetPausedAsync(this WebHost host, bool paused)
        {
            host.MediaPaused = paused;
            if (!host.HasEngine) return false;
            var muted = await host.SetMutedAsync(paused || CoreSettings.Current.BrowserVideoMuted);
            var result = await host.InvokeScriptAsync(Services.BrowserMediaPlaybackScript.ForPaused(paused));
            return muted && result?.Trim('"') == (paused ? "paused" : "playing");
        }

    }
}
