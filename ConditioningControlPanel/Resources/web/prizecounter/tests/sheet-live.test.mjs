// The live pictures: every bubble skin from the lab pops with its own particles (bounded), the Loom spiral is the same
// for a seed, effect items draw on canvases (never stills), and "Your screen" wears the picked look.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';
import { SKINS, DISCO, skinOf, makeBubble, popBubble, stepBubble, stepParts, rollSpec, rng } from '../sheet/skins.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));

test('every bubble skin on sale has its own pop; particles stay bounded and die out', () => {
  for (const kind of ['soap', 'champagne', 'heart', 'spiral', 'marble', 'disco', 'frost']) {
    assert.ok(SKINS[kind], kind);
    for (const r of [12, 60]) {
      const w = { c: null, p: [], cap: 260 }, b = makeBubble(kind, 50, 50, r, { doc: null });
      if (kind === 'spiral') b.spec = rollSpec(rng(b.seed));
      b.grow = 1;
      assert.equal(popBubble(b), true);
      assert.equal(popBubble(b), false, 'a bubble pops once');
      for (let i = 0; i < 40 && b.state !== 'dead'; i++) stepBubble(w, b, 1 / 30);
      assert.equal(b.state, 'dead', kind);
      assert.ok(w.p.length > 0 && w.p.length <= 260, `${kind} ${r}: ${w.p.length}`);
      for (let i = 0; i < 150; i++) stepParts(w, 1 / 30);
      assert.equal(w.p.length, 0, `${kind} particles end`);
    }
  }
  assert.ok(skinOf('gum').draw && skinOf('latex', '#123456').burst, 'a look not out yet still draws and pops');
  assert.deepEqual(rollSpec(rng(42)), rollSpec(rng(42)));
});

test('the shelves draw effect items on live canvases, and the fitting room shows Your screen and what is worn', async () => {
  const server = createPreviewServer(SAMPLE);
  const host = asHost({ init: { motion: 'full', tab: 'effects' }, request: (op, b, i) => server.handle(op, b, i) });
  const doc = fakeDocument(), root = doc.createElement('main');
  const page = await mountSheet(host, { root, doc, win: { addEventListener() {}, removeEventListener() {} }, now: () => 1e6, frame: (f) => f(), later() {}, loadIndex: () => null });
  const item = (id) => page.view.root.all().find((n) => n.dataset && n.dataset.key === 'it:' + id);
  for (const id of ['fx.bubble.heart', 'fx.card.lace', 'fx.fog.twotone', 'fx.word.chalk', 'fx.behind.whisper']) assert.ok(item(id).all().some((n) => n.tagName === 'CANVAS'), id);
  assert.match(page.view.room.text(), /Wearing now Pick an item and this screen tries it on\. Bubble skins Soap Card frames Plain Word styles Classic/);
  assert.ok(page.view.room.all().some((n) => n.tagName === 'CANVAS'));
  page.pick('fx.bubble.heart');
  assert.equal(page.view.live.looks({ ...page.view.live, pv: page.pv(), state: page.shop.state(), pick: 'fx.bubble.heart' }).bubble, 'heart');
  assert.match(page.view.room.text(), /Your screen Trying it on Heart bubbles/);
  page.pick('fx.fog.twotone');
  assert.ok(page.view.room.all().some((n) => n.dataset && n.dataset.key === 'fog2:mint'), 'two-tone picks a second colour');
  const shelfCanvas = item('fx.bubble.heart').all().find((n) => n.tagName === 'CANVAS');
  page.pick('fx.bubble.heart');
  assert.equal(item('fx.bubble.heart').all().find((n) => n.tagName === 'CANVAS'), shelfCanvas, 'a repaint keeps the same live canvas');
});

test('disco wears its palette: coloured tiles, shards in its hues, no beams and no strobe under Gentle', () => {
  const fills = [];
  const grad = () => ({ addColorStop() {} });
  const ctx = new Proxy({}, {
    get: (o, k) => (k in o ? o[k] : k === 'createLinearGradient' || k === 'createRadialGradient' ? grad : () => {}),
    set: (o, k, v) => { o[k] = v; if (k === 'fillStyle' && typeof v === 'string') fills.push(v); return true; },
  });
  const b = makeBubble('disco', 0, 0, 40, { doc: null });
  for (const t of [0.5, 1.7, 3.1]) SKINS.disco.draw(ctx, b, t, 0);
  const tiles = fills.filter((f) => f.startsWith('rgb(')).map((f) => f.slice(4, -1).split(',').map(Number));
  const hot = tiles.filter(([r, g, bl]) => Math.max(r, g, bl) > 128 && Math.max(r, g, bl) - Math.min(r, g, bl) > 60);
  assert.ok(tiles.length > 100 && hot.length > tiles.length * 0.1, `lit tiles carry hue (${hot.length} of ${tiles.length})`);
  const shards = (gentle) => {
    const w = { c: null, p: [], cap: 400 }, d = makeBubble('disco', 0, 0, 60, { doc: null, gentle });
    SKINS.disco.burst(w, d);
    return w.p;
  };
  const full = shards(false), soft = shards(true);
  assert.ok(full.filter((p) => p.kind === 'square').length <= 96 && full.filter((p) => p.kind === 'star').length <= 16);
  assert.equal(full.filter((p) => p.kind === 'beam').length, 12);
  assert.equal(soft.filter((p) => p.kind === 'beam').length, 0, 'no beams under Gentle');
  assert.ok(soft.every((p) => p.kind !== 'square' || p.soft), 'Gentle shards never catch the light');
  const hues = new Set(full.filter((p) => p.kind === 'square').map((p) => p.color));
  assert.deepEqual([...hues].sort(), DISCO.map((n) => `rgb(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255})`).sort());
});
