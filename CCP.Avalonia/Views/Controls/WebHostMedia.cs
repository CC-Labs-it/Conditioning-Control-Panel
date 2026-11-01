using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>Native engine mute covers cross-origin frames (WPF BrowserService.cs:119-122).
    /// The script remains a fallback for adapters without a native mute API.</summary>
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
            if (await host.SetEngineMutedAsync(muted)) return true;
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
