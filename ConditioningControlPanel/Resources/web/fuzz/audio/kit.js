/* ============================================================================
 * audio/kit.js - the one audio surface. Armed on the first gesture, one master gain every voice passes through (mute,
 * pause, panic and the Drop all pull it). Recorded clips only, never synthesis. A missing or broken file is silence.
 * Buses: sfx, ambient, voice, podcast.
 * ==========================================================================*/

export const BUS = Object.freeze({ sfx: 0.6, ambient: 0.2, voice: 0.5, podcast: 0.5 });
export const PLUCKS = Array.from({ length: 18 }, (_, i) => `assets/audio/pluck/wood_${String(i).padStart(2, '0')}.wav`);

export function createKit() {
  let ctx = null, master = null, armed = false;
  let muted = false, quiet = false, held = false;
  const buffers = new Map();
  const loops = new Set();

  function load(url) {
    if (!buffers.has(url)) {
      buffers.set(url, fetch(url).then((r) => (r.ok ? r.arrayBuffer() : null)).then((b) => (b ? ctx.decodeAudioData(b) : null)).catch(() => null));
    }
    return buffers.get(url);
  }

  const level = () => (muted || quiet || held ? 0 : 1);
  function applyLevel(fadeMs = 200) {
    if (!armed) return;
    master.gain.cancelScheduledValues(ctx.currentTime);
    master.gain.setTargetAtTime(level(), ctx.currentTime, fadeMs / 3000);
  }

  function startLoop(l) {
    load(l.url).then((buf) => {
      if (!buf || l.stopped || l.src) return;
      l.src = ctx.createBufferSource();
      l.src.buffer = buf; l.src.loop = true;
      l.g = ctx.createGain();
      l.g.gain.setValueAtTime(0.0001, ctx.currentTime);
      l.g.gain.exponentialRampToValueAtTime(Math.max(0.0001, (BUS[l.bus] ?? BUS.sfx) * l.gain), ctx.currentTime + l.fadeMs / 1000);
      l.src.connect(l.g).connect(master);
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
    } catch { armed = false; }
    return armed;
  }

  async function play(url, { bus = 'sfx', gain = 1, rate = 1 } = {}) {
    if (!armed || !url) return;
    const buf = await load(url);
    if (!buf || !armed || held) return;
    const src = ctx.createBufferSource();
    src.buffer = buf; src.playbackRate.value = rate;
    const g = ctx.createGain();
    g.gain.value = (BUS[bus] ?? BUS.sfx) * gain;
    src.connect(g).connect(master);
    src.start();
  }

  /** A loop asked for before the first gesture starts on the first gesture. Returns {stop(fadeMs)}. */
  function loop(url, { bus = 'ambient', gain = 1, fadeMs = 1500 } = {}) {
    const l = { url, bus, gain, fadeMs, src: null, g: null, stopped: false };
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
    arm, play, loop,
    /** The player's own switch. */
    setMuted(v) { muted = !!v; applyLevel(); },
    /** A run mood may keep the page silent for a while. */
    setQuiet(v) { quiet = !!v; applyLevel(); },
    /** Pause, panic, the end: everything stops now and stays stopped until released. */
    hold(v) {
      held = !!v;
      applyLevel(50);
      if (!armed) return;
      if (held) ctx.suspend?.().catch(() => {});
      else ctx.resume?.().catch(() => {});
    },
    get armed() { return armed; },
    get muted() { return muted; },
  };
}
