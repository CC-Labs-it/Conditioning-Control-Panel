import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as T from '../sim/takeover.js';
import { BY_ID } from '../data/items.js';
import { CONFIG } from '../data/config.js';
import { EMI_BARKS } from '../data/content/emi-takeover.js';

const has = (...ids) => (id) => ids.includes(id);

test('the four rows sit between Fake Online Count and the goon ending, which keeps its id', () => {
  const chain = ['go_pals', ...T.CHAIN, 'go_end'];
  for (let i = 1; i < chain.length; i++) {
    assert.deepEqual(BY_ID[chain[i]].req, [chain[i - 1]], chain[i]);
    assert.ok(BY_ID[chain[i]].cost > BY_ID[chain[i - 1]].cost, chain[i]);
  }
  assert.equal(BY_ID.go_end.kind, 'end');
  assert.equal(BY_ID.go_end.name, 'Ending: EMI Takeover');
  assert.match(BY_ID.go_end.what, /pretend to crash/);
  for (const id of T.CHAIN) assert.equal(BY_ID[id].lane, 'goon');
});

test('the stage follows the rows owned, in order', () => {
  assert.equal(T.stageOf(has()), 'none');
  assert.equal(T.stageOf(has('go_auto')), 'auto');
  assert.equal(T.stageOf(has('go_auto', 'go_will', 'go_copy')), 'copy');
  assert.equal(T.stageOf(has('go_auto', 'go_copy')), 'auto', 'a row with its parent missing does not count');
  assert.equal(T.stageOf(has(...T.CHAIN)), 'hive');
  assert.equal(T.poolOf('hive', ['h_tx']), 'shopping_transmission');
  assert.equal(T.poolOf('hive', ['h_tx', 'h_amp', 'h_uni']), 'shopping_unified');
  assert.equal(T.poolOf('none'), null);
});

test('before HIVE MIND only the player\'s presses make copies: 12 presses each, one per 4 s, never past the cap', () => {
  const cap = T.copyCap('copy', [], false);
  let pop = T.newPop(1), made = 0;
  for (let s = 0; s < 600; s++) { const r = T.popStep(pop, { now: s, stage: 'copy', cap }); pop = r.pop; made += r.made; }
  assert.equal(made, 0, 'ten idle minutes make nobody');
  for (let i = 0; i < 11; i++) pop = T.popPress(pop, { stage: 'copy' });
  assert.equal(T.popStep(pop, { now: 700, stage: 'copy', cap }).made, 0);
  pop = T.popPress(pop, { stage: 'copy' });
  let r = T.popStep(pop, { now: 700, stage: 'copy', cap });
  assert.equal(r.made, 1);
  assert.equal(r.self, false);
  pop = r.pop;
  for (let i = 0; i < 200; i++) pop = T.popPress(pop, { stage: 'copy' });
  assert.equal(T.popStep(pop, { now: 702, stage: 'copy', cap }).made, 0, 'inside the 4 s gap');
  let now = 704;
  for (let i = 0; i < 40; i++, now += 4) {
    for (let k = 0; k < 20; k++) pop = T.popPress(pop, { stage: 'copy' });
    r = T.popStep(pop, { now, stage: 'copy', cap }); pop = r.pop;
  }
  assert.equal(pop.copies, cap);
});

test('after HIVE MIND copies also come on their own, and presses count faster', () => {
  const cap = T.copyCap('hive', [], false);
  let pop = T.newPop(2), made = 0, selfMade = 0;
  for (let s = 0; s <= 60; s++) { const r = T.popStep(pop, { now: s, stage: 'hive', cap }); pop = r.pop; made += r.made; selfMade += r.self ? 1 : 0; }
  assert.ok(made >= 3 && made === selfMade, `${made} on their own in a minute`);
  assert.ok(pop.copies <= cap);
  let p2 = T.newPop(1);
  for (let i = 0; i < T.POP.PRESSES_HIVE; i++) p2 = T.popPress(p2, { stage: 'hive' });
  p2 = { ...p2, selfAt: 9999 };
  assert.equal(T.popStep(p2, { now: 10, stage: 'hive', cap }).made, 1);
  assert.equal(T.popPress(T.newPop(), { stage: 'will' }).meter, 0, 'no meter before Self Copying');
});

test('caps: a phone carries about half, and her tiles raise them', () => {
  assert.equal(T.copyCap('will'), 0);
  assert.deepEqual([T.copyCap('copy'), T.copyCap('hive'), T.copyCap('hive', ['h_tx', 'h_amp']), T.copyCap('hive', ['h_tx', 'h_amp', 'h_uni'])], [6, 8, 12, 16]);
  assert.deepEqual([T.copyCap('copy', [], true), T.copyCap('hive', [], true), T.copyCap('hive', ['h_amp'], true), T.copyCap('hive', ['h_amp', 'h_uni'], true)], [3, 4, 6, 8]);
});

test('her tiles: three, in order, bought with her tips only after HIVE MIND, never two close together', () => {
  assert.deepEqual(T.HER_TILES.map((t) => t.name), ['Transmission', 'Amplifier', 'Unified Transmission']);
  const rich = { tips: 999, bought: [], sinceBuyS: 999, sinceHiveS: 999 };
  assert.equal(T.canBuy({ ...rich, stage: 'copy' }), null);
  assert.equal(T.canBuy({ ...rich, stage: 'hive', tips: 59 }), null);
  assert.equal(T.canBuy({ ...rich, stage: 'hive', sinceHiveS: 5 }), null);
  assert.equal(T.canBuy({ ...rich, stage: 'hive' }).id, 'h_tx');
  let bag = T.buyTile({ tips: 100, bought: [] }, T.HER_TILES[0]);
  assert.deepEqual(bag, { tips: 40, bought: ['h_tx'] });
  assert.equal(T.canBuy({ ...rich, ...bag, tips: 999, stage: 'hive', sinceBuyS: 10 }), null, 'the gap between her buys');
  assert.equal(T.canBuy({ ...rich, ...bag, tips: 999, stage: 'hive' }).id, 'h_amp');
  assert.deepEqual(T.buyTile(bag, T.HER_TILES[1]), bag, 'not enough tips: nothing changes');
  assert.equal(T.nextTile(['h_tx', 'h_amp', 'h_uni']), null);
  assert.equal(T.buyTile.length, 2, 'the bank is not an argument: her shopping never spends the player\'s money');
});

test('a saved bag comes back sane', () => {
  assert.deepEqual(T.cleanBag(null), { copies: 0, meter: 0, tips: 0, bought: [], clicked: 0, self: 0 });
  const bag = T.cleanBag({ copies: 99, meter: -3, tips: 12.7, bought: ['h_amp', 'h_tx', 'nope'], clicked: 4 });
  assert.equal(bag.copies, 16);
  assert.equal(bag.meter, 0);
  assert.equal(bag.tips, 12);
  assert.deepEqual(bag.bought, ['h_tx', 'h_amp']);
  assert.deepEqual(T.cleanBag({ bought: ['h_uni'] }).bought, [], 'a tile without the ones before it is dropped');
});

test('barks: at most 2 bubbles and 3 a 10 s, no line twice inside six minutes, silence when the pool is dry', () => {
  let b = T.newBarks();
  assert.ok(T.barkRoom(b, { now: 0, visible: 0 }));
  assert.ok(!T.barkRoom(b, { now: 0, visible: 2 }));
  assert.ok(!T.barkRoom(b, { now: 0, visible: 1, cost: 2 }));
  assert.ok(T.barkRoom(b, { now: 0, visible: 2, maxVisible: 3 }), 'the ending fill may show three');
  const pool = ['a', 'b', 'c'];
  for (let i = 0; i < 3; i++) { const line = T.pickBark(b, pool, { now: i }); assert.ok(line); b = T.barkSaid(b, line, { now: i }); }
  assert.ok(!T.barkRoom(b, { now: 3, visible: 0 }), 'three in ten seconds is the budget');
  assert.ok(T.barkRoom(b, { now: 13, visible: 0 }));
  assert.equal(T.pickBark(b, pool, { now: 100 }), null);
  assert.ok(T.pickBark(b, pool, { now: 400 }));
  assert.ok(!T.barkRoom(T.barkSaid(T.newBarks(), 'x', { now: 0, cost: 2 }), { now: 1, visible: 0, lite: true }), 'a phone gets two');
  assert.equal(T.barkLife('1234567890'), 2800);
});

test('the bark bank is EMI: lowercase, no exclamation marks, no dashes, never about stopping', () => {
  const all = [...Object.values(EMI_BARKS.solo).flat(), ...Object.values(EMI_BARKS.pairs).flat(2), ...Object.values(EMI_BARKS.reactions).flat()];
  assert.ok(all.length > 150);
  for (const l of all) {
    assert.equal(l, l.toLowerCase(), l);
    assert.ok(!/[!–—]/.test(l), l);
    assert.ok(!/\b(she|her)\b.*\byou\b.*\bshe\b/.test(l), l);
    assert.ok(!/wake up|can't leave|cannot leave|can't stop pressing/.test(l), l);
    assert.ok(l.split(' ').length <= 12, l);
  }
  for (const pool of Object.keys(EMI_BARKS.solo)) assert.ok(EMI_BARKS.solo[pool].length >= 8 && EMI_BARKS.pairs[pool].length >= 4, pool);
});

const SCREENS = [
  { name: '1920x1080', w: 1920, h: 1080, controls: { x: 1920 - 330, y: 0, w: 330, h: 64 } },
  { name: '1366x768', w: 1366, h: 768, controls: { x: 1366 - 330, y: 0, w: 330, h: 64 } },
  { name: '1280x560', w: 1280, h: 560, controls: { x: 1280 - 330, y: 0, w: 330, h: 64 } },
  { name: '390x844', w: 390, h: 844, controls: { x: 390 - 250, y: 0, w: 250, h: 64 } },
];
const hits = (a, b) => a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y;

test('the grid fills the screen, leaves Wake Up\'s corner empty, and its lit cells read DROP', () => {
  for (const s of SCREENS) {
    const g = T.dropGrid(s);
    assert.ok(g.readable, s.name);
    assert.ok(g.cols * g.pitch <= s.w + 0.01 && g.rows * g.pitch <= s.h + 0.01, s.name);
    assert.ok(s.w - g.cols * g.pitch < g.pitch && s.h - g.rows * g.pitch < g.pitch, `${s.name} fills the screen`);
    for (const c of g.cells) assert.ok(!hits({ x: c.x, y: c.y, w: g.pitch, h: g.pitch }, s.controls), `${s.name} cell ${c.c},${c.r} under the controls`);
    /* read the lit cells back as text and compare with the letters */
    const mask = T.dropMask(g.stacked);
    const lit = new Set(g.cells.filter((c) => c.lit).map((c) => `${c.c - g.c0},${c.r - g.r0}`));
    const want = mask.flatMap((line, r) => [...line].flatMap((ch, c) => (ch === '1' ? [`${c},${r}`] : [])));
    assert.deepEqual([...lit].sort(), want.sort(), s.name);
    assert.ok(g.cells.length <= 520, `${s.name}: ${g.cells.length} cells`);
  }
  assert.equal(T.dropGrid(SCREENS[3]).stacked, true, 'a portrait phone stacks DR over OP');
  assert.equal(T.dropGrid(SCREENS[0]).stacked, false);
  assert.deepEqual(T.dropMask(false).map((l) => l.length), Array(7).fill(23));
  assert.equal(T.dropMask(true).length, 15);
  assert.equal(T.dropMask(false)[0], '11110011110001110011110');
});

test('the fill: every cell once, each new EMI beside one already there', () => {
  let seed = 7;
  const rng = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  for (const s of SCREENS) {
    const g = T.dropGrid(s);
    const { order, from } = T.fillOrder(g, [{ x: s.w / 2, y: s.h / 2 }, { x: 40, y: s.h - 40 }], rng);
    assert.equal(order.length, g.cells.length);
    assert.equal(new Set(order).size, g.cells.length);
    const seen = new Set();
    let roots = 0;
    for (const i of order) {
      const p = from.get(i);
      if (p < 0) roots += 1;
      else {
        assert.ok(seen.has(p), 'the source is already on screen');
        const a = g.cells[i], b = g.cells[p];
        assert.equal(Math.abs(a.c - b.c) + Math.abs(a.r - b.r), 1);
      }
      seen.add(i);
    }
    assert.ok(roots >= 2 && roots <= 4, `${s.name}: ${roots} starting points`);
  }
});

test('the fill takes about 24 s untouched, and hammering never gets it under 10 s', () => {
  assert.ok(Math.abs(T.fillTime(0) - T.TAKE.FILL_S) < 0.1);
  assert.ok(T.fillTime(3) < T.fillTime(0));
  assert.ok(T.fillTime(30) >= T.TAKE.FILL_MIN_S - 0.05);
  assert.equal(T.fillCount(0, 400), 1);
  assert.equal(T.fillCount(T.TAKE.FILL_S, 400), 400);
  let last = 0;
  for (let t = 0; t <= T.TAKE.FILL_S; t += 0.25) { const n = T.fillCount(t, 400); assert.ok(n >= last); last = n; }
});

test('no screen changes 3 times a second, and the whole take fits the engine cap', () => {
  for (const lite of [false, true]) for (let i = 0; i <= 100; i++) assert.ok(T.flickerGap(() => i / 100, lite) > 334);
  assert.ok(T.takeMaxS() * 1000 < CONFIG.ENDING_TAKE_MAX_MS - 20000, `${T.takeMaxS()} s`);
  assert.ok(T.TAKE.DROP_S >= 3, 'DROP holds a few seconds');
});

test('the error card says it is the game, keeps the count real, and never claims Wake Up fails', () => {
  const lines = T.errorLines(406);
  assert.ok(lines.some((l) => /This is the game/.test(l)));
  assert.ok(lines.some((l) => /emi {2}x 406/.test(l)));
  assert.ok(lines.some((l) => /Wake Up still works/.test(l)));
  assert.ok(!lines.some((l) => /\b(she|her)\b/i.test(l)));
  assert.match(lines.at(-1), /\.\.\.$/);
});

test('the ending module keeps its hard rules in the source', () => {
  const src = ['emi-takeover.js', 'emi-swarm.js', 'emi-kit.js'].map((f) => readFileSync(new URL(`../ui/unlocks/themes-b/${f}`, import.meta.url), 'utf8')).join('\n');
  assert.ok(!/speechSynthesis|SpeechSynthesisUtterance/.test(src), 'no synthetic speech');
  assert.ok(!/drawImage|getImageData|texImage2D/.test(src), 'pictures never go through a canvas');
  assert.match(src, /referrerPolicy = 'strict-origin'/);
  assert.match(src, /reason === 'panic'/);
  assert.ok(!/getElementById\('wake'\)|#wake\b/.test(src), 'Wake Up is never touched');
  assert.ok(!/setTimeout|setInterval|requestAnimationFrame/.test(src), 'game clock only');
  const goon = readFileSync(new URL('../ui/unlocks/themes-b/goon.js', import.meta.url), 'utf8');
  assert.match(goon, /emiTakeoverAborted\(\)\) return Promise\.resolve\(\)/);
});
