import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { restoreRace, advanceRace, takeRaceBonus, raceCanPlay, RACE_BONUS_MS, RACE_CLIPS, RACE_STRETCHES, nextClip, firstClip } from '../ui/unlocks/arcade/race-model.js';
const RACE_COURSES = ['threshold', 'mainframe', 'boudoir', 'keep', 'goonworks']; // all five courses of glassroad/tracks

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

const clipJson = JSON.parse(readFileSync(new URL('../assets/race/clip.json', import.meta.url), 'utf8'));

test('clip.json lists the same stretches the player rotates through, at least three per course', () => {
  assert.deepEqual(clipJson.clips.map((c) => c.id), RACE_CLIPS.map((c) => c.id));
  for (const c of clipJson.clips) {
    assert.equal(c.file, c.id + '.mp4');
    assert.equal(c.poster, c.id + '.jpg');
    assert.ok(Number.isFinite(c.startMeters) && Number.isFinite(c.endMeters) && c.startMeters !== c.endMeters, c.id + ' covers road');
  }
  for (const course of RACE_COURSES) assert.ok(RACE_STRETCHES[course].length >= 3, course);
  assert.deepEqual(Object.keys(RACE_STRETCHES).sort(), [...RACE_COURSES].sort());
});

test('the rotation never shows one course twice in a row and a fresh run does not always open on Threshold', () => {
  for (let i = 0; i < RACE_CLIPS.length; i++) {
    const j = nextClip(i, null);
    assert.notEqual(RACE_CLIPS[j].course, RACE_CLIPS[i].course, RACE_CLIPS[i].id);
  }
  const seen = new Set();
  for (let i = 0, k = 0; k < RACE_CLIPS.length; k++, i = nextClip(i, null)) seen.add(RACE_CLIPS[i].id);
  assert.equal(seen.size, RACE_CLIPS.length, 'one lap of the rotation shows every stretch');
  const openers = new Set();
  for (let r = 0; r < 1; r += 0.05) openers.add(RACE_CLIPS[firstClip(null, () => r)].course);
  assert.equal(openers.size, RACE_COURSES.length, 'a fresh run can open on any course');
  assert.equal(firstClip('keep-c', () => 0), RACE_CLIPS.findIndex((c) => c.id === 'keep-c'), 'a saved clip resumes');
  assert.equal(firstClip('threshold-loop', () => 2), RACE_CLIPS.length - 1, 'an old save and a bad random stay in range');
});

test('Pick the Course walks that course\'s stretches, never the same one twice in a row', () => {
  const keep = RACE_CLIPS.map((c, i) => c.course === 'keep' ? i : -1).filter((i) => i >= 0);
  let i = 0;
  const shown = [];
  for (let k = 0; k < keep.length; k++) { const j = nextClip(i, 'keep'); assert.notEqual(j, i); i = j; shown.push(i); }
  assert.deepEqual([...shown].sort((a, b) => a - b), keep);
  assert.equal(RACE_CLIPS[nextClip(0, 'keep', RACE_CLIPS, (j) => RACE_CLIPS[j].course === 'keep')].course === 'keep', false, 'a resting pick falls back');
  assert.equal(nextClip(0, 'nowhere'), 1, 'a junk pick is ignored');
  assert.equal(nextClip(0, null, RACE_CLIPS, () => true), -1);
});

// The clips carry nothing git may hold (TC-STANDING): they live on disk for the stage only. A checkout without them
// skips this check instead of failing it.
const haveFootage = RACE_CLIPS.every((c) => existsSync(new URL(`../assets/race/${c.id}.mp4`, import.meta.url)));
test('each stretch ships a distinct small MP4 capture and JPEG poster', { skip: !haveFootage && 'race footage is not on this disk' }, () => {
  const digests = new Set();
  let total = 0;
  for (const { id } of RACE_CLIPS) {
    const clip = readFileSync(new URL(`../assets/race/${id}.mp4`, import.meta.url));
    const poster = readFileSync(new URL(`../assets/race/${id}.jpg`, import.meta.url));
    assert.equal(clip.toString('ascii', 4, 8), 'ftyp', id+' is an MP4');
    assert.equal(poster.readUInt16BE(0), 0xffd8, id+' has a JPEG poster');
    assert.ok(clip.length < 600000, id + ' stays small');
    total += clip.length + poster.length;
    for (const asset of [clip, poster]) digests.add(createHash('sha256').update(asset).digest('hex'));
  }
  assert.equal(digests.size, RACE_CLIPS.length * 2, 'each stretch has its own footage and poster');
  assert.ok(total < 8.5e6, 'all footage together stays under about 8 MB');
});

test('race playback is local, muted and releases the decoder', () => {
  const source = readFileSync(new URL('../ui/unlocks/arcade/race.js', import.meta.url), 'utf8');
  assert.match(source, /video\.muted = true/);
  assert.match(source, /video\.pause\(\)/);
  assert.match(source, /removeAttribute\('src'\)/);
  assert.doesNotMatch(source, /requestAnimationFrame|setInterval|new Audio|https?:/);
});
