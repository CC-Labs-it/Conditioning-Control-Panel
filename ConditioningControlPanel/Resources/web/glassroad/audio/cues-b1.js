/* audio/cues-b1.js - the B1 cue registries (CONTRACT-B1 sections 3 and 4).
 *
 * B1_CUES: bus event name -> (syn, payload) => void, subscribed by audio/bus.js. Before each
 * call the bus sets syn.me (racer 0 is named, or a local event), syn.volume (1 for the player,
 * a distance level for a near bot) and syn.pan. UI_CUES: menu blips, played through audio.cue().
 * Every sound is a synthesised oscillator or noise figure on the engine's root. No speech of any
 * kind. Impacts play at once; beds under a screen effect sit under the quietest engine level.
 */
import { AUDIO_CUES } from './cues.js';
import { mixGet, rollTickTimes, ladderSemis } from './mix.js';

const fg = (syn, path, def) => { try { return syn.feel.get(path); } catch (e) { return def; } };
const m = (syn, k) => mixGet(syn.feel, k);
const st = (syn) => (syn.b1 || (syn.b1 = { popRung: -1, popT: -1e9 }));
const now = (syn) => syn.ctx.currentTime;

/** The DtRH pop: a short bell on a rung of the ladder plus a breath of bright noise. */
function pop(syn, semis = 12, vol = 0.1) {
  syn.tone(syn.semis(semis), { type: 'sine', dur: 0.26, vol, attack: 0.003 });
  syn.noise({ dur: 0.05, vol: vol * 0.5, freq: 4200, q: 1.4 });
}

/** One hit figure per effect kind (the victim's ear). */
const HIT = {
  flash(syn) { syn.tone(syn.semis(31), { type: 'sine', dur: 0.35, vol: 0.07 }); syn.noise({ dur: 0.25, vol: 0.05, freq: 6000, q: 0.8 }); },
  spiral(syn) {
    syn.tone(syn.semis(12), { type: 'sine', dur: 0.5, vol: 0.07, glide: syn.semis(19) });
    syn.tone(syn.semis(19), { type: 'sine', dur: 0.5, vol: 0.05, glide: syn.semis(12), at: 0.12 });
  },
  brainDrain(syn) {
    syn.tone(syn.semis(0), { type: 'triangle', dur: 0.7, vol: 0.09, glide: syn.semis(-12) });
    syn.noise({ dur: 0.5, vol: 0.05, freq: 900, sweep: 200, q: 0.6 });
  },
  freeze(syn) {
    for (const [s, at] of [[24, 0], [31, 0.04], [36, 0.09]]) syn.tone(syn.semis(s), { type: 'sine', dur: 0.4, vol: 0.05, at });
    syn.noise({ dur: 0.3, vol: 0.04, freq: 7000, q: 3 });
  },
  blackout(syn) {
    syn.tone(syn.semis(-24), { type: 'sine', dur: 0.4, vol: 0.18, glide: syn.semis(-31) });
    syn.noise({ dur: 0.45, vol: 0.07, freq: 3200, sweep: 180, q: 0.7 });
  },
  pink(syn) { for (const s of [0, 4, 7]) syn.tone(syn.semis(s + 12), { dur: 0.6, vol: 0.05, attack: 0.04 }); },
  melt(syn) {
    syn.tone(syn.semis(12), { type: 'sine', dur: 0.35, vol: 0.08, glide: syn.semis(-5) });
    syn.tone(syn.semis(7), { type: 'sine', dur: 0.35, vol: 0.06, glide: syn.semis(-10), at: 0.22 });
  },
  shatter(syn) {                                   // the one real crash: thud, glass and a tumble
    syn.tone(syn.semis(-26), { type: 'sine', dur: 0.3, vol: 0.26, glide: syn.semis(-34) });
    syn.noise({ dur: 0.5, vol: 0.12, freq: 2600, q: 0.5 });
    for (let k = 0; k < 5; k++) syn.tone(syn.semis(36 - k * 3), { type: 'sine', dur: 0.12, vol: 0.04, at: 0.05 + k * 0.07 });
  },
};

/** A bed under the local screen effect, keyed 'effect-bed'; replaced on a new hit. */
const BED = {
  flash: { semis: 24, type: 'sine', lfoHz: 0, lfoCents: 0 },
  spiral: { semis: 7, type: 'triangle', lfoHz: 0.4, lfoCents: 35 },
  brainDrain: { semis: -12, type: 'triangle', lowpassHz: 500 },
  freeze: { semis: 19, type: 'sine', lfoHz: 5, lfoCents: 6 },
  blackout: { semis: -24, type: 'sine' },
  pink: { semis: 4, type: 'triangle', lfoHz: 0.25, lfoCents: 12 },
  melt: { semis: -5, type: 'triangle', lfoHz: 0.6, lfoCents: 60, lowpassHz: 700 },
};

/** Self items and the drop figure, by id or mode. */
const FIRE = {
  throw(syn) { syn.noise({ dur: 0.3, vol: 0.09, freq: 700, sweep: 3200, q: 1.2 }); syn.tone(syn.semis(7), { dur: 0.1, vol: 0.05 }); },
  lob(syn) { syn.tone(syn.semis(7), { type: 'sine', dur: 0.8, vol: 0.06, glide: syn.semis(31) }); syn.noise({ dur: 0.4, vol: 0.05, freq: 1200, sweep: 3800 }); },
  wide(syn) {
    syn.noise({ dur: 0.9, vol: 0.06, freq: 400, sweep: 2400, q: 0.5 });
    for (const [s, at] of [[0, 0], [4, 0.08], [7, 0.16], [12, 0.24]]) syn.tone(syn.semis(s + 12), { dur: 0.4, vol: 0.04, at });
  },
  drop(syn) {                                      // a trap set down: a soft wet plop
    syn.tone(syn.semis(-12), { type: 'sine', dur: 0.18, vol: 0.12, glide: syn.semis(-24) });
    syn.noise({ dur: 0.1, vol: 0.04, freq: 500, q: 1 });
  },
  surge(syn) {
    syn.noise({ dur: 0.7, vol: 0.12, freq: 350, sweep: 4200, q: 0.8 });
    syn.tone(syn.semis(0), { type: 'sawtooth', dur: 0.5, vol: 0.04, glide: syn.semis(19) });
  },
  glass(syn) { syn.tone(syn.semis(24), { type: 'sine', dur: 0.6, vol: 0.06, glide: syn.semis(31) }); syn.tone(syn.semis(36), { type: 'sine', dur: 0.5, vol: 0.03, at: 0.1 }); },
  echo(syn) { for (const at of [0, 0.11, 0.22]) syn.tone(syn.semis(19), { type: 'sine', dur: 0.1, vol: 0.05 * (1 - at * 2), at }); },
  golden(syn) {                                    // Golden shimmer: a fast rising arpeggio and air
    [0, 4, 7, 12, 16, 19].forEach((s, k) => syn.tone(syn.semis(s + 24), { type: 'sine', dur: 0.4, vol: 0.04, at: k * 0.05 }));
    syn.noise({ dur: 0.8, vol: 0.03, freq: 8000, q: 2 });
  },
};

/** Phase A cues a near bot may echo, quieter and panned. Held cues (edge, rescue) never. */
export const BOT_ECHO = ['hop', 'land', 'wall', 'drift-start', 'boost-start', 'pad'];

export const B1_CUES = {
  'item-box'(syn, { racer }) {
    syn.noise({ dur: 0.12, vol: 0.06, freq: 2400, sweep: 900, q: 1.5 });    // the card flips open
    if (racer !== 0) return;
    const rollS = fg(syn, 'items.rollS', 1);
    rollTickTimes(m(syn, 'rollTicks'), rollS).forEach((at, k) =>
      syn.tone(syn.semis(k % 2 ? 19 : 24), { type: 'square', dur: 0.03, vol: 0.025, at }));
  },
  'item-roll'(syn, { racer, id }) {
    if (racer !== 0) return;
    const rare = id === 'shatter' || id === 'golden' || id === 'blackout';
    const self = id === 'surge' || id === 'glass' || id === 'echo' || id === 'golden';
    const fig = rare ? [12, 19, 24] : self ? [12, 16] : [12, 19];
    fig.forEach((s, k) => syn.tone(syn.semis(s), { type: 'sine', dur: 0.2, vol: 0.08, at: k * 0.07 }));
  },
  'item-fire'(syn, p) {
    const { racer, id, mode, target } = p;
    if (racer !== 0 && target === 0 && (mode === 'throw' || mode === 'lob')) {
      // incoming: something is coming at you (target 0 makes it full volume)
      syn.tone(syn.semis(19), { type: 'sine', dur: 0.25, vol: 0.05, glide: syn.semis(24) });
      return;
    }
    const f = (mode === 'self' ? FIRE[id] : FIRE[mode]) || FIRE.throw;
    f(syn);
  },
  'item-refused'(syn, { racer }) {
    if (racer !== 0) return;
    syn.tone(syn.semis(-7), { type: 'square', dur: 0.06, vol: 0.04 });
    syn.tone(syn.semis(-8), { type: 'square', dur: 0.06, vol: 0.04, at: 0.09 });
  },
  'item-hit'(syn, { victim, by, kind, verdict }) {
    const mine = victim === 0;
    if (verdict === 'glass') {                      // Glass soaks it: a bright ring
      syn.tone(syn.semis(31), { type: 'sine', dur: 0.9, vol: 0.07 });
      syn.tone(syn.semis(38), { type: 'sine', dur: 0.7, vol: 0.04, at: 0.03 });
      syn.noise({ dur: 0.15, vol: 0.05, freq: 6500, q: 2 });
      return;
    }
    if (verdict === 'golden') { [28, 35].forEach((s, k) => syn.tone(syn.semis(s), { type: 'sine', dur: 0.5, vol: 0.06, at: k * 0.05 })); return; }
    if (verdict === 'decoy') { syn.tone(syn.semis(-5), { dur: 0.12, vol: 0.06 }); pop(syn, 19, 0.05); return; }
    if (verdict === 'mercy') { pop(syn, 12, 0.04); return; }
    pop(syn, mine ? 12 : 19, mine ? 0.1 : 0.07);
    if (mine || (by === 0 && kind === 'shatter')) HIT[kind]?.(syn);
    else if (by === 0) syn.tone(syn.semis(24), { type: 'sine', dur: 0.15, vol: 0.05, at: 0.06 });   // yours landed
  },
  // a word bubble taken (sim/words.js): the synth twin of sfxmap.js wordPopPlan
  'word-pop'(syn, { streak = 1, row = 0, lineDone = false }) {
    const s = 12 + ladderSemis(Math.max(0, (streak | 0) - 1) % m(syn, 'popRungs'));
    pop(syn, s, row ? 0.1 : 0.08);
    if (row) syn.tone(syn.semis(s + 7), { type: 'sine', dur: 0.3, vol: 0.05, at: 0.04 });
    if (lineDone) syn.tone(syn.semis(s + 12), { type: 'sine', dur: 0.4, vol: 0.05, at: 0.08 });
  },
  'bubble-pop'(syn, { racer }) {
    if (racer !== 0) { pop(syn, 12, 0.05); return; }
    const s = st(syn), t = now(syn);
    s.popRung = t - s.popT < m(syn, 'popStreakS') ? Math.min(s.popRung + 1, m(syn, 'popRungs') - 1) : 0;
    s.popT = t;
    pop(syn, 12 + ladderSemis(s.popRung), 0.08);
  },
  'effect-start'(syn, { kind, durationS }) {
    syn.duck?.(durationS);
    const b = BED[kind];
    syn.release('effect-bed', 0.12);
    if (!b) return;
    syn.hold('effect-bed', syn.semis(b.semis), {
      type: b.type, vol: m(syn, 'effectBedGain'), attack: Math.max(0.3, fg(syn, 'items.effectInS', 0.2)),
      lfoHz: b.lfoHz || 0, lfoCents: b.lfoCents || 0, lowpassHz: b.lowpassHz || 0,
    });
  },
  'effect-end'(syn, { replaced }) {
    syn.release('effect-bed', replaced ? 0.12 : fg(syn, 'items.effectOutS', 0.4));
  },
  bump(syn, { dv = 0 }) {
    const min = fg(syn, 'contact.cueMinDvMps', 1.5);
    if (!(dv >= min)) return;
    const v = Math.min(1, dv / m(syn, 'bumpFullDvMps'));
    syn.tone(syn.semis(-22), { type: 'sine', dur: 0.14, vol: 0.22 * v, glide: syn.semis(-29) });
    syn.noise({ dur: 0.09, vol: 0.08 * v, freq: 700, q: 0.9 });
    syn.tone(syn.semis(5), { type: 'square', dur: 0.025, vol: 0.02 * v });
  },
  slipstream(syn, { racer, state }) {
    if (racer !== 0) return;
    if (state === 'in') syn.holdNoise('slip-air', { freq: 2200, q: 0.5, vol: m(syn, 'slipAirGain'), attack: 0.4 });
    else syn.release('slip-air', state === 'fire' ? 0.08 : 0.35);
  },
  overtake(syn, { racer, passed }) {
    if (racer === 0) { syn.tone(syn.semis(12), { type: 'sine', dur: 0.12, vol: 0.07 }); syn.tone(syn.semis(19), { type: 'sine', dur: 0.2, vol: 0.06, at: 0.07 }); }
    else if (passed === 0) syn.tone(syn.semis(7), { type: 'sine', dur: 0.14, vol: 0.04, glide: syn.semis(5) });
  },
  'place-change'(syn, { racer, from, to }) {
    if (racer !== 0 || to !== 1 || from === 1) return;          // overtake covers the rest
    [0, 7, 12, 19].forEach((s, k) => syn.tone(syn.semis(s + 12), { type: 'sine', dur: 0.3, vol: 0.05, at: k * 0.06 }));
  },
  'race-result'(syn, r) {
    const place = r?.me?.place || 8;
    const fig = place === 1 ? [0, 4, 7, 12, 16] : place <= 3 ? [0, 4, 7, 12] : [0, 3, 7];
    fig.forEach((s, k) => syn.tone(syn.semis(s + 12), { type: 'triangle', dur: 0.6, vol: 0.06, attack: 0.02, at: k * 0.09 }));
  },
  'racer-event'(syn, { name, p }) {
    if (!BOT_ECHO.includes(name)) return;         // the bus already scaled volume by distance
    AUDIO_CUES[name]?.(syn, p || {});
  },
};

/** Menu blips (hud on(...) handlers call audio.cue(name)). Not bus events. */
export const UI_CUES = {
  'menu-move'(syn) { syn.tone(syn.semis(19), { type: 'sine', dur: 0.04, vol: 0.04 }); },
  'menu-ok'(syn) { syn.tone(syn.semis(12), { type: 'sine', dur: 0.08, vol: 0.06 }); syn.tone(syn.semis(19), { type: 'sine', dur: 0.12, vol: 0.05, at: 0.05 }); },
  'menu-back'(syn) { syn.tone(syn.semis(12), { type: 'sine', dur: 0.08, vol: 0.05, glide: syn.semis(7) }); },
  'menu-toggle'(syn) { syn.tone(syn.semis(16), { type: 'square', dur: 0.03, vol: 0.03 }); },
  'menu-deny'(syn) { syn.tone(syn.semis(-5), { type: 'square', dur: 0.05, vol: 0.03 }); },
};
