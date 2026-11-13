using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The WebHost media seam. Mute goes to the engine first (WPF CoreWebView2.IsMuted, BrowserService.cs:119-122;
    /// <see cref="Platform.WebViewAudio"/>), which covers cross-origin frames. Where the adapter has no native mute,
    /// the page script below is the fallback; it reaches the document and same-origin frames only, and the Home card
    /// re-runs it after every navigation. Playback shares the WPF script.
    /// </summary>
    internal static class WebHostMedia
    {
        /// <summary>WPF b9368d963 (MainWindow.xaml.cs PanicEndBrowserMedia, EnhancementPlayerWindow.PanicPauseAll): a
        /// panic pauses every page video/audio (playhead kept) and drops page fullscreen. Never latches the Workshop pause.</summary>
        internal const string PanicPauseScript = "window._ccpForcedFs = false; window._ccpTakeoverStopped = true;"
            + "(function(){var pause=function(d){try{"
            + "d.querySelectorAll('video,audio').forEach(function(e){e.pause();});"
            + "if(d.fullscreenElement)d.exitFullscreen();"
            + "d.querySelectorAll('iframe').forEach(function(f){try{if(f.contentDocument)pause(f.contentDocument);}catch(e){}});"
            + "}catch(e){}};pause(document);})();";

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
        /// The saved Home mute preference stays unchanged. Resume restores it by default; the tube menu
        /// passes false to unmute transiently, as WPF AvatarTubeWindow.ChatInput.cs:1407 does.</summary>
        internal static async Task<bool> SetPausedAsync(this WebHost host, bool paused, bool preserveSavedMute = true)
        {
            host.MediaPaused = paused;
            if (!host.HasEngine) return false;
            var muted = await host.SetMutedAsync(paused || (preserveSavedMute && CoreSettings.Current.BrowserVideoMuted));
            var result = await host.InvokeScriptAsync(Services.BrowserMediaPlaybackScript.ForPaused(paused));
            return muted && result?.Trim('"') == (paused ? "paused" : "playing");
        }

    }
}
