// The wallet chip counts down to the receipt's sp; the week tag reads the ISO week.
import test from 'node:test';
import assert from 'node:assert/strict';
import { countDown, weekNo } from '../hud/wallet.js';
import { createHud } from '../hud/index.js';
import { makeT } from '../state/strings.js';
import { fakeDocument } from './fake-dom.mjs';

const clock = (stepMs) => { let now = 0; const q = []; return { now: () => now, frame: (f) => q.push(f), run() { while (q.length) { now += stepMs; q.shift()(); } } }; };

test('count down steps down and ends exactly on the receipt sp', async () => {
  const c = clock(50), seen = [];
  const run = countDown(1850, 1120, { ms: 700, now: c.now, frame: c.frame, onStep: (n) => seen.push(n) });
  c.run();
  assert.equal(await run.done, 1120);
  assert.equal(seen.at(-1), 1120);
  assert.ok(seen.length > 5);
  for (let i = 1; i < seen.length; i++) assert.ok(seen[i] <= seen[i - 1]);
});

test('motion off is a cut; stop() lands on the target', async () => {
  const seen = [];
  await countDown(400, 40, { ms: 0, now: () => 0, frame: () => assert.fail('no frames'), onStep: (n) => seen.push(n) }).done;
  assert.deepEqual(seen, [40]);
  const c = clock(10), s2 = [];
  const run = countDown(400, 40, { ms: 700, now: c.now, frame: c.frame, onStep: (n) => s2.push(n) });
  run.stop(); c.run();
  assert.equal(s2.at(-1), 40);
  assert.ok(s2.length <= 2);
});

test('the HUD chip shows the receipt sp once the count ends, and repaints do not jump it mid-count', async () => {
  const doc = fakeDocument(), root = doc.createElement('main');
  const hud = createHud(root, { t: makeT({}), send: () => {}, goTab: () => {}, motion: 'off' });
  const v = { status: 'ready', sp: 1850, week: '2026-W44', tier: 1, tabs: [], tabId: null, shelves: [], focus: null, card: null };
  hud.paint(v);
  const n = root.querySelectorAll('.pc-wallet-n')[0];
  assert.equal(n.textContent, '1850');
  await hud.receipt(1120, null);
  hud.paint({ ...v, sp: 1120 });
  assert.equal(n.textContent, '1120');
  assert.equal(root.querySelectorAll('.pc-wallet')[0].getAttribute('aria-label'), '1120 Sparkles');
});

test('week tag: ISO week number only', () => {
  assert.equal(weekNo('2026-W44'), '44');
  assert.equal(weekNo('2027-W03'), '3');
  assert.equal(weekNo(''), null);
  assert.equal(weekNo('soon'), null);
});
