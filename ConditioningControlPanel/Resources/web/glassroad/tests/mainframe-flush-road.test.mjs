import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { step } from '../sim/kart.js';
import { S } from '../sim/state.js';
import { place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));

function drive(start, seconds, speed = 25, lateral = 0, endS = Infinity) {
  const st = place(track, { s: start, speed, lateral }), events = recorder(), feel = makeFeel();
  const y0 = st[S.posY];
  let maxOffset = 0, maxHeight = 0, airborne = 0;
  for (let i = 0; i < seconds / DT && st[S.s] < endS; i++) {
    step(st, acts({ throttle: 1 }), track, feel, DT, events);
    maxOffset = Math.max(maxOffset, Math.abs(st[S.posY] - y0));
    maxHeight = Math.max(maxHeight, Math.abs(st[S.height]));
    if (!st[S.grounded]) airborne++;
  }
  return { st, events, maxOffset, maxHeight, airborne };
}

test('Mainframe flush floor crosses repeated visual plate seams without vertical steps or landings', () => {
  const skybridge = track.sections.find(section=>section.id==='skybridge').fromS + 40;
  for (const [start, lateral] of [[20, 0], [20, 3], [skybridge, 0], [skybridge, -3]]) {
    const r = drive(start, 3, 25, lateral);
    assert.ok(r.st[S.s] - start > 80, 'cross at least thirteen 6 m plate seams');
    assert.equal(r.maxOffset, 0, 'world height is constant across every painted seam');
    assert.equal(r.maxHeight, 0);
    assert.equal(r.airborne, 0);
    assert.equal(r.events.all('land').length, 0);
    assert.equal(r.events.all('takeoff').length, 0);
  }
});

test('Mainframe authored climbs retain physical elevation rather than flattening support', () => {
  const upperBus = track.sections.find(section=>section.id==='upper-bus');
  const endS = upperBus.toS - 30;
  const r = drive(upperBus.fromS + 30, (upperBus.toS - upperBus.fromS) / 20, 25, 0, endS);
  assert.ok(r.st[S.s] >= endS, 'physically cross the authored climb before the next bend');
  assert.ok(r.maxOffset > 20, 'the upper bus still physically climbs more than 20 m');
  assert.equal(r.maxHeight, 0);
  assert.equal(r.airborne, 0);
});
