namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class WebHostTakeover
    {
        // WPF MainWindow.Browser.cs:843-955: poll for the player, play, request fullscreen,
        // retry fullscreen after a blocked play, and leave fullscreen on end or double-click.
        // The host owns the cut decision; the page reports metadata and exposes only a seek hook.
        internal const string VideoAutoPlayScript = """
            (async function() {
                window._ccpTakeoverStopped = false;
                let video = document.querySelector('video');
                for (let i = 0; i < 20 && !video; i++) {
                    await new Promise(r => setTimeout(r, 500));
                    if (window._ccpTakeoverStopped) return;
                    video = document.querySelector('video');
                }
                if (!video || window._ccpTakeoverStopped) return;
                const cutRequest = '__CCP_CUT_REQUEST__';
                window.__ccpCutRequest = cutRequest;
                window.__ccpSetCutStart = seconds => {
                    try { if (seconds > 0) video.currentTime = seconds; } catch (_) {}
                };
                const reportMetadata = () => {
                    if (Number.isFinite(video.duration) && video.duration > 0)
                        window.chrome?.webview?.postMessage(JSON.stringify({ type: 'ccpCutMetadata', request: cutRequest, dur: video.duration }));
                };
                video.addEventListener('loadedmetadata', reportMetadata, { once: true });
                if (video.readyState >= 1) reportMetadata();
                const exitFullscreen = () => {
                    if (document.exitFullscreen) document.exitFullscreen();
                    else if (document.webkitExitFullscreen) document.webkitExitFullscreen();
                    else if (document.msExitFullscreen) document.msExitFullscreen();
                };
                video.addEventListener('ended', exitFullscreen, { once: true });
                video.addEventListener('dblclick', e => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) {
                        exitFullscreen();
                        e.preventDefault();
                        e.stopPropagation();
                    }
                });
                if (!window.__ccpMuted) video.muted = false;
                const goFullscreen = () => {
                    if (window._ccpTakeoverStopped) return;
                    if (video.requestFullscreen) {
                        const request = video.requestFullscreen();
                        if (request && request.catch) request.catch(() => {});
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
