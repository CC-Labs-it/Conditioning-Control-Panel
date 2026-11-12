import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as T from '../sim/takeover.js';
import { BY_ID } from '../data/items.js';
import { createPictures } from '../ui/pictures.js';
import { hiveChannel, hivePictures } from '../ui/unlocks/themes-b/emi-kit.js';
import { CONFIG } from '../data/config.js';
import { EMI_BARKS, EMI_UPGRADE_LINES } from '../data/content/emi-takeover.js';

const has = (...ids) => (id) => ids.includes(id);

test('Hive Mind gives neighbours one channel and rotates each channel on the next wave', () => {
  const pictures = createPictures({ scrolller: { consent: true } });
  const ctx = { pictures, pick: () => 'goon' };
  const rows = ['a','b','c'].map(id => ({kind: 'image', url: 'https://cdn.scrolller.com/' + id + '.jpg'}));
  pictures.receiveScrolller({v: 1, rows: rows.slice(0, 1)});
  let previous = hivePictures(ctx, [], 3);
  assert.deepEqual(previous, [rows[0].url, rows[0].url, rows[0].url]);
  pictures.receiveScrolller({v: 1, rows});
  previous = hivePictures(ctx, previous, 3);
  assert.deepEqual(Array.from({length: 10}, (_, i) => hiveChannel(i)), [0,0,0,1,1,1,2,2,2,0]);
  for (let i = 0; i < 20; i++) {
    const next = hivePictures(ctx, previous, 3);
    assert.equal(new Set(next).size, 3);
    assert.ok(next.every((url, group) => url !== previous[group]));
    previous = next;
  }
  pictures.setRemoteConsent(false);
});

test('the four rows sit between Fake Online Count and the goon ending, which keeps its id', () => {
  assert.deepEqual(T.CHAIN, ['go_tx', 'go_will', 'go_corp', 'go_hive']);
  const chain = ['go_pals', ...T.CHAIN, 'go_end'];
  for (let i = 1; i < chain.length; i++) {
    assert.deepEqual(BY_ID[chain[i]].req, [chain[i - 1]], chain[i]);
    assert.ok(BY_ID[chain[i]].cost > BY_ID[chain[i - 1]].cost, chain[i]);
  }
  assert.deepEqual(chain.slice(1).map((id) => BY_ID[id].cost), [780000, 930000, 1120000, 1350000, 1700000]);
  assert.equal(BY_ID.go_tx.name, 'Transmission');
  assert.equal(BY_ID.go_corp.name, 'Founded Startup');
  assert.equal(BY_ID.go_auto, undefined);
  assert.equal(BY_ID.go_copy, undefined);
  assert.equal(BY_ID.go_end.kind, 'end');
  assert.equal(BY_ID.go_end.name, 'Ending: EMI Takeover');
  assert.match(BY_ID.go_end.what, /pretend to crash/);
  for (const id of T.CHAIN) assert.equal(BY_ID[id].lane, 'goon');
});

const ALL = ['h_amp', 'h_oc', 'h_loud', 'h_rep', 'h_over'];

test('Hive Mind waits for EMI to shop, and the takeover itself is never a purchase', () => {
  const run = { owned: { go_tx: 1, go_will: 1, go_corp: 1 }, store: { go_tx: { bag: { bought: [] } } } };
  assert.match(T.purchaseHint(run, 'go_hive'), /Amplifier/);
  run.store.go_tx.bag.bought = ALL.slice(0, 4);
  assert.match(T.purchaseHint(run, 'go_hive'), /Override/);
  run.store.go_tx.bag.bought = ALL;
  assert.equal(T.purchaseHint(run, 'go_hive'), '');
  assert.match(T.purchaseHint(run, 'go_end'), /Keep clicking/);
});

test('only a player-grown crowd with the whole interface converted starts DROP', () => {
  for (const lite of [true, false]) {
    const ready = { count: T.hiveCrowd(lite), lite, presses: 4, objects: 0, buzzing: 0, uiTaken: true };
    assert.ok(T.readyToFill(ready));
    for (const missing of [{ count: ready.count - 1 }, { presses: 3 }, { objects: 1 }, { buzzing: 1 }, { uiTaken: false }]) {
      assert.equal(T.readyToFill({ ...ready, ...missing }), false);
    }
  }
});

test('the stage follows the rows owned, in order; Self Replication and Override are hers, not rows', () => {
  assert.equal(T.stageOf(has()), 'none');
  assert.equal(T.stageOf(has('go_tx')), 'tx');
  assert.equal(T.stageOf(has('go_tx', 'go_will', 'go_corp')), 'corp');
  assert.equal(T.stageOf(has('go_tx', 'go_corp')), 'tx', 'a row with its parent missing (or switched off) holds the story');
  assert.equal(T.stageOf(has(...T.CHAIN)), 'hive');
  assert.equal(T.phaseOf('corp', ['h_amp', 'h_oc']), 'corp');
  assert.equal(T.phaseOf('corp', ['h_amp', 'h_oc', 'h_loud', 'h_rep']), 'rep');
  assert.equal(T.phaseOf('hive', ['h_amp']), 'corp', 'HIVE MIND bought early waits for her Self Replication');
  assert.equal(T.phaseOf('hive', ['h_amp', 'h_oc', 'h_loud', 'h_rep']), 'rep', 'HIVE MIND bought early also waits for Override');
  assert.equal(T.phaseOf('corp', ALL), 'rep');
  assert.equal(T.phaseOf('hive', ALL), 'hive');
  assert.equal(T.phaseOf('will', ['h_rep']), 'will');
  assert.deepEqual(['tx', 'will', 'rep', 'hive'].map((p) => T.poolOf(p)), ['transmission', 'free_will', 'replication', 'hive_mind']);
  assert.equal(T.poolOf('corp', []), 'startup');
  assert.equal(T.poolOf('corp', ['h_amp']), 'shopping');
  assert.equal(T.poolOf('none'), null);
  for (const p of ['tx', 'will', 'corp', 'rep', 'hive']) assert.ok(EMI_BARKS.solo[T.poolOf(p, [])], p);
  assert.ok(EMI_BARKS.solo.shopping);
});

test('her tiles: Amplifier, Overclock, Louder Opinions, Self Replication, then Override, about 30-45 s apart, on her own budget', () => {
  assert.deepEqual(T.HER_TILES.map((t) => t.name), ['Amplifier', 'Overclock', 'Louder Opinions', 'Self Replication', 'Override']);
  assert.deepEqual(T.HER_TILES.map((t) => t.id), ALL);
  /* play it out on the game clock: one second steps, she buys the moment she may; after Self Replication the phase
   * is 'rep' and she keeps shopping for Override */
  let bag = { budget: 0, bought: [] }, last = -Infinity;
  const at = [];
  for (let s = 0; s < 500 && bag.bought.length < 5; s++) {
    const phase = T.phaseOf('corp', bag.bought);
    bag.budget = T.budgetStep(bag.budget, 1, { phase, bought: bag.bought });
    const tile = T.canBuy({ phase, budget: bag.budget, bought: bag.bought, sinceBuyS: s - last });
    if (tile) { bag = T.buyTile(bag, tile); last = s; at.push(s); }
  }
  assert.equal(at.length, 5);
  assert.ok(T.isShopping('rep', ['h_amp', 'h_oc', 'h_loud', 'h_rep']), 'Self Replication runs while she saves for Override');
  assert.ok(!T.isShopping('rep', ALL) && !T.isShopping('hive', ALL) && !T.isShopping('will', []));
  for (let i = 1; i < at.length; i++) assert.ok(at[i] - at[i - 1] >= 30 && at[i] - at[i - 1] <= 46, `gap ${at[i] - at[i - 1]} s`);
  assert.ok(at[0] >= 25 && at[0] <= 45, `first buy at ${at[0]} s`);
  assert.equal(T.budgetStep(5, 10, { phase: 'will' }), 5, 'no budget before Founded Startup');
  assert.equal(T.budgetStep(5, 10, { phase: 'rep', bought: ALL }), 5, 'nothing left to buy');
  assert.equal(T.budgetStep(5, 10, { phase: 'rep', bought: ALL.slice(0, 4) }), 15, 'saving for Override');
  assert.equal(T.canBuy({ phase: 'will', budget: 999, bought: [], sinceBuyS: 999 }), null);
  assert.equal(T.canBuy({ phase: 'corp', budget: 999, bought: ['h_amp'], sinceBuyS: 10 }), null, 'the gap between her buys');
  assert.equal(T.canBuy({ phase: 'corp', budget: 999, bought: ['h_amp'], sinceBuyS: 29 }), null);
  assert.deepEqual(T.buyTile({ budget: 10, bought: [] }, T.HER_TILES[0]), { budget: 10, bought: [] }, 'not enough budget: nothing changes');
  assert.equal(T.nextTile(T.HER_TILES.map((t) => t.id)), null);
  assert.equal(T.buyTile.length, 2, 'the bank is not an argument: her shopping never spends the player\'s money');
});

test('her tiles do visible things: bigger, a little faster (screens still under 3 a second), more comments', () => {
  const none = T.effectsOf([]), all = T.effectsOf(['h_amp', 'h_oc', 'h_loud']);
  assert.equal(none.size, 1);
  assert.ok(all.size > 1.2);
  assert.ok(all.speed > 1 && all.speed < 1.6);
  assert.ok(all.picS[0] < none.picS[0] && all.picS[0] >= 1, 'pictures change a little faster, never fast');
  assert.deepEqual(none.picS, [2, 3], 'Transmission: 2-3 s a picture, 2-3 s her face');
  assert.ok(all.barkS[1] < none.barkS[1] && all.coolS < none.coolS);
  assert.equal(T.effectsOf(ALL.slice(0, 4)).override, false);
  assert.equal(T.effectsOf(ALL).override, true);
});

test('Self Replication only splits; with Override about half the clicks convert an object; 0.4 s cooldown, capped 30 / 12', () => {
  let seed = 3;
  const rng = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  const over = true;
  assert.equal(T.repPress({ now: 1, lastAt: 0.8, count: 2, objects: 5, override: over, rng }), null, 'inside the cooldown');
  for (const lite of [false, true]) {
    const count = T.repCap(lite);
    assert.equal(T.repPress({ now: 10, count, lite, objects: 5, override: over, rng }), 'convert', 'a full crowd can still convert');
    assert.equal(T.repPress({ now: 10, count, lite, objects: 0, override: over, rng }), null, 'no room to split');
  }
  assert.ok(T.repPress({ now: 10, count: 11, lite: true, objects: 5, override: over, rng }));
  assert.equal(T.repPress({ now: 10, count: 2, objects: 0, override: over, rng: () => 0 }), 'split', 'nothing to take: she splits');
  for (let i = 0; i < 200; i++) assert.equal(T.repPress({ now: i, count: 2, objects: 3, rng }), 'split', 'no Override: never a conversion');
  let convert = 0;
  for (let i = 0; i < 400; i++) if (T.repPress({ now: i, count: 2, objects: 3, override: over, rng }) === 'convert') convert += 1;
  assert.ok(convert > 160 && convert < 240, `${convert} of 400 converted`);
});

test('Override: the buzz shakes about 400 ms; the bzzz never stacks', () => {
  assert.ok(T.REP.BUZZ_MS >= 300 && T.REP.BUZZ_MS <= 500);
  assert.ok(T.buzzRoom({ now: 5 }));
  assert.ok(!T.buzzRoom({ now: 5.3, lastAt: 5 }), 'rapid clicks: one bzzz');
  assert.ok(T.buzzRoom({ now: 5 + T.REP.SOUND_GAP_S + 0.01, lastAt: 5 }));
  assert.ok(T.REP.SOUND_GAP_S > 0.25, 'longer than the bzzz itself: never two at once');
});

test('HIVE MIND: forced 150 s after Override, and only then', () => {
  assert.ok(!T.mustForceHive({ phase: 'rep', bought: ALL, ownsHive: false, sinceOverS: 149 }));
  assert.ok(T.mustForceHive({ phase: 'rep', bought: ALL, ownsHive: false, sinceOverS: 150 }));
  assert.ok(!T.mustForceHive({ phase: 'rep', bought: ALL, ownsHive: true, sinceOverS: 999 }));
  assert.ok(!T.mustForceHive({ phase: 'rep', bought: ALL.slice(0, 4), ownsHive: false, sinceOverS: 999 }), 'Self Replication alone starts no clock');
  assert.ok(!T.hiveClockRuns({ phase: 'rep', bought: ALL.slice(0, 4), ownsHive: false }));
  assert.ok(T.hiveClockRuns({ phase: 'rep', bought: ALL, ownsHive: false }));
  assert.ok(!T.mustForceHive({ phase: 'corp', bought: [], ownsHive: false, sinceOverS: 999 }), 'never before Override is hers');
});

test('HIVE MIND patterns: every pattern changes a screen at most twice a wave, well under 3 a second', () => {
  let prev = null;
  for (let i = 0; i < 50; i++) { const p = T.nextPattern(prev); assert.ok(T.PATTERNS.includes(p)); assert.notEqual(p, prev); prev = p; }
  for (const pattern of T.PATTERNS) {
    for (const period of [T.HIVE.PERIOD_S, T.HIVE.PERIOD_S_FAST]) {
      for (const where of [{ x01: 0.1, d01: 0, i: 0, n: 9, side: 0 }, { x01: 0.9, d01: 0.7, i: 7, n: 9, side: 1 }]) {
        let last = null, lastAt = -Infinity, minGap = Infinity, lit = 0;
        for (let t = 0; t < 30; t += 0.01) {
          const on = T.patternLit(pattern, { t, period, ...where });
          if (on) lit += 1;
          if (last !== null && on !== last) { minGap = Math.min(minGap, t - lastAt); lastAt = t; }   // the first change has no gap before it
          last = on;
        }
        assert.ok(minGap > 0.4, `${pattern} ${period}: fastest change ${minGap.toFixed(2)} s`);
        assert.ok(lit > 0, `${pattern} lights the screen`);
      }
    }
  }
  /* coordinated: a sweep reaches the left before the right, a ripple the first EMI before the far ones */
  const firstOn = (pattern, where) => { for (let t = 0.01; t < 6; t += 0.01) if (T.patternLit(pattern, { t, ...where }) && !T.patternLit(pattern, { t: t - 0.01, ...where })) return t; return Infinity; };
  assert.ok(firstOn('sweep', { x01: 0.2 }) < firstOn('sweep', { x01: 0.7 }));
  assert.ok(firstOn('ripple', { d01: 0.1 }) < firstOn('ripple', { d01: 0.6 }));
  assert.notEqual(T.patternLit('halves', { t: 0.3, side: 0 }), T.patternLit('halves', { t: 0.3, side: 1 }));
  assert.ok(T.HIVE.MIN_GAP_S >= 0.334);
});

test('a saved bag comes back sane', () => {
  assert.deepEqual(T.cleanBag(null), { copies: 0, budget: 0, bought: [], sinceOver: 0, clicked: 0, took: 0 });
  const bag = T.cleanBag({ copies: 99, budget: 12.5, bought: ['h_oc', 'h_amp', 'nope'], sinceOver: 9999, clicked: 4 });
  assert.equal(bag.copies, T.REP.CAP - 1);
  assert.equal(bag.budget, 12.5);
  assert.equal(bag.sinceOver, T.HIVE.FORCE_S);
  assert.deepEqual(T.cleanBag({ bought: ALL }).bought, ALL);
  assert.deepEqual(T.cleanBag({ bought: ['h_amp', 'h_over'] }).bought, ['h_amp'], 'Override without Self Replication is dropped');
  assert.deepEqual(bag.bought, ['h_amp', 'h_oc']);
  assert.deepEqual(T.cleanBag({ bought: ['h_rep'] }).bought, [], 'a tile without the ones before it is dropped');
  assert.deepEqual(T.cleanBag({ budget: -3 }).budget, 0);
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
  const upgrades = [...T.CHAIN, ...T.HER_TILES.map(tile => tile.id)];
  assert.equal(new Set(upgrades.map(id => EMI_UPGRADE_LINES[id])).size, upgrades.length);
  for (const id of upgrades) assert.ok(EMI_UPGRADE_LINES[id], id);
  const all = [...Object.values(EMI_UPGRADE_LINES), ...Object.values(EMI_BARKS.solo).flat(), ...Object.values(EMI_BARKS.pairs).flat(2), ...Object.values(EMI_BARKS.reactions).flat()];
  assert.ok(all.length > 150);
  for (const l of all) {
    assert.equal(l, l.toLowerCase(), l);
    assert.ok(!/[!–—]/.test(l), l);
    assert.ok(!/\b(she|her)\b.*\byou\b.*\bshe\b/.test(l), l);
    assert.ok(!/wake up|can't leave|cannot leave|can't stop pressing/.test(l), l);
    assert.ok(l.split(' ').length <= 12, l);
    assert.ok(!/\btips?\b/.test(l), `no tips economy any more: ${l}`);
  }
  for (const pool of ['transmission', 'free_will', 'startup', 'shopping', 'replication', 'hive_mind']) assert.ok(EMI_BARKS.solo[pool].length >= 8 && EMI_BARKS.pairs[pool].length >= 4, pool);
  for (const r of ['copy_made', 'object_taken', 'emi_buys_tile', 'override_bought', 'hive_forced']) assert.ok(EMI_BARKS.reactions[r].length >= 2, r);
});

test('the run-up keeps its hard rules in the source', () => {
  const src = readFileSync(new URL('../ui/unlocks/themes-b/emi-swarm.js', import.meta.url), 'utf8');
  assert.ok(!/ctx\.button\.press\(/.test(src), 'she never taps the button any more');
  assert.match(src, /ev\?\.via !== 'hand' && ev\?\.via !== 'right'/, 'only the player\'s own presses make EMIs');
  for (const line of src.split(/\r?\n/)) {
    if (/scaleY/.test(line)) assert.match(line, /\.bar\.style/, `no stretch or squash on her: ${line.trim()}`);
    if (/scaleX/.test(line)) assert.ok(!/scaleX\((?!-1\))/.test(line), `a flip is the only scaleX: ${line.trim()}`);
  }
  assert.ok(!/pointermove|mousemove|clientX/.test(src), 'no EMI follows the cursor');
  assert.match(src, /removeAttribute\('data-et-gone'\)/, 'every hidden object comes back on cleanup');
  assert.match(src, /sizes: \(\) =>/, 'the ending reads sizes()');
  const app = readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
  assert.match(app, /fuzz:emi-buys-hive[\s\S]{0,400}engine\.buy\('go_hive'\)/, 'the forced buy can only buy HIVE MIND');
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

test('no screen changes 3 times a second, and the whole take fits the engine cap', () => {
  for (const lite of [false, true]) for (let i = 0; i <= 100; i++) assert.ok(T.flickerGap(() => i / 100, lite) > 334);
  assert.ok(T.takeMaxS() * 1000 < CONFIG.ENDING_TAKE_MAX_MS - 20000, `${T.takeMaxS()} s`);
  assert.ok(T.TAKE.DROP_S >= 3, 'DROP holds a few seconds');
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
  assert.ok(!/COPY\.goon\.later|endingStage\(/.test(goon), 'no sunrise card after the take (owner 2026-11-09)');
});
