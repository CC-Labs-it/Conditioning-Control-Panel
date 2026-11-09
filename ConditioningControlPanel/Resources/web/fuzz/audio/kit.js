/* ============================================================================
 * audio/kit.js - the one audio surface. Armed on the first gesture, one master gain every voice passes through (mute,
 * pause, panic and the Drop all pull it). Pre-rendered clips only; spoken words are real recordings. Missing files stay silent.
 * Buses: sfx, ambient, voice, podcast.
 * ==========================================================================*/

import { createCueBudget } from './repetition.js';
import { createMixer } from './mixer.js';

/* Owner 2026-11-08: every sound effect and the hums lower (sfx 0.3 -> 0.18, ambient 0.2 -> 0.12); lo-fi radio and podcasts half. */
export const BUS = Object.freeze({ sfx: 0.18, ambient: 0.12, voice: 0.5, podcast: 0.25 });
export const PLUCKS = Array.from({ length: 18 }, (_, i) => `assets/audio/pluck/wood_${String(i).padStart(2, '0')}.wav`);

export function createKit({ makeMedia = () => new Audio(), now = () => performance.now() } = {}) {
  let ctx = null, master = null, armed = false;
  let muted = false, quiet = false, held = false, blank = false, rule = false;
  let masterVolume = 1;
  const buffers = new Map();
  const loops = new Set();
  const streams = new Set();
  const shots = new Set(), cueBudget = createCueBudget();
  const mixer = createMixer(), sourceNodes = new Set(), sourceLevels = new Map();
  let generation = 0;
  let solo = null;   // solo(keep): while set, a source it does not keep is silent (an ending show's own sounds only)

  const sourceLevel = (id) => (solo && !solo(id) ? 0 : sourceLevels.get(id) ?? 1);
  function registerSource(bus, source, sourceLabel) {
    const id = typeof source === 'string' && source.length && source.length <= 128 ? source : bus === 'voice' ? 'voices' : bus;
    const label = sourceLabel || ({ sfx: 'Effects', ambient: 'Ambient', voices: 'Voices', podcast: 'Podcast' }[id]);
    mixer.register(id, label);
    return id;
  }
  mixer.subscribe((sources) => {
    sourceLevels.clear();
    for (const s of sources) sourceLevels.set(s.id, s.muted ? 0 : s.volume);
    applySources();
  });
  function applySources() {
    for (const route of sourceNodes) {
      const v = sourceLevel(route.source), param = route.g.gain;
      param.cancelScheduledValues(ctx.currentTime);
      if (v === 0) param.setValueAtTime(0, ctx.currentTime);
      else param.setTargetAtTime(v, ctx.currentTime, .015);
    }
    for (const s of streams) syncStream(s);
  }

  function clearShots() {
    generation++;
    for (const shot of [...shots]) { try { shot.src.stop(); } catch { /* already ended */ } shot.done(); }
    cueBudget.reset();
  }

  function decode(url) {
    return fetch(url).then((r) => (r.ok ? r.arrayBuffer() : null)).then((b) => (b ? ctx.decodeAudioData(b) : null));
  }

  /* An .ogg (Opus) that this browser cannot decode (older Safari) falls back to the .wav beside it. */
  function load(url) {
    if (!buffers.has(url)) {
      const wav = url.endsWith('.ogg') ? url.slice(0, -4) + '.wav' : null;
      buffers.set(url, decode(url).catch(() => null).then((buf) => (buf || !wav ? buf : decode(wav))).catch(() => null));
    }
    return buffers.get(url);
  }

  const level = () => (muted || quiet || held || blank || rule ? 0 : masterVolume);
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
  /* muffle(source, on): one shared lowpass + small cut per source, switched in and out of its routes (the Bimbodoll
   * ending swallows an object and its sound goes underwater). Off = the plain route to master, as before. */
  const muffles = new Map();
  const outFor = (source) => muffles.get(source)?.f || master;
  function toMaster(node, pan, source) {
    const g = ctx.createGain();
    g.gain.value = sourceLevel(source);
    node.connect(g);
    const p = Number.isFinite(pan) && pan !== 0 && ctx.createStereoPanner ? ctx.createStereoPanner() : null;
    if (p) { p.pan.value = Math.max(-1, Math.min(1, pan)); g.connect(p).connect(outFor(source)); } else g.connect(outFor(source));
    const route = { source, g, tail: p || g, disconnect() { sourceNodes.delete(route); g.disconnect?.(); p?.disconnect?.(); } };
    sourceNodes.add(route);
    return route;
  }

  /** Route once so source sliders also work where media-element volume is read-only. */
  function routeStream(s) {
    if (!armed || s.routeTried) return;
    s.routeTried = true;
    try {
      s.media = ctx.createMediaElementSource(s.el);
      s.g = ctx.createGain();
      s.media.connect(s.g);
      s.route = toMaster(s.g, s.pan, s.source);
      s.routed = true;
    } catch { /* native playback remains available without Web Audio media routing */ }
    syncStream(s);
  }

  function syncStream(s) {
    const envelope = Math.min(1, Math.max(0, (BUS[s.bus] ?? BUS.sfx) * s.gain * s.fade));
    if (s.routed) s.g.gain.value = envelope;
    s.expected = s.routed ? 1 : Math.min(1, envelope * sourceLevel(s.source) * masterVolume);
    if (s.el.volume !== s.expected) s.el.volume = s.expected;
    const silent = level() === 0 || sourceLevel(s.source) === 0;
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
    s.media?.disconnect?.(); s.g?.disconnect?.(); s.route?.disconnect();
  }

  /** Returns {el, play(), pause(), setGain(v), stop(ms), playing}. Starts on the first gesture unless autoplay is false. */
  /* preload 'metadata' (2026-11-08): 'auto' pulled whole podcast episodes and radio tracks at once (4 MB a minute). A play()
   * still fetches as it goes; pass preload: 'auto' for a short clip that must start with no wait. */
  function stream(url, { bus = 'podcast', gain = 1, loop: looped = false, autoplay = true, pan = 0, preload = 'metadata', source, sourceLabel } = {}) {
    source = registerSource(bus, source, sourceLabel);
    const el = makeMedia();
    const s = { el, bus, gain, source, pan: Number.isFinite(pan) ? pan : 0, routed: false, routeTried: false, fade: 1, expected: 0, wanted: !!autoplay, dead: false, fadeTimer: 0 };
    el.crossOrigin = 'anonymous';  // the graph hears nothing from a cross-origin file without it
    el.preload = preload === 'auto' || preload === 'none' ? preload : 'metadata';
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
      l.route = toMaster(l.g, l.pan, l.source);
      l.src.onended = () => { l.src.disconnect?.(); l.g.disconnect?.(); l.route.disconnect(); };
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

  async function play(url, { bus = 'sfx', gain = 1, rate = 1, pan = 0, fadeInMs = 0, fadeOutMs = 0, source, sourceLabel, signal } = {}) {
    source = registerSource(bus, source, sourceLabel);
    if (!armed || !url || !level() || !sourceLevel(source) || signal?.aborted) return;
    const issued = generation, requestedAt = now();
    const ticket = bus === 'sfx' ? cueBudget.request(url, requestedAt) : null;
    if (bus === 'sfx' && !ticket) return;
    const buf = await load(url);
    if (!buf || !armed || !level() || !sourceLevel(source) || signal?.aborted || issued !== generation || now() - requestedAt > (bus === 'sfx' ? 250 : 1500)) return;
    const release = bus === 'sfx' ? cueBudget.start(ticket, now()) : () => {};
    if (!release) return;
    const src = ctx.createBufferSource();
    src.buffer = buf; src.playbackRate.value = rate;
    const g = ctx.createGain();
    const peak = (BUS[bus] ?? BUS.sfx) * gain * (ticket?.scale ?? 1);
    const duration = Number.isFinite(buf.duration) ? buf.duration / Math.max(.01, Math.abs(rate)) : 0;
    const attack = Math.min(duration / 2, Math.max(0, fadeInMs) / 1000);
    const releaseTime = Math.min(duration / 2, Math.max(0, fadeOutMs) / 1000);
    const start = ctx.currentTime;
    g.gain.value = attack ? 0 : peak;
    if (attack) {
      g.gain.setValueAtTime(0, start);
      g.gain.linearRampToValueAtTime(peak, start + attack);
    }
    if (releaseTime) {
      g.gain.setValueAtTime(peak, start + duration - releaseTime);
      g.gain.linearRampToValueAtTime(0, start + duration);
    }
    src.connect(g);
    const route = toMaster(g, pan, source);
    const stop = () => { try { src.stop(); } catch { /* already ended */ } shot.done(); };
    const shot = { src, done() {
      if (!shots.delete(shot)) return;
      signal?.removeEventListener('abort',stop);
      release(); src.disconnect?.(); g.disconnect?.(); route.disconnect();
    } };
    shots.add(shot);
    signal?.addEventListener('abort',stop,{once:true});
    src.onended = shot.done;
    try { src.start(); } catch { shot.done(); }
  }

  /** A loop asked for before the first gesture starts on the first gesture. Returns {stop(fadeMs)}. */
  function loop(url, { bus = 'ambient', gain = 1, fadeMs = 1500, pan = 0, source, sourceLabel } = {}) {
    source = registerSource(bus, source, sourceLabel);
    const l = { url, bus, gain, fadeMs, pan, source, src: null, g: null, stopped: false };
    loops.add(l);
    if (armed) startLoop(l);
    return {
      stop(ms = 500) {
        if (l.stopped) return;
        l.stopped = true; loops.delete(l);
        if (!l.src) return;
        l.g.gain.cancelScheduledValues(ctx.currentTime);
        if (!(ms > 0)) { try { l.src.stop(); } catch { /* already stopped */ } return; }
        l.g.gain.setTargetAtTime(0.0001, ctx.currentTime, ms / 3000);
        setTimeout(() => { try { l.src.stop(); } catch { /* already stopped */ } }, ms + 50);
      },
    };
  }

  return {
    arm, play, loop, stream, mixer,
    /** The end of a run: every stream dead at once, whoever started it. */
    stopStreams() { clearShots(); for (const s of [...streams]) killStream(s); },
    /** The player's own switch. */
    setMuted(v) { muted = !!v; applyLevel(); },
    setVolume(v) {
      if (!Number.isFinite(v)) return;
      masterVolume = Math.min(1, Math.max(0, v));
      applyLevel(45);
    },
    get volume() { return masterVolume; },
    /** A run mood may keep the page silent for a while. */
    setQuiet(v) { quiet = !!v; applyLevel(); },
    /** An unlock may silence the page for a few seconds (ctx.audio.muteFor). */
    setBlank(v) { blank = !!v; applyLevel(); },
    /** Fine Print's Silent rule: the whole run stays silent until the rule's unlock lets go. */
    setRule(v) { rule = !!v; applyLevel(); },
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
    /** A live input for synthesised sound (an ending's tone, a convolver tail): { ac, node, stop() }, routed like a
     *  play() on its bus and source, so mute, pause, panic and the sliders all reach it. Null before the first gesture. */
    input({ bus = 'sfx', gain = 1, source, sourceLabel } = {}) {
      if (!armed) return null;
      source = registerSource(bus, source, sourceLabel);
      const node = ctx.createGain();
      node.gain.value = (BUS[bus] ?? BUS.sfx) * gain;
      const route = toMaster(node, 0, source);
      return { ac: ctx, node, stop() { node.disconnect?.(); route.disconnect(); } };
    },
    /** solo(keep): only sources `keep(id)` answers true for are heard, those already playing and those started later;
     *  solo(null) lets go. The levels behind it (mixer, mute, hold) are untouched. heard(id): a source's level now. */
    solo(keep) { solo = typeof keep === 'function' ? keep : null; if (armed) applySources(); else for (const s of streams) syncStream(s); },
    heard: (id) => sourceLevel(id),
    /** Every source with a live route except the named ones goes underwater (on) or comes back (off). */
    muffleOthers(except = [], on = true) {
      const skip = new Set(except);
      const ids = new Set([...sourceNodes].map((r) => r.source).filter((id) => typeof id === 'string' && !skip.has(id)));
      for (const id of ids) this.muffle(id, on);
    },
    muffle(source, on, { hz = 600, cut = 0.7, fadeOutMs = 0 } = {}) {
      if (!armed || typeof source !== 'string') return;
      const had = muffles.get(source);
      if (!!on === !!had) return;
      let m = had;
      if (on) {
        const f = ctx.createBiquadFilter();
        f.type = 'lowpass'; f.frequency.value = hz; f.Q.value = 0.9;
        const g = ctx.createGain(); g.gain.value = cut;
        if (fadeOutMs > 0) {
          g.gain.setValueAtTime(cut, ctx.currentTime);
          g.gain.linearRampToValueAtTime(0, ctx.currentTime + fadeOutMs / 1000);
        }
        f.connect(g).connect(master);
        m = { f, g };
        muffles.set(source, m);
      } else muffles.delete(source);
      for (const r of sourceNodes) {
        if (r.source !== source) continue;
        try { r.tail.disconnect(); r.tail.connect(on ? m.f : master); } catch { /* a route already gone */ }
      }
      if (!on) { m.f.disconnect?.(); m.g.disconnect?.(); }
    },
  };
}
