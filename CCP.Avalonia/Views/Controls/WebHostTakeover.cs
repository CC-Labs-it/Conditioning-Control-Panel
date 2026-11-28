namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    internal static class WebHostTakeover
    {
        // WPF MainWindow.Browser.cs:843-955: poll for the player, play, request fullscreen,
        // retry fullscreen after a blocked play, and leave fullscreen on end or double-click.
        // fsExit and fsDenied go to MainShellWindow.BrowserMedia.cs; playback itself comes from the always-on reporter there.
        // The host owns the cut decision; the page reports metadata and exposes only a seek hook.
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
                let fsNotified = false, enteredFs = false;
                const notifyFsExit = () => { if (fsNotified) return; fsNotified = true; post({ type: 'fsExit' }); };
                video.addEventListener('ended', () => { exitFullscreen(); notifyFsExit(); }, { once: true });
                video.addEventListener('dblclick', e => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) {
                        exitFullscreen();
                        notifyFsExit();
                        e.preventDefault();
                        e.stopPropagation();
                    }
                });
                // WPF :906-912: Esc or F11 leaves fullscreen without ended/dblclick; report it once, after a real entry.
                const onFsChange = () => {
                    if (document.fullscreenElement || document.webkitFullscreenElement) { enteredFs = true; return; }
                    if (enteredFs) notifyFsExit();
                };
                document.addEventListener('fullscreenchange', onFsChange);
                document.addEventListener('webkitfullscreenchange', onFsChange);
                if (!window.__ccpMuted) video.muted = false;
                const goFullscreen = () => {
                    if (window._ccpTakeoverStopped) return;
                    post({ type: 'fsAsk' });
                    if (video.requestFullscreen) {
                        const request = video.requestFullscreen();
                        if (request && request.catch) request.catch(err => post({ type: 'fsDenied',
                            reason: String((err && ((err.name || '') + ': ' + (err.message || ''))) || err).slice(0, 90),
                            connected: video.isConnected, visibility: document.visibilityState,
                            activation: !!(navigator.userActivation && navigator.userActivation.isActive) }));
                    } else if (video.webkitRequestFullscreen) video.webkitRequestFullscreen();
                    else if (video.msRequestFullscreen) video.msRequestFullscreen();
                };
                video.play().then(goFullscreen).catch(() => {
                    video.addEventListener('playing', goFullscreen, { once: true });
                });
            })();
            """;

        // WPF MainWindow.Browser.cs:2999-3018. Never restart a stopped or ended video.
        internal const string FullscreenRecheckScript = """
            (function() {
                if (window._ccpTakeoverStopped || document.fullscreenElement || document.webkitFullscreenElement) return;
                var v = document.querySelector('video');
                if (!v || v.ended || !v.requestFullscreen) return;
                try {
                    var p = v.requestFullscreen();
                    if (p && p.catch) p.catch(function(err) {
                        try {
                            var message = JSON.stringify({ type: 'fsDenied',
                                reason: 'recheck ' + String((err && ((err.name || '') + ': ' + (err.message || ''))) || err).slice(0, 80),
                                activation: !!(navigator.userActivation && navigator.userActivation.isActive), focus: document.hasFocus() });
                            if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction(message);
                            else window.chrome.webview.postMessage(message);
                        } catch (_) {}
                    });
                } catch (_) {}
            })();
            """;

        // WebKit has no gesture-evaluation API. Present the same video in the fullscreen host.
        // The marker restores its position and inline style when fullscreen ends.
        internal const string FullscreenFallbackScript = """
            (function() {
                if (window._ccpTakeoverStopped !== false || document.fullscreenElement || document.webkitFullscreenElement || window.__ccpTakeoverFallbackExit) return false;
                var v = document.querySelector('video');
                if (!v || !v.isConnected || v.ended || !v.parentNode || !document.body) return false;
                var parent = v.parentNode, marker = document.createComment('ccp video');
                var style = v.getAttribute('style'), playing = !v.paused;
                var overlay = document.createElement('div');
                overlay.style.cssText = 'position:fixed;inset:0;z-index:2147483647;background:black;display:flex;align-items:center;justify-content:center';
                // The page holds focus here, so the window never sees Esc/F11 (as GameWindow.JustDrop.cs:157-160).
                var key = function(e) {
                    if (e.key !== 'Escape' && e.key !== 'F11' && e.keyCode !== 27) return;
                    e.preventDefault(); e.stopImmediatePropagation();
                    try {
                        if (typeof window.invokeCSharpAction === 'function') window.invokeCSharpAction('ccp_browser_fullscreen_off');
                        else window.chrome.webview.postMessage('ccp_browser_fullscreen_off');
                    } catch (_) {}
                };
                var restore = function() {
                    if (!window.__ccpTakeoverFallbackExit) return;
                    delete window.__ccpTakeoverFallbackExit;
                    window.removeEventListener('keydown', key, true);
                    if (marker.parentNode) marker.parentNode.replaceChild(v, marker);
                    else if (parent.isConnected) parent.appendChild(v);
                    if (style === null) v.removeAttribute('style'); else v.setAttribute('style', style);
                    overlay.remove();
                };
                try {
                    parent.insertBefore(marker, v);
                    document.body.appendChild(overlay);
                    window.__ccpTakeoverFallbackExit = restore;
                    window.addEventListener('keydown', key, true);
                    overlay.appendChild(v);
                    ['position','inset','width','height','max-width','max-height','object-fit','transform','z-index'].forEach(function(k, i) {
                        v.style.setProperty(k, ['relative','auto','100%','100%','100%','100%','contain','none','auto'][i], 'important');
                    });
                    if (playing && v.paused) { var p = v.play(); if (p && p.catch) p.catch(function() {}); }
                    return true;
                } catch (_) { restore(); if (marker.parentNode) marker.remove(); overlay.remove(); return false; }
            })();
            """;

        internal const string FullscreenFallbackCleanupScript = "if (window.__ccpTakeoverFallbackExit) window.__ccpTakeoverFallbackExit();";

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
