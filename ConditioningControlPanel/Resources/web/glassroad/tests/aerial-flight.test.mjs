import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createAi } from '../sim/ai.js';
import { glideActive } from '../sim/glide.js';
import { createAirWordRun, bindAirWords, airWordQuery } from '../sim/airWords.js';
import { DT, place, makeFeel, recorder, acts, fakeTrack } from './sim-fake.mjs';
const tracks = ['threshold', 'keep', 'boudoir', 'mainframe', 'goonworks'].map(id => loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url), 'utf8'))));

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

function wordScene() {
  const tr = fakeTrack({ flags: i => i >= 10 && i < 21 ? ['jump'] : ['rail'], w: 24 });
  tr.aerials = [{ id: 'air', rampS0: 80, rampS1: 100, gapS0: 100, gapS1: 210, landingS0: 210, landingS1: 250, widthM: 24 }];
  const st = place(tr, { s: 99, speed: 20 });
  const run = createAirWordRun({ words: Array.from({ length: 20 }, (_, i) => ({ w: 'recorded' + i, startS: .5 + i, endS: .8 + i })) }, tr);
  bindAirWords(st, run); return { tr, st, run };
}

test('a sung word is genuine ground, hop onward and deploy cap later', () => {
  const { tr, st, run } = wordScene(), ev = recorder(), feel = makeFeel();
  let landed = false, hopped = false, deployed = false, finished = false;
  for (let i = 0; i < 900; i++) {
    run.update(i * DT, st);
    const onWord = ev.all('air-word-land').length > 0;
    const press = onWord && st[S.grounded] && !hopped;
    const cap = hopped && st[S.airTime] > .45;
    step(st, acts({ throttle: 1, hopPressed: press, hop: cap }), tr, feel, DT, ev);
    landed ||= onWord; hopped ||= press; deployed ||= glideActive(st);
    if (st[S.grounded] && st[S.s] >= 210) { finished = true; break; }
    if (st[S.respawnPhase]) break;
  }
  assert.ok(landed && hopped && deployed && finished, `land${landed} hop${hopped} cap${deployed} finish${finished} s${st[S.s]}`);
});

test('word landing, pressed hop and delayed cap finish every actual long launch', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const st = place(tr, { s: zone.gapS0 - 1, speed: 26 }), ev = recorder(), feel = makeFeel();
    const run = createAirWordRun({ words: Array.from({ length: 20 }, (_, i) => ({ w: 'real' + i, startS: .5 + i, endS: .8 + i })) }, tr);
    bindAirWords(st, run);
    let hopped = false, deployed = false, finished = false;
    for (let i = 0; i < 900; i++) {
      run.update(i * DT, st);
      const press = ev.all('air-word-land').length > 0 && st[S.grounded] && !hopped;
      step(st, acts({ throttle: 1, hopPressed: press, hop: hopped && st[S.airTime] > .45 }), tr, feel, DT, ev);
      hopped ||= press; deployed ||= glideActive(st);
      if (st[S.grounded] && st[S.s] >= zone.landingS0) { finished = true; break; }
      if (st[S.respawnPhase]) break;
    }
    assert.ok(hopped && deployed && finished, `${tr.id}/${zone.id} hop${hopped} cap${deployed} finish${finished}`);
  }
});

test('suitable sung phrases make complete word routes without rotor lift, including Gentle', () => {
  for (const tr of tracks) for (const zone of tr.aerials) for (const gentle of [false, true]) {
    const st = place(tr, { s: zone.gapS0 - 1, speed: 26 }), ev = recorder(), feel = makeFeel({ 'speed.topMps': 26, 'gentle.on': gentle });
    const run = createAirWordRun({ words: Array.from({ length: 20 }, (_, i) => ({ w: 'word' + i, startS: .5 + i * .9, endS: .8 + i * .9 })) }, tr);
    bindAirWords(st, run); let finished = false;
    for (let i = 0; i < 900; i++) {
      run.update(i * DT, st);
      const row = run.platforms.find(r => Math.abs(st[S.s] - r.s) < r.lengthM / 2 + 1);
      const press = st[S.grounded] && row && st[S.s] > row.s + row.lengthM / 2 - 2.5;
      step(st, acts({ throttle: 1, hopPressed: !!press }), tr, feel, DT, ev);
      assert.equal(glideActive(st), false);
      if (st[S.grounded] && st[S.s] >= zone.landingS0) { finished = true; break; }
      if (st[S.respawnPhase]) break;
    }
    assert.ok(finished && ev.all('air-word-land').length >= 2, `${tr.id}/${zone.id} contacts${ev.all('air-word-land').length} s${st[S.s]}`);
  }
});

test('word surfaces reject underside, above, sideways, faded and teleport contacts', () => {
  const { tr, st, run } = wordScene(); run.update(0, st); run.update(.49, st);
  const row = run.platforms[0]; assert.ok(row?.solid);
  const query = () => ({ normal: [0, 1, 0], onRoad: false, height: 1, flags: ['jump'] });
  st[S.airborne] = 1; st[S.grounded] = 0;
  const above = row.p.map((v, i) => v + row.n[i]);
  const below = row.p.map((v, i) => v - row.n[i]);
  assert.equal(airWordQuery(st, below, query(), above).airWord, row);
  assert.equal(airWordQuery(st, above, query(), below).airWord, null);
  assert.equal(airWordQuery(st, above, query(), above.map((v, i) => v + row.n[i])).airWord, null);
  const side = below.map((v, i) => v + row.b[i] * (row.widthM / 2 + .1));
  assert.equal(airWordQuery(st, side, query(), side.map((v, i) => v + row.n[i] * 2)).airWord, null);
  row.solid = false; assert.equal(airWordQuery(st, below, query(), above).airWord, null);
  row.solid = true; assert.equal(airWordQuery(st, below, query(), above.map((v, i) => v + row.n[i] * 30)).airWord, null);
});

test('song pause freezes platforms, seek and rescue clear them, pool stays bounded', () => {
  const { st, run } = wordScene(); run.update(0, st); run.update(.49, st);
  const before = JSON.stringify(run.platforms); run.update(.49, st, { paused: true }); assert.equal(JSON.stringify(run.platforms), before);
  run.update(20, st); assert.ok(run.platforms.length <= 12);
  st[S.respawnPhase] = RESPAWN.PULL; run.update(20.1, st); assert.equal(run.platforms.length, 0);
  st[S.respawnPhase] = 0; run.update(0, st); assert.equal(run.timeS, 0);
});
