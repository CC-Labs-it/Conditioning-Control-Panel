/* tests/sparkles-slip-hold.test.mjs - desk run 2026-10-30: the Sparkles slip after a solo race left after 6 s, before
 * the owner read it. Glassroad holds it while the results are up and at least 10 s from when it lands; a new run
 * still takes it away at once. The shared slip keeps 6 s for every other tree. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { slipHold, releaseSlip } from '../sparkles-slip.js';

test('slipHold: 6 s by default, a tree may hold longer or until it lets go', () => {
  assert.equal(slipHold(), 6000);
  assert.equal(slipHold({ calm: true }), 6000, 'other trees keep their 6 s');
  assert.equal(slipHold({ holdMs: 10000 }), 10000);
  assert.equal(slipHold({ holdMs: Infinity }), null, 'Infinity: no timer, the tree releases it');
  assert.equal(slipHold({ holdMs: 'x' }), 6000, 'a bad hold reads as the default');
  assert.equal(slipHold({ holdMs: -5 }), 0);
  assert.doesNotThrow(() => releaseSlip(10000), 'nothing on screen: a release is a no-op');
});

test('Glassroad holds the slip while the results are up, at least 10 s, and a new run takes it away', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(boot, /const SLIP_MIN_MS = 10000;/);
  assert.match(boot, /showSlip\(m, \{ calm: !!gate\?\.calm, holdMs: held \? Infinity : SLIP_MIN_MS \}\)/, 'held on the results, else 10 s');
  assert.match(boot, /if \(slipGate\(\)\?\.results\) return;\s+clearInterval\(slipWatch\); slipWatch = 0; releaseSlip\(SLIP_MIN_MS\);/,
    'the results card leaving lets it go once 10 s have passed');
  assert.match(boot, /results: state === 'results' \|\| \(state === 'race' && !!race && race\.phase === 'finished'\)/,
    'results = the finished race and its card, solo or online');
  assert.equal((boot.match(/hideSlip\(\);/g) || []).length, 2, 'both run starts (solo, online) still take it away');
  assert.match(boot, /shared\.bridge\.on\('sparkles-receipt', receiptSlip\);/);
});
