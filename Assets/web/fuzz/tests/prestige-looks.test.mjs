import { test } from 'node:test';
import assert from 'node:assert/strict';
import { LOOKS, LOOK_BY_ID, FAMILY_STAGES, parseLooks, canBuyLook, buyLook, applyLooks, ownsLook } from '../sim/prestige-looks.js';
import { createPrestige, parsePrestige } from '../sim/prestige.js';
import { SHOP_WAVES } from '../ui/wave-ribbon.js';
import { trailCollection, stagePrices } from '../data/trail-collection.js';

test('looks match the run shops (ids, names, prices)', () => {
  for (const w of SHOP_WAVES.filter((s) => s.price > 0)) {
    const l = LOOK_BY_ID[`wave:${w.id}`];
    assert.ok(l, `wave ${w.id} on the shelf`);
    assert.equal(l.cost, w.price);
  }
  for (const f of trailCollection) {
    const l = LOOK_BY_ID[`family:${f.id}`];
    assert.ok(l, `family ${f.id} on the shelf`);
    assert.equal(l.cost, stagePrices.reduce((a, b) => a + b, 0));
    assert.equal(f.levels.length, FAMILY_STAGES);
  }
  assert.equal(new Set(LOOKS.map((l) => l.id)).size, LOOKS.length);
});

test('locked before the core, then bought once with Stardust', () => {
  const p = createPrestige();
  p.dust = 1e6;
  assert.deepEqual(canBuyLook(p, 'wave:harp'), { ok: false, reason: 'core' });
  p.core = true;
  assert.equal(canBuyLook(p, 'nope').reason, 'unknown');
  const r = buyLook(p, 'wave:harp');
  assert.equal(r.ok, true);
  assert.equal(p.dust, 1e6 - 9000);
  assert.equal(ownsLook(p, 'wave:harp'), true);
  assert.equal(buyLook(p, 'wave:harp').reason, 'owned');
  p.dust = 10;
  assert.equal(canBuyLook(p, 'face:emi').reason, 'dust');
});

test('the bag survives a save round trip and drops unknown ids', () => {
  const p = createPrestige();
  p.core = true; p.dust = 1e6;
  buyLook(p, 'family:ink'); buyLook(p, 'cursor:cursor_nova');
  p.store.looks.own.bogus = 1;
  const back = parsePrestige(JSON.parse(JSON.stringify(p)));
  assert.deepEqual(parseLooks(back.store.looks).own, { 'family:ink': 1, 'cursor:cursor_nova': 1 });
  assert.deepEqual(parseLooks(null), { own: {} });
  assert.deepEqual(parseLooks({ own: 'x' }), { own: {} });
});

test('applyLooks owns, never equips, never lowers, and is idempotent', () => {
  const p = createPrestige();
  p.core = true; p.dust = 1e6;
  for (const id of ['face:emi', 'wave:pyramid', 'cursor:cursor_prism', 'family:atlas', 'family:ink']) buyLook(p, id);
  const run = { store: { bubble: { choice: 'round', waveStyles: { aurora: true } }, cursorKit: { stages: { ink: 3, circuit: 1 } } } };
  assert.equal(applyLooks(run, p), 4);
  assert.equal(run.store.bubble.choice, 'round');
  assert.deepEqual(run.store.bubble.faces, { emi: true });
  assert.deepEqual(run.store.bubble.waveStyles, { aurora: true, pyramid: true });
  assert.deepEqual(run.store.cursorKit.skins, { cursor_prism: true });
  assert.deepEqual(run.store.cursorKit.stages, { ink: 3, circuit: 1, atlas: 3 });
  assert.equal(run.store.cursorKit.trail, undefined);
  assert.equal(applyLooks(run, p), 0);
  assert.equal(applyLooks({ store: {} }, createPrestige()), 0);
  assert.equal(applyLooks(null, p), 0);
});
