import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { restoreRace, advanceRace, takeRaceBonus, raceCanPlay, RACE_BONUS_MS } from '../ui/unlocks/arcade/race-model.js';

test('race bonus waits for active time, persists ready state and pays only once', () => {
  let state = restoreRace();
  advanceRace(state, 34999);
  assert.equal(takeRaceBonus(state), false);
  advanceRace(state, 1);
  state = restoreRace(JSON.parse(JSON.stringify(state)));
  assert.equal(takeRaceBonus(state), true);
  assert.equal(takeRaceBonus(state), false);
  assert.equal(state.leftMs, RACE_BONUS_MS);
});

test('bad saved values and invalid time cannot create bonus debt or extra credit', () => {
  const state = restoreRace({ leftMs: Infinity });
  advanceRace(state, NaN); advanceRace(state, -100);
  assert.equal(state.leftMs, RACE_BONUS_MS);
  assert.equal(restoreRace({ leftMs: -3 }).leftMs, 0);
});

test('footage stops for every hold, hidden tab, invisible dock and reduced motion', () => {
  const flags = { paused: false, hidden: false, gentle: false, visible: true, dead: false, failed: false };
  assert.equal(raceCanPlay(flags), true);
  for (const key of ['paused', 'hidden', 'gentle', 'dead', 'failed']) assert.equal(raceCanPlay({ ...flags, [key]: true }), false, key);
  assert.equal(raceCanPlay({ ...flags, visible: false }), false);
});

test('the race ships an actual local video and its static poster', () => {
  for (const file of ['threshold-loop.mp4', 'threshold-poster.jpg']) assert.ok(existsSync(new URL(`../assets/race/${file}`, import.meta.url)));
  const source = readFileSync(new URL('../ui/unlocks/arcade/race.js', import.meta.url), 'utf8');
  assert.match(source, /video\.muted = true/);
  assert.match(source, /video\.pause\(\)/);
  assert.match(source, /removeAttribute\('src'\)/);
  assert.doesNotMatch(source, /requestAnimationFrame|setInterval|new Audio|https?:/);
});
