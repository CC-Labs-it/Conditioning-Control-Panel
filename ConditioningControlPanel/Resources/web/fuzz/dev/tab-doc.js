import { createTabTunnel, MANIFEST_URL } from '../ui/documentary/index.js';
import { createPictures } from '../ui/pictures.js';
import { createAds } from '../ui/ads.js';

const query = new URLSearchParams(location.search);
const events = new Map(), frames = new Set(), timers = new Set(), cleanups = new Set(), animations = new Set();
let active = 0, paused = false, manual = query.get('manual') === '1', last = performance.now(), raf = null;
let simulatedWall = 0, openTabs = 0;
const $ = id => document.getElementById(id);
const emit = (event, data) => { for (const fn of [...(events.get(event) || [])]) fn(data); };
const pictures = createPictures({ source: query.get('pics') || 'none', onChange: source => emit('pictures', { source }) });
const ads = createAds({ noCcp: () => ['basic', 'prime'].includes(query.get('plan')) });
function schedule() {
  if (raf === null && !manual && !paused && !document.hidden && (frames.size || timers.size)) raf = requestAnimationFrame(loop);
}
function advance(seconds) {
  if (paused || document.hidden) return;
  let remaining = seconds;
  while (remaining > 0.000001) {
    const dt = Math.min(1 / 60, remaining);
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
  layer: () => $('l-ending'), audio: { solo() {} },
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
document.addEventListener('visibilitychange', () => { last = performance.now(); schedule(); });
const manifest = await fetch(MANIFEST_URL).then(response => response.json());
const tunnel = createTabTunnel(ctx, $('l-ending'), {
  manifest, at: query.get('at') || 'booth', lite: query.get('lite') === '1' || innerWidth < 600,
  hours: query.get('hours'), now: () => performance.now() + simulatedWall,
  onDispose() { $('finished').hidden = false; },
});
$('unlock').onclick = () => { tunnel.beat('booth.stamp'); tunnel.beat('booth.unlock'); $('preview').open = false; };
const panic = () => { emit('suspend', { on: true, reason: 'panic' }); $('pause').hidden = true; };
$('panic').onclick = $('stop').onclick = panic;
window.__tabDoc = {
  tunnel, ctx, emit, pause, panic,
  step(seconds) { manual = true; if (raf !== null) cancelAnimationFrame(raf); raf = null; advance(seconds); },
  wall(seconds) { simulatedWall += seconds * 1000; tunnel.beat('stage.timer'); },
  stats() { return { ...tunnel.debug(), callbacks: frames.size, timers: timers.size, subscriptions: [...events.values()].reduce((n, set) => n + set.size, 0), openTabs, nodes: document.querySelectorAll('*').length }; },
};
addEventListener('pagehide', () => { for (const cleanup of cleanups) cleanup(); for (const animation of animations) animation.cancel(); });
