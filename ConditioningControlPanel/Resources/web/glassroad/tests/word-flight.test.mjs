import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN } from '../sim/state.js';
import { step, spawn } from '../sim/kart.js';
import { glideActive } from '../sim/glide.js';
import { createAirWordRun, bindAirWords, airWordQuery, phraseBounceActive } from '../sim/airWords.js';
import { DT, place, makeFeel, recorder, acts } from './sim-fake.mjs';

test('interpolated kart views share contact squash state and detach on run replacement', () => {
  const f = setup(), view = f.st.slice();
  bindAirWords(view, f.run);
  for (let i = 0; i < 900 && !phraseBounceActive(f.st); i++) tick(f);
  assert.equal(phraseBounceActive(f.st), true);
  assert.equal(phraseBounceActive(view), true);
  bindAirWords(view, null);
  assert.equal(phraseBounceActive(view), false);
});
test('an asynchronously loaded or replaced song preserves the active airborne route', () => {
  const f = setup(), { run, st, ev } = f;
  while (!run.platforms.some(r => r.alpha > .1)) tick(f);
  const row = run.platforms[0], original = row.text, section = run.activeSection;
  run.setSong({ words: Array.from({ length: 30 }, (_, i) => ({ w: 'replacement', startS: i * .8 })) });
  assert.equal(run.activeSection, section);
  assert.equal(row.text, original, 'the approaching support keeps its visible lettering');
  for (let i = 0; i < 900 && (st[S.s] < f.zone.gapS1 + 12 || run.activeSection); i++) tick(f);
  assert.ok(st[S.grounded] && st[S.s] >= f.zone.gapS1 + 12);
  assert.deepEqual(ev.all('air-word-land').map(e => e.payload.text), [original], 'the single in-flight phrase is preserved');
  assert.equal(st[S.respawnPhase], RESPAWN.NONE);
});
test('late glyph stroke anchors guide the catch smoothly onto real lettering', () => {
  const f = setup(), { run, st, ev } = f;
  let contacts = 0, previous = [st[S.posX], st[S.posY], st[S.posZ]];
  for (let i = 0; i < 900 && st[S.s] < f.zone.gapS1 + 12; i++) {
    for (const row of run.platforms) {
      row.catchText = row.text; row.catchPoints = [{ x: .1, z: .14 }, { x: -.35, z: .3 }];
    }
    tick(f);
    const p = [st[S.posX], st[S.posY], st[S.posZ]];
    assert.ok(Math.hypot(...p.map((x, k) => x - previous[k])) < 1.5, 'stroke correction never teleports');
    previous = p;
    if (ev.all('air-word-land').length > contacts) {
      const row = run.platforms.find(r => r.id === ev.all('air-word-land').at(-1).payload.id);
      for (let k = 0; k < 3; k++) {
        const expected = row.p[k] + row.b[k] * .1 * row.widthM + row.t[k] * .14 * row.lengthM;
        assert.ok(Math.abs(p[k] - expected) < .003, 'catch sits on occupied glyph stroke');
      }
      contacts++;
    }
  }
  assert.equal(contacts, 1);
});
test('every landing releases into ordinary driving with retained forward speed', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const f = setup(tr, zone), { st, run } = f;
    while (st[S.s] < zone.gapS1 + 12 || run.activeSection) tick(f);
    assert.equal(run.activeSection, null);
    assert.ok(Math.hypot(st[S.velX], st[S.velZ]) > 18, 'road handoff retains velocity');
    const exitS = st[S.s];
    for (let i = 0; i < 120; i++) tick(f, acts({ throttle: 1 }));
    assert.equal(st[S.respawnPhase], RESPAWN.NONE, tr.id + '/' + zone.id);
    assert.ok(st[S.s] > exitS + 15);
  }
});
const tracks = ['threshold', 'keep', 'boudoir', 'mainframe', 'goonworks'].map(id =>
  loadTrack(JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url), 'utf8'))));
const song = { words: Array.from({ length: 30 }, (_, i) => ({ w: 'sample' + i, startS: i * .8 })) };
function setup(tr = tracks[0], zone = tr.aerials[0], speed = 26, music = song) {
  const st = place(tr, { s: zone.rampS0 + .5, speed }), ev = recorder(), feel = makeFeel();
  const run = createAirWordRun(music, tr); bindAirWords(st, run);
  return { tr, zone, st, ev, feel, run };
}
function tick(f, action = acts(), t = f.ev.t) {
  f.run.update(t, f.st); f.ev.t += DT;
  step(f.st, action, f.tr, f.feel, DT, f.ev);
}

test('the full approach width enters the automatic route before a narrowing gap', () => {
  for (const tr of tracks) for (const zone of tr.aerials) for (const side of [-1, 1]) {
    const f = setup(tr, zone), s = zone.rampS0 + .5;
    f.st.set(place(tr, { s, lateral: side * (tr.frameAt(s).w / 2 - 1), speed: 26 }));
    for (let i = 0; i < 1000 && f.st[S.s] < zone.gapS1 + 12; i++) tick(f, acts({ steer: side, brake: 1 }));
    assert.equal(f.st[S.respawnPhase], RESPAWN.NONE, zone.id);
    assert.ok(f.st[S.s] >= zone.gapS1 + 12 && f.ev.all('air-word-land').length === 1, zone.id);
  }
});

test('all ten gaps auto-cross with no input, braking and full steering at varied entry speeds', () => {
  for (const tr of tracks) for (const zone of tr.aerials) for (const speed of [0, 14, 26, 44])
    for (const steer of [0, -1, 1]) {
      const f = setup(tr, zone, speed, steer ? song : null), { st, run, ev } = f;
      let done = false, contacts = 0, previousS = st[S.s], maxSide = 0, rotorTicks = 0;
      for (let i = 0; i < 1000; i++) {
        tick(f, acts({ steer, brake: steer ? 1 : 0 }), steer ? i * DT : NaN);
        assert.equal(st[S.respawnPhase], RESPAWN.NONE);
        if (glideActive(st)) { assert.equal(contacts, 1, 'rotor only follows the phrase hop'); rotorTicks++; }
        assert.ok(st[S.s] >= previousS - .001, 'script never reverses');
        previousS = st[S.s]; maxSide = Math.max(maxSide, Math.abs(st[S.lateral]));
        const count = ev.all('air-word-land').length;
        if (count > contacts) {
          const row = run.platforms.find(r => r.id === ev.all('air-word-land').at(-1).payload.id);
          assert.ok(row?.solid && row.alpha >= .98, 'contact waits for visible solid lettering');
          assert.ok(Math.abs(st[S.posY] - row.p[1]) < .002);
          contacts = count;
        }
        if (st[S.s] > zone.gapS1 + 12 && st[S.grounded]) { done = true; break; }
      }
      assert.ok(done && contacts === 1 && rotorTicks > 60, `${tr.id}/${zone.id} speed${speed} steer${steer} contacts${contacts}`);
      assert.equal(ev.all('airborne').length + ev.all('air-word-hop').length, 2, 'exactly two long airborne legs');
      assert.equal(glideActive(st), false, 'hat closes after the far-deck catch');
      assert.equal(ev.all('air-word-hop').length, contacts);
      assert.equal(ev.all('hop').length, 0);
      assert.ok(maxSide <= 3.81 && (!steer || maxSide > 1), 'steering works inside the safe corridor');
    }
});

test('one phrase arrives early enough to read and sends the kart across the rest of the gap', () => {
  const f = setup(), { run, ev, st } = f;
  let firstSeen = null, firstContact = null;
  for (let i = 0; i < 900; i++) {
    tick(f);
    if (run.platforms.length && firstSeen === null) {
      firstSeen = ev.t;
      assert.ok(st[S.airborne], 'the phrase arrives while approaching the catch');
    }
    if (firstContact === null && ev.all('air-word-land').length) firstContact = ev.t;
    if (st[S.s] > f.zone.gapS1 + 12) break;
  }
  assert.ok(firstContact - firstSeen >= 1.3 && firstContact - firstSeen <= 1.45);
  assert.equal(ev.all('air-word-hop').length, 1);
});

test('contact compresses, springs visibly upward and opens the hat only after the rebound apex', () => {
  const f = setup(), { st, ev } = f;
  while (!ev.all('air-word-land').length) tick(f);
  const landed = ev.t, p = ev.all('air-word-land')[0].payload.p, height = st[S.posY];
  assert.ok(phraseBounceActive(st));
  assert.ok(Math.hypot(...p.map((v, i) => v - st[S.posX + i])) < .001);
  while (!ev.all('air-word-hop').length) {
    assert.equal(glideActive(st), false); tick(f);
  }
  assert.ok(ev.t - landed >= .17 && ev.t - landed <= .21, 'the compression has readable time');
  assert.deepEqual(ev.all('air-word-hop')[0].payload.p, p);
  for (let i = 0; i < 18; i++) tick(f);
  assert.ok(st[S.posY] > height + 1 && st[S.velY] > 0, 'visible immediate spring ascent');
  assert.equal(glideActive(st), false, 'hat does not mask the launch');
  while (!glideActive(st)) tick(f);
  assert.ok(st[S.velY] <= 0, 'hat follows the actual world-space apex');
  f.run.reset(); assert.equal(phraseBounceActive(st), false);
});

test('steering changes the real flight lane and releasing the key keeps that lane', () => {
  const f = setup(), { st, ev } = f;
  while (!ev.all('air-word-hop').length) tick(f);
  for (let i = 0; i < 30; i++) tick(f, acts({ steer: 1 }));
  const heldLane = st[S.lateral]; assert.ok(heldLane > 1.5);
  for (let i = 0; i < 30; i++) tick(f);
  assert.ok(Math.abs(st[S.lateral] - heldLane) < .15, 'release does not spring the kart back to centre');
  for (let i = 0; i < 45; i++) tick(f, acts({ steer: -1 }));
  assert.ok(st[S.lateral] < heldLane - 2, 'the opposite key traverses the lane');
});

test('pause freezes flight and particles, while song seeks or silence cannot remove the catch', () => {
  const f = setup(), { run, st, ev } = f;
  while (!run.platforms.some(r => r.alpha > .1)) tick(f);
  run.update(3, st, { paused: true });
  const state = [...st], rows = JSON.stringify(run.platforms), clock = run.timeS;
  for (let i = 0; i < 120; i++) step(st, acts({ steer: 1 }), f.tr, f.feel, DT, ev);
  assert.deepEqual([...st], state); assert.equal(JSON.stringify(run.platforms), rows); assert.equal(run.timeS, clock);
  for (let i = 0; i < 800 && st[S.s] < f.zone.gapS1 + 12; i++) tick(f, acts(), i < 100 ? 90 : NaN);
  assert.ok(st[S.grounded] && st[S.s] >= f.zone.gapS1 + 12);
  assert.equal(ev.all('air-word-land').length, 1);
});

test('reset, rescue, inactive race and teleports clear rows and release ownership', () => {
  for (const kind of ['reset', 'rescue', 'inactive', 'teleport', 'spawn']) {
    const f = setup(), { run, st } = f;
    while (!run.platforms.length) tick(f);
    if (kind === 'reset') run.reset();
    if (kind === 'rescue') { st[S.respawnPhase] = RESPAWN.PULL; run.update(1, st); }
    if (kind === 'inactive') run.update(1, st, { active: false });
    if (kind === 'teleport') { st[S.posX] += 100; run.update(1, st); }
    if (kind === 'spawn') spawn(st, f.tr, f.feel);
    assert.equal(run.platforms.length, 0, kind);
    assert.equal(run.activeSection, null, kind);
  }
});

test('ordinary contacts still reject word undersides, faded and distant crossings', () => {
  const f = setup(), { st, run } = f;
  while (!run.platforms.some(r => r.solid)) tick(f);
  const row = run.platforms.find(r => r.solid);
  const query = () => ({ normal: [0, 1, 0], onRoad: false, height: 1, flags: ['jump'] });
  st[S.airborne] = 1; st[S.grounded] = 0;
  const above = row.p.map((v, i) => v + row.n[i]), below = row.p.map((v, i) => v - row.n[i]);
  assert.equal(airWordQuery(st, below, query(), above).airWord, row);
  assert.equal(airWordQuery(st, above, query(), below).airWord, null);
  row.solid = false; assert.equal(airWordQuery(st, below, query(), above).airWord, null);
  row.solid = true;
  assert.equal(airWordQuery(st, below, query(), above.map((v, i) => v + row.n[i] * 30)).airWord, null);
});

test('quiet song sections get compact trigger words, with only two supports visible', () => {
  for (const music of [null, { words: [{ w: 'far-away-lyric', startS: 1000 }] }]) {
    const f = setup(tracks[0], tracks[0].aerials[0], 26, music);
    const seen = new Set();
    for (let i = 0; i < 1000 && f.st[S.s] < f.zone.gapS1 + 12; i++) {
      tick(f);
      assert.ok(f.run.platforms.length <= 2);
      for (const row of f.run.platforms) {
        assert.ok(row.widthM <= 7 && row.lengthM <= 2.6, 'letters leave the chase view open');
        assert.ok(['LET GO', 'DRIFT AWAY', 'BREATHE SLOW', 'FLOAT ON'].includes(row.text));
        seen.add(row.text);
      }
    }
    assert.equal(seen.size, 1);
    assert.ok(f.st[S.grounded] && f.st[S.s] >= f.zone.gapS1 + 12);
  }
});

test('word catches ease vertical speed instead of reversing a hard fall', () => {
  const f = setup(); let contacts = 0;
  for (let i = 0; i < 1000 && f.st[S.s] < f.zone.gapS1 + 12; i++) {
    tick(f);
    const count = f.ev.all('air-word-land').length;
    if (count > contacts) {
      assert.ok(Math.abs(f.st[S.velY]) < 2, 'touchdown has a soft vertical approach');
      contacts = count;
    }
  }
  assert.equal(contacts, 1);
});

test('phrases join nearby words from one lyric line and never borrow an isolated word', () => {
  const groups = { words: [
    {w:'follow',startS:1.8,line:2}, {w:'the',startS:2.1,line:2}, {w:'light',startS:2.4,line:2},
    {w:'another',startS:2.5,line:3}, {w:'line',startS:2.7,line:3},
  ] };
  const f = setup(tracks[0], tracks[0].aerials[0], 26, groups);
  while (!f.run.platforms.length) tick(f, acts(), .3);
  assert.ok(['follow the light', 'another line'].includes(f.run.platforms[0].text));
  const isolated = setup(tracks[0], tracks[0].aerials[0], 26, { words: [{w:'alone',startS:2.4,line:1}] });
  while (!isolated.run.platforms.length) tick(isolated, acts(), .3);
  assert.equal(isolated.run.platforms[0].text, 'LET GO');
});

test('automatic rotor survives held brake, freezes on pause and closes on reset', () => {
  const f = setup();
  for (let i = 0; i < 1000 && !glideActive(f.st); i++) tick(f, acts({brake:1,steer:1,hop:false}));
  assert.equal(glideActive(f.st), true);
  f.run.update(3, f.st, {paused:true});
  const pose = [...f.st];
  for (let i = 0; i < 60; i++) step(f.st, acts(), f.tr, f.feel, DT, f.ev);
  assert.deepEqual([...f.st], pose); assert.equal(glideActive(f.st), true);
  f.run.reset(); assert.equal(glideActive(f.st), false);
});

test('long lyric lines choose complete short phrase windows instead of squeezing the whole line', () => {
  const f = setup(tracks[0], tracks[0].aerials[0], 26, {words:
    ['follow','the','gentle','light','across','the','water'].map((w,i)=>({w,startS:1.4+i*.25,line:1}))});
  while (!f.run.platforms.length) tick(f, acts(), .3);
  const text = f.run.platforms[0].text;
  assert.ok(text.length <= 26);
  assert.ok(text.split(' ').length >= 2 && text.split(' ').length <= 4);
  assert.ok('follow the gentle light across the water'.includes(text));
});
