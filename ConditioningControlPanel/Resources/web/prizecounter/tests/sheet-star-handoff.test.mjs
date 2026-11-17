// The last leg (CONTRACT.md "Sheet additions", wallet-stars): with a host wallet the page hands the host every star's
// whole path once, and sends no wallet-land for that flight; without the host's yes it behaves as before.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createFx } from '../sheet/fx.js';
import { asHost } from '../host.js';

function fakeDoc() {
  const anims = [];
  const node = (rect) => ({
    hidden: false, children: [], style: { setProperty() {} }, className: '', isConnected: true,
    append(n) { this.children.push(n); }, remove() {}, querySelector() { return null; },
    getBoundingClientRect: () => rect,
    animate(k, o) { const a = { k, o, onfinish: null }; anims.push(a); return a; },
  });
  const doc = { createElement: () => node({ left: 0, top: 0, width: 26, height: 26 }) };
  return { doc, node, anims };
}

const sound = { air() {}, tick() {}, chord() {}, bell() {}, NOTES: [1, 2, 3, 4] };

function setup(onStars) {
  const { doc, node, anims } = fakeDoc();
  const root = node({ left: 10, top: 20, width: 800, height: 600 });
  const item = node({ left: 300, top: 300, width: 100, height: 100 });
  const landed = [];
  const fx = createFx({ root, doc, sound, wallet: () => null, walletAt: () => ({ x: 700, y: -30 }), onWallet: (k) => landed.push(k), onStars });
  return { fx, item, anims, landed };
}

test('a spend hands the host every path in page pixels (star centres), and the chip is not told at once', () => {
  const plans = [];
  const { fx, item, anims, landed } = setup((p) => { plans.push(p); return true; });
  assert.ok(fx.flyOut(item, 400) > 0);
  assert.equal(plans.length, 1);
  const p = plans[0];
  assert.equal(p.dir, 'out');
  assert.equal(p.n, 400);
  assert.deepEqual(p.e, [0.45, 0, 0.3, 1]);
  assert.equal(p.stars.length, anims.length);
  // The first key is the host's wallet star itself: walletAt, in the page's own pixels.
  for (const s of p.stars) { assert.equal(s.k[0][1], 700); assert.equal(s.k[0][2], -30); assert.equal(s.k[0][0], 0); assert.equal(s.k.at(-1)[0], 1); }
  assert.deepEqual(landed, []);
  // The page's own flight is the same path.
  assert.equal(anims[0].k.length, p.stars[0].k.length);
});

test('a gain hands off once and sends no wallet-land as its stars land', () => {
  const plans = [];
  const { fx, item, anims, landed } = setup((p) => { plans.push(p); return true; });
  fx.fly(item, 20);
  assert.equal(plans.length, 1);
  assert.equal(plans[0].dir, 'in');
  const last = plans[0].stars[0].k.at(-1);
  assert.equal(last[1], 700); assert.equal(last[2], -30);
  anims.forEach((a) => a.onfinish && a.onfinish());
  assert.deepEqual(landed, []);
});

test('without the host taking it: spends tell the chip at once and gains on landings, as before', () => {
  const a = setup(null);
  a.fx.flyOut(a.item, 400);
  assert.deepEqual(a.landed, [1]);
  const b = setup(() => false);
  b.fx.fly(b.item, 20);
  b.anims.forEach((x) => x.onfinish && x.onfinish());
  assert.ok(b.landed.length > 0);
  assert.equal(b.landed.at(-1), 1);
});

test('motion off: no plan, the number just changes', () => {
  const { doc, node } = fakeDoc();
  const root = node({ left: 0, top: 0, width: 800, height: 600 });
  const plans = [], landed = [];
  const fx = createFx({ root, doc, sound, motion: 'off', wallet: () => null, walletAt: () => ({ x: 1, y: -1 }), onWallet: (k) => landed.push(k), onStars: (p) => { plans.push(p); return true; } });
  assert.equal(fx.flyOut(node({ left: 0, top: 0, width: 10, height: 10 }), 100), 0);
  assert.deepEqual(plans, []);
  assert.deepEqual(landed, [1]);
});

test('a mounted host passes wallet-stars through', () => {
  const heard = [];
  asHost({ walletStars: (p) => heard.push(p) }).walletStars({ dir: 'in', stars: [] });
  assert.equal(heard.length, 1);
  assert.doesNotThrow(() => asHost({}).walletStars({ stars: [] }));
});
