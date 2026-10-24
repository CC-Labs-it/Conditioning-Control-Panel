/* ============================================================================
 * games/daily-trigger/smoke/one-try-check.mjs - ONE TRY A DAY, ONE CLOCK.
 *
 *   node games/daily-trigger/smoke/one-try-check.mjs   (from Resources/web/arcademy; 0 on pass)
 *
 *   ccp-bugs#1455  a double Esc left the Daily Trigger before finish() wrote
 *                  anything, so the next run was a first run again, forever.
 *   ticket 1557537313851056138  the retake check read the LOCAL day while the
 *                  script and the XP ledger roll at UTC midnight.
 *
 * core/dayclock.js is pure and imported for real; the wiring in shell.js and
 * daily-trigger/index.js is read as TEXT (the exit-sign-check tripwire shape),
 * since both modules need a DOM to load.
 * ==========================================================================*/

import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, join } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = join(HERE, '..', '..', '..');

const { utcDayOf, rowForUtcDay, attemptedOnUtcDay } =
  await import(pathToFileURL(join(ROOT, 'core', 'dayclock.js')).href);

let fails = 0;
const ok = (cond, what) => { if (!cond) { console.error('FAIL ' + what); fails++; } else console.log('  ok  ' + what); };

/* ---- utcDayOf ---- */
ok(utcDayOf(Date.UTC(2026, 9, 17, 23, 30)) === '2026-10-17', 'utcDayOf reads the UTC date');
ok(utcDayOf(undefined) === '' && utcDayOf('x') === '' && utcDayOf(0) === '', 'utcDayOf refuses a non-stamp');

/* ---- rowForUtcDay: the row must have been written on the script's UTC day ---- */
const U = '2026-10-17';
// East of UTC (UTC+2): graded at 23:00 local on the 17th = 21:00 UTC on the 17th, filed under
// local day 17. At 00:30 local on the 18th (22:30 UTC, still the 17th) the local day is 18 and
// its row is empty, so the old check said "first run". The neighbour (index 1) now speaks.
const eastRow = { grade: 'B', at: Date.UTC(2026, 9, 17, 21, 0) };
ok(rowForUtcDay([null, eastRow, null], U) === eastRow, 'east of UTC: last night\'s local row on today\'s UTC day is a retake');
// West of UTC (UTC-5): graded at 18:00 local on the 17th = 23:00 UTC on the 17th. At 19:30 local
// (00:30 UTC on the 18th) the script is NEW and the host pays; the old check still said Retake.
const westRow = { grade: 'A', at: Date.UTC(2026, 9, 17, 23, 0) };
ok(rowForUtcDay([westRow, null, null], '2026-10-18') === null, 'west of UTC: a new UTC day is a first run even on the same local day');
ok(rowForUtcDay([westRow, null, null], U) === westRow, 'same UTC day, same local day: retake');
// Older rows carry no `at`: only the local day's own row speaks, exactly as before.
const legacy = { grade: 'C' };
ok(rowForUtcDay([legacy, null, null], U) === legacy, 'legacy row (no at) on the local day still counts');
ok(rowForUtcDay([null, legacy, null], U) === null, 'legacy row on a neighbouring local day does not');
ok(rowForUtcDay([{ at: Date.UTC(2026, 9, 17, 1) }], U) === null, 'a row with no grade is not a record');

/* ---- attemptedOnUtcDay: a try left mid-class still counts ---- */
ok(attemptedOnUtcDay({ gameKey: 'daily_trigger', utcDay: U, gameMeta: { lastPlayedUtc: U } }) === true,
  'the start stamp makes the next run a retake');
ok(attemptedOnUtcDay({ gameKey: 'daily_trigger', utcDay: U, gameMeta: { lastPlayedUtc: '2026-10-16' } }) === false,
  'yesterday\'s stamp does not');
ok(attemptedOnUtcDay({ gameKey: 'sort', utcDay: U, xpPaidDays: { [U]: ['sort'] } }) === true,
  'the host XP ledger for the UTC day counts');
ok(attemptedOnUtcDay({ gameKey: 'sort', utcDay: U, xpPaidDays: { '2026-10-16': ['sort'] } }) === false,
  'yesterday\'s ledger does not');
ok(attemptedOnUtcDay({ gameKey: '', utcDay: U, gameMeta: { lastPlayedUtc: U } }) === false
  && attemptedOnUtcDay({ gameKey: 'sort', utcDay: '', xpPaidDays: { '': ['sort'] } }) === false,
  'no key or no day is never an attempt');

/* ---- the wiring (text tripwires) ---- */
const dt = readFileSync(join(ROOT, 'games', 'daily-trigger', 'index.js'), 'utf8');
const startAt = dt.indexOf('start(classSpec)');
const retakeAt = dt.indexOf('retake = readMeta().lastPlayedUtc', startAt);
const stampAt = dt.indexOf('recordAttempt()', retakeAt);
const finishAt = dt.indexOf('function finish()');
ok(startAt > 0 && retakeAt > startAt && stampAt > retakeAt, 'daily trigger stamps the try in start(), after reading retake');
ok(/if \(!\(spec && spec\.endless\)\) recordAttempt\(\);/.test(dt), 'a free swim never stamps the try');
ok(dt.indexOf('function recordAttempt()') > 0 && dt.indexOf('function recordAttempt()') < finishAt,
  'recordAttempt is defined beside finish()');
ok(/const fluent = !retake && /.test(dt), 'a retake never buys a fluency pip');

const shell = readFileSync(join(ROOT, 'shell', 'shell.js'), 'utf8');
ok(shell.includes("from '../core/dayclock.js'"), 'the shell imports the one clock');
ok(/const retake = endless \? false : \(!!todaysRecord\(cls\.gameKey\) \|\| attemptedToday\(cls\.gameKey\)\);/.test(shell),
  'a class starts as a retake on a UTC-day row OR a try');
ok(/finishClass\(cls, gradeTier, result, \{[^}]*\bretake\b[^}]*\}\)/.test(shell), 'finishClass is told how the class started');
ok(/type: 'class-started'[^\n]*dayUtc: utcDateSeed/.test(shell), 'class-started carries the UTC day the host claims on a leave');

if (fails) { console.error(fails + ' failed'); process.exit(1); }
console.log('one-try-check: all passed');
