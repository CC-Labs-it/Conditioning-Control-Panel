import { LITE } from '../perf.js';
import { createRide, validateManifest } from './model.js';
import { createScene } from './scene.js';
import { createMedia } from './media.js';
import { createStage } from './stage.js';

export const DOCUMENTARY_SHOW_MAX_MS = 4 * 3600e3;
export const MANIFEST_URL = new URL('../../assets/documentary/exhibits.json', import.meta.url);

/** Audio authors subscribe to this object and send beats. With fallback:false their timing owns the ride. */
export function createTabTunnel(ctx, parent, opts = {}) {
  validateManifest(opts.manifest);
  const doc = parent.ownerDocument;
  const win = doc.defaultView;
  const root = doc.createElement('div');
  root.className = 'td-root';
  root.setAttribute('aria-label', 'Museum railway');
  const stylesReady = Promise.all(['scene.css', 'stage.css'].map(file => new Promise(resolve => {
    const link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = new URL(file, import.meta.url).href;
    link.onload = link.onerror = resolve;
    root.append(link);
  })));
  const listeners = new Map();
  const cleanups = new Set();
  const jobs = new Map();
  const completed = new Set();
  const heldStops = new Map();
  const lite = opts.lite ?? LITE;
  let disposed = false, started = false, frameOff = null, floorRelease = null, paintWait = 0;
  let stage, scene, frameCount = 0;
  const emit = (event, data) => {
    for (const fn of [...(listeners.get(event) || [])]) {
      try { fn(data); } catch (error) { console.error(error); }
    }
  };
  const ride = createRide(opts.manifest, { cues: opts.cues, reduced: ctx.reduced, at: opts.at }, (event, data) => {
    if (event === 'cue' && opts.fallback !== false) {
      if (data.id === 'floor.creak') beat('floor.creak');
      if (data.id === 'floor.snap') beat('floor.snap');
      if (data.id === 'floor.park') {
        later('floor.replay', 2000);
        later('floor.freeze', 9000);
        later('floor.release', 12000);
      }
    }
    if (event === 'arrive') {
      stage.arrive();
      scene.draw(ride.state);
      frameCount++;
      frameOff?.(); frameOff = null;
      emit('stage', { ready: true });
      if (opts.fallback !== false) { beat('stage.pitch'); later('stage.buttons', opts.at === 'stage' ? 800 : 48000); }
    }
    if (event === 'pump') scene.beat('pump', data);
    emit(event, data);
  });
  parent.append(root);
  const media = createMedia(ctx, { lite });
  try {
    scene = createScene(ctx, root, opts.manifest, media, { lite, onWake: () => doc.getElementById('wake')?.click() });
    stage = createStage(ctx, scene.stageAnchor, opts.manifest, {
      onNewRun: () => { try { opts.onNewRun?.(); } finally { dispose(); } },
      hours: opts.hours, now: opts.now, onEvent: emit,
    });
  } catch (error) {
    scene?.ready.catch(() => {});
    scene?.dispose(); media.dispose(); root.remove();
    throw error;
  }
  const captions = doc.createElement('p');
  captions.className = 'td-captions';
  captions.setAttribute('aria-live', 'polite');
  root.append(captions);
  function later(id, ms) {
    if (disposed || completed.has(id) || jobs.has(id)) return;
    jobs.set(id, ctx.after(ms, () => { jobs.delete(id); beat(id); }));
  }
  function beat(id, data) {
    if (disposed) return;
    jobs.get(id)?.(); jobs.delete(id);
    // Replays and booth state changes can be sent repeatedly by the recording lane.
    completed.add(id);
    if (id === 'booth.unlock') ride.unlock();
    if (id === 'floor.snap' && !floorRelease) floorRelease = hold('floorboard', 305);
    if (id === 'floor.release') { floorRelease?.(); floorRelease = null; }
    scene.beat(id, data);
    stage.beat(id, data);
    emit('beat', { id, data });
  }
  function tick(dt) {
    if (disposed || ctx.paused || doc.hidden) return;
    ride.step(dt);
    for (const stop of heldStops.values()) {
      if (opts.fallback !== false && !stop.cancel && ride.state.z >= stop.at) {
        stop.cancel = ctx.after(12000, stop.release);
      }
    }
    if (ride.state.docked) return;
    paintWait += dt;
    const interval = lite ? 1 / 30 : 1 / 60;
    if (paintWait + 0.00001 < interval) return;
    paintWait = Math.max(0, paintWait - interval) % interval;
    scene.draw(ride.state); frameCount++;
  }
  function hold(id, at) {
    heldStops.get(id)?.release();
    const releaseModel = ride.hold(id, at);
    const stop = { at, cancel: null, release() {
      stop.cancel?.(); releaseModel(); heldStops.delete(id);
    } };
    heldStops.set(id, stop);
    return stop.release;
  }
  const interactive = target => target?.closest?.('button,a,input,textarea,select,[contenteditable=true],[role=button]');
  function pump(event) {
    if (disposed || ctx.paused || doc.hidden || interactive(event.target)) return;
    if (event.type === 'keydown') {
      if (event.repeat || event.ctrlKey || event.altKey || event.metaKey || ![' ', 'Enter'].includes(event.key)) return;
      event.preventDefault();
    } else if (event.button !== 0 || event.isPrimary === false) return;
    ride.pump();
  }
  function sync() {
    media.pause(ctx.paused || doc.hidden);
    scene.setPaused(ctx.paused || doc.hidden);
    stage.visibility();
  }
  function start() {
    if (started || disposed) return;
    started = true;
    sync();
    scene.resize();
    frameOff = ctx.frame(tick);
    ride.step(0);
    scene.draw(ride.state); frameCount++;
    if (opts.fallback !== false && ride.state.locked) { later('booth.stamp', 30000); later('booth.unlock', 40000); }
    emit('ready');
  }
  root.addEventListener('pointerdown', pump);
  doc.addEventListener('keydown', pump);
  doc.addEventListener('visibilitychange', sync);
  win.addEventListener('resize', scene.resize);
  cleanups.add(ctx.on('pause', () => { stage.pause(true); sync(); }));
  cleanups.add(ctx.on('resume', () => { stage.pause(false); sync(); }));
  cleanups.add(ctx.on('suspend', event => {
    if (event?.on && event.reason === 'panic') { dispose(); return; }
    if (event?.reason !== 'focus') stage.pause(Boolean(event?.on));
    sync();
  }));
  function dispose() {
    if (disposed) return;
    disposed = true;
    frameOff?.(); frameOff = null;
    for (const cancel of jobs.values()) cancel();
    jobs.clear(); floorRelease?.();
    for (const stop of heldStops.values()) stop.release();
    for (const cleanup of cleanups) cleanup?.();
    cleanups.clear();
    root.removeEventListener('pointerdown', pump);
    doc.removeEventListener('keydown', pump);
    doc.removeEventListener('visibilitychange', sync);
    win.removeEventListener('resize', scene.resize);
    scene.dispose(); stage.dispose(); media.dispose(); root.remove();
    emit('dispose'); listeners.clear(); opts.onDispose?.();
  }
  ctx.cleanup(dispose);
  // The caller gets one synchronous opportunity to attach the audio lane before ready fires.
  const ready = Promise.all([scene.ready, stylesReady]).then(start);
  ready.catch(error => { console.error(error); dispose(); });
  return Object.freeze({
    state: ride.state, captions, beat, dispose, ready,
    addCues: ride.addCues,
    hold,
    on(event, fn) {
      if (disposed) return () => {};
      if (!listeners.has(event)) listeners.set(event, new Set());
      listeners.get(event).add(fn);
      return () => listeners.get(event)?.delete(fn);
    },
    debug: () => ({ frames: frameCount, disposed, callbacks: Number(Boolean(frameOff)), pending: jobs.size, ...scene.debug?.() }),
  });
}

/** Registry ending(ctx) entry point. New run resolves into the engine's ordinary recap. */
export async function tabDocumentary(ctx, opts = {}) {
  ctx.audio.solo(true);
  const abort = new AbortController();
  let tunnel, resolveDone;
  const done = new Promise(resolve => { resolveDone = resolve; });
  const finish = () => { abort.abort(); tunnel?.dispose(); resolveDone(); };
  const off = ctx.on('suspend', event => { if (event?.on && event.reason === 'panic') finish(); });
  ctx.cleanup(finish);
  try {
    const manifest = opts.manifest || await fetch(MANIFEST_URL, { signal: abort.signal }).then(response => {
      if (!response.ok) throw new Error('Documentary exhibit manifest unavailable');
      return response.json();
    });
    if (abort.signal.aborted) return;
    tunnel = createTabTunnel(ctx, ctx.layer('ending'), { ...opts, manifest, onDispose: resolveDone });
    opts.connect?.(tunnel);
    await done;
  } catch (error) {
    if (error.name !== 'AbortError') throw error;
  } finally { off?.(); finish(); }
}
