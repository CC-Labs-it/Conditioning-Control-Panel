import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
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

test('each circuit ships a distinct MP4 capture and JPEG poster', () => {
  const digests = new Set();
  for (const id of ['threshold', 'mainframe', 'boudoir', 'keep', 'goonworks']) {
    const clip = readFileSync(new URL(`../assets/race/${id}-loop.mp4`, import.meta.url));
    const poster = readFileSync(new URL(`../assets/race/${id}-poster.jpg`, import.meta.url));
    assert.equal(clip.toString('ascii', 4, 8), 'ftyp', id+' is an MP4');
    assert.equal(poster.readUInt16BE(0), 0xffd8, id+' has a JPEG poster');
    for (const asset of [clip, poster]) digests.add(createHash('sha256').update(asset).digest('hex'));
  }
  assert.equal(digests.size, 10, 'each circuit has its own footage and poster');
});

test('race playback is local, muted and releases the decoder', () => {
  const source = readFileSync(new URL('../ui/unlocks/arcade/race.js', import.meta.url), 'utf8');
  assert.match(source, /video\.muted = true/);
  assert.match(source, /video\.pause\(\)/);
  assert.match(source, /removeAttribute\('src'\)/);
  assert.doesNotMatch(source, /requestAnimationFrame|setInterval|new Audio|https?:/);
});
