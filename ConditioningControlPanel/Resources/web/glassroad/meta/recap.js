/* meta/recap.js - the recap payload (CONTRACT-LOGBOOK.md section 5). Pure; every value is a display string.
 *
 * buildRecap(summary, diff, before) picks exactly three highlights: with history, what stood out against the
 * before-record's per-race averages; without, a fixed priority list. Page 3 data (pb, medal, achievements, unlocks)
 * comes from the applyRun diff.
 */
import { MEDALS } from './medals.js';
import { ACHIEVEMENT_BY_ID } from './achievements.js';
import { UNLOCKS } from './unlocks.js';

const n = (x) => String(Math.round(x || 0));
const sec = (x) => (Math.round((x || 0) * 10) / 10).toFixed(1) + ' s';
const pct = (x) => Math.round((x || 0) * 100) + '%';
const km = (m) => ((m || 0) / 1000).toFixed(1) + ' km';

// key, label, value(summary) -> number, display(summary), the record total that gives a per-race average (or null)
const STATS = [
  ['driftBoosts3', 'Drift boosts, tier 3', (s) => s.driftBoosts?.[2] || 0, (s) => n(s.driftBoosts?.[2]), null],
  ['overtakes', 'Overtakes', (s) => s.overtakes, (s) => n(s.overtakes), 'overtakes'],
  ['airS', 'Seconds in the air', (s) => s.airS, (s) => sec(s.airS), 'airS'],
  ['wordsCaught', 'Words caught', (s) => s.wordsCaught, (s) => `${n(s.wordsCaught)} of ${n(s.wordsOffered)}`, 'wordsCaught'],
  ['itemsLanded', 'Toys landed', (s) => s.itemsLanded, (s) => n(s.itemsLanded), 'itemsLanded'],
  ['cleanLaps', 'Clean laps', (s) => s.cleanLaps, (s) => `${n(s.cleanLaps)} of ${n(s.laps)}`, 'cleanLaps'],
  ['driftBoosts', 'Drift boosts', (s) => (s.driftBoosts || []).reduce((a, b) => a + (b || 0), 0), (s) => n((s.driftBoosts || []).reduce((a, b) => a + (b || 0), 0)), 'driftBoosts'],
  ['longestAirS', 'Longest jump', (s) => s.longestAirS, (s) => sec(s.longestAirS), null],
  ['leadS', 'Time in front', (s) => s.leadS, (s) => sec(s.leadS), null],
  ['slipstreams', 'Slipstreams', (s) => s.slipstreams, (s) => n(s.slipstreams), null],
  ['tricks', 'Tricks', (s) => s.tricks, (s) => n(s.tricks), null],
  ['topSpeedPct', 'Top speed', (s) => s.topSpeedPct, (s) => pct(s.topSpeedPct), null],
  ['distanceM', 'Distance', (s) => s.distanceM, (s) => km(s.distanceM), null],
];
const FILLER = ['topSpeedPct', 'distanceM', 'leadS'];   // always shown when nothing else made the cut

/** @returns {Array<{key: string, label: string, value: string}>} exactly three */
export function pickHighlights(summary, before) {
  const s = summary || {};
  const races = before?.totals?.races || 0;
  const scored = [];
  STATS.forEach(([key, label, val, show, total], i) => {
    const v = +val(s) || 0;
    if (v <= 0) return;
    let score = (STATS.length - i) / STATS.length;                      // priority order, 0..1
    if (races > 0 && total) score += v / Math.max(0.5, (before.totals[total] || 0) / races);   // above the usual
    else if (races > 0 && key === 'driftBoosts3') score += 1 + v / 3;
    scored.push({ key, label, value: show(s), score });
  });
  if (races > 0 && !s.falls && (before.totals.falls || 0) / races >= 0.5) scored.push({ key: 'noFalls', label: 'Falls', value: 'None', score: 1.5 });
  scored.sort((a, b) => b.score - a.score);
  const out = scored.slice(0, 3).map(({ key, label, value }) => ({ key, label, value }));
  for (const key of FILLER) {
    if (out.length >= 3) break;
    if (out.some((h) => h.key === key)) continue;
    const [, label, , show] = STATS.find((x) => x[0] === key);
    out.push({ key, label, value: show(s) });
  }
  return out;
}

function bestSector(summary, before) {
  const laps = (summary.sectorsS || []).filter((l) => Array.isArray(l));
  if (!laps.length) return null;
  const best = [0, 1, 2].map((i) => Math.min(...laps.map((l) => (l[i] > 0 ? l[i] : Infinity))));
  const ref = before?.courses?.[summary.track]?.bestSectorsS || [0, 0, 0];
  let pick = null, score = Infinity;
  for (let i = 0; i < 3; i++) {
    if (!Number.isFinite(best[i])) continue;
    const x = ref[i] > 0 ? best[i] / ref[i] : best[i] / 1e6;   // against the old best, else the fastest third
    if (x < score) { score = x; pick = i; }
  }
  return pick;
}

/** @returns {object|null} the recap payload, or null without a summary */
export function buildRecap(summary, diff, before) {
  if (!summary) return null;
  const d = diff || {};
  const medal = d.medal ? { from: d.medal.from || null, to: d.medal.to, times: { ...(MEDALS[summary.track] || {}) } } : null;
  return {
    mode: summary.mode === 'trial' ? 'trial' : 'race',
    highlights: pickHighlights(summary, before),
    sketch: { fallsAtS: (summary.fallsAtS || []).slice(), bestSector: bestSector(summary, before), lengthM: summary.lengthM || summary.distanceM || 0 },
    pb: { race: !!d.pbRace, lap: !!d.pbLap, trial: !!d.pbTrial },
    medal,
    achievements: (d.newAchievements || []).filter((id) => ACHIEVEMENT_BY_ID[id]).map((id) => ({ id, name: ACHIEVEMENT_BY_ID[id].name, line: ACHIEVEMENT_BY_ID[id].line })),
    unlocks: (d.newUnlocks || []).filter((id) => UNLOCKS[id]).map((id) => ({ id, label: UNLOCKS[id].label })),
    xp: null,
  };
}
