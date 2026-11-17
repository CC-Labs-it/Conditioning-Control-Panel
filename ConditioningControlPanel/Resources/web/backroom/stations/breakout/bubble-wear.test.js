import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { resolveSkin, SKIN_IDS, interiorTime, STILL_T, popParticles, bubbleRecord, createSkinWear, WEAR_ALPHA, PART_CAP } from './bubble-wear.js';

/* A canvas 2D stand-in that records every call; gradients answer addColorStop. */
function fakeCtx(log = []) {
  const grad = { addColorStop() {} };
  return new Proxy({ globalAlpha: 1 }, {
    get(t, k) {
      if (k in t) return t[k];
      if (k === 'createLinearGradient' || k === 'createRadialGradient') return () => grad;
      return (...a) => { log.push(k); };
    },
    set(t, k, v) { t[k] = v; return true; },
  });
}

test('unknown, absent and odd ids are soap; known ids pass in any case', () => {
  for (const v of [undefined, null, '', 'latex', 42, {}, 'soap']) assert.equal(resolveSkin(v), 'soap');
  assert.equal(resolveSkin(' Heart '), 'heart');
  assert.equal(resolveSkin('DISCO'), 'disco');
  assert.deepEqual(SKIN_IDS, ['soap', 'champagne', 'heart', 'spiral', 'marble', 'disco', 'frost']);
  for (const id of SKIN_IDS.slice(1)) assert.ok(WEAR_ALPHA[id] > 0 && WEAR_ALPHA[id] <= 1, id);
});

test('soap draws nothing and pops nothing, so the bubble is exactly today\'s', () => {
  const wear = createSkinWear({ doc: null });
  const log = [], g = fakeCtx(log);
  assert.equal(wear.draw(g, { x: 10, y: 10, age: 1 }, 40, 1, { skin: 'soap' }), false);
  assert.equal(wear.draw(g, { x: 10, y: 10, age: 1 }, 40, 1, { skin: 'nope' }), false);
  assert.equal(wear.draw(g, { x: 10, y: 10, age: 1 }, 40, 1, null), false);
  assert.equal(wear.pop({ x: 1, y: 1, r: 40 }, { skin: 'soap' }), 0);
  assert.equal(log.length, 0);
  assert.equal(bubbleRecord('soap', 1, 40), null);
});

test('a pop is deterministic for a seed and every skin has its own particles', () => {
  const kinds = new Map();
  for (const id of SKIN_IDS.slice(1)) {
    const a = { p: [], cap: PART_CAP }, b = { p: [], cap: PART_CAP };
    const n = popParticles(a, id, 100, 200, 61, 1234);
    popParticles(b, id, 100, 200, 61, 1234);
    assert.ok(n > 10 && n <= PART_CAP, id + ' made ' + n);
    assert.deepEqual(JSON.parse(JSON.stringify(a.p)), JSON.parse(JSON.stringify(b.p)), id + ' is not deterministic');
    const c = { p: [], cap: PART_CAP }; popParticles(c, id, 100, 200, 61, 99);
    assert.notDeepEqual(JSON.parse(JSON.stringify(a.p)), JSON.parse(JSON.stringify(c.p)), id + ' ignores its seed');
    kinds.set(id, [...new Set(a.p.map((p) => p.kind))].sort().join(','));
  }
  assert.ok(kinds.get('heart').includes('heart'));
  assert.ok(kinds.get('champagne').includes('ring'));
  assert.ok(kinds.get('marble').includes('shard'));
  assert.ok(kinds.get('disco').includes('square'));
  assert.ok(kinds.get('frost').includes('flake'));
  assert.ok(kinds.get('spiral').includes('orbit'));
});

test('the pop world stays under its cap, Calm keeps Disco free of beams, reduced motion pops no skin particles', () => {
  const wear = createSkinWear({ doc: null });
  for (let i = 0; i < 8; i++) wear.pop({ x: 50, y: 50, r: 61 }, { skin: 'champagne' });
  assert.ok(wear.particles() <= PART_CAP);
  wear.clear(); assert.equal(wear.particles(), 0);
  const w = { p: [], cap: PART_CAP };
  popParticles(w, 'disco', 0, 0, 61, 7, { gentle: true });
  assert.equal(w.p.filter((p) => p.kind === 'beam').length, 0);
  assert.equal(wear.pop({ x: 50, y: 50, r: 61 }, { skin: 'heart', reduced: true }), 0);
});

test('reduced motion holds one still frame; Calm runs at half speed', () => {
  assert.equal(interiorTime(9, { reduced: true }), STILL_T);
  assert.equal(interiorTime(3, { reduced: true, calm: true }), STILL_T);
  assert.equal(interiorTime(8, { calm: true }), 4);
  assert.equal(interiorTime(8, {}), 8);
});

test('per-bubble skins draw inside the field transform and the steps are cheap', () => {
  const wear = createSkinWear({ doc: null });
  const log = [], g = fakeCtx(log), col = { x: 300, y: 200, age: 2 };
  wear.tick(0.5);
  assert.equal(wear.draw(g, col, 61, 1, { skin: 'frost' }), true);
  assert.ok(log.includes('translate') && log.length < 400, 'frost made ' + log.length + ' calls');
  // Shared skins need a canvas: without a document the bubble falls back to soap.
  assert.equal(wear.draw(g, col, 61, 1, { skin: 'champagne' }), false);
  wear.pop({ x: 300, y: 200, r: 61 }, { skin: 'frost' }, [col]);
  const plog = [], pg = fakeCtx(plog);
  wear.frame(pg, 1 / 30);
  assert.ok(plog.length > 0);
});

test('the wear module imports only its own tree', () => {
  const src = readFileSync(fileURLToPath(new URL('./bubble-wear.js', import.meta.url)), 'utf8');
  for (const line of src.split('\n').filter((l) => /^\s*import /.test(l))) assert.match(line, /from '\.\/[a-z-]+\.js'/);
});
