import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  CELLS, GRID, MAX_LOD, FXART_LOOK, FAMILIES, UPRIGHT, ALIGNED, cellIndex, cellInset, cellUV, spriteToAtlas, atlasLod, legacyShape,
  sparkFamily, pickCell, familyOf, hitFamily, paletteWeights, moteMix, maskLook, ditherEdge,
} from '../render/atlas.js';

const manifest = JSON.parse(readFileSync(new URL('../art/manifest.json', import.meta.url), 'utf8'));
const row = (id) => manifest.find((r) => r.id === id);
const near = (a, b, e = 1e-9) => Math.abs(a - b) < e;

test('fxart: the built-in cell map matches the manifest atlas row', () => {
  const r = row('particles-atlas');
  assert.ok(r, 'particles-atlas row');
  assert.deepEqual(r.cellGrid, [GRID, GRID]);
  assert.equal(r.maxSafeLod, MAX_LOD);
  assert.equal(r.cellOrigin, 'top-left');
  for (const [name, at] of Object.entries(CELLS)) assert.deepEqual(r.cells[name], at, name);
});

test('fxart: cell index is row * 4 + col, an unknown name draws the disc', () => {
  assert.equal(cellIndex('disc'), 0);
  assert.equal(cellIndex('spark'), 1);
  assert.equal(cellIndex('petal'), 4);
  assert.equal(cellIndex('frost'), 10);
  assert.equal(cellIndex('star'), 13);
  assert.equal(cellIndex('nope'), 0);
  assert.equal(cellIndex('ring', { disc: [0, 0], ring: [3, 3] }), 15);
});

test('fxart: cell UVs stay inside their cell with a half texel inset at mip 4', () => {
  const e = cellInset(1024, 4);
  assert.ok(near(e, 8 / 256), 'half a mip 4 texel = 8 base px of a 256 px cell');
  assert.ok(e * 256 < 20, 'inside the 20 px gutter');
  for (const name of Object.keys(CELLS)) {
    const [c, r] = CELLS[name], uvr = cellUV(name);
    assert.ok(uvr.u0 > c / GRID && uvr.u1 < (c + 1) / GRID, name + ' u');
    assert.ok(uvr.v0 > r / GRID && uvr.v1 < (r + 1) / GRID, name + ' v (row 0 at v 0 when flipY is off)');
    assert.ok(near(uvr.u1 - uvr.u0, (1 - 2 * e) / GRID));
  }
  const f = cellUV('drip', { flipY: true });
  assert.ok(f.v0 > f.v1 && f.v0 < 1 - 2 / GRID && f.v1 > 1 - 3 / GRID, 'flipY mirrors the rows');
});

test('fxart: sprite uv maps upright into the cell (sprite top = cell top)', () => {
  const r = cellUV('drip');
  const [u0, vTop] = spriteToAtlas('drip', 0, 1), [u1, vBot] = spriteToAtlas('drip', 1, 0);
  assert.ok(near(u0, r.u0) && near(u1, r.u1));
  assert.ok(near(vTop, r.v0) && near(vBot, r.v1));
  const [uc, vc] = spriteToAtlas('star', 0.5, 0.5);
  assert.ok(near(uc, 1.5 / GRID) && near(vc, 3.5 / GRID), 'centre of the star cell');
});

test('fxart: the sampled mip never passes the safe clamp', () => {
  assert.equal(atlasLod(0), 0);
  assert.equal(atlasLod(1 / 1024), 0);
  assert.ok(near(atlasLod(4 / 1024), 2));
  assert.equal(atlasLod(1), MAX_LOD);
  assert.equal(atlasLod(1, 1024, 3), 3);
});

test('fxart: every family draws only real cells, as the brief asks', () => {
  for (const [fam, list] of Object.entries(FAMILIES)) {
    assert.ok(list.length > 0, fam);
    for (const [name, w] of list) { assert.ok(CELLS[name], fam + ': ' + name); assert.ok(w > 0); }
  }
  const has = (fam, ...names) => names.every((n) => FAMILIES[fam].some(([c]) => c === n));
  assert.ok(has('trail', 'disc', 'glitter'), 'boost trail = soft disc + glitter');
  assert.ok(has('dust', 'ring'), 'landing = ring');
  assert.ok(has('bump', 'shard', 'glint'), 'bump = shard and glint');
  assert.ok(has('pop', 'bubble', 'glitter'), 'pickup pop = bubble rim + glitter');
  assert.ok(has('freeze', 'frost'), 'Freeze hit = frost crystals');
  assert.ok(has('golden', 'star', 'glitter'), 'Golden = star and glitter');
  assert.ok(has('melt', 'drip'), 'Melt = drips');
  assert.ok(UPRIGHT.has('drip') && ALIGNED.has('spark'));
});

test('fxart: drift sparks are streaks, glints join at tier 3, petals by the share', () => {
  const t1 = sparkFamily(1, 0), t3 = sparkFamily(3, 0.3);
  assert.deepEqual(t1.map(([c]) => c), ['spark']);
  assert.ok(!t1.some(([c]) => c === 'glint') && !sparkFamily(2, 0.1).some(([c]) => c === 'glint'));
  assert.deepEqual(t3.map(([c]) => c).sort(), ['glint', 'petal', 'spark']);
  const sum = t3.reduce((a, [, w]) => a + w, 0);
  assert.ok(near(sum, 1));
  assert.ok(near(t3.find(([c]) => c === 'petal')[1], 0.3));
});

test('fxart: pickCell walks the weights and never falls off either end', () => {
  const list = [['disc', 3], ['glitter', 1]];
  assert.equal(pickCell(list, 0), 'disc');
  assert.equal(pickCell(list, 0.74), 'disc');
  assert.equal(pickCell(list, 0.76), 'glitter');
  assert.equal(pickCell(list, 1), 'glitter');
  assert.equal(pickCell(list, -1), 'disc');
  assert.equal(pickCell([], 0.5), 'disc');
  let glit = 0;
  for (let i = 0; i < 1000; i++) if (pickCell(list, (i + 0.5) / 1000) === 'glitter') glit++;
  assert.equal(glit, 250);
});

test('fxart: a burst finds its family by name or by the LOOK colour list it carries', () => {
  const L = { bumpColours: ['#fff'], popColours: ['#bff'], boxColours: ['#fd6'] };
  assert.equal(familyOf({ colours: L.bumpColours }, L), 'bump');
  assert.equal(familyOf({ colours: L.popColours }, L), 'pop');
  assert.equal(familyOf({ colours: L.boxColours }, L), 'box');
  assert.equal(familyOf({ colours: ['#fff'] }, L), 'box', 'a copy is not the list');
  assert.equal(familyOf({ family: 'melt', colours: L.bumpColours }, L), 'melt');
  assert.equal(familyOf({ family: 'nope', colours: L.popColours }, L), 'pop');
  assert.equal(hitFamily('freeze'), 'freeze');
  assert.equal(hitFamily('golden'), 'golden');
  assert.equal(hitFamily('melt'), 'melt');
  assert.equal(hitFamily('spiral'), 'spiral', 'polish 2: every effect sprays its own cells');
  assert.equal(hitFamily('mercy'), null);
});

test('fxart: without the atlas each cell falls back to a B1 shape (0 disc, 1 sparkle, 2 petal)', () => {
  assert.equal(legacyShape('disc'), 0);
  assert.equal(legacyShape('spark'), 1);
  assert.equal(legacyShape('petal'), 2);
  assert.equal(legacyShape('nope'), 0);
  for (const name of Object.keys(CELLS)) assert.ok([0, 1, 2].includes(legacyShape(name)), name);
});

test('fxart: palette weights follow palette.set, motes bring petals in afterglow and swirls in deep', () => {
  const w = paletteWeights([['dawn', 0.5], ['deep', 0.5]], 'afterglow', 0.4);
  assert.ok(near(w.dawn, 0.3) && near(w.deep, 0.3) && near(w.afterglow, 0.4));
  assert.deepEqual(paletteWeights('deep', 'deep', 1), { deep: 1 });
  const dawn = moteMix({ dawn: 1 }), deep = moteMix({ deep: 1 }), glow = moteMix({ afterglow: 1 });
  assert.ok(glow.petal > dawn.petal && glow.petal > deep.petal, 'petals in afterglow');
  assert.ok(deep.swirl > dawn.swirl && deep.swirl > glow.swirl, 'swirls in deep');
  assert.equal(dawn.swirl, FXART_LOOK.moteSwirl[0]);
  const both = moteMix({ afterglow: 1, deep: 1 });
  assert.ok(both.petal + both.swirl <= 1 + 1e-9, 'discs never go negative');
});

test('fxart: mask numbers have defaults and the dither leaves clear and full untouched', () => {
  const X = maskLook({});
  for (const k of ['frostMask', 'meltFlowTile', 'meltFlowUvps', 'crackScale', 'irisMaskR', 'ditherEdge', 'ditherOut', 'noisePx']) assert.ok(X[k] !== undefined, k);
  assert.equal(maskLook({ irisMaskR: 0.3 }).irisMaskR, 0.3);
  assert.ok(X.crackLight <= 0.15, 'Shatter cracks never flash');
  assert.ok(X.ditherOut <= 2 / 255, 'output dither below two steps');
  for (const j of [-0.5, 0, 0.5]) {
    assert.equal(ditherEdge(0, j, X.ditherEdge), 0, 'a clear pixel stays clear');
    assert.equal(ditherEdge(1, j, X.ditherEdge), 1);
  }
  assert.ok(Math.abs(ditherEdge(0.5, 0.5, X.ditherEdge) - 0.5) <= X.ditherEdge / 2 + 1e-9, 'a small jitter mid fade');
});

test('fxart: the art loader asks for every wave 2 id this lane draws', async () => {
  const src = readFileSync(new URL('../render/art.js', import.meta.url), 'utf8');
  for (const id of ['particles-atlas', 'fx-frost-edge', 'fx-melt-flow', 'fx-crack', 'fx-iris', 'fx-noise']) {
    assert.ok(src.includes(`'${id}'`), id + ' in WANT');
    assert.ok(row(id), id + ' in the manifest');
  }
  const fx = readFileSync(new URL('../render/screenfx.js', import.meta.url), 'utf8');
  for (const id of ['fx-frost-edge', 'fx-melt-flow', 'fx-crack', 'fx-iris', 'fx-noise']) assert.ok(fx.includes(`'${id}'`), 'screenfx reads ' + id);
  assert.ok(fx.includes('max(col, bright)'), 'never blind: the highlight floor stays');
});
