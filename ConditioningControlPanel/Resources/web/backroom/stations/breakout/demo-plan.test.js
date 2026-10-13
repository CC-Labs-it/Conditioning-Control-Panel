// THE DEMO CARD's plan row (conversion lane G1, brief C10). station.js needs a DOM, so this reads its source the
// way host-escape.test.js does, and proves the row against the shared module it imports.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { planOf, PLAN_FALLBACK } from '../../shared/win/end-card.js';

const here = path.dirname(fileURLToPath(import.meta.url));
const src = fs.readFileSync(path.join(here, 'station.js'), 'utf8');

test('the demo card names the tier as Prime, never Tier 2', () => {
  assert.match(src, /Included with Prime\./);
  assert.doesNotMatch(src, /Tier 2/);
});

test('the plan row hangs under "Included with Prime" and offers Prime', () => {
  assert.match(src, /from '\.\.\/\.\.\/shared\/win\/end-card\.js'/, 'the station reads only its own tree');
  assert.match(src, /buildEndCard\(\{ plan: planOf\(ctx\.plans \?\? globalThis\.__backroom\?\.state\?\.plans, 'prime'\), feature: 'breakout_demo'/);
  assert.match(src, /postFunnel\('gate_shown', 'breakout_demo', 'prime'\)/);
  // The row is a link, so `ui.demoCard.querySelector('button').focus()` still lands on Play again.
  assert.match(src, /ui\.demoCard\.querySelector\('button'\)\.focus\(\)/);
});

test('the desktop Breakout window hands the host plans to the station', () => {
  const play = fs.readFileSync(path.join(here, 'play.html'), 'utf8');
  assert.match(play, /if \(m\.plans && typeof m\.plans === 'object'\) ctx\.plans = m\.plans;/);
});

test('with no host plans the row still opens Prime checkout, with no price', () => {
  const p = planOf(undefined, 'prime');
  assert.equal(p.url, PLAN_FALLBACK.prime.url);
  assert.equal(p.price, '');
});
