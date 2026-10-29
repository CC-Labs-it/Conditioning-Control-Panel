import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createAi } from '../sim/ai.js';
import { glideActive } from '../sim/glide.js';
import { DT, place, makeFeel, recorder, acts } from './sim-fake.mjs';
const tracks = ['threshold', 'keep', 'boudoir', 'mainframe', 'goonworks'].map(id => loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url), 'utf8'))));

test('contact-launched racers entering an aerial already airborne can still open the finite cap', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const st = place(tr, { s: zone.gapS0 + 1, speed: 45, height: 1, airborne: true });
    const ev = recorder(), feel = makeFeel(); st[S.velY] = Math.max(12, st[S.velY]);
    let used = false, landed = false;
    for (let i = 0; i < 900; i++) {
      step(st, acts({ throttle: 1, hop: true }), tr, feel, DT, ev);
      used ||= glideActive(st);
      if (st[S.grounded] && st[S.s] >= zone.landingS0) { landed = true; break; }
      if (st[S.respawnPhase]) break;
    }
    assert.ok(used && landed, tr.id + '/' + zone.id);
  }
  const tr = tracks[0], zone = tr.aerials[0];
  const fallen = place(tr, { s: zone.gapS0 + 20, height: -5, speed: 20, airborne: true });
  step(fallen, acts({ hop: true }), tr, makeFeel(), DT, recorder());
  assert.equal(glideActive(fallen), false, 'being far below a gap never arms a new cap');
});

test('all ten long launches physically clear at14/26/44mps, including late cap', () => {
  for (const tr of tracks) for (const zone of tr.aerials) for (const speed of [14, 26, 44]) for (const delay of [0, .7]) {
    const st = place(tr, { s: zone.gapS0 - 1, speed }), ev = recorder(), feel = makeFeel();
    let time = 0, flight = 0, tookOff = false, landed = false, used = false;
    for (let i = 0; i < 60 * 15; i++) {
      if (st[S.airborne]) { tookOff = true; flight += DT; }
      step(st, acts({ throttle: 1, hop: flight > delay }), tr, feel, DT, ev); time += DT;
      used ||= glideActive(st);
      if (tookOff && st[S.grounded] && st[S.s] >= zone.landingS0) { landed = true; break; }
      if (st[S.respawnPhase] !== RESPAWN.NONE) break;
    }
    assert.ok(landed && used, `${tr.id}/${zone.id} speed${speed} delay${delay} s${st[S.s]} y${st[S.posY]} t${time}`);
  }
});

test('all ten launches let real bot actions clear without a song', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const feel = makeFeel(), st = place(tr, { s: zone.rampS0 - 30, speed: 28 }), ev = recorder(), ai = createAi(tr, feel);
    const r = { i: 1, st, feel, rubber: 1, meta: { isBot: true }, events: ev }, action = acts();
    let air = false, landed = false;
    for (let i = 0; i < 900; i++) {
      ai.drive(r, { race: { phase: 'racing', timeS: 10 + i * DT }, racers: [r] }, DT, action);
      step(st, action, tr, feel, DT, ev); air ||= !!st[S.airborne];
      if (air && st[S.grounded] && st[S.s] > zone.landingS0) { landed = true; break; }
      if (st[S.respawnPhase]) break;
    }
    assert.ok(landed, tr.id + '/' + zone.id);
  }
});

test('rescue grace can redeploy the finite cap from a launch lip', () => {
  const tr = tracks[0], zone = tr.aerials[0], st = place(tr, { s: zone.gapS0 - 1, speed: 26 }), feel = makeFeel(), ev = recorder();
  st[S.respawnPhase] = RESPAWN.GRACE;
  let active = false, flew = false;
  for (let i = 0; i < 900; i++) {
    step(st, acts({ throttle: 1, hop: true }), tr, feel, DT, ev);
    active ||= glideActive(st); flew ||= !!st[S.airborne];
    if (flew && st[S.grounded] && st[S.s] >= zone.landingS0) break;
  }
  assert.ok(active && st[S.grounded] && st[S.s] >= zone.landingS0);
});
