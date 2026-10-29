import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { shapeGoonworksNodes } from '../track/shapeGoonworks.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { boostTurnRadius, BOOST_TURN } from '../track/boostSafety.js';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';
import { calmMul } from '../sim/steer.js';
import { steerTo } from '../dev/hook.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';

const source = JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'));
const shaped = structuredClone(source);
shapeGoonworksNodes(shaped.nodes);
const track = loadTrack(shaped, { moving: true }), layout = getGoonworksLayout(track);
const angle = (a, b) => Math.atan2(a[0] * b[2] - a[2] * b[0], a[0] * b[0] + a[2] * b[2]);

test('GOONWORKS broadens real road shapes while retaining station, flight and jump identities', () => {
  assert.equal(shaped.nodes.length, source.nodes.length);
  for (let i = 0; i < source.nodes.length; i++) {
    assert.equal(shaped.nodes[i].p[1], source.nodes[i].p[1]);
    for (const key of ['flags', 'roll', 'w', 'surface', 'speed']) assert.deepEqual(shaped.nodes[i][key], source.nodes[i][key]);
  }
  assert.deepEqual(shaped.routes, source.routes);
  assert.deepEqual(shaped.fields, source.fields);
  assert.deepEqual(shaped.jumps, source.jumps);
  assert.ok(track.lengthM > 9400 && track.lengthM < 9900, 'broad final course remains bounded');
  for (const flight of track.aerials) assert.ok(flight.gapS1 - flight.gapS0 >= 89.9 && flight.gapS1 - flight.gapS0 <= 130,
    flight.id + ' retains its real authored long-flight distance');
  for (let s = 0; s < track.lengthM; s += 5) {
    const a = track.frameAt(s), b = track.frameAt(s + 60);
    assert.ok(Math.abs(angle(a.t, b.t)) < .46, 'at most 26.4 degrees within 60m at ' + s);
  }
  const twice = structuredClone(shaped.nodes);
  shapeGoonworksNodes(twice);
  assert.deepEqual(twice, shaped.nodes, 'runtime shape is deterministic and idempotent');
});

test('GOONWORKS ribbon is one broad circle with full-speed boost lanes', () => {
  const span = layout.byId['ribbon-braid'];
  let turn = 0;
  for (let s = span.s0 + 4; s < span.s1 - 4; s += 2) turn += angle(track.frameAt(s).t, track.frameAt(s + 2).t);
  assert.ok(Math.abs(turn) > 6.1 && Math.abs(turn) < 6.4, 'one full circle replaces two tight coils');
  const pads = track.boosts.filter(pad => pad.s >= span.s0 && pad.s < span.s1);
  const authored = pads.filter(pad => source.boosts.some(p => p.node === pad.node && p.x === pad.x)
    && Math.abs(pad.s - track.nodeS[pad.node]) < 1e-6);
  assert.equal(authored.length, 6, 'both authored three-pad trains remain fun alongside generated prefixes');
  assert.ok(pads.length >= authored.length, 'generated fun pads are included in the real plan');
  for (const pad of pads) assert.ok(boostTurnRadius(track, pad) > 305, 'boost lane has steering reserve with maximum clean pace and towing');
});

test('GOONWORKS enlarged districts do not cross at the same height', () => {
  const samples = [];
  for (let s = 0; s < track.lengthM; s += 10) samples.push({ s, ...track.frameAt(s) });
  for (let i = 0; i < samples.length; i++) for (let j = i + 1; j < samples.length; j++) {
    const a = samples[i], b = samples[j], distance = Math.min(b.s - a.s, track.lengthM - b.s + a.s);
    if (distance < 80 || a.flags.includes('jump') || b.flags.includes('jump')) continue;
    const horizontal = Math.hypot(a.p[0] - b.p[0], a.p[2] - b.p[2]);
    const clearance = Math.abs(a.p[1] - b.p[1]) - Math.abs(a.b[1]) * a.w / 2 - Math.abs(b.b[1]) * b.w / 2;
    if (horizontal < (a.w + b.w) / 2) assert.ok(clearance >= 10, 'separate banked decks at ' + a.s + '/' + b.s);
  }
});

test('every actual GOONWORKS pad train, including generated prefixes, clears towing boosts and Surge entries', () => {
  const runs = [];
  for (const [index, pad] of track.boosts.entries()) {
    const previous = runs.at(-1)?.at(-1);
    if (previous && pad.s - previous.s < 90 && pad.x === previous.x) runs.at(-1).push({ ...pad, index });
    else runs.push([{ ...pad, index }]);
  }
  assert.ok(runs.length >= 2);
  assert.equal(runs.reduce((count, run) => count + run.length, 0), track.boosts.length, 'every live pad is covered');
  for (const run of runs) for (const entrySpeed of [64.584, 66.7368]) {
    const feel = makeFeel(), events = recorder();
    feel.set('speed.topMps', 53.82);
    const st = place(track, { s: run[0].s - 12, lateral: run[0].x, speed: entrySpeed });
    const end = run.at(-1).s + run.at(-1).len + BOOST_TURN.rolloutM;
    for (let i = 0; i < 12 / DT && st[S.s] < end; i++) {
      const steer = steerTo(st, track, st[S.s] + 12, run[0].x, 3) / Math.max(.2, calmMul(st[S.engineV], feel));
      step(st, acts({ throttle: 1, steer }), track, feel, DT, events);
      updateRespawn(st, track, feel, DT, events);
    }
    assert.ok(st[S.s] >= end, 'full-speed train and its complete reaction, tail and braking runout completed');
    assert.deepEqual(events.all('pad').map(event => event.payload.index), run.map(pad => pad.index),
      'all authored and generated pads pay at entry speed ' + entrySpeed);
    assert.equal(events.all('wall').length, 0);
    assert.equal(events.all('drop').length, 0);
  }
});

test('GOONWORKS grand-fold cuts still open toward a supported lower revolution', () => {
  assert.equal(track.routes.length, 2);
  for (const route of track.routes) {
    const from = track.surfaceAt((route.openS0 + route.openS1) / 2, -track.frameAt(route.openS0).w / 2);
    const target = track.query(from.p[0], route.pad.p[1], from.p[2], (route.land.from + route.land.to) >> 1);
    assert.ok(target.seg >= route.land.from && target.seg <= route.land.to, route.id + ' landing remains one revolution ahead');
    assert.ok(target.onRoad && !target.hole, route.id + ' lower deck under the open side');
    assert.ok(from.p[1] - target.p[1] > 10, route.id + ' genuine drop');
  }
});
