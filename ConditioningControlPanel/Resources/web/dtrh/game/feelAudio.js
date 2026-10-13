/* ============================================================================
 * feelAudio.js - the soft, pitched half of the descent's feel pass.
 *
 * DtRH is a trance game: these voices make the fall warmer, never louder.
 *   - the pop ladder: each pop in a streak sounds one rung higher on the C
 *     pentatonic (no semitone clashes, so no order of pops can sound wrong),
 *     capped at LADDER_RUNGS, back to the root when the streak breaks;
 *   - the arrival tick: a reward landing in its HUD slot;
 *   - the resolve: one low root note when a landing (boon draft) opens.
 * Plus the pure maths the bus uses so depth can drive the mix.
 *
 * This file imports NOTHING: the audio context, the master out, mute, the
 * effects slider and "is the voice speaking" are handed in, so the pure
 * helpers run under node:test and the voices go through the one shared bus
 * (mute, slider and the biome/depth low-pass all apply).
 * Every voice is a sine with one quiet octave partial, a short attack (the IN)
 * and an exponential decay (the OUT). Nothing here speaks.
 * ==========================================================================*/

// ---- tunables (by ear) -------------------------------------------------------
export const LADDER_ROOT_HZ = 261.63;   // C4, rung 0
export const LADDER_RUNGS = 8;          // C4 D4 E4 G4 A4 C5 D5 E5
export const LADDER_TILT = 0.04;        // each rung is this much quieter than the one below
export const POP_GAIN = 0.06;           // ladder note, at the default effects slider
export const POP_DUR_S = 0.26;
export const POP_GAP_MS = 45;           // a sweep popping many at once sounds one note, not a cluster
export const GOLD_GAIN = 0.07;          // a gold/special pop: the current rung, one octave up, no climb
export const ARRIVE_GAIN = 0.035;       // reward landing in its slot
export const ARRIVE_DUR_S = 0.11;
export const ARRIVE_RUNG = 9;           // A5: above the ladder so it never masks a pop
export const ARRIVE_GAP_MS = 40;
export const RESOLVE_GAIN = 0.08;       // the landing's root
export const RESOLVE_DUR_S = 0.3;
export const RESOLVE_HZ = LADDER_ROOT_HZ / 2;   // C3
export const PARTIAL_GAIN = 0.22;       // octave partial, relative to the fundamental
export const ATTACK_S = 0.008;
export const VOICE_DUCK = 0.4;          // every voice here sits under a speaking voice line
export const FX_SLIDER_DEFAULT = 0.48;  // audioLevels.js 'fx' default; gains above are quoted at this
export const DEPTH_OPEN_HZ = 20000;     // master low-pass at depth 0 (transparent)
export const DEPTH_DEEP_HZ = 3200;      // master low-pass at depth 1
export const DEPTH_DRONE_LIFT = 0.3;    // drone bed is this much fuller at depth 1
export const FAIL_HZ = 350;             // a detonation closes the mix to here...
export const FAIL_CLOSE_S = 0.09;       // ...this fast...
export const FAIL_BACK_S = 2.6;         // ...and it opens back over this long

// ---- wave 2 tunables (by ear) --------------------------------------------------
export const HOLD_GAIN = 0.03;          // the held tone under a live bubble: barely there
export const HOLD_TOP_RUNG = 5;         // it climbs from the root (C4) to the octave (C5) at full progress
export const HOLD_ATTACK_S = 0.18;      // the IN: it swells, it never starts
export const HOLD_GLIDE_TC = 0.09;      // seconds; rung to rung is a glide, not a step
export const HOLD_RELEASE_TC = 0.07;    // the OUT: time constant of the fade on release
export const HOLD_MAX_S = 12;           // a hold whose end never arrives fades itself out here
export const HOLD_RESOLVE_RUNG = 5;     // a defuse lands on the octave root
export const HOLD_RESOLVE_GAIN = 0.05;
export const HOLD_RESOLVE_DUR_S = 0.36;
export const HOLD_RESOLVE_GAP_MS = 400; // the hold's own end and the 'defused' moment are one note
export const MOMENT_GAIN = 0.05;        // a moment figure's first note
export const MOMENT_SECOND = 0.8;       // later notes of a figure, relative to the first
export const MOMENT_DUR_S = 0.3;
export const MOMENT_STEP_S = 0.11;      // gap between the notes of a rising or falling pair
export const MOMENT_GAP_MS = 150;       // the same moment twice inside this is one figure
export const MILESTONE_DUR_S = 0.6;     // a streak milestone is held a little longer
export const MILESTONE_FIFTH_RUNGS = 3; // "its fifth" on the ladder: three rungs up stays in key
export const CURSE_GAIN = 0.045;        // a curse: low root + minor third, the one darker colour
export const CURSE_DUR_S = 0.5;
export const CURSE_THIRD_SEMIS = 3;     // the only interval here outside the pentatonic, on purpose
export const TICK_GAIN = 0.03;          // recap count-up tick
export const TICK_DUR_S = 0.07;
export const TICK_BASE_RUNG = 5;        // C5 at t = 0 ...
export const TICK_TOP_RUNG = 10;        // ... C6 at t = 1
export const TICK_GAP_MS = 70;          // at most about 14 ticks a second however fast the count runs
export const COUNT_GAIN = 0.06;         // countdown notes, an octave under the ladder
export const COUNT_DUR_S = 0.4;
export const COUNT_RUNGS = [2, 3, 4];   // index n-1: "1" = E3, "2" = G3, "3" = A3, walking down to the root
export const COUNT_GO_GAIN = 0.07;      // GO: the low root (C3) with the ladder root (C4) over it
export const COUNT_GO_DUR_S = 0.7;
export const COUNT_GAP_MS = 200;

const PENTATONIC = [0, 2, 4, 7, 9];
const clamp01 = (v) => (v > 0 ? (v < 1 ? v : 1) : 0);

// ---- pure helpers (unit tested) ----------------------------------------------
/** Streak 1 is the root; each pop climbs one rung; capped; anything else = root. */
export function rungForStreak(streak) {
  const n = Math.floor(Number(streak));
  if (!(n >= 1)) return 0;
  return Math.min(LADDER_RUNGS - 1, n - 1);
}

/** Frequency of a pentatonic rung above the root (rung 5 is the octave). */
export function freqForRung(rung, rootHz = LADDER_ROOT_HZ) {
  const r = Math.max(0, Math.floor(Number(rung)) || 0);
  const semis = 12 * Math.floor(r / PENTATONIC.length) + PENTATONIC[r % PENTATONIC.length];
  return rootHz * Math.pow(2, semis / 12);
}

/** Level tilt: the ladder gets a touch quieter as it climbs, never brighter-and-louder. */
export function gainForRung(rung, base = POP_GAIN) {
  return base * Math.max(0.5, 1 - LADDER_TILT * Math.max(0, rung));
}

/** Depth 0..1 to the master low-pass cutoff. Exponential: equal steps sound equal. */
export function depthCutoffHz(depth) {
  return DEPTH_OPEN_HZ * Math.pow(DEPTH_DEEP_HZ / DEPTH_OPEN_HZ, clamp01(Number(depth) || 0));
}

/** Depth 0..1 to the drone bed multiplier. Linear: it is a gain. */
export function depthDroneMul(depth) {
  return 1 + DEPTH_DRONE_LIFT * clamp01(Number(depth) || 0);
}

/** Hold progress 0..1 to the rung the held tone glides toward. */
export function holdRung(t) {
  return Math.min(HOLD_TOP_RUNG, Math.floor(clamp01(Number(t) || 0) * HOLD_TOP_RUNG + 1e-9));
}

/** Recap count-up progress 0..1 to its tick pitch: rising, always on the ladder. */
export function tickHz(t) {
  const span = TICK_TOP_RUNG - TICK_BASE_RUNG;
  return freqForRung(TICK_BASE_RUNG + Math.round(clamp01(Number(t) || 0) * span));
}

/** Countdown number to its note. 3, 2, 1 walk down toward the root; 0 (GO) is the root. Else null. */
export function countNotes(n) {
  const k = Math.floor(Number(n));
  if (k === 0) {
    return [
      { hz: RESOLVE_HZ, gain: COUNT_GO_GAIN, dur: COUNT_GO_DUR_S, at: 0 },
      { hz: LADDER_ROOT_HZ, gain: COUNT_GO_GAIN * 0.5, dur: COUNT_GO_DUR_S, at: 0 },
    ];
  }
  if (!(k >= 1 && k <= COUNT_RUNGS.length)) return null;
  return [{ hz: freqForRung(COUNT_RUNGS[k - 1]) / 2, gain: COUNT_GAIN, dur: COUNT_DUR_S, at: 0 }];
}

/**
 * A game moment (the `event` of a dtrh:feel 'moment') to its figure: a list of
 * { hz, gain, dur, at } notes, `at` in seconds from now. Unknown moments are [].
 */
export function momentNotes(event, data) {
  const g = MOMENT_GAIN, g2 = MOMENT_GAIN * MOMENT_SECOND, d = MOMENT_DUR_S, step = MOMENT_STEP_S;
  const note = (rung, gain, at = 0, dur = d) => ({ hz: freqForRung(rung), gain, dur, at });
  switch (event) {
    case 'rabbit-caught': return [note(3, g), note(5, g2, step)];              // G4 up to C5
    case 'darter-caught': return [note(6, g2, 0, d * 0.6)];                    // one quick D5
    case 'gold-first': return [note(5, g), note(7, g2, step)];                 // C5 up to E5
    case 'combo-milestone':
    case 'combo-big': {
      const rung = rungForStreak(data && data.combo);
      const out = [note(rung, g, 0, MILESTONE_DUR_S), note(rung + MILESTONE_FIFTH_RUNGS, g2, 0, MILESTONE_DUR_S)];
      if (event === 'combo-big') out.push(note(rung + 5, g2 * MOMENT_SECOND, step, MILESTONE_DUR_S));   // and its octave
      return out;
    }
    case 'boon-picked': return [note(0, g, 0, d * 1.4), note(2, g2, 0.06, d * 1.4)];   // root + third
    case 'curse-picked': return [
      { hz: RESOLVE_HZ, gain: CURSE_GAIN, dur: CURSE_DUR_S, at: 0 },
      { hz: RESOLVE_HZ * Math.pow(2, CURSE_THIRD_SEMIS / 12), gain: CURSE_GAIN * MOMENT_SECOND, dur: CURSE_DUR_S, at: 0.06 },
    ];
    case 'wave-cleared': return [note(3, g), note(2, g2, step * 1.5)];         // G4 down to E4
    default: return [];
  }
}

// ---- the voices ----------------------------------------------------------------
/**
 * @param {object} io
 * @param {() => AudioContext|null} io.getCtx      the shared context
 * @param {() => AudioNode|null}    io.getOut      the master out (falls back to destination)
 * @param {() => boolean}           io.silent      muted or hard-ducked: play nothing
 * @param {() => number}            io.level       the effects slider, 0..1
 * @param {() => boolean}           [io.voiceActive] a voice line is speaking: sit under it
 * @param {() => number}            [io.now]       ms clock
 */
export function createFeelAudio(io) {
  const now = io.now || (() => performance.now());
  let lastPop = -1e9, lastArrive = -1e9, lastTick = -1e9, lastCount = -1e9, lastResolve = -1e9, dead = false;
  let hold = null;               // the one held tone: { osc, env, ducked }
  const lastMoment = new Map();  // event -> ms, so one moment is one figure

  const speaking = () => !!(io.voiceActive && io.voiceActive());
  /** A quoted gain at the player's effects slider and under the voice. 0 = play nothing. */
  function levelled(gain) {
    if (dead || io.silent()) return 0;
    const slider = Math.max(0, Number(io.level()) || 0) / FX_SLIDER_DEFAULT;
    let g = gain * Math.min(2, slider);
    if (speaking()) g *= VOICE_DUCK;
    return g > 0.0005 ? g : 0;
  }

  function bell(hz, gain, dur, at = 0) {
    const g = levelled(gain);
    if (!g) return false;
    const c = io.getCtx();
    if (!c || c.state !== 'running') return false;   // never queue notes into a sleeping context
    try {
      const t = c.currentTime + Math.max(0, at);
      const env = c.createGain();
      env.gain.setValueAtTime(0.0001, t);
      env.gain.linearRampToValueAtTime(g, t + ATTACK_S);
      env.gain.exponentialRampToValueAtTime(0.0001, t + dur);
      env.connect(io.getOut() || c.destination);
      const part = c.createGain();
      part.gain.value = PARTIAL_GAIN;
      part.connect(env);
      const a = c.createOscillator(); a.type = 'sine'; a.frequency.value = hz;
      const b = c.createOscillator(); b.type = 'sine'; b.frequency.value = hz * 2;
      a.connect(env); b.connect(part);
      a.onended = () => {
        try { a.disconnect(); b.disconnect(); part.disconnect(); env.disconnect(); } catch (e) { /* ignore */ }
      };
      a.start(t); b.start(t);
      a.stop(t + dur + 0.02); b.stop(t + dur + 0.02);
      return true;
    } catch (e) { return false; }
  }

  return {
    /** One pop. `streak` is the streak AFTER this pop; `gold` = a special that does not climb. */
    pop(streak, opts) {
      const t = now();
      if (t - lastPop < POP_GAP_MS) return false;
      lastPop = t;
      const rung = rungForStreak(streak);
      if (opts && opts.gold) return bell(freqForRung(rung) * 2, gainForRung(rung, GOLD_GAIN), POP_DUR_S);
      return bell(freqForRung(rung), gainForRung(rung), POP_DUR_S);
    },
    /** A reward landed in its HUD slot. */
    arrive() {
      const t = now();
      if (t - lastArrive < ARRIVE_GAP_MS) return false;
      lastArrive = t;
      return bell(freqForRung(ARRIVE_RUNG), ARRIVE_GAIN, ARRIVE_DUR_S);
    },
    /** A landing opened: the ladder comes home to one low root. */
    resolve() { return bell(RESOLVE_HZ, RESOLVE_GAIN, RESOLVE_DUR_S); },
    /** A game moment: its small figure on the ladder. Unknown moments are silent. */
    moment(event, data) {
      const notes = momentNotes(event, data);
      if (!notes.length) return false;
      const t = now();
      // a big streak and its milestone land on the same pop: one figure (the big one fires first)
      const key = event === 'combo-big' || event === 'combo-milestone' ? 'combo' : event;
      if (t - (lastMoment.get(key) || -1e9) < MOMENT_GAP_MS) return false;
      lastMoment.set(key, t);
      let any = false;
      for (const n of notes) any = bell(n.hz, n.gain, n.dur, n.at) || any;
      return any;
    },
    /** Recap count-up: `t` 0..1 is how far the count has run. Rate-limited. */
    tick(t) {
      const ms = now();
      if (ms - lastTick < TICK_GAP_MS) return false;
      lastTick = ms;
      return bell(tickHz(t), TICK_GAIN, TICK_DUR_S);
    },
    /** Countdown: 3, 2, 1 are low soft notes, 0 is GO (the root). */
    count(n) {
      const notes = countNotes(n);
      if (!notes) return false;
      const ms = now();
      if (ms - lastCount < COUNT_GAP_MS) return false;
      lastCount = ms;
      let any = false;
      for (const k of notes) any = bell(k.hz, k.gain, k.dur, k.at) || any;
      return any;
    },
    /** A live bubble is being held: one very quiet sine swells in. A second start is a progress. */
    holdStart(t = 0) {
      if (hold) return this.holdProgress(t);
      const g = levelled(HOLD_GAIN);
      if (!g) return false;
      const c = io.getCtx();
      if (!c || c.state !== 'running') return false;
      try {
        const at = c.currentTime;
        const env = c.createGain();
        env.gain.setValueAtTime(0.0001, at);
        env.gain.linearRampToValueAtTime(g, at + HOLD_ATTACK_S);
        env.gain.setTargetAtTime(0.0001, at + HOLD_MAX_S, HOLD_RELEASE_TC);   // an end that never comes
        env.connect(io.getOut() || c.destination);
        const osc = c.createOscillator(); osc.type = 'sine';
        osc.frequency.value = freqForRung(holdRung(t));
        osc.connect(env);
        osc.onended = () => { try { osc.disconnect(); env.disconnect(); } catch (e) { /* ignore */ } };
        osc.start(at);
        osc.stop(at + HOLD_MAX_S + HOLD_RELEASE_TC * 8);
        hold = { osc, env, ducked: speaking() };
        return true;
      } catch (e) { hold = null; return false; }
    },
    /** Hold progress 0..1: the tone glides up the ladder. Muting mid-hold lets it go. */
    holdProgress(t) {
      if (!hold) return false;
      if (dead || io.silent()) { this.holdEnd(false); return false; }
      const c = io.getCtx();
      if (!c) return false;
      try {
        hold.osc.frequency.setTargetAtTime(freqForRung(holdRung(t)), c.currentTime, HOLD_GLIDE_TC);
        const ducked = speaking();
        if (ducked !== hold.ducked) {           // the voice started or stopped over a hold
          hold.ducked = ducked;
          const g = levelled(HOLD_GAIN);
          hold.env.gain.setTargetAtTime(g || 0.0001, c.currentTime, HOLD_ATTACK_S / 2);
        }
      } catch (e) { /* ignore */ }
      return true;
    },
    /** The hold is over. `resolved` = it was defused: the tone lands on the octave root. */
    holdEnd(resolved) {
      const h = hold;
      hold = null;
      if (h) {
        try {
          const c = io.getCtx();
          const at = c ? c.currentTime : 0;
          h.env.gain.cancelScheduledValues(at);
          h.env.gain.setTargetAtTime(0.0001, at, HOLD_RELEASE_TC);
          h.osc.stop(at + HOLD_RELEASE_TC * 8);
        } catch (e) { /* ignore */ }
      }
      if (resolved) this.holdResolve();
      return !!h;
    },
    /** The resolve on its own (a defuse with no hold events). One note per defuse. */
    holdResolve() {
      const ms = now();
      if (ms - lastResolve < HOLD_RESOLVE_GAP_MS) return false;
      lastResolve = ms;
      return bell(freqForRung(HOLD_RESOLVE_RUNG), HOLD_RESOLVE_GAIN, HOLD_RESOLVE_DUR_S);
    },
    holdActive() { return !!hold; },
    dispose() { this.holdEnd(false); dead = true; },
  };
}

// ---- the seam ------------------------------------------------------------------
/**
 * Wire a feel (createFeelAudio) to the `dtrh:feel` events it does not already get
 * from chaosRun: moment, phase, hold, tick, count. (pop and arrive stay where
 * wave 1 put them, so nothing sounds twice.) Every kind may never arrive.
 * Returns a detach function; calling it also lets go of a held tone.
 */
export function attachFeelEvents(feel, target = (typeof window !== 'undefined' ? window : null)) {
  if (!feel || !target || typeof target.addEventListener !== 'function') return () => {};
  let goOwed = false;   // a countdown started and its GO has not sounded yet
  const onFeel = (e) => {
    try {
      const d = e && e.detail;
      if (!d) return;
      switch (d.kind) {
        case 'moment':
          if (d.event === 'defused') { if (!feel.holdEnd(true)) feel.holdResolve(); }
          else if (d.event === 'defuse-release' || d.event === 'detonated' || d.event === 'click-detonate') feel.holdEnd(false);
          else feel.moment(d.event, d);
          break;
        case 'hold':
          if (d.phase === 'start') feel.holdStart(d.t);
          else if (d.phase === 'progress') { if (!feel.holdProgress(d.t) && !feel.holdActive()) feel.holdStart(d.t); }
          else if (d.phase === 'end') feel.holdEnd(!!(d.ok || d.defused || d.how === 'defused'));
          break;
        case 'tick': feel.tick(d.t); break;
        case 'count':
          if (Math.floor(Number(d.n)) === 0) goOwed = false;
          feel.count(d.n);
          break;
        case 'phase':
          if (d.state === 'surfacing') break;   // progress pings of the melt, not a change of phase
          feel.holdEnd(false);   // no hold outlives its phase
          if (d.state === 'countdown') goOwed = true;
          else if (d.state === 'running' && goOwed) { goOwed = false; feel.count(0); }
          else goOwed = false;
          break;
        default: break;
      }
    } catch (err) { /* a sound must never break the game */ }
  };
  target.addEventListener('dtrh:feel', onFeel);
  return () => {
    try { target.removeEventListener('dtrh:feel', onFeel); } catch (e) { /* ignore */ }
    try { feel.holdEnd(false); } catch (e) { /* ignore */ }
  };
}
