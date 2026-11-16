namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class WebHostTakeover
    {
        // WPF MainWindow.Browser.cs:843-955: poll for the player, play, request fullscreen,
        // retry fullscreen after a blocked play, and leave fullscreen on end or double-click.
        // fsExit and fsDenied go to MainShellWindow.BrowserMedia.cs; playback itself comes from the always-on reporter there.
        // Metadata for the random cut start is not ported.
        internal const string VideoAutoPlayScript = """
            (async function() {
                window._ccpTakeoverStopped = false;
                const post = o => {
                    try {
                        const s = JSON.stringify(o);
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(s);
                        else window.chrome.webview.postMessage(s);
                    } catch (_) {}
                };
                let video = document.querySelector('video');
                for (let i = 0; i < 20 && !video; i++) {
                    await new Promise(r => setTimeout(r, 500));
                    if (window._ccpTakeoverStopped) return;
                    video = document.querySelector('video');
                }
                if (window._ccpTakeoverStopped) return;
                if (!video) { post({ type: 'fsExit' }); return; }
                const exitFullscreen = () => {
                    if (document.exitFullscreen) document.exitFullscreen();
                    else if (document.webkitExitFullscreen) document.webkitExitFullscreen();
                    else if (document.msExitFullscreen) document.msExitFullscreen();
                };
                video.addEventListener('ended', () => { exitFullscreen(); post({ type: 'fsExit' }); }, { once: true });
                video.addEventListener('dblclick', e => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) {
                        exitFullscreen();
                        post({ type: 'fsExit' });
                        e.preventDefault();
                        e.stopPropagation();
                    }
                });
                if (!window.__ccpMuted) video.muted = false;
                const goFullscreen = () => {
                    if (window._ccpTakeoverStopped) return;
                    if (video.requestFullscreen) {
                        const request = video.requestFullscreen();
                        if (request && request.catch) request.catch(err => post({ type: 'fsDenied',
                            reason: String((err && (err.name || err.message)) || err).slice(0, 60) }));
                    } else if (video.webkitRequestFullscreen) video.webkitRequestFullscreen();
                    else if (video.msRequestFullscreen) video.msRequestFullscreen();
                };
                video.play().then(goFullscreen).catch(() => {
                    video.addEventListener('playing', goFullscreen, { once: true });
                });
            })();
            """;

        // WPF MainWindow.Browser.cs:981: latch before stopping media, so retries cannot resume it.
        internal const string StopScript = "window._ccpForcedFs = false; window._ccpTakeoverStopped = true;" +
            "document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });" +
            "if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});";
    }
}
