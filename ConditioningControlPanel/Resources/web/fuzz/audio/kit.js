/* ============================================================================
 * audio/kit.js - the one audio surface. Armed on the first gesture, one master
 * gain every voice passes through (mute, BLANK and the Drop all pull it), bodies
 * first and voices quiet (JUICE-PLAYBOOK). Recorded clips only: the word bank
 * comes from data/words.js or init.caps.words, never synthesis.
 *
 * Buses: sfx (pops, plucks, chimes), voice (whispered words, 0.13 max),
 * bed (hum, heartbeat, loops, 0.15 max).
 * ==========================================================================*/

export const BUS = Object.freeze({ sfx: 0.6, voice: 0.13, bed: 0.15 });
export const PLUCKS = Array.from({ length: 18 }, (_, i) => `assets/audio/pluck/wood_${String(i).padStart(2, '0')}.wav`);

export function createKit({ muted = false } = {}) {
  let ctx = null, master = null, armed = false;
  const buffers = new Map();
  const loops = new Map();
  let isMuted = muted;

  async function load(url) {
    if (buffers.has(url)) return buffers.get(url);
    const p = fetch(url).then((r) => r.arrayBuffer()).then((b) => ctx.decodeAudioData(b)).catch(() => null);
    buffers.set(url, p);
    return p;
  }

  function arm() {
    if (armed) return true;
    try {
      ctx = new (window.AudioContext || window.webkitAudioContext)();
      master = ctx.createGain();
      master.gain.value = isMuted ? 0 : 1;
      master.connect(ctx.destination);
      armed = true;
      if (ctx.state === 'suspended') ctx.resume().catch(() => {});
    } catch { armed = false; }
    return armed;
  }

  async function play(url, { bus = 'sfx', gain = 1, rate = 1 } = {}) {
    if (!armed || !url) return;
    const buf = await load(url);
    if (!buf || !armed) return;
    const src = ctx.createBufferSource();
    src.buffer = buf; src.playbackRate.value = rate;
    const g = ctx.createGain();
    g.gain.value = BUS[bus] * gain;
    src.connect(g).connect(master);
    src.start();
  }

  async function loop(key, url, { bus = 'bed', gain = 1, fadeMs = 2000 } = {}) {
    if (!armed || loops.has(key)) return;
    const buf = await load(url);
    if (!buf || loops.has(key)) return;
    const src = ctx.createBufferSource();
    src.buffer = buf; src.loop = true;
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, ctx.currentTime);
    g.gain.exponentialRampToValueAtTime(Math.max(0.0001, BUS[bus] * gain), ctx.currentTime + fadeMs / 1000);
    src.connect(g).connect(master);
    src.start();
    loops.set(key, { src, g });
  }

  function stopLoop(key, fadeMs = 600) {
    const l = loops.get(key);
    if (!l) return;
    loops.delete(key);
    l.g.gain.cancelScheduledValues(ctx.currentTime);
    l.g.gain.setTargetAtTime(0.0001, ctx.currentTime, fadeMs / 3000);
    setTimeout(() => { try { l.src.stop(); } catch { /* already stopped */ } }, fadeMs + 50);
  }

  function setMuted(v, fadeMs = 300) {
    isMuted = !!v;
    if (!armed) return;
    master.gain.cancelScheduledValues(ctx.currentTime);
    master.gain.setTargetAtTime(isMuted ? 0 : 1, ctx.currentTime, fadeMs / 3000);
  }

  /** Everything off at once, for the Drop and for suspend {on:true}. */
  function silence() {
    for (const key of [...loops.keys()]) stopLoop(key, 200);
    setMuted(true, 100);
  }

  return { arm, play, loop, stopLoop, setMuted, silence, get armed() { return armed; }, get muted() { return isMuted; } };
}
