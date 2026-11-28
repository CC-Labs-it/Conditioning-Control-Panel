// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Browser.cs:1089-1240 (OnBrowserWebMessageReceived,
// HandleBrowserMediaMessage) and the always-on media reporter of Services/Browser/BrowserService.cs:337-460.
// The session itself is Core BrowserMediaSession. The reporter is injected after each completed Home navigation
// (WebHost has no general document-created script); its bound flag keeps a second injection harmless.

using System.Text.Json;
using ConditioningControlPanel.Services.Browser;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BrowserService.cs:343-460, posting through invokeCSharpAction where WebKitGTK provides it.</summary>
        internal const string BrowserMediaReporterScript = """
            (function() {
                if (window.__ccpMediaBound) return;
                window.__ccpMediaBound = true;
                function post(o) {
                    try {
                        var s = JSON.stringify(o);
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(s);
                        else window.chrome.webview.postMessage(s);
                    } catch (_) {}
                }
                function durOf(m) { var d = m.duration; return (isFinite(d) && d > 0) ? d : 0; }
                function playedOf(m) {
                    var seconds = 0;
                    for (var i = 0; i < m.played.length; i++) seconds += m.played.end(i) - m.played.start(i);
                    return seconds;
                }
                // Muted hover previews are not watching: audible playback in a player-sized element only.
                function isRealPlayback(m) {
                    if (!m || m.muted) return false;
                    if (m.tagName === 'AUDIO') return true;
                    return (m.offsetWidth || 0) >= 200;
                }
                var active = null, beat = null, pauseTimer = null;
                function startBeat() {
                    if (beat) return;
                    beat = setInterval(function() {
                        if (!active || active.paused || active.ended) return;
                        post({ type: 'ccpMedia', state: 'progress', pos: active.currentTime || 0, dur: durOf(active), played: playedOf(active) });
                    }, 1000);
                }
                function report(reason) {
                    active = null;
                    if (beat) { clearInterval(beat); beat = null; }
                    post({ type: 'ccpMedia', state: 'stopped', reason: reason });
                }
                function cancelPendingStop() { if (pauseTimer) { clearTimeout(pauseTimer); pauseTimer = null; } }
                function onPlaying(e) {
                    var m = e.target;
                    if (!isRealPlayback(m)) return;
                    cancelPendingStop();
                    active = m;
                    post({ type: 'ccpMedia', state: 'playing', pos: m.currentTime || 0, dur: durOf(m) });
                    startBeat();
                }
                function onEnded(e) {
                    if (active && e.target !== active) return;
                    cancelPendingStop();
                    report('ended');
                }
                // A pause must last 2 s before it counts: seeks, buffering and ad breaks pause too.
                function onMaybeStop(e) {
                    if (!active || e.target !== active) return;
                    cancelPendingStop();
                    pauseTimer = setTimeout(function() {
                        pauseTimer = null;
                        if (active && active.paused && !active.ended) report('paused');
                    }, 2000);
                }
                function bind(m) {
                    if (!m || m.__ccpMediaHooked) return;
                    m.__ccpMediaHooked = true;
                    m.addEventListener('playing', onPlaying);
                    m.addEventListener('ended', onEnded);
                    m.addEventListener('pause', onMaybeStop);
                    if (!m.paused && !m.ended && m.readyState >= 3) onPlaying({ target: m });
                }
                function scan() {
                    try {
                        var all = document.querySelectorAll('video,audio');
                        for (var i = 0; i < all.length; i++) bind(all[i]);
                    } catch (_) {}
                }
                function init() {
                    scan();
                    try { new MutationObserver(scan).observe(document.documentElement, { childList: true, subtree: true }); } catch (_) {}
                    setInterval(scan, 3000);
                }
                if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
                else init();
                window.addEventListener('pagehide', function() { report('navigated'); });
            })();
            """;

        /// <summary>WPF OnBrowserWebMessageReceived's media branches (MainWindow.Browser.cs:1127-1175) and
        /// HandleBrowserMediaMessage (:1207). Other Home messages (remix watch, fullscreen off) have their own handlers.</summary>
        internal void OnBrowserMediaMessage(string body)
        {
            try
            {
                using var outer = JsonDocument.Parse(body);
                using var inner = outer.RootElement.ValueKind == JsonValueKind.String ? JsonDocument.Parse(outer.RootElement.GetString()!) : null;
                var o = (inner ?? outer).RootElement;
                if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("type", out var type)) return;
                double Num(string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                string? Str(string key) => o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var media = BrowserMediaSession.Shared;
                switch (type.GetString())
                {
                    case "ccpMedia":
                        switch (Str("state"))
                        {
                            case "playing": media.OnMediaPlaying(Num("dur")); break;
                            case "progress": media.OnMediaProgress(Num("pos"), Num("dur"), Num("played")); break;
                            case "stopped": media.OnMediaStopped(Str("reason") ?? "page"); break;
                        }
                        break;
                    case "videoStarted": media.OnMediaPlaying(0); break;
                    case "videoEnded":
                        media.OnMediaStopped("takeover-injection");
                        ExitBrowserPopoutFullscreen();
                        break;
                    case "videoDuration": media.OnMediaPlaying(Num("seconds")); break;
                    case "fsDenied": Log.Warning("Web takeover fullscreen refused by the page: {Reason}", Str("reason")); break;
                    // Ends the takeover only; the page may keep playing for the user.
                    case "fsExit":
                        media.OnFullscreenExited();
                        ExitBrowserPopoutFullscreen();
                        break;
                }
            }
            catch (JsonException) { }
        }
    }
}
