import { test } from 'node:test';
import assert from 'node:assert/strict';
import { buildMainframe } from '../dev/gen-mainframe.mjs';
import { loadTrack } from '../track/load.js';
import { canalProfile } from '../track/canal.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { qlook } from '../core/math.js';
import { makeFeel, acts, recorder } from './sim-fake.mjs';

const track = loadTrack(buildMainframe().track), section = track.sections.find(s => s.id === 'coolant-canal');
const middle = (section.fromS + section.toS) / 2;
test('canal has high curved sides with smooth flat entry and exit', () => {
  for (const s of [section.fromS, section.toS]) {
    const p = canalProfile(track.sections, s, 18);
    assert.equal(p.height, 0); assert.equal(p.slope, 0); assert.equal(p.alongSlope, 0);
  }
  assert.ok(canalProfile(track.sections, middle, 18).height > 11);
  assert.ok(canalProfile(track.sections, middle, 18).slope > 2);
  assert.equal(canalProfile(track.sections, middle, -18).height, canalProfile(track.sections, middle, 18).height);
});

test('rendered canal vertices agree with physical support and tilted normals', () => {
  const mesh = track.meshes.road; let checked = 0;
  for (let i = 0; i < mesh.positions.length / 3; i++) {
    const s = mesh.uvs[i * 2 + 1] % track.lengthM, x = mesh.uvs[i * 2];
    if (s < section.fromS + 45 || s > section.toS - 45 || Math.abs(x) < 8) continue;
    const f = track.frameAt(s), j = i * 3;
    const alignment = f.n.reduce((v, n, k) => v + n * mesh.normals[j + k], 0);
    if (alignment < 0.2) continue;
    const p = [...mesh.positions.slice(j, j + 3)], q = track.query(...p, f.seg);
    assert.ok(q.onRoad || q.edgeDist > -0.001, 'bank is driveable at ' + q.s + ', edge ' + q.edgeDist);
    assert.ok(Math.abs(q.height) < 0.015, 'mesh/support gap ' + q.height);
    const agreement = q.normal.reduce((v, n, k) => v + n * mesh.normals[j + k], 0);
    assert.ok(agreement > 0.999, 'physical and visible bank normal agree');
    checked++;
  }
  assert.ok(checked > 100);
});

for (const side of [-1, 1]) test('kart steers onto the ' + side + ' bank and stays on the curved floor', () => {
  const st = createState(), f = track.surfaceAt(section.fromS + 55, 0), heading = f.t.map((n, k) => n * 0.8660254 + f.b[k] * side * 0.5);
  setPos(st, f.p); setQuat(st, qlook([0, 0, 0, 1], heading, f.n)); setVel(st, heading.map(v => v * 24));
  st[S.seg] = st[S.latchSeg] = f.seg; st[S.s] = section.fromS + 55; st[S.grounded] = 1; st[S.hopT] = 1000; st[S.engineV] = 24;
  const feel = makeFeel(), input = acts({ throttle: 1 }), events = recorder();
  let maxRise = 0, maxBank = 0, airborne = 0;
  for (let i = 0; i < 180; i++) {
    step(st, input, track, feel, 1 / 120, events);
    const base = track.frameAt(st[S.s]), rise = [st[S.posX], st[S.posY], st[S.posZ]].reduce((v, p, k) => v + (p - base.p[k]) * base.n[k], 0);
    maxRise = Math.max(maxRise, rise); maxBank = Math.max(maxBank, side * st[S.lateral]);
    if (!st[S.grounded]) airborne++;
  }
  assert.ok(maxBank > 12, 'steering reaches the side: ' + maxBank);
  assert.ok(maxRise > 4, 'kart actually climbs, rather than floating over a flat support: ' + maxRise);
  assert.equal(airborne, 0, 'curved support holds the kart');
});
