import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { mapSpillStations } from '../track/shapeWaterSpills.js';
import { waterSpills, waterRuns, isWaterSurface } from '../render/waterMath.js';

for (const id of ['mainframe', 'boudoir']) test(id + ': authored cascades follow their wet node intervals after broadening', () => {
  const file = new URL('../tracks/' + id + '.json', import.meta.url), bytes = readFileSync(file);
  const json = JSON.parse(bytes.toString('utf8')), before = structuredClone(json);
  const track = loadTrack(json), mapped = mapSpillStations(json, track.nodeS);
  assert.equal(mapped.length, json.waterSpills.length);
  assert.notDeepEqual(mapped, json.waterSpills, 'authored metre positions actually move with the road');
  for (const [i, row] of mapped.entries()) {
    assert.equal(row.side, json.waterSpills[i].side); assert.equal(row.dropM, json.waterSpills[i].dropM);
    assert.ok(row.toS > row.fromS && row.fromS >= 0 && row.toS <= track.lengthM);
    assert.ok(isWaterSurface(track.frameAt((row.fromS + row.toS) / 2).surface), 'cascade stays on its real wet district');
  }
  const original = loadTrack(json, { courseShape: false, aerials: false });
  const curtains = waterSpills({ ...track, waterSpills: mapped }, waterRuns(track));
  assert.ok(curtains.length > 0);
  for (const [i, row] of json.waterSpills.entries()) {
    if (!waterSpills({ ...original, waterSpills: [row] }).length) continue;
    assert.ok(waterSpills({ ...track, waterSpills: [mapped[i]] }).length > 0,
      'every originally visible cascade retains its real curtain; contained canal banks still refuse leaks');
  }
  assert.deepEqual(json, before); assert.deepEqual(readFileSync(file), bytes, 'source track is never rewritten');
  const unchanged = mapSpillStations(json, original.nodeS);
  for (const [i, row] of unchanged.entries()) {
    assert.ok(Math.abs(row.fromS - json.waterSpills[i].fromS) < 1e-8);
    assert.ok(Math.abs(row.toS - json.waterSpills[i].toS) < 1e-8);
  }
});

test('explicitly disabled and absent water spills retain their author intent', () => {
  assert.deepEqual(mapSpillStations({}, [], []), []);
  assert.equal(mapSpillStations({}, [], undefined), undefined);
});
