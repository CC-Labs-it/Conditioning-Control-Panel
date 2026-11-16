namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class WebHostTakeover
    {
        // WPF MainWindow.Browser.cs:843-955: poll for the player, play, request fullscreen,
        // retry fullscreen after a blocked play, and leave fullscreen on end or double-click.
        // Media reporting and metadata wait for the browser media service.
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

        // WPF MainWindow.Browser.cs:1017-1072. Panic also cancels polling and late callbacks.
        internal const string PlaylistAutoPlayScript = """
            (async function() {
                window._ccpTakeoverStopped = false;
                const post = message => {
                    if (window._ccpTakeoverStopped) return;
                    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
                    else if (window.invokeCSharpAction) window.invokeCSharpAction(JSON.stringify(message));
                };
                let button = document.querySelector('button.play-action');
                for (let i = 0; i < 20 && !button; i++) {
                    await new Promise(r => setTimeout(r, 250));
                    if (window._ccpTakeoverStopped) return;
                    button = document.querySelector('button.play-action');
                }
                if (window._ccpTakeoverStopped) return;
                if (!button) { post({ type: 'videoEnded', reason: 'noPlayButton' }); return; }
                let notified = false;
                const started = () => {
                    if (!notified && !window._ccpTakeoverStopped) {
                        notified = true;
                        post({ type: 'videoStarted' });
                    }
                };
                const bind = audio => {
                    if (!audio || audio.__bcBound) return;
                    audio.__bcBound = true;
                    audio.addEventListener('playing', started);
                    audio.addEventListener('ended', () => post({ type: 'videoEnded', reason: 'ended' }));
                };
                const bindAll = () => document.querySelectorAll('audio').forEach(bind);
                bindAll();
                new MutationObserver(bindAll).observe(document.body, { childList: true, subtree: true });
                button.click();
                setTimeout(started, 3000);
            })();
            """;

        // WPF MainWindow.Browser.cs:981: latch before stopping media, so retries cannot resume it.
        internal const string StopScript = "window._ccpForcedFs = false; window._ccpTakeoverStopped = true;" +
            "document.querySelectorAll('video,audio').forEach(v => { try { v.pause(); } catch (e) {} });" +
            "if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen().catch(() => {});";
    }
}
