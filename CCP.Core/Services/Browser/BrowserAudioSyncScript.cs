namespace ConditioningControlPanel.Services.Browser
{
    // WPF Services/Browser/BrowserService.cs:765-1033. The host supplies the message carrier.
    public static class BrowserAudioSyncScript
    {
        public const string Source = """
(function() {
    if (window.__hapticSyncInjected) return;
    window.__hapticSyncInjected = true;
    window.__hapticReady = false;
    window.__hapticVideoReported = false;
    window.__hapticProcessing = false;
    window.__hapticOverlay = null;

    console.log('[HapticSync] Script injected');

    // Create processing overlay - supports both normal and fullscreen modes
    const createOverlay = () => {
        if (window.__hapticOverlay) return window.__hapticOverlay;

        const overlay = document.createElement('div');
        overlay.id = 'hapticSyncOverlay';
        overlay.style.cssText = `
            position: fixed;
            top: 0; left: 0; right: 0; bottom: 0;
            background: rgba(0, 0, 0, 0.85);
            display: flex;
            flex-direction: column;
            justify-content: center;
            align-items: center;
            z-index: 2147483647;
            color: white;
            font-family: 'Segoe UI', sans-serif;
        `;
        overlay.innerHTML = `
            <div style='font-size: 24px; margin-bottom: 20px; color: #FF69B4;'>Preparing Haptic Sync...</div>
            <div style='width: 200px; height: 4px; background: #333; border-radius: 2px; overflow: hidden;'>
                <div id='hapticProgress' style='width: 0%; height: 100%; background: linear-gradient(90deg, #FF69B4, #FF1493); transition: width 0.3s;'></div>
            </div>
            <div id='hapticStatus' style='margin-top: 15px; font-size: 14px; color: #aaa;'>Analyzing audio...</div>
        `;

        // For fullscreen support, try to attach near the video element
        const video = document.querySelector('video');
        const fullscreenEl = document.fullscreenElement || document.webkitFullscreenElement;
        if (fullscreenEl) {
            // Attach to fullscreen container for visibility
            fullscreenEl.appendChild(overlay);
        } else if (video && video.parentElement) {
            // Attach to video's parent so overlay works when video goes fullscreen
            video.parentElement.style.position = 'relative';
            video.parentElement.appendChild(overlay);
        } else {
            document.body.appendChild(overlay);
        }
        window.__hapticOverlay = overlay;
        return overlay;
    };

    const showOverlay = () => {
        const overlay = createOverlay();
        overlay.style.display = 'flex';
    };

    const hideOverlay = () => {
        if (window.__hapticOverlay) {
            window.__hapticOverlay.style.display = 'none';
        }
    };

    const updateProgress = (percent, status) => {
        const progress = document.getElementById('hapticProgress');
        const statusEl = document.getElementById('hapticStatus');
        if (progress) progress.style.width = percent + '%';
        if (statusEl) statusEl.textContent = status;
    };

    // Expose functions for C# to call
    window.__hapticUpdateProgress = updateProgress;
    window.__hapticShowOverlay = (message) => {
        showOverlay();
        if (message) {
            const statusEl = document.getElementById('hapticStatus');
            if (statusEl) statusEl.textContent = message;
        }
    };
    window.__hapticHideOverlay = hideOverlay;

    // Find video element
    const findVideo = () => document.querySelector('video');

    // Get best available video URL
    const getVideoUrl = (video) => {
        if (video.currentSrc && video.currentSrc.startsWith('http')) return video.currentSrc;
        if (video.src && video.src.startsWith('http')) return video.src;
        const source = video.querySelector('source');
        if (source && source.src && source.src.startsWith('http')) return source.src;
        return null;
    };

    // Report playback state continuously using requestAnimationFrame
    // rAF is more consistent than setInterval and less prone to browser throttling
    let syncActive = false;
    let lastSyncTime = 0;
    const SYNC_INTERVAL_MS = 33; // ~30fps for haptic updates (smooth but not excessive)

    const syncLoop = (timestamp) => {
        if (!syncActive) return;

        // Throttle to ~30fps to avoid flooding
        if (timestamp - lastSyncTime >= SYNC_INTERVAL_MS) {
            lastSyncTime = timestamp;
            const video = findVideo();
            if (video && window.__hapticReady && !video.paused) {
                window.invokeCSharpAction(JSON.stringify({
                    type: 'audioSyncState',
                    currentTime: video.currentTime,
                    paused: video.paused,
                    duration: video.duration || 0
                }));
            }
        }

        requestAnimationFrame(syncLoop);
    };

    const startSync = () => {
        if (syncActive) return;
        console.log('[HapticSync] Starting sync with requestAnimationFrame');
        syncActive = true;
        lastSyncTime = 0;
        requestAnimationFrame(syncLoop);
    };

    const stopSync = () => {
        syncActive = false;
        console.log('[HapticSync] Stopping sync');
    };

    // Setup video interception
    const setupVideoInterception = (video) => {
        if (!video || video.__hapticIntercepted) return;
        video.__hapticIntercepted = true;
        console.log('[HapticSync] Setting up video interception');

        // Store original play function
        const originalPlay = video.play.bind(video);

        // Intercept play
        video.play = function() {
            console.log('[HapticSync] Play intercepted, ready=' + window.__hapticReady + ', processing=' + window.__hapticProcessing);

            if (window.__hapticReady) {
                // Haptics ready, allow play
                console.log('[HapticSync] Haptics ready, allowing play');
                startSync();
                return originalPlay();
            }

            if (!window.__hapticProcessing && !window.__hapticVideoReported) {
                // First play attempt - start processing
                window.__hapticProcessing = true;
                window.__hapticVideoReported = true;

                const url = getVideoUrl(video);
                if (url) {
                    console.log('[HapticSync] Intercepting play, starting processing for: ' + url);
                    showOverlay();

                    // Report video URL to C#
                    window.invokeCSharpAction(JSON.stringify({
                        type: 'audioSyncVideoDetected',
                        url: url,
                        duration: video.duration || 0
                    }));
                }
            }

            // Return a promise that resolves when ready
            return Promise.resolve();
        };

        // Setup other event listeners
        video.addEventListener('pause', () => {
            console.log('[HapticSync] Video pause event');
            if (window.__hapticReady) {
                window.invokeCSharpAction(JSON.stringify({
                    type: 'audioSyncState',
                    currentTime: video.currentTime,
                    paused: true
                }));
            }
        });

        video.addEventListener('seeked', () => {
            console.log('[HapticSync] Video seeked to ' + video.currentTime);
            if (window.__hapticReady) {
                window.invokeCSharpAction(JSON.stringify({
                    type: 'audioSyncSeek',
                    currentTime: video.currentTime
                }));
            }
        });

        video.addEventListener('ended', () => {
            console.log('[HapticSync] Video ended');
            stopSync();
            window.invokeCSharpAction(JSON.stringify({
                type: 'audioSyncEnded'
            }));
        });
    };

    // Function to signal haptics ready and resume video
    window.__hapticSignalReady = () => {
        console.log('[HapticSync] Ready signal received');
        window.__hapticReady = true;
        window.__hapticProcessing = false;
        hideOverlay();

        // Auto-play the video
        const video = findVideo();
        if (video) {
            console.log('[HapticSync] Starting video playback');
            const originalPlay = HTMLMediaElement.prototype.play.bind(video);
            startSync();
            originalPlay().catch(e => console.log('[HapticSync] Play failed:', e));
        }
    };

    // Re-arm after a LATE device connection (toy turned on while the page /
    // video was already open). The first play() may have already fired with no
    // device, so the one-shot guards are set and the video is running un-synced.
    // Reset them and re-report the current video so C# (now connected) processes
    // it and signals ready, which restarts the sync loop in step with playback.
    window.__hapticRearm = () => {
        const v = findVideo();
        if (!v) { console.log('[HapticSync] Re-arm: no video yet'); return; }
        setupVideoInterception(v);
        window.__hapticReady = false;
        window.__hapticProcessing = true;
        window.__hapticVideoReported = true;
        const url = getVideoUrl(v);
        if (url) {
            console.log('[HapticSync] Re-arm: reporting current video ' + url);
            showOverlay();
            window.invokeCSharpAction(JSON.stringify({
                type: 'audioSyncVideoDetected',
                url: url,
                duration: v.duration || 0
            }));
        }
    };

    // Setup on existing video
    const video = findVideo();
    if (video) {
        setupVideoInterception(video);
    }

    // Watch for dynamically added videos
    const observer = new MutationObserver(() => {
        const v = findVideo();
        if (v) setupVideoInterception(v);
    });
    observer.observe(document.body, { childList: true, subtree: true });

    console.log('[HapticSync] Script fully initialized with play interception');
})();
""";
    }
}
