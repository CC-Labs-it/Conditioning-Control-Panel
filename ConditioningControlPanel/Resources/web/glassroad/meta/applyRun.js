/* meta/applyRun.js - fold one RunSummary into Record v1 (CONTRACT-LOGBOOK.md section 4). Pure: never mutates.
 *
 * Race mode: course races / finishes / wins / podiums and bestRaceS / bestLapS / bestSectorsS. Trial mode: trials,
 * trialBestS / trialBestLapS / bestSectorsS and the medal (medals.js). Totals take every run. An unfinished run
 * counts a race but sets no bests. A pb flag means an existing best was beaten (a first time is not a pb).
 * Achievements evaluate on the updated record; their rewards become unlocks.
 */
import { cleanRecord, RECENT_MAX } from './record.js';
import { medalFor, medalRank } from './medals.js';
import { evaluate, ACHIEVEMENT_BY_ID } from './achievements.js';

const better = (t, old) => t > 0 && (!(old > 0) || t < old);

/** @returns {{record: object, diff: {pbRace: boolean, pbLap: boolean, pbTrial: boolean, medal: null|{from, to},
 *   newAchievements: string[], newUnlocks: string[]}}} */
export function applyRun(record, summary) {
  const r = cleanRecord(record);          // a fresh deep copy (and a valid shape)
  const diff = { pbRace: false, pbLap: false, pbTrial: false, medal: null, newAchievements: [], newUnlocks: [] };
  const s = summary;
  const c = s && r.courses[s.track];
  if (!c) return { record: r, diff };
  const trial = s.mode === 'trial', fin = !!s.finished, t = r.totals;

  if (trial) c.trials++;
  else {
    c.races++; t.races++;
    if (s.online) t.online++;
    if (fin) {
      c.finishes++; t.finishes++;
      if (s.of > 1 && s.place === 1) { c.wins++; t.wins++; }
      if (s.of > 1 && s.place >= 1 && s.place <= 3) { c.podiums++; t.podiums++; }
    }
  }

  if (fin) {
    if (trial) {
      if (better(s.timeS, c.trialBestS)) { diff.pbTrial = c.trialBestS > 0; c.trialBestS = s.timeS; }
      if (better(s.bestLapS, c.trialBestLapS)) { diff.pbLap = c.trialBestLapS > 0; c.trialBestLapS = s.bestLapS; }
      const got = medalFor(s.track, s.timeS);
      if (got && medalRank(got) > medalRank(c.medal)) { diff.medal = { from: c.medal, to: got }; c.medal = got; }
    } else {
      if (better(s.timeS, c.bestRaceS)) { diff.pbRace = c.bestRaceS > 0; c.bestRaceS = s.timeS; }
      if (better(s.bestLapS, c.bestLapS)) { diff.pbLap = c.bestLapS > 0; c.bestLapS = s.bestLapS; }
    }
    for (const lap of s.sectorsS || []) for (let i = 0; i < 3; i++) if (better(lap?.[i], c.bestSectorsS[i])) c.bestSectorsS[i] = lap[i];
  }

  t.distanceM += s.distanceM || 0;
  t.driftBoosts += (s.driftBoosts || []).reduce((a, b) => a + (b || 0), 0);
  t.airS += s.airS || 0;
  t.falls += s.falls || 0;
  t.rescues += s.rescues || 0;
  t.overtakes += s.overtakes || 0;
  t.itemsLanded += s.itemsLanded || 0;
  t.tetherBreaks += s.tetherBreaks || 0;
  t.wordsCaught += s.wordsCaught || 0;
  t.songGates += s.songGatesPassed || 0;
  t.cleanLaps += s.cleanLaps || 0;

  const at = s.at || new Date().toISOString();
  r.recent = [{ track: s.track, mode: trial ? 'trial' : 'race', place: s.place | 0, of: s.of | 0, timeS: fin ? s.timeS || 0 : 0, at },
    ...r.recent].slice(0, RECENT_MAX);
  r.updatedAt = at;

  diff.newAchievements = evaluate(r, s);
  for (const id of diff.newAchievements) {
    r.achievements[id] = at;
    const reward = ACHIEVEMENT_BY_ID[id].reward;
    if (reward && !r.unlocks.includes(reward)) { r.unlocks.push(reward); diff.newUnlocks.push(reward); }
  }
  r.unlocks.sort();
  return { record: cleanRecord(r), diff };
}
