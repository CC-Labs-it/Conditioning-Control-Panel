/* fx/sfx.js - the counter's one sfx bus (lane B3-W3). Procedural WebAudio, no files, no speech. Silent until the
 * first user gesture (the AudioContext is made then). duck(on) drops the bus when the counter opens inside a game. */
export const BUS = { level: 0.7, ducked: 0.3, thudGap: 0.05 };
const GESTURES = ['pointerdown', 'keydown', 'touchstart'];

export function createSfx({ target = globalThis, Ctx = globalThis.AudioContext || globalThis.webkitAudioContext, rng = Math.random } = {}) {
  let ac = null, bus = null, noise = null, ducked = false, lastThud = -1;
  const gain = () => BUS.level * (ducked ? BUS.ducked : 1);
  function unlock() {
    if (!ac && Ctx) {
      try {
        ac = new Ctx(); bus = ac.createGain(); bus.gain.value = gain(); bus.connect(ac.destination);
        noise = ac.createBuffer(1, Math.floor(ac.sampleRate * 0.5), ac.sampleRate);
        const d = noise.getChannelData(0);
        for (let i = 0; i < d.length; i++) d[i] = rng() * 2 - 1;
      } catch { ac = null; }
    }
    if (ac && ac.resume) ac.resume();
    GESTURES.forEach((g) => target.removeEventListener && target.removeEventListener(g, unlock, true));
  }
  GESTURES.forEach((g) => target.addEventListener && target.addEventListener(g, unlock, true));

  const env = (t, peak, dur) => { const g = ac.createGain(); g.gain.setValueAtTime(peak, t); g.gain.exponentialRampToValueAtTime(0.001, t + dur); g.connect(bus); return g; };
  function tone(type, f0, f1, t, dur, peak) {
    const o = ac.createOscillator(); o.type = type;
    o.frequency.setValueAtTime(f0, t); if (f1 !== f0) o.frequency.exponentialRampToValueAtTime(f1, t + dur * 0.7);
    o.connect(env(t, peak, dur)); o.start(t); o.stop(t + dur + 0.05);
  }
  function hiss(t, dur, type, f0, f1, peak, q = 0.7) {
    const n = ac.createBufferSource(), f = ac.createBiquadFilter();
    n.buffer = noise; f.type = type; f.Q.value = q;
    f.frequency.setValueAtTime(f0, t); if (f1 !== f0) f.frequency.exponentialRampToValueAtTime(f1, t + dur);
    n.connect(f).connect(env(t, peak, dur)); n.start(t); n.stop(t + dur + 0.05);
  }

  return {
    get live() { return !!ac; },
    thud(k = 1) {
      if (!ac) return;
      const t = ac.currentTime;
      if (t - lastThud < BUS.thudGap) return;
      lastThud = t;
      tone('sine', 110, 38, t, 0.35, 0.55 * k); hiss(t, 0.2, 'lowpass', 500, 500, 0.5 * k);
    },
    rattle() {
      if (!ac) return;
      const t = ac.currentTime;
      for (let i = 0; i < 2; i++) { const f = 900 + rng() * 1600; tone('triangle', f, f, t + i * 0.045, 0.06, 0.06); }
    },
    whoosh() { if (ac) hiss(ac.currentTime, 0.45, 'bandpass', 320, 1900, 0.16, 0.9); },
    slam() {
      if (!ac) return;
      const t = ac.currentTime;
      tone('sine', 170, 55, t, 0.24, 0.5); hiss(t, 0.05, 'highpass', 1800, 1800, 0.22); hiss(t, 0.14, 'lowpass', 700, 300, 0.3);
    },
    duck(on) { ducked = !!on; if (bus) bus.gain.setTargetAtTime(gain(), ac.currentTime, 0.08); },
    dispose() { GESTURES.forEach((g) => target.removeEventListener && target.removeEventListener(g, unlock, true)); if (ac && ac.close) ac.close(); ac = null; },
  };
}
