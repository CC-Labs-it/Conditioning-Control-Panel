import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { COURSES, selectedCourse, courseUrl } from '../track/catalog.js';
import { selectTrack } from '../track/selection.js';
import { loadTrack } from '../track/load.js';
import { createContact } from '../sim/contact.js';
import { createFeelOverlay, identityMods } from '../sim/racers.js';
import { createState, S } from '../sim/state.js';
import { placeKart } from '../dev/hook.js';
import { waterRuns, waterSpills } from '../render/waterMath.js';
import { makeFeel, recorder, DT } from './sim-fake.mjs';

test('the garage catalog and course loader agree on all three bundled tracks', () => {
  assert.deepEqual(COURSES.map(c => c.id).sort(), ['keep', 'mainframe', 'threshold']);
  for (const c of COURSES) {
    const url = new URL(courseUrl('http://localhost:18910/?chart=0&r=invite', c.id));
    assert.equal(selectedCourse(url.search).id, c.id);
    assert.equal(selectTrack(url.search), c.id);
    assert.equal(url.searchParams.has('r'), false);
  }
});

for (const { id } of COURSES) test(`${id}: shared water spills and bubble launch operate on the real track`, () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url), 'utf8')));
  assert.ok(waterRuns(track).length > 0);
  assert.ok(waterSpills(track).length > 0);
  for (const shield of ['glass', 'golden']) {
    const feel = makeFeel(), events = recorder();
    const racers = [0, 1].map(i => {
      const st = createState(); placeKart(st, track, feel, 20, i * 1.8, 20);
      return { i, st, feel: createFeelOverlay(feel), mods: identityMods(), fxv: {}, events };
    });
    racers[0].fxv.shield = shield;
    createContact(track, feel, events).update(DT, racers, { phase: 'racing', timeS: 10 });
    const victim = racers[1].st, n = track.frameAt(20).n;
    assert.equal(victim[S.airborne], 1);
    assert.ok(victim[S.velX] * n[0] + victim[S.velY] * n[1] + victim[S.velZ] * n[2] >= 6.49);
    assert.equal(events.all('bubble-ram').length, 1);
  }
});
