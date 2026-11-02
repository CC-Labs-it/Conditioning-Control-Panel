// One small PCM/spectrum/history atlas per world. No recordings or textures accumulate across races.
import { hitEnv, sectionOf, lastAtOrBefore } from '../audio/chart.js';
export const VISUAL_DATA = Object.freeze({ width: 64, height: 18, historyHz: 20 });
const unit = v => Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
const EVENTS = ['kick', 'snare', 'hats'];
export const INSTRUMENT_FIELDS = Object.freeze(EVENTS.map(name => Object.freeze({ age: name + 'Age', strength: name + 'Strength', count: name + 'Count' })));
const REFRACTORY = [.16, .14, .055];
// Gentle: a section change walks the floor phrase over GENTLE_SECTION_S, so floorPhrase crossfades the patterns
// (about 1 s) instead of swapping the whole floor in one frame. gentleMix fades the Gentle bass layer in.
export const GENTLE_SECTION_S = 8, GENTLE_RISE_S = .5;
export function createMusicVisualData() {
  const data = new Uint8Array(64 * 18 * 4);
  for (let i = 0; i < 64; i++) data[i * 4] = 128;
  const v = { data, drive: 0, kick: 0, snare: 0, hats: 0, onset: 0, hitAge: 9, hitStrength: 0,
    section: 0, sectionTarget: 0, gentleMix: 0, lastS: NaN, historyS: NaN, lastHitS: -9, song: null, active: false,
    baseline: new Float32Array(3), flux: new Float32Array(3), chartHit: new Float64Array(3).fill(-9), eventS: new Float64Array(3).fill(-9), peaks: new Float32Array(64) };
  for (const fields of INSTRUMENT_FIELDS) { v[fields.age] = 9; v[fields.strength] = v[fields.count] = 0; }
  return v;
}
function reset(v) {
  v.data.fill(0); for (let i = 0; i < 64; i++) v.data[i * 4] = 128;
  v.baseline.fill(0); v.flux.fill(0); v.chartHit.fill(-9); v.peaks.fill(0); v.drive = v.kick = v.snare = v.hats = v.onset = 0;
  v.hitAge = 9; v.hitStrength = 0; v.historyS = NaN; v.lastHitS = -9;
  v.eventS.fill(-9);
  for (const fields of INSTRUMENT_FIELDS) { v[fields.age] = 9; v[fields.strength] = v[fields.count] = 0; }
}
/** Full snaps to a new section (strong accents); Gentle, or Full still catching up after Gentle, walks there. */
function stepSection(v, target, step, gentle, restarted) {
  const settled = v.section === v.sectionTarget;
  v.sectionTarget = target;
  if (!gentle && (restarted || settled)) { v.section = target; return; }
  const gap = target - v.section, walk = step / GENTLE_SECTION_S;
  v.section = Math.abs(gap) <= walk ? target : v.section + Math.sign(gap) * walk;
}
/** True means the 4.5 KB atlas changed. Pause is a complete hold; mute/silence clears it once. */
export function stepMusicVisualData(v, signal, dt, { paused = false, gentle = false, song = null } = {}) {
  if (paused || signal?.frozen) return false;
  if (!signal?.active || !Number.isFinite(signal.songS)) {
    const changed = v.active; if (changed) reset(v); v.active = false; v.lastS = NaN; return changed;
  }
  const t = Math.max(0, signal.songS), elapsed = Number.isFinite(v.lastS) ? t - v.lastS : 0;
  const restarted = v.song !== song || elapsed < 0 || elapsed > .5;
  if (restarted) reset(v);
  v.song = song; v.lastS = t; v.active = true;
  const step = Math.max(0, Math.min(.1, Number(dt) || 0));
  const target = unit(signal.rawEnergy ?? signal.energy);
  v.drive += (target - v.drive) * (1 - Math.exp(-step / (target > v.drive ? .035 : .14)));
  let struck = 0;
  for (let i = 0; i < 3; i++) {
    const level = unit(i === 0 ? signal.rawBass ?? signal.bass : i === 1 ? signal.rawMid ?? signal.mid : signal.rawTreble ?? signal.treble);
    const flux = Math.max(0, level - v.baseline[i] * 1.18 - .015);
    v.baseline[i] += (level - v.baseline[i]) * (1 - Math.exp(-step / .16));
    const chart = song?.hits ? hitEnv(song, EVENTS[i], t, .012, i === 2 ? .065 : .13) : 0;
    const times = song?.hits?.[EVENTS[i]]?.t, index = times ? lastAtOrBefore(times, t + .012) : -1;
    const eventT = index >= 0 ? times[index] : -9;
    const attack = (flux > .08 && v.flux[i] <= .08) || (chart > .22 && eventT !== v.chartHit[i]);
    if (attack) struck = Math.max(struck, unit(flux * 3.5), chart);
    const fields = INSTRUMENT_FIELDS[i];
    // A hat never steals the bass ring's origin; close attacks keep their own short clocks.
    if (!gentle && attack && t - v.eventS[i] > REFRACTORY[i]) {
      v.eventS[i] = t; v[fields.strength] = Math.max(unit(flux * 3.5), chart);
      v[fields.count] = (v[fields.count] + 1) % 4096;
    }
    v[fields.age] = Math.max(0, Math.min(9, t - v.eventS[i]));
    v.flux[i] = flux; if (chart > .22) v.chartHit[i] = eventT;
    v[EVENTS[i]] = gentle ? 0 : Math.max(unit(flux * 3.5), chart, v[EVENTS[i]] * Math.exp(-step / .11));
  }
  v.onset = Math.max(v.kick, v.snare * .8, v.hats * .5);
  if (!gentle && struck > .22 && t - v.lastHitS > .18) {
    v.lastHitS = t; v.hitStrength = struck;
  }
  v.hitAge = Math.max(0, Math.min(9, t - v.lastHitS));
  const sectionNow = song?.sections ? Math.max(0, song.sections.indexOf(sectionOf(song, t))) : v.sectionTarget;
  stepSection(v, sectionNow, step, gentle, restarted);
  v.gentleMix = gentle ? Math.min(1, v.gentleMix + step / GENTLE_RISE_S) : 0;
  // Gentle holds the fast PCM/history picture; its slow level/tint controls remain live.
  if (gentle) return false;
  if (!Number.isFinite(v.historyS) || t - v.historyS >= 1 / VISUAL_DATA.historyHz) {
    v.data.copyWithin(64 * 4 * 2, 64 * 4, 64 * 4 * 17);
    for (let i = 0; i < 64; i++) v.data[(64 + i) * 4] = Math.round(Math.sqrt(unit(signal.spectrum?.[i])) * 255);
    v.historyS = t;
  }
  for (let i = 0; i < 64; i++) {
    const wave = Number.isFinite(signal.waveform?.[i]) ? signal.waveform[i] : 0;
    const spectrum = Math.sqrt(unit(signal.spectrum?.[i]));
    v.peaks[i] = Math.max(spectrum, v.peaks[i] - step * .65);
    const at = i * 4;
    v.data[at] = Math.round((Math.max(-1, Math.min(1, wave)) * .5 + .5) * 255);
    v.data[at + 1] = Math.round(spectrum * 255); v.data[at + 2] = Math.round(v.peaks[i] * 255); v.data[at + 3] = 255;
  }
  return true;
}
