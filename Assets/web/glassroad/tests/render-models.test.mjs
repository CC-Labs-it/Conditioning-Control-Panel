// Polish pass 1, lane MODELS: the pure helpers behind the wave 2 models (rival tints and accessories,
// the item card's edge glow, the effect-bubble fit, the Gentle Flash scale).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { LOOK } from '../render/look.js';
import { rivalLook, accessoryFor, pushColour, hexRgb, srgbToLinear, RIVAL_DEFAULTS, rivalNumbers } from '../render/kartPose.js';
import { cardEdgeGlow, shellFit, screenAmounts, flashRect } from '../render/items/fxmath.js';
import { ITEM_LOOK, SCREEN_LOOK } from '../render/items/look.js';

const read = (p) => JSON.parse(fs.readFileSync(new URL(p, import.meta.url), 'utf8'));
const KARTS = read('../art/palettes.json').karts;
const MANIFEST = read('../art/manifest.json');
const row = (id) => MANIFEST.find((r) => r.id === id) || null;
const hsl = ([r, g, b]) => {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2, d = mx - mn;
  return { s: d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1)), l };
};

test('models: the player keeps EMI colours and no accessory; rival i wears colourway i - 1', () => {
  assert.equal(rivalLook(0, KARTS, LOOK), null);
  assert.equal(accessoryFor(0, KARTS, LOOK), null);
  const names = Object.keys(KARTS);
  for (let i = 1; i <= 7; i++) {
    const r = rivalLook(i, KARTS, LOOK);
    assert.equal(r.name, names[i - 1]);
    assert.equal(r.accessory, KARTS[names[i - 1]].accessory);
    for (const c of [...r.cup, ...r.saucer, ...r.rim]) assert.ok(c >= 0 && c <= 1);
    assert.match(r.hex, /^#[0-9a-f]{6}$/);
  }
});

test('models: the seven rivals read apart (cup plus rim), and the push adds colour', () => {
  const all = [1, 2, 3, 4, 5, 6, 7].map((i) => rivalLook(i, KARTS, LOOK));
  const vec = (r) => [...r.cup, ...r.rim];
  for (let a = 0; a < 7; a++) {
    for (let b = a + 1; b < 7; b++) {
      const d = Math.hypot(...vec(all[a]).map((x, k) => x - vec(all[b])[k]));
      assert.ok(d > 0.3, all[a].name + ' vs ' + all[b].name + ' only ' + d.toFixed(3));
    }
  }
  // every non-pale cup ends more saturated than its palette porcelain
  for (const r of all) {
    const src = hsl(hexRgb(KARTS[r.name].cup));
    if (src.s >= RIVAL_DEFAULTS.paleBelow) assert.ok(hsl(r.cup).s > src.s, r.name);
  }
});

test('models: a near-grey colourway stays pale; multipliers follow ART.md (tint / porcelain reference)', () => {
  const pale = pushColour(hexRgb('#eadbe2'), 1.8, 0.5, [0.5, 0.72]);
  assert.ok(hsl(pale).l >= RIVAL_DEFAULTS.paleLight - 1e-6);
  assert.ok(hsl(pale).s < hsl(hexRgb('#eadbe2')).s);
  const r = rivalLook(1, KARTS, LOOK);
  r.cup.forEach((c, k) => assert.ok(Math.abs(r.cupMul[k] * RIVAL_DEFAULTS.porcelainRef[k] - srgbToLinear(c)) < 1e-9));
  r.saucer.forEach((c, k) => assert.ok(Math.abs(r.saucerMul[k] * RIVAL_DEFAULTS.porcelainRef[k] - srgbToLinear(c)) < 1e-9));
  // the standard sRGB curve (ART.md: decode, then tint in linear)
  assert.ok(Math.abs(srgbToLinear(0.5) - 0.214) < 1e-3);
  assert.equal(srgbToLinear(0), 0);
});

test('models: accessories are spread over the seven and exist in the manifest at head_attach', () => {
  const ids = [1, 2, 3, 4, 5, 6, 7].map((i) => accessoryFor(i, KARTS, LOOK));
  assert.deepEqual([...new Set(ids)].sort(), ['head-bow', 'head-cat-ears', 'head-halo', 'head-spiral-crown']);
  for (const id of ids) {
    const r = row(id);
    assert.ok(r, id + ' in the manifest');
    assert.equal(r.attachPoint, 'head_attach');
  }
  assert.equal(row('emi-teacup-kart').attachPoint, 'head_attach');
  // no table: the four still spread, in turn
  const bare = [1, 2, 3, 4, 5].map((i) => accessoryFor(i, null, LOOK));
  assert.deepEqual(bare, ['head-bow', 'head-cat-ears', 'head-halo', 'head-spiral-crown', 'head-bow']);
});

test('models: without palettes.json rivals fall back to LOOK.kartTints; LOOK.rival overrides numbers', () => {
  const r = rivalLook(2, null, LOOK);
  assert.ok(r && r.cup.length === 3);
  const want = pushColour(hexRgb(LOOK.kartTints[2]), RIVAL_DEFAULTS.cupSatMul, RIVAL_DEFAULTS.cupSatMin, RIVAL_DEFAULTS.cupLight);
  r.cup.forEach((c, k) => assert.ok(Math.abs(c - want[k]) < 1e-9));
  const L2 = { ...LOOK, rival: { rimHex: { petal: '#000000' } } };
  assert.deepEqual(rivalLook(1, KARTS, L2).rim, [0, 0, 0]);
  assert.equal(rivalNumbers(L2).rimGain, RIVAL_DEFAULTS.rimGain);
});

test('models: the item card edge breathes slowly round its glow; Gentle holds it steady and lower', () => {
  const g = ITEM_LOOK.cardEdgeGlow, b = ITEM_LOOK.cardEdgeBreath;
  assert.ok(ITEM_LOOK.cardEdgeHz <= 1, 'a breath, never a flicker');
  let lo = Infinity, hi = -Infinity;
  for (let t = 0; t < 10; t += 0.01) { const v = cardEdgeGlow(t, false); lo = Math.min(lo, v); hi = Math.max(hi, v); }
  assert.ok(lo >= g * (1 - b) - 1e-9 && hi <= g * (1 + b) + 1e-9 && hi > lo);
  assert.equal(cardEdgeGlow(0.3, true), cardEdgeGlow(7.1, true));
  assert.ok(cardEdgeGlow(0, true) < g);
});

test('models: the effect-bubble fit follows the manifest row (shell centre, radius, icon carrier)', () => {
  const r = row('effect-bubble');
  assert.ok(r, 'effect-bubble in the manifest');
  const f = shellFit(r);
  assert.ok(Math.abs(f.radius - 0.55) < 1e-3 && Math.abs(f.centreY - 0.55) < 1e-3);
  assert.ok(Math.abs(f.carrier - 0.7 / 1.1) < 1e-3);
  assert.deepEqual(shellFit(null), { radius: 0.55, centreY: 0.55, carrier: 0.7 / 1.1 });
  for (const id of [ITEM_LOOK.boxCard, ITEM_LOOK.shellModel, ITEM_LOOK.meltMask, 'finish-portal-lod0', 'finish-portal-lod1']) assert.ok(row(id), id);
  assert.ok(row('finish-portal-lod0').materialSlots.includes('finish_membrane'));
});

test('models: the Gentle Flash card uses params.scale when fx sends it, else flashCornerScale', () => {
  const scr = (params) => ({ kind: 'flash', t: 0.5, durS: 2, k: 1, strength: 1, prevKind: null, prevK: 0, params: { corner: true, gentle: true, cover: 0.4, ...params } });
  assert.equal(screenAmounts(scr({ scale: 0.5 })).cornerScale, 0.5);
  assert.equal(screenAmounts(scr({})).cornerScale, SCREEN_LOOK.flashCornerScale);
  const a = flashRect(0.4, 16 / 9, { corner: true }), b = flashRect(0.4, 16 / 9, { corner: true, scale: SCREEN_LOOK.flashCornerScale });
  assert.deepEqual(a, b);
  const big = flashRect(0.4, 16 / 9, { corner: true, scale: 0.7 });
  assert.ok(Math.abs(big.hw / a.hw - 0.7 / SCREEN_LOOK.flashCornerScale) < 1e-9, 'the scale is linear');
});
