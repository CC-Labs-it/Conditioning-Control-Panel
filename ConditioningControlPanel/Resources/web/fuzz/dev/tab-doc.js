import { createTabTunnel, MANIFEST_URL } from '../ui/documentary/index.js';
import { createPictures } from '../ui/pictures.js';
import { createAds } from '../ui/ads.js';
import { createKit } from '../audio/kit.js';
import { createPreviewFeed } from './tab-doc-feed.js';

const query = new URLSearchParams(location.search);
const events = new Map(), frames = new Set(), timers = new Set(), cleanups = new Set(), animations = new Set();
let active = 0, paused = false, manual = query.get('manual') === '1', last = performance.now(), raf = null;
let simulatedWall = 0, openTabs = 0, ended = false;
const $ = id => document.getElementById(id);
const emit = (event, data) => { for (const fn of [...(events.get(event) || [])]) fn(data); };
let galleryDismissed = false;
function updateGalleryStatus(text) {
  $('media-status').textContent = text;
  $('gallery-status').textContent = text;
}
function syncGallerySource(source) {
  $('gallery-connect').hidden = galleryDismissed || source === 'own' || source === 'scrolller';
}
const feed = createPreviewFeed({ onStatus: updateGalleryStatus });
const pictures = createPictures({ source: query.get('pics') || 'none', scrolller:feed.capability,
  send:(type,fields)=>feed.send(type,fields),
  onChange: source => { emit('pictures', { source }); syncGallerySource(source); }, onAdded: source => emit('pictures-added', { source }) });
feed.attach(pictures);
const ads = createAds({ noCcp: () => ['basic', 'prime'].includes(query.get('plan')) });
const audio = createKit();
document.addEventListener('pointerdown', () => audio.arm(), { capture: true });
document.addEventListener('keydown', () => audio.arm(), { capture: true });
function schedule() {
  if (raf === null && !manual && !paused && !document.hidden && (frames.size || timers.size)) raf = requestAnimationFrame(loop);
}
function advance(seconds) {
  if (paused || document.hidden) return;
  let remaining = seconds;
  while (remaining > 0.000001) {
    // The ride integrates its own 60Hz physics. Manual drives need only eight visual samples per second.
    const dt = Math.min(manual ? 1 / 8 : 1 / 60, remaining);
    remaining -= dt;
    active += dt * 1000;
    for (const timer of [...timers]) if (timer.at <= active) { timers.delete(timer); timer.fn(); }
    for (const frame of [...frames]) frame(dt, active);
  }
}
function loop(now) {
  raf = null;
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  advance(dt);
  schedule();
}
const ctx = {
  get paused() { return paused; },
  get gentle() { return query.get('gentle') === '1'; },
  get reduced() { return query.get('reduced') === '1' || matchMedia('(prefers-reduced-motion: reduce)').matches; },
  state: { seed: 1110 }, identity: { displayName: query.get('name') || 'The evening visitor' },
  caps: { plan: query.get('plan') || 'free' }, pictures, ads,
  layer: () => $('l-ending'), audio: {
    solo(on) { audio.solo(on ? id => ['documentary', 'documentary-music', 'documentary-projectors'].includes(id) : null); },
    stream(url, options = {}) { return audio.stream(url, { source:'documentary', sourceLabel:'Museum narrator', ...options }); },
    input(options = {}) { return audio.input({ source:'documentary-projectors', sourceLabel:'Museum projectors', ...options }); },
  },
  cleanup(fn) { cleanups.add(fn); },
  frame(fn) { frames.add(fn); schedule(); return () => frames.delete(fn); },
  after(ms, fn) { const timer = { at: active + ms, fn }; timers.add(timer); schedule(); return () => timers.delete(timer); },
  on(event, fn) {
    if (!events.has(event)) events.set(event, new Set());
    events.get(event).add(fn);
    return () => events.get(event)?.delete(fn);
  },
  anim(node, keyframes, options) {
    const animation = node.animate(keyframes, options);
    animations.add(animation);
    animation.finished.then(() => animations.delete(animation), () => animations.delete(animation));
    if (paused) animation.pause();
    return animation;
  },
  openAdLink(url) {
    if (openTabs >= 2) return false;
    if (query.get('test') === '1') { openTabs++; return true; }
    const tab = window.open(url, '_blank', 'noopener');
    openTabs++;
    return Boolean(tab);
  },
};
function pause(on) {
  paused = on;
  pictures.setGameplayActive(!on && !document.hidden && !ended);
  audio.hold(on);
  $('pause').hidden = !on;
  if (!on) { $('l-ending').tabIndex = -1; $('l-ending').focus(); }
  emit(on ? 'pause' : 'resume');
  for (const animation of animations) if (on) animation.pause(); else animation.play();
  last = performance.now();
  schedule();
}
$('wake').onclick = () => pause(true);
$('resume').onclick = () => pause(false);
document.addEventListener('keydown', event => {
  if (event.key === 'Escape') { event.preventDefault(); pause(!paused); }
});
document.addEventListener('visibilitychange', () => {
  last = performance.now(); pictures.setGameplayActive(!paused && !document.hidden && !ended); schedule();
});
const manifest = await fetch(MANIFEST_URL).then(response => response.json());
const requestedStart = query.get('at') || 'booth';
const narratedPreview = query.get('voice') === '1' || (!manual && query.get('test') !== '1' && query.get('voice') !== '0');
// Chapter doors enter on the approach so the narration has room to travel.
const chapterDoors = { lever:92, floorboard:268, wakeup:370, '303':268, '410':370 };
const startAt = narratedPreview && Object.hasOwn(chapterDoors, requestedStart) ? chapterDoors[requestedStart] :
  /^\d+(?:\.\d+)?$/.test(requestedStart) ? Number(requestedStart) : requestedStart;
const tunnel = createTabTunnel(ctx, $('l-ending'), {
  manifest, at: startAt, lite: query.get('lite') === '1' || innerWidth < 600,
  narration: narratedPreview,
  music: query.get('music') === '1' || (!manual && query.get('test') !== '1' && query.get('music') !== '0'),
  hours: query.get('hours'), now: () => performance.now() + simulatedWall,
  onDispose() { ended=true; galleryDismissed=true; pictures.setGameplayActive(false);pictures.setSource('none');feed.dispose();$('finished').hidden = false; },
});
pictures.setGameplayActive(!paused && !document.hidden);
async function connectGallery() {
  galleryDismissed=false;
  $('use-scrolller').disabled=$('connect-gallery').disabled=true;
  try { if(await feed.choose()) $('preview').open=false; }
  finally { $('use-scrolller').disabled=$('connect-gallery').disabled=false;syncGallerySource(pictures.source); }
}
$('use-scrolller').onclick = $('connect-gallery').onclick = connectGallery;
$('use-none').onclick = () => { galleryDismissed=true;feed.cancel();pictures.setSource('none');syncGallerySource('none');updateGalleryStatus('Pictures are off.'); };
$('use-own').onclick = () => {
  feed.cancel();
  if(pictures.ownCount) { pictures.setSource('own');$('media-status').textContent=`Using ${pictures.ownCount} personal media files`; }
  else $('media-files').click();
};
if(query.get('pics')==='scrolller') $('media-status').textContent='Choose Use Scrolller to connect this visit to your current CCP profile.';
syncGallerySource(pictures.source);
$('media-files').addEventListener('change', event => {
  if (pictures.replaceOwn(event.target.files)) {
    feed.cancel();
    pictures.setSource('own');
    $('media-label').textContent = `${pictures.ownCount} personal media files`;
    $('media-status').textContent = `Using ${pictures.ownCount} personal media files`;
    $('preview').open = false;
  }
});
$('mute').onclick = () => {
  audio.setMuted(!audio.muted);
  $('mute').textContent = audio.muted ? 'Sound off' : 'Sound on';
};
$('unlock').onclick = () => { tunnel.beat('booth.stamp'); tunnel.beat('booth.unlock'); $('preview').open = false; };
const panic = () => { emit('suspend', { on: true, reason: 'panic' }); $('pause').hidden = true; };
$('panic').onclick = $('stop').onclick = panic;
window.__tabDoc = {
  tunnel, ctx, emit, pause, panic,
  step(seconds) { manual = true; if (raf !== null) cancelAnimationFrame(raf); raf = null; advance(seconds); },
  wall(seconds) { simulatedWall += seconds * 1000; tunnel.beat('stage.timer'); },
  stats() { return { ...tunnel.debug(), callbacks: frames.size, timers: timers.size, subscriptions: [...events.values()].reduce((n, set) => n + set.size, 0), openTabs, nodes: document.querySelectorAll('*').length }; },
};
addEventListener('pagehide', () => { for (const cleanup of cleanups) cleanup(); for (const animation of animations) animation.cancel(); feed.dispose(); audio.stopStreams(); pictures.clearOwn(); });
