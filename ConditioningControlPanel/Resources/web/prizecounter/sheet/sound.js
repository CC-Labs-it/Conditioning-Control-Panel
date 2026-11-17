/* sheet/sound.js - the counter's sounds, made live with WebAudio as in the owner's mockup and bubble lab: soft bells in
 * A major pentatonic (bell, tick, chord), the lift of air, the OWNED stamp's knock (thud), and each bubble skin's own pop
 * (skins.js sfx). Never louder than chrome sounds. The context is only ever created by a click (unlock() runs in the click
 * handler); nothing sounds with motion off or when the host says muted, and a page without WebAudio stays silent. */

const NOTES = [880, 987.77, 1108.73, 1318.51, 1479.98, 1760, 1975.53, 2217.46];
/** The juice's sounds as plain numbers (Hz, peak gain before the 0.42 master, seconds): soft() sines and bell() notes. */
export const SOFT = {
  blip: { f: 1318.51, vol: 0.035, len: 0.14 },
  pick: { f0: 659.25, f: 880, vol: 0.05, len: 0.12, glide: 0.05 },
  pickBell: { f: 1108.73, vol: 0.022, len: 0.3, at: 0.03 },
  knock: { f0: 220, f: 110, vol: 0.08, len: 0.16, glide: 0.1 },
  knockBell: { f: 880, vol: 0.025, len: 0.35, at: 0.02 },
  fanfare: [{ f: 880, vol: 0.05, len: 0.45, at: 0 }, { f: 1108.73, vol: 0.05, len: 0.45, at: 0.085 }, { f: 1318.51, vol: 0.05, len: 0.5, at: 0.17 }, { f: 1760, vol: 0.06, len: 1.1, at: 0.27 }],
  nope1: { f: 659.25, vol: 0.045, len: 0.16 },
  nope2: { f: 554.37, vol: 0.04, len: 0.22, at: 0.13 },
};

export function createSound({ muted = false, win = globalThis.window } = {}) {
  const s = { ctx: null, out: null, lab: null, noise: null, last: 0, unlocked: false };
  function ac() {
    if (muted || !s.unlocked) return null;
    try {
      if (!s.ctx) {
        const C = win && (win.AudioContext || win.webkitAudioContext);
        if (!C) return null;
        const c = s.ctx = new C(), d = c.createDelay(0.6), fb = c.createGain(), wet = c.createGain();
        s.out = c.createGain(); s.out.gain.value = 0.42; s.out.connect(c.destination);
        d.delayTime.value = 0.21; fb.gain.value = 0.3; wet.gain.value = 0.2;   // a short soft echo, so the bells ring out
        s.out.connect(d); d.connect(fb); fb.connect(d); d.connect(wet); wet.connect(c.destination);
        const comp = c.createDynamicsCompressor(); s.lab = c.createGain(); s.lab.gain.value = 0.5; s.lab.connect(comp); comp.connect(c.destination);
        const n = c.sampleRate, buf = c.createBuffer(1, n, n), ch = buf.getChannelData(0);
        for (let i = 0; i < n; i++) ch[i] = Math.random() * 2 - 1;
        s.noise = buf;
      }
      if (s.ctx.state === 'suspended') s.ctx.resume();
      return s.ctx;
    } catch (e) { return null; }
  }
  function bell(freq, vol, len, when = 0) {   // a struck bar: the note, and two quiet upper partials that die sooner
    const c = ac(); if (!c) return;
    const t = c.currentTime + when;
    for (const p of [[1, 1, 1], [2.76, 0.2, 0.45], [5.4, 0.06, 0.22]]) {
      const o = c.createOscillator(), g = c.createGain(); o.type = 'sine'; o.frequency.value = freq * p[0];
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(vol * p[1], t + 0.008); g.gain.exponentialRampToValueAtTime(0.0001, t + len * p[2]);
      o.connect(g); g.connect(s.out); o.start(t); o.stop(t + len * p[2] + 0.05);
    }
  }
  /** A pure round sine: a 6 ms rise, an exponential fall over len, an optional glide from f0 to f. */
  function soft({ f, f0 = f, vol, len, at = 0, glide = 0.05 }) {
    const c = ac(); if (!c) return;
    const t = c.currentTime + at, o = c.createOscillator(), g = c.createGain();
    o.type = 'sine'; o.frequency.setValueAtTime(f0, t); if (f0 !== f) o.frequency.exponentialRampToValueAtTime(f, t + glide);
    g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(vol, t + 0.006); g.gain.exponentialRampToValueAtTime(0.0001, t + len);
    o.connect(g); g.connect(s.out); o.start(t); o.stop(t + len + 0.05);
  }
  /* the bubble lab's helpers, for each skin's own pop */
  const env = (g, t, at, peak, dec) => { g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(Math.max(peak, 0.0002), t + at); g.gain.exponentialRampToValueAtTime(0.0001, t + at + dec); };
  const au = {
    get ac() { return s.ctx; }, get out() { return s.lab; },
    osc(type, f0, f1, t, dur, peak, dest) {
      const o = s.ctx.createOscillator(), g = s.ctx.createGain(); o.type = type;
      o.frequency.setValueAtTime(f0, t); if (f1 !== f0) o.frequency.exponentialRampToValueAtTime(f1, t + dur);
      env(g, t, 0.004, peak, dur); o.connect(g); g.connect(dest || s.lab); o.start(t); o.stop(t + dur + 0.06); return o;
    },
    nz(t, dur, peak, type, f0, f1, q, dest) {
      const src = s.ctx.createBufferSource(); src.buffer = s.noise; src.loop = true;
      const f = s.ctx.createBiquadFilter(); f.type = type; f.frequency.setValueAtTime(f0, t);
      if (f1 && f1 !== f0) f.frequency.exponentialRampToValueAtTime(f1, t + dur); f.Q.value = q || 0.7;
      const g = s.ctx.createGain(); env(g, t, 0.003, peak, dur); src.connect(f); f.connect(g); g.connect(dest || s.lab);
      src.start(t, Math.random() * 0.5); src.stop(t + dur + 0.06);
    },
    echo(time, fb) {
      const d = s.ctx.createDelay(1), g = s.ctx.createGain(), inp = s.ctx.createGain();
      d.delayTime.value = time; g.gain.value = fb; inp.connect(s.lab); inp.connect(d); d.connect(g); g.connect(d); d.connect(s.lab); return inp;
    },
  };
  return {
    /** Call from a click handler: the only door to the audio context. */
    unlock() { if (!muted) s.unlocked = true; return ac(); },
    bell, NOTES,
    /** The lift: a breath of filtered noise sliding up (or down, for spending). */
    air(len, vol, up) {
      const c = ac(); if (!c) return;
      const t = c.currentTime, src = c.createBufferSource(), f = c.createBiquadFilter(), g = c.createGain();
      src.buffer = s.noise; f.type = 'bandpass'; f.Q.value = 1.4;
      f.frequency.setValueAtTime(up ? 700 : 2600, t); f.frequency.exponentialRampToValueAtTime(up ? 3200 : 600, t + len);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(vol, t + len * 0.35); g.gain.exponentialRampToValueAtTime(0.0001, t + len);
      src.connect(f); f.connect(g); g.connect(s.out); src.start(t); src.stop(t + len + 0.02);
    },
    /** One landing star: the bells climb as the wallet fills, never faster than 16 a second. */
    tick(k) { const c = ac(); if (!c || c.currentTime - s.last < 0.06) return; s.last = c.currentTime; bell(NOTES[Math.min(NOTES.length - 1, Math.floor(k * NOTES.length))], 0.11, 0.5); },
    chord() { [0, 2, 3, 5].forEach((n, i) => bell(NOTES[n], 0.12, 1.5, i * 0.05)); bell(440, 0.1, 1.7); },
    /** The OWNED stamp: a soft low knock under one bell. */
    thud() {
      const c = ac(); if (!c) return;
      const t = c.currentTime, o = c.createOscillator(), g = c.createGain();
      o.type = 'sine'; o.frequency.setValueAtTime(150, t); o.frequency.exponentialRampToValueAtTime(58, t + 0.16);
      g.gain.setValueAtTime(0.0001, t); g.gain.exponentialRampToValueAtTime(0.5, t + 0.01); g.gain.exponentialRampToValueAtTime(0.0001, t + 0.26);
      o.connect(g); g.connect(s.out); o.start(t); o.stop(t + 0.3); bell(NOTES[3], 0.1, 1.3, 0.03); bell(NOTES[5], 0.08, 1.5, 0.09);
    },
    /* The juice's UI sounds (sheet/juice.js): round sines in the same A major pentatonic, each well under the bells
     * above (peaks 0.03 to 0.06 against the bells' 0.1 to 0.12), never on hover, only after a click unlocked the page. */
    /** A tab: one short round E6. */
    blip() { soft(SOFT.blip); },
    /** A card picked: a small pop gliding E5 up to A5, a quiet C#6 after it. */
    pick() { soft(SOFT.pick); soft(SOFT.pickBell); },
    /** The Yes step: a soft low knock and a quiet A5. */
    knock() { soft(SOFT.knock); soft(SOFT.knockBell); },
    /** The buy: A5, C#6, E6, then A6 held, as soft bells. */
    fanfare() { SOFT.fanfare.forEach((n) => bell(n.f, n.vol, n.len, n.at)); },
    /** Not enough Sparkles: a gentle two-note fall, E5 then C#5. */
    nope() { soft(SOFT.nope1); soft(SOFT.nope2); },
    /** A bubble's own pop (sheet/skins.js sfx). */
    pop(b) { if (!ac()) return; try { b.skin.sfx(b, s.ctx.currentTime + 0.01, au); } catch (e) { /* sound is a bonus */ } },
    get ready() { return !!s.ctx; },
  };
}
