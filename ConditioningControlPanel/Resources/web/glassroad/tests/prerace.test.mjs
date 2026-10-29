// Playtest fixes 2026-10-28, lane prerace: solo laps (1 by default), the pre-race card, the speedo option, and a
// logbook that never lets a short race beat a full race's record.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { OPTION_DEFAULTS, OPTION_CHOICES, cleanOptions, cycleOption, choiceLabel, assistHint } from '../hud/options.js';
import { preraceHtml } from '../hud/howto.js';
import { emptyRecord } from '../meta/record.js';
import { applyRun, fullLength } from '../meta/applyRun.js';
import { ghostKey, loadGhost, saveBestGhost } from '../meta/ghost.js';
import { courseDetails } from '../hud/trackPicker.js';

test('track picker: the tile reads the solo lap choice and the distance for it', () => {
  assert.equal(courseDetails('5.4', 1), '1 lap · 5.4 km');
  assert.equal(courseDetails('5.4', 2), '2 laps · 10.8 km');
  assert.equal(courseDetails('4.3', 3), '3 laps · 12.9 km');
  assert.equal(courseDetails('x', 0), '1 lap', 'no distance, no km; a bad count reads as 1');
});

const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

test('options: Laps 1 / 2 / 3, 1 by default; Speedo off by default; Steer assist reads Help / Trance / Off', () => {
  assert.equal(OPTION_DEFAULTS.laps, 1);
  assert.deepEqual([...OPTION_CHOICES.laps], [1, 2, 3]);
  assert.equal(cleanOptions({ laps: '2' }).laps, 2, 'a chip value arrives as a string');
  assert.equal(cleanOptions({ laps: 7 }).laps, 1, 'an unknown count reads as the default');
  assert.equal(cleanOptions(null).laps, 1);
  assert.equal(cycleOption('laps', 3), 1);
  assert.equal(OPTION_DEFAULTS.speedo, false);
  assert.equal(cleanOptions({ speedo: true }).speedo, true);
  assert.equal(cleanOptions({ speedo: 'yes' }).speedo, false, 'only a real boolean turns it on');
  assert.deepEqual(['light', 'strong', 'off'].map((m) => choiceLabel('steerAssist', m)), ['Help', 'Trance', 'Off']);
  assert.equal(cleanOptions({ steerAssist: 'strong' }).steerAssist, 'strong', 'stored ids survive the new labels');
});

test('pre-race rows: one chip pressed per row, plain copy', () => {
  const html = preraceHtml({ laps: 2, steerAssist: 'off' }, choiceLabel);
  const pressed = [...html.matchAll(/data-key="(\w+)" data-val="(\w+)" aria-pressed="true"/g)].map((m) => m[1] + '=' + m[2]);
  assert.deepEqual(pressed, ['laps=2', 'steerAssist=off']);
  assert.equal((html.match(/data-act="pick"/g) || []).length, 6);
  assert.ok(html.includes('>Help<') && html.includes('>Trance<'));
  assert.ok(!/[–—!]/.test(html.replace(/<!--[^]*?-->/g, '')), 'no dashes, no exclamation marks');
  assert.ok(preraceHtml({}, choiceLabel).includes('data-val="1" aria-pressed="true"'), 'defaults: 1 lap');
  assert.deepEqual(['light', 'strong', 'off', 'nope'].map(assistHint), ['Leans to the middle', 'Drives itself when you let go', 'All yours', 'Leans to the middle']);
  assert.ok(preraceHtml({ steerAssist: 'strong' }, choiceLabel, assistHint).includes('<small class="pr-h">Drives itself when you let go</small>'));
});

const sum = (o = {}) => ({ v: 1, track: 'threshold', mode: 'race', online: false, laps: 3, finished: true, place: 2, of: 8,
  timeS: 600, bestLapS: 190, sectorsS: [[60, 60, 70]], at: '2026-10-28T12:00:00.000Z', ...o });

test('logbook: a short race keeps lap bests and counts but never a whole-race best or a medal', () => {
  assert.equal(fullLength({ laps: 1 }, 3), false);
  assert.equal(fullLength({ laps: 3 }, 3), true);
  assert.equal(fullLength({ laps: 1 }, 0), true, 'no fullLaps: the old rule');
  const full = applyRun(emptyRecord(), sum(), { fullLaps: 3 }).record;
  assert.equal(full.courses.threshold.bestRaceS, 600);
  const short = applyRun(full, sum({ laps: 1, timeS: 200, bestLapS: 185 }), { fullLaps: 3 });
  const c = short.record.courses.threshold;
  assert.equal(c.bestRaceS, 600, 'a 1 lap time never beats the 3 lap record');
  assert.equal(short.diff.pbRace, false);
  assert.equal(c.bestLapS, 185, 'a lap is a lap');
  assert.equal(c.races, 2); assert.equal(c.finishes, 2);
  const trial = applyRun(emptyRecord(), sum({ mode: 'trial', laps: 1, timeS: 100, bestLapS: 100 }), { fullLaps: 3 });
  assert.equal(trial.record.courses.threshold.trialBestS, 0);
  assert.equal(trial.record.courses.threshold.medal, null, 'medal times are for the full race');
  assert.equal(trial.diff.medal, null);
  assert.equal(trial.record.courses.threshold.trialBestLapS, 100);
  const two = applyRun(emptyRecord(), sum({ track: 'keep', laps: 2, timeS: 500 }), { fullLaps: 2 });
  assert.equal(two.record.courses.keep.bestRaceS, 500, 'a two lap course: its own laps are the full race');
});

test('ghosts: the course laps keep the plain key, other counts get their own and never replace it', () => {
  assert.equal(ghostKey('threshold'), 'glassroad.ghost.v1.threshold');
  assert.equal(ghostKey('threshold', 3, 3), 'glassroad.ghost.v1.threshold');
  assert.equal(ghostKey('threshold', 1, 3), 'glassroad.ghost.v1.threshold.l1');
  const map = new Map(), store = { getItem: (k) => map.get(k) ?? null, setItem: (k, v) => map.set(k, String(v)) };
  const g = (laps, timeS) => ({ v: 1, track: 'threshold', laps, timeS, hz: 10, splits: [timeS], n: 2, rows: Int32Array.from(new Array(18).fill(0)) });
  assert.equal(saveBestGhost(g(3, 600), store, 3), true);
  assert.equal(saveBestGhost(g(1, 200), store, 3), true, 'a 1 lap ghost has its own slot');
  assert.equal(loadGhost('threshold', store, 3, 3).timeS, 600, 'the 3 lap ghost survives');
  assert.equal(loadGhost('threshold', store, 1, 3).timeS, 200);
});

test('boot: solo laps follow the option at every start, online keeps the room cfg, the logbook knows the full race', () => {
  const boot = read('../boot.js');
  assert.ok(/const lapsFor = \(o\) => \(o && o\.laps > 0 \? o\.laps : TRACK_LAPS\)/.test(boot));
  assert.ok(/setSoloLaps\(opts\);\s*prepareRunMode\(\);/.test(boot), 'startRace sets the laps before the run is built');
  assert.ok(/online = null;\s*setSoloLaps\(opts\);/.test(boot), 'leaving a room goes back to the solo laps');
  assert.ok(/track\.laps = \(s\.cfg && s\.cfg\.laps\) \|\| TRACK_LAPS; laps = track\.laps;/.test(boot), 'online: the room cfg');
  assert.ok(/applyRun\(before, summary, \{ fullLaps: TRACK_LAPS \}\)/.test(boot));
  assert.ok(/saveBestGhost\(g, undefined, TRACK_LAPS\)/.test(boot) && /loadGhost\(track\.id, undefined, laps, TRACK_LAPS\)/.test(boot));
  const cards = read('../hud/cards.js');
  assert.ok(/data\.then === 'race' \? preraceHtml\(opts, choiceLabel, assistHint\)/.test(cards), 'the Race door card carries the rows');
  assert.ok(/tog\('speedo'/.test(cards) && /data-key="laps"/.test(cards), 'Options: Speed readout and Laps');
});
