import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { S, RESPAWN } from '../sim/state.js';
import { step, spawn } from '../sim/kart.js';
import { glideActive } from '../sim/glide.js';
import { phraseFlightPoints, phraseEase } from '../core/phraseFlight.js';
import { createAirWordRun, bindAirWords, airWordQuery, phraseBounceActive, airWordLateral, ARC_M,
  WORD_LENGTH_M, WORD_MAX_WIDTH_M } from '../sim/airWords.js';
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
// Online the room judges these flights (rooms/src/judge.js, owner 2026-10-30): over its speed cap (top x max x word bonus x 1.1 = 51.4
// m/s on every course) for more than 0.5 s flags the seat, more than 15 s a lap above 2.5 m uncatchable by items too.
test('every word flight stays inside the room judge limits, with or without a song', () => {
  const CAP_MPS = 51.4, OVER_S = .5, HIGH_M = 2.5, IMMUNE_LAP_S = 15;
  for (const tr of tracks) {
    let lapHigh = 0;
    for (const zone of tr.aerials) for (const music of [song, null]) {
      const f = setup(tr, zone, 44, music);
      let over = 0, longest = 0, high = 0, frames = 0;
      for (; frames < 2400 && f.st[S.s] < zone.gapS1 + 12; frames++) {
        tick(f, acts({ throttle: 1 }), music ? f.ev.t : NaN);
        over = Math.hypot(f.st[S.velX], f.st[S.velZ]) > CAP_MPS ? over + DT : 0;
        longest = Math.max(longest, over);
        if (!f.st[S.grounded] && f.st[S.height] > HIGH_M) high += DT;
      }
      assert.equal(f.st[S.respawnPhase], RESPAWN.NONE, zone.id);
      assert.ok(f.st[S.s] >= zone.gapS1 + 12, zone.id + ' crossed');
      assert.ok(longest < OVER_S, `${zone.id}: ${longest.toFixed(2)} s over the cap`);
      if (music) lapHigh += high;
    }
    assert.ok(lapHigh < IMMUNE_LAP_S / 2, `${tr.id}: ${lapHigh.toFixed(1)} s high a lap leaves the budget to its ramps`);
  }
});
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
      assert.ok(done && contacts === 1 && rotorTicks >= 18, `${tr.id}/${zone.id} speed${speed} steer${steer} contacts${contacts} rotorTicks${rotorTicks}`);
      assert.equal(ev.all('airborne').length + ev.all('air-word-hop').length, 2, 'exactly two long airborne legs');
      assert.equal(glideActive(st), false, 'hat closes after the far-deck catch');
      assert.equal(ev.all('air-word-hop').length, contacts);
      assert.equal(ev.all('air-word-launch').length, 1, 'first takeoff also plays the quiet spring');
      assert.equal(ev.all('hop').length, 0);
      assert.ok(maxSide <= 3.81 && (!steer || maxSide > 1), 'steering works inside the safe corridor');
    }
});

test('both floating-word legs cross briskly on every course while keeping the real phrase catch', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const f = setup(tr, zone), launches = [], landings = [];
    for (let i = 0; i < 1000 && (f.st[S.s] < zone.gapS1 + 12 || f.run.activeSection); i++) {
      tick(f);
      const count = f.ev.all('airborne').length + f.ev.all('air-word-hop').length;
      if (count > launches.length) launches.push(f.ev.t);
      if (f.ev.all('land').length > landings.length) landings.push(f.ev.t);
    }
    assert.equal(launches.length, 2); assert.equal(landings.length, 2);
    assert.ok(landings[0] - launches[0] <= 4.4, tr.id + ' first arc is no longer a seven-second float');
    assert.ok(landings[1] - launches[1] <= 3.3, tr.id + ' rebound is no longer a five-second float');
    assert.equal(f.ev.all('air-word-land').length, 1);
    assert.equal(f.st[S.respawnPhase], RESPAWN.NONE);
  }
});

test('one phrase rescues a fast visible fall and sends the kart across the rest of the gap', () => {
  const f = setup(), { run, ev, st } = f;
  let firstSeen = null, firstContact = null;
  for (let i = 0; i < 900; i++) {
    tick(f);
    if (run.platforms.length && firstSeen === null) {
      firstSeen = ev.t;
      assert.ok(st[S.airborne], 'the phrase arrives while approaching the catch');
      assert.ok(st[S.velY] < 0, 'the word appears under the kart as it starts to drop from the apex');
    }
    if (firstContact === null && ev.all('air-word-land').length) firstContact = ev.t;
    if (st[S.s] > f.zone.gapS1 + 12) break;
  }
  assert.ok(firstContact - firstSeen >= .60 && firstContact - firstSeen <= .67);
  assert.equal(ev.all('air-word-hop').length, 1);
});

test('the first arc remains within 2.2 metres of the steady camera grade on every gap', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const f = setup(tr, zone), [start, catchPoint] = phraseFlightPoints(tr, zone);
    let peak = 0;
    while (!f.ev.all('air-word-land').length) {
      tick(f);
      if (f.st[S.s] <= start.s) continue;
      const u = Math.min(1, (f.st[S.s] - start.s) / (catchPoint.s - start.s));
      const baseline = start.y + (catchPoint.y - start.y) * phraseEase(u);
      const above = f.st[S.posY] - baseline;
      assert.ok(above <= ARC_M[0] + .001, tr.id + ': first jump left the stable framing envelope');
      peak = Math.max(peak, above);
    }
    assert.ok(peak > ARC_M[0] * .97, tr.id + ': first arc still has a visible jump');
  }
});

test('the higher rebound clears the far lip and lands well onto the deck sooner on every gap', () => {
  for (const tr of tracks) for (const zone of tr.aerials) {
    const f = setup(tr, zone), [, start, end] = phraseFlightPoints(tr, zone);
    while (!f.ev.all('air-word-land').length) tick(f);
    const catchTime = f.ev.t, oldEndS = zone.gapS1 + 2;
    const oldDuration = Math.max(2.8, Math.min(7, (oldEndS - start.s) / 23));
    let peak = 0, lipClearance = null;
    while (f.st[S.s] < end.s) {
      const previousS = f.st[S.s];
      tick(f);
      const u = Math.min(1, (f.st[S.s] - start.s) / (end.s - start.s));
      const baseline = start.y + (end.y - start.y) * phraseEase(u);
      const above = f.st[S.posY] - baseline;
      assert.ok(above <= ARC_M[1] + .001, tr.id + ': rebound remains within its bounded height envelope');
      peak = Math.max(peak, above);
      if (previousS < zone.gapS1 && f.st[S.s] >= zone.gapS1) lipClearance = f.st[S.posY] - tr.frameAt(f.st[S.s]).p[1];
    }
    assert.ok(peak > ARC_M[1] * .97 && ARC_M[1] > ARC_M[0], tr.id + ': visibly higher spring');
    assert.ok(lipClearance > .6, `${tr.id}: clear the far edge instead of skimming it (${lipClearance})`);
    assert.ok(end.s >= zone.gapS1 + 7.9 && end.s < zone.landingS1, 'catch safely onto the landing deck');
    assert.ok(f.ev.t - catchTime <= oldDuration * .9 + DT, tr.id + ': quicker rebound despite the longer clear landing');
  }
});

test('contact springs without stopping forward travel and opens the hat only after the rebound apex', () => {
  const f = setup(), { st, ev } = f;
  while (!ev.all('air-word-land').length) tick(f);
  const p = ev.all('air-word-land')[0].payload.p, height = st[S.posY];
  assert.ok(phraseBounceActive(st));
  assert.ok(Math.hypot(...p.map((v, i) => v - st[S.posX + i])) < .001);
  assert.equal(ev.all('air-word-hop').length, 1, 'contact and spring occur in the same simulation step');
  assert.ok(st[S.airborne] && !st[S.grounded], 'contact never starts a stationary grounded hold');
  assert.deepEqual(ev.all('air-word-hop')[0].payload.p, p);
  for (let i = 0; i < 18; i++) {
    const previousS = st[S.s]; tick(f);
    assert.ok(st[S.s] > previousS + .05, 'forward progress continues throughout the visible compression');
  }
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
        assert.ok(row.widthM <= WORD_MAX_WIDTH_M && row.lengthM <= WORD_LENGTH_M, 'big letters stay inside their caps');
        assert.ok(row.widthM >= 6.6 && row.lengthM >= 4.7, 'phrases read about one and a half times the old glyph size');
        assert.ok(['LET GO', 'DRIFT AWAY', 'BREATHE SLOW', 'FLOAT ON'].includes(row.text));
        seen.add(row.text);
      }
    }
    assert.equal(seen.size, 1);
    assert.ok(f.st[S.grounded] && f.st[S.s] >= f.zone.gapS1 + 12);
  }
});

test('word catches retain falling momentum until the immediate spring reversal', () => {
  const f = setup(); let contacts = 0;
  for (let i = 0; i < 1000 && f.st[S.s] < f.zone.gapS1 + 12; i++) {
    tick(f);
    const count = f.ev.all('air-word-land').length;
    if (count > contacts) {
      assert.ok(f.st[S.velY] < -3, 'touchdown retains visible falling speed without a pre-contact hover');
      const s = f.st[S.s]; tick(f);
      assert.ok(f.st[S.velY] > 3, 'the phrase springs upward on the next step');
      assert.ok(f.st[S.s] > s, 'the rebound keeps forward motion');
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

test('the word spawns under the racer near either edge, clamped so the whole phrase stays over the road', () => {
  assert.equal(airWordLateral(3, 14, 8), 3);
  assert.equal(airWordLateral(13, 14, 8), 14 - 4 - 1.2);
  assert.equal(airWordLateral(-13, 14, 8), -(14 - 4 - 1.2));
  assert.equal(airWordLateral(5, 3, 8), 0, 'a road too narrow to slide keeps the centre');
  for (const tr of tracks) for (const zone of tr.aerials) for (const side of [-1, 0, 1]) {
    const s = zone.rampS0 + .5, f = setup(tr, zone), lateral = side * (tr.frameAt(s).w / 2 - 1);
    f.st.set(place(tr, { s, lateral, speed: 26 }));
    let seen = null, contact = null;
    for (let i = 0; i < 1000 && f.st[S.s] < zone.gapS1 + 12; i++) {
      tick(f);
      if (!seen && f.run.platforms.length) seen = { row: f.run.platforms[0], lane: f.st[S.lateral] };
      if (!contact && f.ev.all('air-word-land').length) contact = f.st[S.lateral];
    }
    assert.ok(seen && contact !== null, zone.id);
    const { row } = seen, half = Math.min(tr.frameAt(row.s).w, zone.widthM) / 2;
    const centre = row.p.reduce((v, x, i) => v + (x - tr.frameAt(row.s).p[i]) * tr.frameAt(row.s).b[i], 0) - row.shift[1];
    assert.ok(Math.abs(centre - row.lateral) < .01, zone.id + ' the platform sits at its lateral offset');
    assert.ok(Math.abs(row.lateral) + row.widthM / 2 <= half, zone.id + ' the whole word stays over the road');
    if (!side) assert.ok(Math.abs(row.lateral) < .01, zone.id + ' a centred racer keeps a centred word');
    else assert.ok(Math.sign(row.lateral) === side && Math.abs(row.lateral) > 3, zone.id + ' the word follows the racer toward the edge');
    const room = half - row.widthM / 2 - 1.2, under = Math.max(-room, Math.min(room, seen.lane));
    assert.ok(Math.abs(under - row.lateral) < .05, zone.id + ' the word materialises right under the kart (or at its clamp)');
    if (Math.abs(seen.lane) <= room + 2.6) assert.ok(Math.abs(contact - row.lateral) < row.widthM / 2, zone.id + ' the catch lands on the shifted word');
    assert.equal(f.st[S.respawnPhase], RESPAWN.NONE);
  }
});
