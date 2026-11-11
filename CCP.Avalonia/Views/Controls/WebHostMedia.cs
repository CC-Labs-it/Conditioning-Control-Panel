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

        /// <summary>WPF AutoPlayAndFullscreenVideoAsync's script (MainWindow.Browser.cs:846): wait up to 10 s for the
        /// player to build its video, unmute, play, then request fullscreen (or on 'playing' when autoplay was blocked);
        /// the clip ending or a double-click leaves fullscreen. WPF's postMessage reports have no consumer here.</summary>
        internal const string TakeoverScript =
            "(async function(){window._ccpTakeoverStopped=false;" +
            "let v=document.querySelector('video');" +
            "for(let i=0;!v&&i<20;i++){await new Promise(r=>setTimeout(r,500));if(window._ccpTakeoverStopped)return 'stopped';v=document.querySelector('video');}" +
            "if(!v)return 'no-video';" +
            "const exit=()=>{if(document.exitFullscreen)document.exitFullscreen().catch(()=>{});else if(document.webkitExitFullscreen)document.webkitExitFullscreen();};" +
            "v.addEventListener('ended',exit,{once:true});" +
            "v.addEventListener('dblclick',e=>{if(document.fullscreenElement||document.webkitFullscreenElement){exit();e.preventDefault();e.stopPropagation();}});" +
            "const fs=()=>{if(window._ccpTakeoverStopped)return;if(v.requestFullscreen){const q=v.requestFullscreen();if(q&&q.catch)q.catch(()=>{});}else if(v.webkitRequestFullscreen)v.webkitRequestFullscreen();};" +
            "v.muted=false;v.play().then(fs).catch(()=>v.addEventListener('playing',fs,{once:true}));" +
            "return 'armed';})();";

        /// <summary>WPF EndWebVideoTakeover's script (MainWindow.Browser.cs:981): stop a pending takeover, pause, leave fullscreen.</summary>
        internal const string EndTakeoverScript =
            "window._ccpTakeoverStopped=true;" +
            "document.querySelectorAll('video,audio').forEach(v=>{try{v.pause();}catch(e){}});" +
            "if(document.fullscreenElement&&document.exitFullscreen)document.exitFullscreen().catch(()=>{});";

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
