import { test } from 'node:test';
import assert from 'node:assert/strict';
import { keepChainPhase, createKeepChains, KEEP_CHAIN } from '../sim/keepChains.js';
import { S, RESPAWN } from '../sim/state.js';
import { fakeTrack, makeFeel, place, recorder } from './sim-fake.mjs';
const track = fakeTrack({ w: 34 }), row = { id: 'swing', s: 110, offsetS: 0 };
const racer = (s, x = 0, height = 0) => ({ i: 0, st: place(track, { s, lateral: x, height, speed: 40 }), events: recorder(), feel: makeFeel() });
test('chain position repeats on one race clock with a real low sweep and high escape', () => {
  const low = keepChainPhase(row, 0), high = keepChainPhase(row, KEEP_CHAIN.cycleS / 4);
  assert.equal(low.x, 0); assert.equal(low.y, 3);
  assert.ok(high.x > 7 && high.y > 5);
  for (const t of [0, 1, 3, 4.8]) {
    const a = keepChainPhase(row, t), b = keepChainPhase(row, t + KEEP_CHAIN.cycleS);
    assert.ok(Math.abs(a.x - b.x) < 1e-12); assert.ok(Math.abs(a.y - b.y) < 1e-12);
    assert.deepEqual(keepChainPhase(row, t), a);
  }
});
test('a fast crossing hits the actual hanging weight and keeps bounded cooldown', () => {
  const chains = createKeepChains(track, { chains: [row] }), r = racer(115);
  assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .01), 'swing');
  assert.ok(r.st[S.stalled] <= .451); assert.equal(chains.inspect().hits, 1);
  r.st.set(place(track, { s: 115, speed: 40 }));
  assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .1), null);
  chains.reset(); assert.equal(chains.inspect().hits, 0);
});
test('both bypasses, raised weights and remote/rescued karts stay free', () => {
  for (const [x, time, height] of [[-13, .01, 0], [13, .01, 0], [0, 1.6, 0], [0, .01, 12]]) {
    const chains = createKeepChains(track, { chains: [row] }), r = racer(115, x, height);
    assert.equal(chains.apply(r, place(track, { s: 105, lateral: x, height, speed: 40 }), time), null);
  }
  for (const mutate of [r => r.kind = 'remote', r => r.finished = true, r => r.st[S.respawnPhase] = RESPAWN.PULL]) {
    const chains = createKeepChains(track, { chains: [row] }), r = racer(115); mutate(r);
    assert.equal(chains.apply(r, place(track, { s: 105, speed: 40 }), .01), null);
  }
  assert.throws(() => createKeepChains(fakeTrack({ w: 24 }), { chains: [row] }), /30 m/);
});
