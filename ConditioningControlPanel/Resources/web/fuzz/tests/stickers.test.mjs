import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import * as E from '../sim/economy.js';
import { ACHIEVEMENTS } from '../data/content/index.js';
import { STICKER_PAGES, STICKERS, STICKER_BY_ID } from '../data/content/stickers.js';
import { ENDING_IDS, createLife, parse, recordEnding, serialize } from '../sim/save.js';
import { dueThisRun, earnSticker, met, meterMult, noteLife, pageView, parseCardSnapshot, shown, snapshotRun, syncLife } from '../sim/stickers.js';
import { OFF_SHELF } from '../sim/economy.js';
import { THEME_IDS } from '../data/content/index.js';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const st = (o = {}) => ({ taps: 0, buys: 0, total: 0, clock: 0, picks: {}, owned: {}, tally: {}, ...o });

test('the book: 77 stickers on 9 pages, every old achievement kept, every page and picture real', () => {
  assert.equal(STICKERS.length, 77);
  assert.equal(new Set(STICKERS.map((s) => s.id)).size, STICKERS.length);
  for (const a of ACHIEVEMENTS) {
    const s = STICKER_BY_ID[a.id];
    assert.ok(s, `${a.id} is in the book`);
    assert.equal(s.when, a.when);
    assert.equal(s.hidden, a.hidden);
  }
  assert.equal(STICKERS.length - ACHIEVEMENTS.length, 44);
  const pages = new Set(STICKER_PAGES.map((p) => p.id));
  for (const s of STICKERS) {
    assert.ok(pages.has(s.page), `${s.id} page`);
    assert.ok(s.name && s.line && s.how, `${s.id} copy`);
    assert.ok(!/[—–!]/.test(s.name + s.line + s.how), `${s.id}: no dashes or exclamation marks`);
    const src = s.art.k === 'img' || s.art.k === 'scene' ? s.art.src : s.art.k === 'portrait' ? `assets/adcard/${s.art.theme}.webp` : null;
    if (src) assert.ok(existsSync(join(ROOT, src)), `${s.id} art ${src}`);
  }
});

test('retired endings: Drop and One More show only once earned, and Every Ending counts the endings on offer', () => {
  const life = createLife();
  assert.deepEqual(STICKERS.filter((s) => s.legacy).map((s) => s.id).sort(), [...OFF_SHELF].map((id) => `e_${id}`).sort());
  assert.equal(shown(life).length, STICKERS.length - OFF_SHELF.size);
  const shelf = ENDING_IDS.filter((id) => !OFF_SHELF.has(id));
  assert.equal(STICKER_BY_ID.e_all.when, `shelfEndings:${shelf.length}`);
  for (const id of shelf) life.endings[id] = 1;
  assert.ok(met(STICKER_BY_ID.e_all, st(), life));
  earnSticker(life, 'e_drop', 1);
  assert.equal(shown(life).length, STICKERS.length - OFF_SHELF.size + 1, 'an old Drop sticker stays in the book');
});

test('every ending and every theme has its sticker', () => {
  for (const id of ENDING_IDS) assert.ok(STICKER_BY_ID[`e_${id}`], `ending ${id}`);
  for (const t of THEME_IDS) assert.ok(STICKERS.some((s) => s.when === `theme:${t}`), `theme ${t}`);
});

test('scope: endings, Renewal and lifetime counters only go in the book', () => {
  for (const id of ['e_drop', 'e_three', 'p_renew', 'p_llama', 'n_gold25', 'n_terms50', 'n_wake', 't_all']) assert.equal(STICKER_BY_ID[id].scope, 'life', id);
  for (const id of ['ac_first', 't_sissy', 't_two', 'n_wheel', 'n_away']) assert.equal(STICKER_BY_ID[id].scope, 'run', id);
});

test('conditions read the run, the run tally and the lifetime', () => {
  const life = createLife();
  assert.ok(met(STICKER_BY_ID.ac_100, st({ taps: 100 }), life));
  assert.ok(!met(STICKER_BY_ID.ac_100, st({ taps: 99 }), life));
  assert.ok(met(STICKER_BY_ID.t_goon, st({ picks: { f5: 'goon' } }), life));
  assert.ok(!met(STICKER_BY_ID.t_goon, st({ picks: { f5: 'sissy' } }), life));
  assert.ok(met(STICKER_BY_ID.t_two, st({ owned: { taste2: 1 } }), life));
  assert.ok(met(STICKER_BY_ID.n_wheel, st({ tally: { wheel: 10 } }), life));
  assert.ok(met(STICKER_BY_ID.ac_rest2, st({ tally: { rest: 120 } }), life));
  noteLife(life, 'golden', 25);
  assert.ok(met(STICKER_BY_ID.n_gold25, st(), life));
  assert.equal(noteLife(life, 'nonsense'), 0, 'unknown lifetime counters are refused');
});

test('this run: due stickers skip what the run already earned and every lifetime sticker', () => {
  const life = createLife();
  life.endings.drop = 1;
  const due = dueThisRun(st({ taps: 150 }), life, new Set(['ac_first'])).map((s) => s.id);
  assert.deepEqual(due, ['ac_100']);
});

test('the meter keeps +10% per ten and never pays past the old +30%', () => {
  assert.equal(meterMult(0), 1);
  assert.equal(meterMult(9), 1);
  assert.equal(meterMult(10), 1.1);
  assert.equal(meterMult(33), 1.3);
  assert.equal(meterMult(60), 1.3);
});

test('the book keeps the first run; an ending writes its sticker under the run that ended', () => {
  const life = createLife();
  assert.ok(earnSticker(life, 'ac_first'));
  assert.equal(life.stickers.ac_first, 1);
  assert.ok(!earnSticker(life, 'ac_first', 4), 'never written twice');
  assert.ok(!earnSticker(life, 'nope'));
  const s = E.createRun({});
  recordEnding(life, s, 'drop');
  recordEnding(life, s, 'outside');
  recordEnding(life, s, 'o_out');
  assert.equal(life.stickers.e_drop, 1);
  assert.equal(life.stickers.e_outside, 2);
  assert.equal(life.stickers.e_three, 3, 'three different endings');
  assert.ok(!life.stickers.e_all);
});

test('sync: themes count across runs, Renewal and llamas come from the save', () => {
  const life = createLife();
  for (const t of THEME_IDS) earnSticker(life, `t_${t}`, 2);
  life.prestige.depth = 5;
  life.llamas = 12;
  const got = syncLife(life, 3);
  for (const id of ['t_all', 'p_renew', 'p_depth5', 'p_llama']) assert.ok(got.includes(id), id);
  assert.ok(!got.includes('p_core'));
  assert.deepEqual(syncLife(life, 3), [], 'nothing twice');
  assert.equal(pageView(life, 'sky').find((v) => v.sticker.id === 'p_renew').run, 3);
});

test('a save keeps the book and drops what it does not know', () => {
  const life = createLife();
  earnSticker(life, 'ac_gold', 2);
  noteLife(life, 'terms', 7);
  life.stickers.bogus = 3;
  life.tally.bogus = 9;
  const back = parse(serialize(E.createRun({}), life));
  assert.deepEqual(back.life.stickers, { ac_gold: 2 });
  assert.deepEqual(back.life.tally, { terms: 7 });
  const old = parse(serialize(E.createRun({}), { ...createLife(), stickers: undefined, tally: undefined }));
  assert.deepEqual(old.life.stickers, {});
});

test('the Living Ad sticker keeps the latest card as numbers, and a pasted save cannot smuggle markup in', () => {
  const life = createLife();
  const s = E.createRun({});
  s.picks.f5 = 'goon';
  s.store.taste2 = { theme: 'sissy' };
  s.store.adcard = { pics: 12, golden: 3, terms: 40 };
  recordEnding(life, s, 'o_out');
  assert.equal(life.stickers.e_o_out, 1);
  assert.deepEqual({ f5: life.adcard.f5, second: life.adcard.second, terms: life.adcard.terms }, { f5: 'goon', second: 'sissy', terms: 40 });
  assert.equal(snapshotRun(life.adcard).picks.f5, 'goon');
  const bad = JSON.parse(serialize(E.createRun({}), life));
  bad.life.adcard = { f5: '<img src=x onerror=alert(1)>', terms: 3 };
  assert.equal(parse(JSON.stringify(bad)).life.adcard, null);
  bad.life.adcard = { f5: 'trance', second: '<b>', seed: 'x', terms: -4 };
  assert.deepEqual(parseCardSnapshot(bad.life.adcard), { f5: 'trance', second: null, seed: 0, total: 0, clock: 0, pics: 0, golden: 0, terms: 0, llamas: 0 });
});
