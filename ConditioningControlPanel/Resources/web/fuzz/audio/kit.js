/* ============================================================================
 * audio/kit.js - the one audio surface. Armed on the first gesture, one master gain every voice passes through (mute,
 * pause, panic and the Drop all pull it). Recorded clips only, never synthesis. A missing or broken file is silence.
 * Buses: sfx, ambient, voice, podcast.
 * ==========================================================================*/

import { createCueBudget } from './repetition.js';

export const BUS = Object.freeze({ sfx: 0.3, ambient: 0.2, voice: 0.5, podcast: 0.5 });
export const PLUCKS = Array.from({ length: 18 }, (_, i) => `assets/audio/pluck/wood_${String(i).padStart(2, '0')}.wav`);

export function createKit({ makeMedia = () => new Audio(), now = () => performance.now() } = {}) {
  let ctx = null, master = null, armed = false;
  let muted = false, quiet = false, held = false, blank = false;
  const buffers = new Map();
  const loops = new Set();
  const streams = new Set();
  const shots = new Set(), cueBudget = createCueBudget();
  let generation = 0;

  function clearShots() {
    generation++;
    for (const shot of [...shots]) { try { shot.src.stop(); } catch { /* already ended */ } shot.done(); }
    cueBudget.reset();
  }

  function load(url) {
    if (!buffers.has(url)) {
      buffers.set(url, fetch(url).then((r) => (r.ok ? r.arrayBuffer() : null)).then((b) => (b ? ctx.decodeAudioData(b) : null)).catch(() => null));
    }
    return buffers.get(url);
  }

  const level = () => (muted || quiet || held || blank ? 0 : 1);
  function applyLevel(fadeMs = 200) {
    if (!level()) clearShots();
    for (const s of streams) syncStream(s);
    if (!armed) return;
    master.gain.cancelScheduledValues(ctx.currentTime);
    master.gain.setTargetAtTime(level(), ctx.currentTime, fadeMs / 3000);
  }

  /* ---- streams: long audio on a media element, never decoded whole. The kit owns the element and enforces its level
   * and its pause itself, in the same call that changes the state, so panic never waits on an unlock. Hands on `el` are
   * put back: a volume or muted change is reverted, a play during a hold is paused. `muted` beside `volume` because iOS
   * ignores volume on media elements. */
  function toMaster(node, pan) {
    const p = Number.isFinite(pan) && pan !== 0 && ctx.createStereoPanner ? ctx.createStereoPanner() : null;
    if (p) { p.pan.value = Math.max(-1, Math.min(1, pan)); node.connect(p).connect(master); } else node.connect(master);
    return p;
  }

  /** A panned stream goes through the graph, once, as soon as there is one. Unpanned ones stay off it. */
  function routeStream(s) {
    if (!armed || s.routed || !s.pan) return;
    s.routed = true;
    try { toMaster(ctx.createMediaElementSource(s.el), s.pan); } catch { /* plays unpanned */ }
  }

  function syncStream(s) {
    s.expected = Math.min(1, Math.max(0, (BUS[s.bus] ?? BUS.sfx) * s.gain * s.fade));
    if (s.el.volume !== s.expected) s.el.volume = s.expected;
    const silent = level() === 0;
    if (s.el.muted !== silent) s.el.muted = silent;
  }

  function playStream(s) {
    if (!armed || held || s.dead || !s.wanted) return;
    routeStream(s);
    try { s.el.play()?.catch?.(() => {}); } catch { /* a broken file is silence */ }
  }

  function killStream(s) {
    if (s.dead) return;
    s.dead = true;
    streams.delete(s);
    clearInterval(s.fadeTimer);
    try { s.el.pause(); s.el.removeAttribute?.('src'); s.el.load?.(); } catch { /* already gone */ }
  }

  /** Returns {el, play(), pause(), setGain(v), stop(ms), playing}. Starts on the first gesture unless autoplay is false. */
  function stream(url, { bus = 'podcast', gain = 1, loop: looped = false, autoplay = true, pan = 0 } = {}) {
    const el = makeMedia();
    const s = { el, bus, gain, pan: Number.isFinite(pan) ? pan : 0, routed: false, fade: 1, expected: 0, wanted: !!autoplay, dead: false, fadeTimer: 0 };
    if (s.pan) el.crossOrigin = 'anonymous';  // the graph hears nothing from a cross-origin file without it
    el.preload = 'auto';
    el.loop = !!looped;
    el.addEventListener('play', () => { if (held || s.dead) el.pause(); else s.wanted = true; });
    el.addEventListener('pause', () => { if (!held && !s.dead && el.paused) s.wanted = false; });
    el.addEventListener('ended', () => { s.wanted = false; });
    el.addEventListener('volumechange', () => { if (!s.dead) syncStream(s); });
    el.src = url;
    streams.add(s);
    syncStream(s);
    playStream(s);
    return {
      el,
      play() { s.wanted = true; playStream(s); },
      pause() { s.wanted = false; if (!s.dead) el.pause(); },
      setGain(v) { if (Number.isFinite(v)) { s.gain = Math.max(0, v); syncStream(s); } },
      stop(ms = 0) {
        if (s.dead || s.fadeTimer) return;
        if (!(ms > 0) || held) { killStream(s); return; }
        const t0 = Date.now();
        s.fadeTimer = setInterval(() => {
          s.fade = Math.max(0, 1 - (Date.now() - t0) / ms);
          if (s.fade <= 0) killStream(s); else syncStream(s);
        }, 50);
      },
      get playing() { return !s.dead && !el.paused; },
    };
  }

  function startLoop(l) {
    load(l.url).then((buf) => {
      if (!buf || l.stopped || l.src) return;
      l.src = ctx.createBufferSource();
      l.src.buffer = buf; l.src.loop = true;
      l.g = ctx.createGain();
      l.g.gain.setValueAtTime(0.0001, ctx.currentTime);
      l.g.gain.exponentialRampToValueAtTime(Math.max(0.0001, (BUS[l.bus] ?? BUS.sfx) * l.gain), ctx.currentTime + l.fadeMs / 1000);
      l.src.connect(l.g);
      toMaster(l.g, l.pan);
      l.src.start();
    });
  }

  function arm() {
    if (armed) { if (ctx.state === 'suspended' && !held) ctx.resume().catch(() => {}); return true; }
    try {
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      master = ctx.createGain();
      master.gain.value = level();
      master.connect(ctx.destination);
      armed = true;
      if (ctx.state === 'suspended' && !held) ctx.resume().catch(() => {});
      for (const l of loops) startLoop(l);
      for (const s of streams) playStream(s);
    } catch { armed = false; }
    return armed;
  }

  async function play(url, { bus = 'sfx', gain = 1, rate = 1, pan = 0 } = {}) {
    if (!armed || !url || !level()) return;
    const issued = generation, requestedAt = now();
    const ticket = bus === 'sfx' ? cueBudget.request(url, requestedAt) : null;
    if (bus === 'sfx' && !ticket) return;
    const buf = await load(url);
    if (!buf || !armed || !level() || issued !== generation || now() - requestedAt > (bus === 'sfx' ? 250 : 1500)) return;
    const release = bus === 'sfx' ? cueBudget.start(ticket, now()) : () => {};
    if (!release) return;
    const src = ctx.createBufferSource();
    src.buffer = buf; src.playbackRate.value = rate;
    const g = ctx.createGain();
    g.gain.value = (BUS[bus] ?? BUS.sfx) * gain * (ticket?.scale ?? 1);
    src.connect(g);
    const panner = toMaster(g, pan);
    const shot = { src, done() { if (!shots.delete(shot)) return; release(); src.disconnect?.(); g.disconnect?.(); panner?.disconnect?.(); } };
    shots.add(shot);
    src.onended = shot.done;
    try { src.start(); } catch { shot.done(); }
  }

  /** A loop asked for before the first gesture starts on the first gesture. Returns {stop(fadeMs)}. */
  function loop(url, { bus = 'ambient', gain = 1, fadeMs = 1500, pan = 0 } = {}) {
    const l = { url, bus, gain, fadeMs, pan, src: null, g: null, stopped: false };
    loops.add(l);
    if (armed) startLoop(l);
    return {
      stop(ms = 500) {
        if (l.stopped) return;
        l.stopped = true; loops.delete(l);
        if (!l.src) return;
        l.g.gain.cancelScheduledValues(ctx.currentTime);
        l.g.gain.setTargetAtTime(0.0001, ctx.currentTime, ms / 3000);
        setTimeout(() => { try { l.src.stop(); } catch { /* already stopped */ } }, ms + 50);
      },
    };
  }

  return {
    arm, play, loop, stream,
    /** The end of a run: every stream dead at once, whoever started it. */
    stopStreams() { clearShots(); for (const s of [...streams]) killStream(s); },
    /** The player's own switch. */
    setMuted(v) { muted = !!v; applyLevel(); },
    /** A run mood may keep the page silent for a while. */
    setQuiet(v) { quiet = !!v; applyLevel(); },
    /** An unlock may silence the page for a few seconds (ctx.audio.muteFor). */
    setBlank(v) { blank = !!v; applyLevel(); },
    /** Pause, panic, the end: everything stops now and stays stopped until released. */
    hold(v) {
      held = !!v;
      for (const s of [...streams]) {
        if (held) { if (s.fadeTimer) killStream(s); else s.el.pause(); }
      }
      applyLevel(50);
      if (!held) for (const s of streams) playStream(s);
      if (!armed) return;
      if (held) ctx.suspend?.().catch(() => {});
      else ctx.resume?.().catch(() => {});
    },
    get armed() { return armed; },
    get muted() { return muted; },
  };
}
