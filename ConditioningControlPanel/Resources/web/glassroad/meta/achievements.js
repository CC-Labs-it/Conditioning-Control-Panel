/* meta/achievements.js - the achievement table (CONTRACT-LOGBOOK.md sections 4 and 6).
 *
 * Each row: { id, name, line, kind, hidden, reward, test(summary, record) }. `record` is the record AFTER the
 * run's totals landed, so lifetime rules read it directly. Rewards are cosmetic only and come from section 6.
 * Course rules need a finished run on that course; online races count for everything except Time Trial rules.
 * evaluate(record, summary) returns the ids that are new this run, in table order.
 */
import { COURSES } from '../track/catalog.js';
import { medalRank } from './medals.js';

export const HANG_TIME_S = 4;              // one jump this long is rare: most lips and pads give 1 to 2.5 s
export const BUSY_SLIPSTREAMS = 10;        // slipstreams in one race (the record keeps no lifetime slipstream count)

const done = (s) => !!s && s.finished;
const raced = (s) => done(s) && s.mode === 'race';
const won = (s) => raced(s) && s.place === 1 && s.of > 1;
const on = (id) => (s) => done(s) && s.track === id;
const every = (record, fn) => COURSES.every((c) => fn(record.courses[c.id] || {}));

const row = (kind, id, name, line, test, extra = {}) => Object.freeze({ id, name, line, kind, hidden: false, reward: null, test, ...extra });

export const ACHIEVEMENTS = Object.freeze([
  // first steps
  row('first', 'licensed', 'Licensed', 'Finish a race. Any race.', (s) => raced(s)),
  row('first', 'five-rooms', 'Five Rooms', 'Finish on all five courses.', (s, r) => every(r, (c) => c.finishes > 0 || c.trialBestS > 0) && done(s), { reward: 'hat:umbrella' }),
  row('first', 'company', 'Company', 'Finish an online race.', (s) => raced(s) && s.online),
  row('first', 'first-place', 'First Place', 'Win a race.', (s) => won(s)),
  row('first', 'on-the-clock', 'On the Clock', 'Finish a Time Trial.', (s) => done(s) && s.mode === 'trial' && !s.online),
  // skill
  row('skill', 'long-hold', 'Long Hold', 'Hold a drift to the third tier and cash it.', (s) => (s.driftBoosts?.[2] || 0) > 0),
  row('skill', 'drift-habit', 'Drift Habit', 'Cash 100 drift boosts, all told.', (s, r) => r.totals.driftBoosts >= 100),
  row('skill', 'spotless', 'Spotless', 'Finish a race with no wall, fall or hit.', (s) => raced(s) && !s.walls && !s.falls && !s.hitsTaken),
  row('skill', 'last-to-first', 'Last to First', 'Run 8th after lap one and still win.', (s) => won(s) && (s.placeAtLap?.[0] || 0) >= 8, { reward: 'kart:velvet-lock' }),
  row('skill', 'wire-to-wire', 'Wire to Wire', 'Lead at the end of every lap and win.', (s) => won(s) && s.ledEveryLap),
  row('skill', 'hang-time', 'Hang Time', `Stay in the air ${HANG_TIME_S} seconds in one go.`, (s) => s.longestAirS >= HANG_TIME_S),
  row('skill', 'slipstream-habit', 'Tailgater', `Fire ${BUSY_SLIPSTREAMS} slipstreams in one race.`, (s) => s.slipstreams >= BUSY_SLIPSTREAMS),
  row('skill', 'tricky', 'Tricky', 'Land 5 tricks in one race.', (s) => s.tricks >= 5),
  row('skill', 'clean-sweep', 'Clean Sweep', 'Every lap clean, two laps or more.', (s) => done(s) && s.laps >= 2 && s.cleanLaps >= s.laps),
  // courses
  row('course', 'island-hopper', 'Island Hopper', 'Finish Threshold without a fall.', (s) => on('threshold')(s) && !s.falls),
  row('course', 'gate-etiquette', 'Gate Etiquette', 'Finish the Keep without touching a gate or chain.', (s) => on('keep')(s) && !s.traps?.keepGate && !s.traps?.keepChain),
  row('course', 'seven-years', 'Seven Years', 'Crack a mirror at every Boudoir station.', (s) => on('boudoir')(s) && s.mirrorStations >= 3),
  row('course', 'clean-scan', 'Clean Scan', 'Finish Mainframe without being scanned.', (s) => on('mainframe')(s) && !s.traps?.scan),
  row('course', 'windmill-proof', 'Windmill Proof', 'Finish GOONWORKS without a toy landing on you.', (s) => on('goonworks')(s) && !s.traps?.goonworks),
  // songs
  row('song', 'word-perfect', 'Word Perfect', 'Catch every word the song offers.', (s) => done(s) && s.wordsOffered > 0 && s.wordsCaught >= s.wordsOffered),
  row('song', 'on-the-beat', 'On the Beat', 'Pass every song gate.', (s) => done(s) && s.songGatesOffered > 0 && s.songGatesPassed >= s.songGatesOffered),
  row('song', 'sing-along', 'Sing Along', 'Catch 500 words, all told.', (s, r) => r.totals.wordsCaught >= 500),
  // toys
  row('toys', 'tow-truck', 'Tow Truck', 'Pass a kart you towed and snap the Tether.', (s) => s.tetherBreaks > 0),
  row('toys', 'bank-shot', 'Bank Shot', 'Land a Disc off a bounce.', (s) => s.discBankHits > 0),
  row('toys', 'big-catch', 'Big Catch', 'Catch three karts in one Big Spiral.', (s) => s.bigSpiralMax >= 3),
  row('toys', 'sharpshooter', 'Sharpshooter', 'Land 100 toys, all told.', (s, r) => r.totals.itemsLanded >= 100),
  // falls
  row('falls', 'rescued', 'Rescued', 'Fall off and get fished out.', (s, r) => r.totals.rescues > 0),
  row('falls', 'deep-end', 'Deep End', 'Fall off 100 times. It happens.', (s, r) => r.totals.falls >= 100, { hidden: true }),
  // mastery
  row('mastery', 'gold-leaf', 'Gold Leaf', 'Gold in the Time Trial on all five courses.', (s, r) => every(r, (c) => medalRank(c.medal) >= medalRank('gold')), { reward: 'kart:undertow' }),
  row('mastery', 'glass-driver', 'Glass Driver', 'Glass in the Time Trial on all five courses.', (s, r) => every(r, (c) => c.medal === 'glass')),
  row('mastery', 'regular', 'Regular', 'Start 100 races.', (s, r) => r.totals.races >= 100),
  // hidden
  row('mastery', 'no-hands', 'No Hands', 'Win with the steer assist off.', (s) => won(s) && s.assist === 'off', { hidden: true }),
  row('mastery', 'photo-finish', 'Photo Finish', 'Win by less than a fifth of a second.', (s) => won(s) && s.winMarginS > 0 && s.winMarginS < 0.2, { hidden: true }),
  row('course', 'blue-screen', 'Blue Screen', 'Get scanned five times on one Mainframe run. The system has notes.', (s) => on('mainframe')(s) && (s.traps?.scan || 0) >= 5, { hidden: true }),
]);

export const ACHIEVEMENT_BY_ID = Object.freeze(Object.fromEntries(ACHIEVEMENTS.map((a) => [a.id, a])));

/** @returns {string[]} ids earned this run and not held before, in table order */
export function evaluate(record, summary) {
  if (!record || !summary) return [];
  const held = record.achievements || {};
  const out = [];
  for (const a of ACHIEVEMENTS) {
    if (held[a.id]) continue;
    let ok = false;
    try { ok = !!a.test(summary, record); } catch (e) { ok = false; }
    if (ok) out.push(a.id);
  }
  return out;
}
