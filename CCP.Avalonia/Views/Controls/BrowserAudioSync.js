
(function() {
if (window.__hapticSyncInjected) { window.__hapticDisabled = false; window.__hapticReady = false; window.__hapticVideoReported = false; window.__hapticProcessing = false; return; }
window.__hapticSyncInjected = true;
window.__hapticReady = false;
window.__hapticVideoReported = false;
window.__hapticProcessing = false;
window.__hapticOverlay = null;


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

const video = document.querySelector('video');
const fullscreenEl = document.fullscreenElement || document.webkitFullscreenElement;
if (fullscreenEl) {
fullscreenEl.appendChild(overlay);
} else if (video && video.parentElement) {
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

window.__hapticUpdateProgress = updateProgress;
window.__hapticShowOverlay = (message) => {
showOverlay();
if (message) {
const statusEl = document.getElementById('hapticStatus');
if (statusEl) statusEl.textContent = message;
}
};
window.__hapticHideOverlay = hideOverlay;

const findVideo = () => document.querySelector('video');

const getVideoUrl = (video) => {
if (video.currentSrc && video.currentSrc.startsWith('http')) return video.currentSrc;
if (video.src && video.src.startsWith('http')) return video.src;
const source = video.querySelector('source');
if (source && source.src && source.src.startsWith('http')) return source.src;
return null;
};

let syncActive = false;
let lastSyncTime = 0;
const SYNC_INTERVAL_MS = 33; // ~30fps for haptic updates (smooth but not excessive)

const syncLoop = (timestamp) => {
if (!syncActive) return;

if (timestamp - lastSyncTime >= SYNC_INTERVAL_MS) {
lastSyncTime = timestamp;
const video = findVideo();
if (video && window.__hapticReady && !video.paused) {
window.chrome.webview.postMessage(JSON.stringify({
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
if (syncActive || window.__hapticDisabled) return;
syncActive = true;
lastSyncTime = 0;
requestAnimationFrame(syncLoop);
};

const stopSync = () => {
syncActive = false;
};

window.__hapticStopSync = stopSync;
const setupVideoInterception = (video) => {
if (!video || video.__hapticIntercepted) return;
video.__hapticIntercepted = true;

const originalPlay = video.play.bind(video);

video.play = function() {
if (window.__hapticDisabled) return originalPlay();

if (window.__hapticReady) {
startSync();
return originalPlay();
}

if (!window.__hapticProcessing && !window.__hapticVideoReported) {
window.__hapticProcessing = true;
window.__hapticVideoReported = true;

const url = getVideoUrl(video);
if (url) {
showOverlay();

window.chrome.webview.postMessage(JSON.stringify({
type: 'audioSyncVideoDetected',
url: url,
duration: video.duration || 0
}));
} else { window.__hapticReady = true; window.__hapticProcessing = false; return originalPlay(); }
}

return Promise.resolve();
};

video.addEventListener('pause', () => {
if (window.__hapticReady) {
window.chrome.webview.postMessage(JSON.stringify({
type: 'audioSyncState',
currentTime: video.currentTime,
paused: true
}));
}
});

video.addEventListener('seeked', () => {
if (window.__hapticReady) {
window.chrome.webview.postMessage(JSON.stringify({
type: 'audioSyncSeek',
currentTime: video.currentTime
}));
}
});

video.addEventListener('ended', () => {
stopSync();
window.chrome.webview.postMessage(JSON.stringify({
type: 'audioSyncEnded'
}));
});
};

window.__hapticSignalReady = () => {
window.__hapticReady = true;
window.__hapticProcessing = false;
hideOverlay();

const video = findVideo();
if (video) {
const originalPlay = HTMLMediaElement.prototype.play.bind(video);
startSync();
originalPlay().catch(e => console.log('[HapticSync] Play failed:', e));
}
};

window.__hapticRearm = () => {
const v = findVideo();
if (!v) { console.log('[HapticSync] Re-arm: no video yet'); return; }
setupVideoInterception(v);
window.__hapticReady = false;
window.__hapticProcessing = true;
window.__hapticVideoReported = true;
const url = getVideoUrl(v);
if (url) {
showOverlay();
window.chrome.webview.postMessage(JSON.stringify({
type: 'audioSyncVideoDetected',
url: url,
duration: v.duration || 0
}));
}
};

const video = findVideo();
if (video) {
setupVideoInterception(video);
}

const observer = new MutationObserver(() => {
const v = findVideo();
if (v) setupVideoInterception(v);
});
observer.observe(document.body, { childList: true, subtree: true });

})();
