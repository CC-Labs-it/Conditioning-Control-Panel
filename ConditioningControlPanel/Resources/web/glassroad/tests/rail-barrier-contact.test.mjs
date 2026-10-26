import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { S } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { createContact } from '../sim/contact.js';
import { loadTrack } from '../track/load.js';
import { createFeelOverlay, identityMods } from '../sim/racers.js';
import { fakeTrack, makeFeel, place, recorder, acts, DT } from './sim-fake.mjs';

for (const height of [.5, 2.5, 8, 20]) for (const side of [-1, 1]) {
  test(`guard catches a swept airborne crossing at ${height}m on side ${side}`, () => {
    const track = fakeTrack(), feel = makeFeel(), rec = recorder();
    const st = place(track, { lateral: side * 6.4, height, airborne: true, headingDeg: side * 80, speed: 80 });
    step(st, acts({ throttle: 1 }), track, feel, DT, rec);
    const q = track.query(st[S.posX], st[S.posY], st[S.posZ]);
    assert.ok(q.edgeDist >= .499, 'the complete saucer stays inside after crossing the rail line in one tick');
    assert.ok(st[S.airborne], 'a rail bounce does not falsely land the kart');
    const hit = rec.first('wall').payload, b = track.frameAt(q.s).b;
    assert.equal(hit.side, side);
    assert.ok((st[S.velX] * b[0] + st[S.velY] * b[1] + st[S.velZ] * b[2]) * side < 0, 'outward speed rebounds inward');
    for (const key of ['p', 'normal', 'tangent', 'up']) assert.ok(hit[key].length === 3 && hit[key].every(Number.isFinite), key);
    assert.ok(hit.normal.reduce((n, v, i) => n + v * b[i] * side, 0) < -.99, 'impact normal faces the road');
  });
}

test('open sides, jump gaps and below-deck falls keep their physical openings', () => {
  for (const options of [{ flags: () => [] }, { flags: () => ['rail', 'jump'] }, {}]) {
    const track = fakeTrack(options), rec = recorder(), height = Object.keys(options).length ? 3 : -2;
    const st = place(track, { lateral: 6.4, height, airborne: true, headingDeg: 80, speed: 80 });
    step(st, acts({ throttle: 1 }), track, makeFeel(), DT, rec);
    assert.equal(rec.all('wall').length, 0);
    assert.ok(track.query(st[S.posX], st[S.posY], st[S.posZ]).edgeDist < 0, 'opening remains passable');
  }
});

for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
  test(`${id}: its real guarded road stops an airborne boost-speed edge crossing`, () => {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    let at = null;
    for (let s = 10; s < track.lengthM; s += 5) {
      const f = track.frameAt(s);
      if (!f.flags.includes('rail') || f.flags.includes('jump') || f.n[1] < .99) continue;
      const p = f.p.map((v, i) => v + f.b[i] * (f.w / 2 - .6));
      const q = track.query(...p, f.seg);
      if (q.onRoad && !q.hole && Math.abs(q.height) < .001) { at = { s, lateral: f.w / 2 - .6 }; break; }
    }
    assert.ok(at, 'course has an ordinary guarded approach');
    const rec = recorder(), st = place(track, { ...at, height: 3, airborne: true, headingDeg: 80, speed: 80 });
    step(st, acts({ throttle: 1 }), track, makeFeel(), DT, rec);
    assert.ok(rec.first('wall'), 'airborne impact reaches the guard');
    assert.ok(track.query(st[S.posX], st[S.posY], st[S.posZ], st[S.seg]).edgeDist >= .49);
  });
}

test('a shield shove at height rebounds off the same barrier and emits its contact basis', () => {
  const track = fakeTrack(), feel = makeFeel(), rec = recorder(), contact = createContact(track, feel, rec);
  const racers = [5, 6.6].map((lateral, i) => ({ i, st: place(track, { lateral, height: 3, airborne: true, speed: 20 }),
    feel: createFeelOverlay(feel), mods: identityMods(), fxv: { ghost: false, slip: 0 }, events: recorder() }));
  racers[0].fxv.shield = 'glass';
  contact.update(DT, racers, { phase: 'racing', timeS: 10 });
  const victim = racers[1], st = victim.st, q = track.query(st[S.posX], st[S.posY], st[S.posZ]);
  assert.ok(q.edgeDist >= .499);
  const b = track.frameAt(q.s).b;
  assert.ok(st[S.velX] * b[0] + st[S.velY] * b[1] + st[S.velZ] * b[2] < 0);
  assert.ok(victim.events.first('wall').payload.p.every(Number.isFinite));
});

test('a shove beside a stacked neighbouring deck cannot snap onto that deck\'s rail', () => {
  const track = fakeTrack(), baseQuery = track.query, feel = makeFeel(), rec = recorder();
  // Two concentric roads, 14.0m apart in plan and 2m apart vertically. The
  // airborne victim's short shove crosses their nearest-centreline boundary.
  track.query = (x, y, z, hint, out) => {
    const q = baseQuery(x, y, z, hint, out);
    if (Math.hypot(q.lateral - 14.0, y - 2) < Math.hypot(q.lateral, y)) {
      q.s += track.lengthM / 3; q.lateral -= 14.0; q.height = y - 2;
      q.edgeDist = 7 - Math.abs(q.lateral); q.onRoad = q.edgeDist >= 0;
      q.p[1] = 2;
    }
    return q;
  };
  const contact = createContact(track, feel, rec);
  const racers = [5, 6.6].map((lateral, i) => ({ i, st: place(track, { lateral, height: 3, airborne: true, speed: 20 }),
    feel: createFeelOverlay(feel), mods: identityMods(), fxv: { ghost: false, slip: 0 }, events: recorder() }));
  racers[0].fxv.shield = 'glass';
  contact.update(DT, racers, { phase: 'racing', timeS: 10 });
  const victim = racers[1], st = victim.st, q = track.query(st[S.posX], st[S.posY], st[S.posZ]);
  assert.ok(q.s > track.lengthM / 3, 'nearest query really switched to the adjacent deck');
  assert.ok(q.edgeDist < .5, 'no correction to the unrelated deck\'s rail');
  assert.equal(victim.events.all('wall').length, 0, 'no false guard hit from another deck');
  assert.equal(st[S.posY], 3, 'no landing or height change');
});
