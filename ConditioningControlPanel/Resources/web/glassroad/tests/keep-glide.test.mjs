import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { S, RESPAWN } from '../sim/state.js';
import { step, spawn } from '../sim/kart.js';
import { glideActive } from '../sim/glide.js';
import { makeFeel, place, acts, recorder, DT } from './sim-fake.mjs';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url), 'utf8')));
const layout = getKeepLayout(track), bridge = layout.drawbridge;
function flight({ speed = 30, held = true, release = Infinity, manual = false } = {}) {
  const st = place(track, { s: bridge.rampS0 - 35, speed }), feel = makeFeel(), ev = recorder();
  let launched = false, flightTime = 0, activeTicks = 0, launchS = 0, releaseChecked = false;
  for (let tick = 0; tick < 900; tick++) {
    const hop = held && flightTime < release;
    const a = acts({ throttle: 1, hop, hopPressed: manual && !launched && st[S.s] > bridge.rampS1 - 6 });
    const prior = st[S.airborne];
    step(st, a, track, feel, DT, ev);
    if (!launched && !prior && st[S.airborne]) { launched = true; launchS = st[S.s]; }
    if (launched) {
      flightTime += DT;
      if (glideActive(st)) activeTicks++;
      if (!hop && release < Infinity) { assert.equal(glideActive(st), false, 'release ends lift this tick'); releaseChecked = true; }
      if (!st[S.airborne] || ev.all('land').length) return { st, ev, launchS, flightTime, activeTicks, releaseChecked, distance: st[S.s] - launchS };
    }
    assert.equal(st[S.respawnPhase], RESPAWN.NONE, 'flight never requires rescue');
  }
  assert.fail('drawbridge flight did not land within 15 seconds');
}
for (const speed of [0, 12, 26, 40]) test('Held rotor extends novice drawbridge flight and lands before stones, speed ' + speed, () => {
  const off = flight({ speed, held: false }), on = flight({ speed });
  assert.ok(on.activeTicks > 20, 'held rotor deployed');
  assert.ok(on.distance > off.distance + 8, `held ${on.distance}, normal ${off.distance}`);
  assert.ok(on.flightTime > off.flightTime + .3, 'slower descent extends airtime');
  assert.ok(on.st[S.s] >= bridge.landingS0 && on.st[S.s] < layout.stoneFields[0].s0, 'lands on wide approach');
  assert.ok(on.flightTime < 6, 'finite flight while continuously held');
  assert.equal(glideActive(on.st), false, 'landing resets rotor');
});
test('Releasing the rotor shortens flight', () => {
  const release = flight({ release: .65 }), held = flight();
  assert.ok(release.releaseChecked);
  assert.ok(release.distance < held.distance - 8, 'released flight lands earlier');
});
test('A manual hop near the drawbridge lip can deploy the rotor', () => {
  const r = flight({ manual: true });
  assert.ok(r.activeTicks > 0); assert.ok(r.ev.all('hop').length);
  assert.ok(r.st[S.s] > bridge.landingS0 && r.st[S.s] < layout.stoneFields[0].s0);
});
test('Rescue and spawn clear flight state', () => {
  const st = place(track, { s: bridge.rampS1 - 2, speed: 30 }), feel = makeFeel(), ev = recorder(), a = acts({ throttle: 1, hop: true });
  for (let i = 0; i < 40 && !glideActive(st); i++) step(st, a, track, feel, DT, ev);
  assert.equal(glideActive(st), true);
  st[S.respawnPhase] = RESPAWN.PULL; step(st, a, track, feel, DT, ev);
  assert.equal(glideActive(st), false);
  spawn(st, track, feel); assert.equal(glideActive(st), false);
});
test('Threshold and an ordinary Keep hop never receive rotor lift', () => {
  const threshold = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
  for (const t of [track, threshold]) {
    const st = place(t, { s: 20, speed: 30 }), feel = makeFeel(), ev = recorder();
    for (let i = 0; i < 60; i++) {
      step(st, acts({ throttle: 1, hop: true, hopPressed: i === 0 }), t, feel, DT, ev);
      assert.equal(glideActive(st), false);
    }
  }
});

test('Re-holding after release cannot renew lift during the same flight', () => {
  const st = place(track, { s: bridge.rampS1 - 2, speed: 30 }), feel = makeFeel(), ev = recorder();
  for (let i = 0; i < 40 && !glideActive(st); i++) step(st, acts({ throttle: 1, hop: true }), track, feel, DT, ev);
  assert.equal(glideActive(st), true);
  step(st, acts({ throttle: 1, hop: false }), track, feel, DT, ev);
  for (let i = 0; i < 20; i++) {
    step(st, acts({ throttle: 1, hop: true }), track, feel, DT, ev);
    assert.equal(glideActive(st), false);
  }
});
