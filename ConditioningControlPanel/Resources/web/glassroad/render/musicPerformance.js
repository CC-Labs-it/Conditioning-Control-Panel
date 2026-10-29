// Musical expression from the existing heard-song tap. Midrange prominence is not instrument isolation.
import { lastAtOrBefore, sectionOf } from '../audio/chart.js';

const NONE = Object.freeze({});
const unit = v => Number.isFinite(v) ? Math.max(0, Math.min(1, v)) : 0;
const AGES = Object.freeze(['pluckAge0', 'pluckAge1', 'pluckAge2', 'pluckAge3']);
const STRENGTHS = Object.freeze(['pluckStrength0', 'pluckStrength1', 'pluckStrength2', 'pluckStrength3']);
export const PERFORMANCE_FIELDS = Object.freeze(['drop', 'lead', 'dropAge', 'dropCount', ...AGES, ...STRENGTHS]);
export const MUSIC_PERFORMANCE = Object.freeze({ dropCooldownS: 2.75, quietArmS: .45,
  sustainS: .12, gentleRiseS: .35, gentleGain: .4 });

export function createMusicPerformance() {
  const v = { active: false, song: null, lastS: NaN, drop: 0, lead: 0, dropAge: 9, dropCount: 0,
    dropS: -9, dropPeak: 0, energyFast: 0, energySlow: 0, quietS: 0, quietLevel: 0,
    armedUntil: -9, candidateS: 0, stableS: 0, silenceStartedS: NaN, silenceLastS: NaN, activity: 0, lastPluckS: -9, nextString: 0,
    previousAttack: 0, baseline: new Float32Array(3), previousBins: new Float32Array(16),
    pluckS: new Float64Array(4).fill(-9), pluckPeak: new Float32Array(4) };
  for (let i = 0; i < 4; i++) { v[AGES[i]] = 9; v[STRENGTHS[i]] = 0; }
  return v;
}

function reset(v) {
  v.active = false; v.lastS = NaN; v.drop = v.lead = v.dropCount = v.dropPeak = 0;
  v.dropAge = 9; v.dropS = -9; v.energyFast = v.energySlow = v.quietS = v.quietLevel = 0;
  v.armedUntil = v.lastPluckS = -9; v.candidateS = v.activity = v.previousAttack = v.nextString = 0;
  v.stableS = 0; v.silenceStartedS = v.silenceLastS = NaN;
  v.baseline.fill(0); v.previousBins.fill(0); v.pluckS.fill(-9); v.pluckPeak.fill(0);
  for (let i = 0; i < 4; i++) { v[AGES[i]] = 9; v[STRENGTHS[i]] = 0; }
}

function chartDrop(song, before, t) {
  let strength = 0;
  if (song?.triggerStarts && song.triggers) {
    let i = lastAtOrBefore(song.triggerStarts, t);
    for (let n = 0; i >= 0 && n < 8 && song.triggerStarts[i] > before; i--, n++) {
      const hit = song.triggers[i];
      if (hit.kind === 'drop') strength = Math.max(strength, unit(hit.strength));
    }
  }
  const section = song?.sections ? sectionOf(song, t) : null;
  if (section && section.startS > before && section.startS <= t && (section.mood === 'drop' || section.mood === 'peak'))
    strength = Math.max(strength, unit(section.energy));
  return strength;
}

function chartAttack(song, kind, before, t) {
  const hits = song?.hits?.[kind];
  if (!hits?.t?.length) return 0;
  const i = lastAtOrBefore(hits.t, t);
  return i >= 0 && hits.t[i] > before ? unit(hits.v[i]) : 0;
}

/** Mutates fixed storage. Event ages and detection follow heard song seconds, never simulation time. */
export function stepMusicPerformance(v, signal, dt, options = NONE) {
  if (options.paused || signal?.frozen) return v;
  const song = options.song || null;
  const energy = unit(signal?.rawEnergy ?? signal?.energy), bass = unit(signal?.rawBass ?? signal?.bass);
  const mid = unit(signal?.rawMid ?? signal?.mid), high = unit(signal?.rawTreble ?? signal?.treble);
  if (signal?.silent && !signal.muted && !options.muted && options.motion !== 'off' && Number.isFinite(signal.songS)) {
    const t = Math.max(0, signal.songS);
    const continuation = v.song === song && Number.isFinite(v.silenceLastS) && t >= v.silenceLastS && t - v.silenceLastS <= .5;
    const start = continuation ? v.silenceStartedS : t;
    reset(v); v.song = song; v.silenceStartedS = start; v.silenceLastS = t; return v;
  }
  if (!signal?.active || signal.muted || options.muted || options.motion === 'off' || !Number.isFinite(signal.songS)
    || Math.max(energy, bass, mid, high) < 1e-6) {
    reset(v); v.song = song; return v;
  }
  const t = Math.max(0, signal.songS), elapsed = t - v.lastS;
  if (!v.active || v.song !== song || elapsed < 0 || elapsed > .5) {
    const reentry = v.song === song && Number.isFinite(v.silenceStartedS) && t >= v.silenceLastS && t - v.silenceLastS <= .5
      && t - v.silenceStartedS >= MUSIC_PERFORMANCE.quietArmS;
    reset(v); v.song = song; v.active = true; v.lastS = t;
    v.energyFast = v.energySlow = v.quietLevel = energy;
    if (reentry) { v.quietLevel = 0; v.energySlow = energy * .2; v.armedUntil = t + 1.5; }
    v.baseline[0] = bass; v.baseline[1] = mid; v.baseline[2] = high;
    for (let i = 0; i < 16; i++) v.previousBins[i] = unit(signal.spectrum?.[20 + i * 2]);
    return v; // Loading or seeking into a loud phrase is not a musical attack.
  }
  if (!(elapsed > 0)) return v;
  const before = v.lastS, step = Math.min(.1, elapsed, Math.max(0, Number(dt) || 0));
  v.lastS = t;
  const gentle = !!options.gentle, gain = (gentle ? MUSIC_PERFORMANCE.gentleGain : 1) * (options.motion === 'less' ? .5 : 1);
  v.energyFast += (energy - v.energyFast) * (1 - Math.exp(-step / .075));
  v.energySlow += (energy - v.energySlow) * (1 - Math.exp(-step / 2));
  // Require a sustained quieter stretch before a measured re-entry. Single kicks cannot arm a drop.
  if (v.energyFast < Math.max(.008, v.energySlow * .66)) {
    v.quietS += step; v.quietLevel += (v.energyFast - v.quietLevel) * (1 - Math.exp(-step / .16));
    if (v.quietS >= MUSIC_PERFORMANCE.quietArmS) v.armedUntil = t + 1.5;
  } else v.quietS = 0;
  v.stableS = v.energyFast < v.energySlow * 1.08 ? v.stableS + step : 0;
  if (v.stableS > .6 && v.quietS === 0 && t > v.armedUntil) { v.quietLevel = v.energySlow; v.armedUntil = t + 1.5; }
  const contrast = v.energyFast - v.quietLevel;
  const reentry = t < v.armedUntil && v.energyFast > Math.max(.035, v.energySlow * 1.22)
    && contrast > Math.max(.025, v.quietLevel * .6) && v.energyFast > v.quietLevel * 1.65;
  v.candidateS = reentry ? v.candidateS + step : 0;
  const marked = chartDrop(song, before, t);
  const dropHit = t - v.dropS >= MUSIC_PERFORMANCE.dropCooldownS
    && (marked > 0 || v.candidateS >= MUSIC_PERFORMANCE.sustainS);
  if (dropHit) {
    v.dropS = t; v.dropPeak = .86 + .14 * Math.max(marked, unit(contrast * 2));
    v.dropCount = (v.dropCount + 1) % 4096; v.armedUntil = -9; v.candidateS = 0;
    for (let i = 0; i < 4; i++) { v.pluckS[i] = t; v.pluckPeak[i] = v.dropPeak; }
    v.lastPluckS = t;
  }

  const bassFlux = Math.max(0, bass - v.baseline[0] * 1.08 - .012);
  const midFlux = Math.max(0, mid - v.baseline[1] * 1.06 - .01);
  let spectralFlux = 0, spectralLevel = 0;
  for (let i = 0; i < 16; i++) {
    const value = unit(signal.spectrum?.[20 + i * 2]);
    spectralFlux += Math.max(0, value - v.previousBins[i]); spectralLevel += value;
    v.previousBins[i] = value;
  }
  spectralFlux = spectralLevel > .08 ? spectralFlux / spectralLevel : 0;
  const activity = unit(Math.max(midFlux * 1.4 / (v.baseline[1] + .045), spectralFlux * 1.6));
  v.activity = Math.max(activity, v.activity * Math.exp(-step / .48));
  const prominence = unit((mid / (mid + bass + high * .6 + .008) - .36) / .36);
  const leadTarget = Math.sqrt(mid / (mid + .055)) * prominence * (.22 + .78 * v.activity) * gain;
  v.lead = Math.min(v.lead, gain);
  v.lead += (leadTarget - v.lead) * (1 - Math.exp(-step / (leadTarget > v.lead ? (gentle ? .35 : .07) : .28)));
  const attack = unit(Math.max(midFlux * .9 / (v.baseline[1] + .04), spectralFlux * 1.5, bassFlux * .65 / (v.baseline[0] + .06)));
  const chart = Math.max(chartAttack(song, 'snare', before, t) * .7, chartAttack(song, 'kick', before, t) * .4);
  if (!dropHit && t - v.lastPluckS > .16 && ((attack > .17 && v.previousAttack <= .17) || chart > .1)) {
    const i = v.nextString;
    v.pluckS[i] = t; v.pluckPeak[i] = Math.min(.82, .18 + Math.max(attack, chart) * .4 + v.lead * .24);
    v.nextString = (i + 1) % 4; v.lastPluckS = t;
  }
  v.previousAttack = attack;
  const adapt = 1 - Math.exp(-step / .16);
  v.baseline[0] += (bass - v.baseline[0]) * adapt; v.baseline[1] += (mid - v.baseline[1]) * adapt;
  v.baseline[2] += (high - v.baseline[2]) * adapt;
  v.dropAge = Math.max(0, Math.min(9, t - v.dropS));
  const dropTarget = v.dropPeak * Math.exp(-v.dropAge / (gentle ? 1.25 : .85)) * gain;
  v.drop = Math.min(v.drop, gain);
  v.drop += (dropTarget - v.drop) * (1 - Math.exp(-step / (dropTarget > v.drop ? (gentle ? .35 : .025) : .22)));
  for (let i = 0; i < 4; i++) {
    v[AGES[i]] = Math.max(0, Math.min(9, t - v.pluckS[i]));
    const target = v[AGES[i]] < 9 ? v.pluckPeak[i] * gain : 0;
    v[STRENGTHS[i]] = gentle ? Math.min(gain, v[STRENGTHS[i]]) + (target - Math.min(gain, v[STRENGTHS[i]]))
      * (1 - Math.exp(-step / .35)) : target;
  }
  return v;
}
