import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { trackTableWidth, patternBytes, patternZones } from '../render/spectrum.js';
import { ROAD_HALF_WIDTH_RANGE, encodeRoadHalfWidth } from '../render/musicTilesMath.js';
import { land } from '../sim/air.js';
import { S, createState } from '../sim/state.js';
import { makeFeel, recorder, acts, DT } from './sim-fake.mjs';

test('long course lookup textures fit default WebGPU limits and cover the entire lap', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));
  assert.ok(track.lengthM > 8192, 'exercise the enlarged course above the default texture limit');
  assert.equal(trackTableWidth(track.lengthM), 8192);
  const bytes = patternBytes(patternZones(track.sections), track.lengthM);
  assert.equal(bytes.length, 8192 * 4);
  assert.equal(bytes[bytes.length - 1], 255, 'last sample remains defined');
  for (const half of [14, 36, 108]) {
    const decoded = encodeRoadHalfWidth(half) / 255 * ROAD_HALF_WIDTH_RANGE;
    assert.ok(Math.abs(decoded - half) <= ROAD_HALF_WIDTH_RANGE / 510 + 1e-6, 'wide approaches retain their real edge');
  }
});

test('challenge landings stay planted while ordinary road landings keep their bounce', () => {
  for (const planted of [false, true]) {
    const st = createState(), feel = makeFeel(), ev = recorder(), velocity = [0, 0, 25];
    st[S.airTime] = 1; st[S.airborne] = 1;
    land(st, acts(), feel, ev, velocity, [0, 1, 0], 25, DT, planted);
    assert.equal(ev.all('land').length, 1, 'touchdown feedback survives');
    assert.equal(st[S.grounded], planted ? 1 : 0);
    assert.equal(st[S.airborne], planted ? 0 : 1);
    assert.equal(velocity[1] > 0, !planted);
  }
});
