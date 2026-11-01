// tests/tunnel-landing.test.mjs - Tunnel Vision 2 (owner 2026-11-01): who gets it, the ride inside 8 s, the bump as it lands.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeTrack, place, makeFeel, recorder, acts, DT } from './sim-fake.mjs';
import { S } from '../sim/state.js';
import { createItems } from '../sim/items.js';
import { createContact } from '../sim/contact.js';
import { createProgress } from '../sim/progress.js';
import { step as kartStep } from '../sim/kart.js';
import { bucketOf, makeRand, rollItem, tunnelOffered } from '../sim/itemTable.js';
import { loadTrack } from '../track/load.js';

const RACE = { phase: 'racing', timeS: 60, lapStartS: 0, finish() {} };
const COURSES = ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks'];
const read = (id) => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
const realTrack = (id) => loadTrack(read(id), { moving: true });

function racer(track, feel, i, o, rec) {
  const st = place(track, o);
  const r = { i, meta: { i, id: 'k' + i, name: 'k', isBot: i > 0, tint: i, slot: i }, st, input: acts({ item: false, back: false }), speed: o.speed || 0,
    mods: { steerMul: 1, steerAdd: 0, throttleMul: 1, gripMul: 1, slideMul: 1, capMul: 1, shoveMul: 1, blind: 0 },
    item: { id: null, rolling: false, rollT: 0, heldT: 0 }, fxv: { spinRad: 0, shield: null, echo: 0, effect: null, ghost: false, slip: 0, place: 0 }, finished: false, feel };
  r.events = rec;
  r.raceView = RACE;
  r.progress = createProgress(track, recorder());
  r.progress.reset?.(st);
  return r;
}

/** Karts by lap-free progress; the rider's laps come from its real progress.js, the rest roll on at their speed. */
function setup(track, karts, feelOver = {}) {
  const feel = makeFeel(feelOver), rec = recorder(), L = track.lengthM;
  const items = createItems(track, feel, rec, { seed: 3 }), contact = createContact(track, feel, rec);
  const list = karts.map((o, i) => racer(track, feel, i, { ...o, s: o.s % L }, rec));
  const prog = list.map((r, i) => karts[i].s);
  const last = list.map((r) => r.st[S.s]);
  const std = { order: [], progressOf: (i) => prog[i], placeOf: (i) => std.order.indexOf(i) + 1, gapToLeader: (i) => prog[std.order[0]] - prog[i] };
  const sort = () => { std.order = list.map((r) => r.i).sort((a, b) => (list[b].finished - list[a].finished) || (prog[b] - prog[a])); };
  sort();
  const roll = (r) => {
    if (r.drive) kartStep(r.st, acts({ throttle: 1 }), track, feel, DT, rec);   // from here on the real kart sim
    else if (r.speed > 0) {
      const s = (r.st[S.s] + r.speed * DT) % L, lat = r.st[S.lateral], air = r.st[S.airborne];
      r.st.set(place(track, { s, lateral: lat, speed: r.speed }));
      if (air) { r.st[S.airborne] = 1; r.st[S.grounded] = 0; }
    }
  };
  let peak = 0;
  const tick = () => {
    rec.t += DT;
    for (const r of list) {
      if (items.tunnel.riding(r.i)) {
        const n = items.tunnel.slicesFor(r, DT);
        for (let k = 0; k < n; k++) { items.tunnel.step(r, DT / n); r.progress.update(r.st, RACE); }
        peak = Math.max(peak, Math.hypot(r.st[S.velX], r.st[S.velY], r.st[S.velZ]));
      } else if (!r.finished) roll(r);
    }
    contact.update(DT, list, RACE);
    for (let i = 0; i < list.length; i++) { let d = (list[i].st[S.s] - last[i]) % L; if (d < -L / 2) d += L; prog[i] += d; last[i] = list[i].st[S.s]; }
    sort();
    items.update(DT, list, std, RACE);
  };
  const fire = (who) => {
    Object.assign(list[who].item, { id: 'tunnel', rolling: false, rollT: 0, heldT: 0 });
    list[who].input.item = true; tick(); list[who].input.item = false;
  };
  const ride = (who, maxS = 25) => {
    const t0 = rec.t;
    for (let n = Math.round(maxS / DT); n > 0 && items.tunnel.riding(who); n--) tick();
    return rec.t - t0;
  };
  return { feel, rec, items, list, std, prog, tick, fire, ride, peak: () => peak };
}

test('who: last and second last only, never the leader, whatever the gap', () => {
  const f = makeFeel();
  const offered = (n) => Array.from({ length: n }, (_, k) => k + 1).filter((p) => tunnelOffered(f, p, n));
  assert.deepEqual(offered(8), [7, 8]);
  assert.deepEqual(offered(4), [3, 4]);
  assert.deepEqual(offered(3), [2, 3]);
  assert.deepEqual(offered(2), [2], 'a field of 2 offers it only to last');
  assert.deepEqual(offered(1), []);
});

test('who: about one box in four for those places in every bucket, none for anyone else', () => {
  const f = makeFeel();
  for (const b of ['front', 'middle', 'back']) {
    const rand = makeRand(7);
    let hit = 0, n = 40000, other = 0;
    for (let k = 0; k < n; k++) {
      if (rollItem(f, b, rand, true) === 'tunnel') hit++;
      if (rollItem(f, b, rand, false) === 'tunnel') other++;
    }
    assert.ok(Math.abs(hit / n - 0.25) < 0.012, b + ' share ' + hit / n);
    assert.equal(other, 0);
  }
  assert.equal(bucketOf(f, 8, 10), 'front', 'a close last place is in the front bucket and still gets its odds');
});

test('a real box: only the last two of a field ever draw it', () => {
  const track = fakeTrack();
  const feel = makeFeel({ 'items.boxRespawnS': 0.001 }), rec = recorder();
  track.items = [{ kind: 'box', s: 500, x: 0 }];
  const items = createItems(track, feel, rec, { seed: 9 });
  const list = [500, 500.1, 500.2, 500.3, 500.4].map((s, i) => racer(track, feel, i, { s }, rec));
  const std = { order: [4, 3, 2, 1, 0], progressOf: (i) => list[i].st[S.s], placeOf: (i) => 5 - i, gapToLeader: () => 0 };
  const seen = new Set();
  for (let k = 0; k < 6000; k++) {
    // one kart free to take the box each tick, the others holding something
    for (const r of list) Object.assign(r.item, { id: r.i === k % 5 ? null : 'surge', rolling: false, heldT: 0 });
    items.update(DT, list, std, RACE);
    const r = list[k % 5];
    if (r.item.id === 'tunnel') seen.add(std.placeOf(r.i));
  }
  assert.deepEqual([...seen].sort(), [4, 5]);
});

test('the ride: at most 8 s on each course, small, large and over-a-lap gaps, no lead taken', () => {
  for (const id of COURSES) {
    const track = realTrack(id), L = track.lengthM, base = 200;
    for (const gapM of [30, 400, 1500, L + 300]) {
      const t = setup(track, [{ s: base, speed: 30 }, { s: base + gapM, speed: 30 }, { s: base + gapM + 80, speed: 30 }, { s: base + gapM + 160, speed: 30 }, { s: base + gapM + 240, speed: 30 }]);
      t.fire(0);
      assert.ok(t.items.tunnel.riding(0), id);
      const arrival = (() => { for (let n = 0; n < 900 && t.items.tunnel.riding(0); n++) { t.tick(); const g = t.prog[1] - t.prog[0]; if (g < 2.1) return t.rec.t; } return t.rec.t; })();
      assert.ok(arrival <= 7.65, id + ' gap ' + gapM + ' arrived at ' + arrival);
      const left = t.ride(0);
      assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'target', id);
      assert.equal(t.std.placeOf(0), 5, id + ' it never passes the target');
      assert.ok(left < 13, id + ' waited ' + left);
      if (gapM >= L) assert.ok(t.peak() > L / 8, id + ' a lap in 8 s needs ' + L / 8 + ', rode ' + t.peak());
      assert.equal(t.list[0].st[S.lap] | 0, gapM >= L ? 1 : t.list[0].st[S.lap] | 0, id + ' the lap line counted at speed');
    }
  }
});

test('8 s on solid road: a far ride that lands straight away ends inside tunnelMaxS', () => {
  const track = fakeTrack();
  for (const gapM of [30, 400, 3000, track.lengthM + 500]) {
    const t = setup(track, [{ s: 100, speed: 30 }, { s: 100 + gapM, speed: 30 }, { s: 100 + gapM + 90, speed: 30 }]);
    t.fire(0);
    const rode = t.ride(0);
    assert.ok(rode <= 8.05, 'gap ' + gapM + ' rode ' + rode);
    if (gapM === 30) assert.ok(rode < 4, 'a small gap arrives sooner: ' + rode);
  }
});

test('the landing: beside the target, one normal bump, no launch, both on the road', () => {
  const track = fakeTrack();
  for (const lateral of [0, 4, -4, 5.8]) {
    const t = setup(track, [{ s: 100, speed: 30 }, { s: 500, speed: 32, lateral }, { s: 700, speed: 32 }, { s: 800, speed: 32 }]);
    t.fire(0);
    t.ride(0);
    const me = t.list[0], tg = t.list[1];
    const v = Math.hypot(me.st[S.velX], me.st[S.velY], me.st[S.velZ]);
    assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'target');
    assert.ok(Math.abs(v - 32) < 3.5, 'speed matched: ' + v);
    assert.ok(Math.abs(me.st[S.lateral] - lateral) < 1.7, 'beside: ' + me.st[S.lateral] + ' by ' + lateral);
    assert.ok(Math.abs(me.st[S.lateral]) <= 7 - 0.5, 'inside the rails');
    me.drive = tg.drive = true;
    for (let k = 0; k < 20 && !t.rec.all('bump').length; k++) t.tick();
    const bumps = t.rec.all('bump');
    assert.ok(bumps.length >= 1, 'a visible contact for lateral ' + lateral);
    assert.ok(bumps[0].payload.dv < t.feel.get('contact.hardBumpDvMps'), 'a normal bump: ' + bumps[0].payload.dv);
    assert.equal(t.rec.all('hard-bump').length, 0);
    assert.equal(t.rec.all('tunnel-ram').length, 0, 'the target is never rammed');
    assert.equal(me.st[S.airborne], 0); assert.equal(tg.st[S.airborne], 0);
  }
});

test('karts on the way are rammed even at a many-metre tick, the target and top karts beside it are not', () => {
  const track = fakeTrack();
  const t = setup(track, [{ s: 100, speed: 30 }, { s: 12000, speed: 30 }, { s: 12200, speed: 30 }, { s: 12400, speed: 30 }, { s: 12600, speed: 30 }, { s: 5000 }, { s: 6000 }]);
  t.fire(0);
  t.ride(0);
  const rams = t.rec.all('tunnel-ram').map((e) => e.payload.victim).sort();
  assert.deepEqual(rams, [5, 6]);
  assert.ok(t.peak() > 1000, 'fast enough for many metres a tick: ' + t.peak());
});

test('unsafe road: the landing waits beside the target, then hands back on solid road', () => {
  const open = (seg) => (seg >= 60 && seg < 90 ? [] : ['rail']);   // 600..900 m has no rails
  const track = fakeTrack({ flags: open });
  const t = setup(track, [{ s: 100, speed: 30 }, { s: 640, speed: 30 }, { s: 900, speed: 30 }, { s: 1000, speed: 30 }], { 'items.tunnelMaxS': 1.2 });
  t.fire(0);
  const rode = t.ride(0);
  assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'target');
  assert.ok(t.list[0].st[S.s] > 900, 'past the open stretch: ' + t.list[0].st[S.s]);
  assert.ok(rode > 1.2, 'the wait runs past the limit: ' + rode);
  // an airborne target holds it too
  const u = setup(fakeTrack(), [{ s: 100, speed: 30 }, { s: 300, speed: 30 }, { s: 600, speed: 30 }]);
  u.fire(0);
  u.list[1].st[S.airborne] = 1; u.list[1].st[S.grounded] = 0;
  while (u.rec.t < 9) u.tick();
  assert.ok(u.items.tunnel.riding(0), 'still alongside at 9 s');
  u.list[1].st[S.airborne] = 0; u.list[1].st[S.grounded] = 1;
  assert.ok(u.ride(0) < 1.5, 'lands once the target is down');
  assert.equal(u.rec.all('tunnel-end').at(-1).payload.reason, 'target');
});

test('a target that changes mid-ride: the nearest top-4 kart ahead at the time', () => {
  const track = fakeTrack();
  const t = setup(track, [{ s: 100, speed: 30 }, { s: 2000, speed: 30 }, { s: 2100, speed: 30 }, { s: 2200, speed: 30 }, { s: 2300, speed: 30 }]);
  t.fire(0);
  assert.equal(t.items.tunnel.targetOf(t.list[0], t.list, t.std), t.list[1]);
  for (let k = 0; k < 60; k++) t.tick();
  // kart 2 drops behind kart 1: same places filled, the nearer one is now the aim
  t.prog[2] = t.prog[1] - 40;
  t.tick();
  assert.equal(t.items.tunnel.targetOf(t.list[0], t.list, t.std), t.list[2]);
  t.ride(0);
  assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'target');
  assert.equal(t.rec.all('tunnel-ram').filter((e) => e.payload.victim === 2).length, 0);
});

test('a target that finishes the race: the ride moves on, and with nobody left it ends on time', () => {
  const track = fakeTrack();
  const t = setup(track, [{ s: 100, speed: 30 }, { s: 500, speed: 30 }, { s: 800, speed: 30 }, { s: 1100, speed: 30 }]);
  t.fire(0);
  for (let k = 0; k < 30; k++) t.tick();
  t.list[1].finished = true; t.list[1].finishS = 1;
  t.tick();
  assert.equal(t.items.tunnel.targetOf(t.list[0], t.list, t.std), t.list[2]);
  t.list[2].finished = true; t.list[2].finishS = 2; t.list[3].finished = true; t.list[3].finishS = 3;
  t.tick();
  assert.equal(t.items.tunnel.targetOf(t.list[0], t.list, t.std), null);
  t.ride(0);
  assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'time');
  assert.ok(!t.list[0].finished);
});

test('the rider finishes first: the ride cancels with no guard', () => {
  const track = fakeTrack();
  const t = setup(track, [{ s: 100, speed: 30 }, { s: 900, speed: 30 }]);
  t.fire(0);
  t.tick();
  t.list[0].finished = true;
  t.tick();
  assert.equal(t.rec.all('tunnel-end').at(-1).payload.reason, 'cancel');
  assert.ok(!t.items.tunnel.riding(0));
});
