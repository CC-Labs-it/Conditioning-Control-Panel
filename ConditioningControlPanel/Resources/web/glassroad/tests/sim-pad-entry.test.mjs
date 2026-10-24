import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { place, makeFeel, acts, recorder, run } from './sim-fake.mjs';
import { S } from '../sim/state.js';

test('exact real pad edge survives Float32 state storage and allows a later real re-entry', () => {
  const json = JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8'));
  // Independent of the authored pad plan: this real spline node reproduced duplicate entry.
  const track = loadTrack({ ...json, boosts: [{ node: 2, x: 0, w: 7, len: 6 }] }), pad = track.boosts[0];
  assert.ok(Math.abs(pad.s - 20.00053858577529) < 1e-8 && Math.fround(pad.s) < pad.s, 'offending exact edge rounds below metadata');
  const st = place(track, { s: pad.s - 2, speed: 30 }), events = recorder(), feel = makeFeel(), actions = acts({ throttle: 1 });
  run(st, track, feel, actions, 1, events, () => st[S.s] > pad.s + pad.len + 2);
  assert.equal(events.all('pad').length, 1, 'one entry across exact Float64/Float32 boundary');
  st.set(place(track, { s: pad.s - 3.3, speed: 30 }));
  run(st, track, feel, actions, 1, events, () => st[S.s] > pad.s + pad.len + 2);
  assert.equal(events.all('pad').length, 2, 'leaving and returning remains a legitimate entry');
});
