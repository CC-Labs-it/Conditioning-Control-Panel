import { test } from 'node:test';
import assert from 'node:assert/strict';
import { S, BOOST } from '../sim/state.js';
import { createContact } from '../sim/contact.js';
import { createFeelOverlay, identityMods } from '../sim/racers.js';
import { makeFeel, fakeTrack, place, recorder, DT } from './sim-fake.mjs';

// interaction wave lane A: hard-bump { winner, loser, dv, p } and the winner's bump boost
function racer(i, track, feel, opts) {
  return { i, st: place(track, opts), feel: createFeelOverlay(feel), mods: identityMods(), fxv: { ghost: false, slip: 0 }, events: recorder() };
}
const RACING = { phase: 'racing', timeS: 10 };
const railed = () => fakeTrack({ flags: () => ['rail'] });

/** a slides sideways into b at closing m/s; a is faster (heavier) when aSpeed > bSpeed. */
function pair(tr, feel, closing, aSpeed = 24, bSpeed = 18) {
  const a = racer(0, tr, feel, { s: 100, lateral: -0.5, speed: aSpeed }), b = racer(1, tr, feel, { s: 100, lateral: 0.5, speed: bSpeed });
  const f = tr.frameAt(100, {});
  a.st[S.velX] += f.b[0] * closing; a.st[S.velZ] += f.b[2] * closing;
  return { a, b };
}

test('hard-bump: fires at contact.hardBumpDvMps, winner = the heavier kart, with dv and the contact point', () => {
  const feel = makeFeel(), tr = railed(), rec = recorder(), c = createContact(tr, feel, rec);
  const { a, b } = pair(tr, feel, feel.get('contact.hardBumpDvMps') + 1);
  c.update(DT, [a, b], RACING);
  const e = rec.all('hard-bump');
  assert.equal(e.length, 1);
  const p = e[0].payload;
  assert.equal(p.winner, 0); assert.equal(p.loser, 1);
  assert.ok(p.dv >= feel.get('contact.hardBumpDvMps'));
  assert.equal(p.p.length, 3);
  assert.equal(rec.all('bump').length, 1, 'the plain bump still fires');
});

test('hard-bump: a soft bump stays a plain bump, and the pair cooldown holds the next one', () => {
  const feel = makeFeel(), tr = railed(), rec = recorder(), c = createContact(tr, feel, rec);
  const soft = pair(tr, feel, 3);
  c.update(DT, [soft.a, soft.b], RACING);
  assert.equal(rec.all('hard-bump').length, 0);
  const hard = pair(tr, feel, 12);
  c.update(DT, [hard.a, hard.b], RACING);
  assert.equal(rec.all('hard-bump').length, 0, 'same pair inside contact.cooldownS');
});

test('bump boost: the winner gets a small grant, once per bumpBoostCooldownS, the loser none', () => {
  const feel = makeFeel(), tr = railed(), rec = recorder(), c = createContact(tr, feel, rec);
  let { a, b } = pair(tr, feel, 10);
  c.update(DT, [a, b], RACING);
  assert.equal(a.st[S.boostKind], BOOST.PAD);
  assert.ok(Math.abs(a.st[S.boostAmount] - feel.get('contact.bumpBoostAmount')) < 1e-6);
  assert.equal(a.events.first('boost-start').payload.kind, 'bump');
  assert.equal(b.st[S.boostKind], BOOST.NONE, 'the loser gets no boost');
  // past the pair cooldown but inside the racer's bump boost cooldown: an event, no second grant
  for (let t = 0; t < feel.get('contact.cooldownS') + 0.1; t += DT) c.update(DT, [], RACING);
  ({ a, b } = pair(tr, feel, 10));
  c.update(DT, [a, b], RACING);
  assert.equal(rec.all('hard-bump').length, 2);
  assert.equal(a.st[S.boostKind], BOOST.NONE, 'no second bump boost inside contact.bumpBoostCooldownS');
});

test('bump boost: never on an open stretch, never cuts a bigger boost', () => {
  const feel = makeFeel(), open = fakeTrack({ flags: () => [] }), rec = recorder();
  let { a, b } = pair(open, feel, 10);
  createContact(open, feel, rec).update(DT, [a, b], RACING);
  assert.equal(rec.all('hard-bump').length, 1);
  assert.equal(a.st[S.boostKind], BOOST.NONE, 'open side: no kick');
  const tr = railed(), rec2 = recorder();
  ({ a, b } = pair(tr, feel, 10));
  a.st[S.boostKind] = BOOST.PAD; a.st[S.boostAmount] = 0.3; a.st[S.boostT] = 0; a.st[S.boostHoldS] = 1;
  createContact(tr, feel, rec2).update(DT, [a, b], RACING);
  assert.ok(Math.abs(a.st[S.boostAmount] - 0.3) < 1e-6, 'the bigger boost runs on');
});

test('slipstream: fxv.slipFrom names the kart ahead while drafting, -1 otherwise', () => {
  const feel = makeFeel(), tr = railed(), rec = recorder(), c = createContact(tr, feel, rec);
  const lead = racer(0, tr, feel, { s: 108, lateral: 0, speed: 30 }), back = racer(1, tr, feel, { s: 100, lateral: 0, speed: 30 });
  lead.st[S.speedPct] = back.st[S.speedPct] = 0.9;
  c.update(DT, [lead, back], RACING);
  assert.equal(back.fxv.slipFrom, 0);
  assert.equal(lead.fxv.slipFrom, -1);
  assert.ok(back.fxv.slip > 0);
});
