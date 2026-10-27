// Compile song phrasing once. No analyser jitter or live pattern switches enter collision.
import { lastAtOrBefore, sectionOf } from '../audio/chart.js';
const cache = new WeakMap();
// Two closes per 16-beat phrase; the global spacing below thins any pair that lands too close.
const profiles = { threshold: [4, 12], keep: [6], mainframe: [2, 10], boudoir: [6, 14], goonworks: [4, 12] };
/** Beats of visible warning before a close (owner, 2026-10-27: traps telegraph; the stroke itself is unchanged). */
export const WARN_BEATS = 4;
/** Minimum beats between two closes: hold 2 + rise 1 + warn 4 always leaves a whole clear beat (5 s per cycle at 96). */
export const SPACING_BEATS = 8;
const clamp = x => Math.max(0, Math.min(1, x));
const ease = x => { x = clamp(x); return x * x * (3 - 2 * x); };
const stretchOf = beatS => Math.max(1, Math.ceil(.4 / beatS));

export function trapArrangement(song, theme = 'threshold') {
  if (!song || typeof song !== 'object' || !(song.bpm >= 40 && song.bpm <= 240)) return null;
  let themes = cache.get(song);
  if (!themes) { themes = new Map(); cache.set(song, themes); }
  if (themes.has(theme)) return themes.get(theme);
  const beatS = 60 / song.bpm, stretch = stretchOf(beatS), spacing = SPACING_BEATS * stretch, origin = song.downbeatS ?? song.downbeats?.[0] ?? 0;
  const duration = Number.isFinite(song.durationS) ? song.durationS : 600;
  const count = Math.ceil(duration / beatS / 16), candidates = [];
  const seed = [...String(song.id || song.title || 'song')].reduce((a, c) => a + c.charCodeAt(0), 0);
  for (let phrase = 0; phrase < count; phrase++) {
    const start = phrase * 16, time = origin + start * beatS;
    const section = sectionOf({ sections: song.sections || [] }, time);
    const bar = Math.max(0, lastAtOrBefore(song.downbeats || [], time));
    const energy = section?.energy ?? song.barEnergy?.[bar] ?? .5;
    const quiet = ['intro', 'break', 'outro'].includes(section?.mood) || energy < .3;
    const high = ['drop', 'peak'].includes(section?.mood) || energy > .75;
    let slots = quiet ? (phrase % 2 ? [] : [8]) : profiles[theme] || profiles.threshold;
    if (!quiet && high) slots = (phrase + seed) % 2 ? [2, 10] : [6, 14];
    else if (!quiet && theme === 'goonworks' && (phrase + seed) % 2) slots = [6, 14];
    for (const slot of slots) candidates.push({ beat: start + slot, accent: false });
  }
  // Only already-marked words count. Ordinary lyrics never turn into surprise strikes.
  for (const cue of [...(song.triggers || []), ...(song.words || []).filter(w => w.gate),
    ...(song.sections || []).filter(s => s.mood === 'drop' || s.mood === 'peak')]) {
    const beat = Math.round((cue.startS - origin) / beatS);
    // A marked hit needs its whole warning inside the song.
    if (Number.isFinite(beat) && beat >= WARN_BEATS * stretch && beat * beatS < duration)
      candidates.push({ beat, accent: true });
  }
  // A word/drop replaces nearby routine hits; every pair of closes keeps the spacing (marked ones first).
  const selected = [];
  for (const cue of candidates.filter(c => c.accent).sort((a, b) => a.beat - b.beat))
    if (!selected.length || cue.beat - selected.at(-1).beat >= spacing) selected.push(cue);
  for (const cue of candidates.filter(c => !c.accent))
    if (selected.every(c => Math.abs(c.beat - cue.beat) >= spacing)) selected.push(cue);
  const result = { closes: selected.sort((a, b) => a.beat - b.beat).map(c => c.beat), beatS };
  themes.set(theme, result); return result;
}

// A full beat to move, two beats held, a full beat to return, then a real clear window.
// At high tempos lengthen travel and holds by whole beats, preserving the marked hit time.
// The warning (WARN_BEATS before the close) is open road with a countdown for the telegraph.
export function trapStroke(row, frame) {
  if (!frame?.arrangement) return null;
  const stretch = stretchOf(frame.beatS), warn = WARN_BEATS * stretch;
  const offset = row.offsetBeats ?? Math.round((row.offsetS || 0) / frame.beatS);
  const now = frame.songBeat + offset, closes = frame.arrangement.closes;
  const i = lastAtOrBefore(closes, now + warn);
  const close = i < 0 ? Infinity : closes[i];
  const age = now - close, eligible = close - warn >= frame.epochBeat + offset;
  const safe = frame.safe || !eligible;
  const phase = safe ? 'warn' : age < -stretch ? 'warn' : age < 0 ? 'lower'
    : age < 2 * stretch ? 'hold' : age < 3 * stretch ? 'rise' : 'clear';
  const extension = safe || age < -stretch || age >= 3 * stretch ? 0
    : age < 0 ? ease(1 + age / stretch) : age < 2 * stretch ? 1 : 1 - ease((age - 2 * stretch) / stretch);
  const inWarn = !safe && age >= -warn && age < 0, warning = safe || inWarn;
  return { extension, phase: i < 0 || age < -warn ? (safe ? 'warn' : 'clear') : phase,
    warning, safe, impactIndex: safe ? null : lastAtOrBefore(closes, now),
    // telegraph: beats until the head is across (null outside the warning) and the opening stroke
    countdown: inWarn ? -age : null, opening: !safe && age >= 2 * stretch && age < 3 * stretch,
    cycleS: Number.isFinite(age) ? age * frame.beatS : 0 };
}

// Held endpoints for sweepers and wheels, with analytic velocity for swept contact.
export function trapSwing(beat, beatS) {
  const period = 8, u = ((beat % period) + period) % period;
  const f = u < 2 ? 0 : u < 4 ? (u - 2) / 2 : u < 6 ? 1 : (8 - u) / 2;
  const sign = u < 4 ? -1 : 1;
  return { swing: 1 - 2 * ease(f), speed: (u < 2 || (u >= 4 && u < 6)) ? 0 : sign * 6 * f * (1 - f) / beatS };
}
