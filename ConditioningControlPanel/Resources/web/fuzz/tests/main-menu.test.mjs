/* The main menu's rules (lane M, owner 2026-11-22): when it shows, which rows in which order, the Expo row's four states,
 * the counters, and every label letter for letter. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readEdition } from '../ui/edition.js';
import { MENU_COPY, MENU_REUSED, menuDue, menuRows, expoKind, endingsCount, menuKey } from '../ui/menu-model.js';
import { ENDINGS } from '../data/content/endings.js';
import { galleryIds } from '../sim/economy.js';

const hosted = (caps) => readEdition({ caps }, { hosted: true });
const FULL_READY = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'ready', base: 'https://x.example/expo/' } });
const FULL_ABSENT = hosted({ edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 131 } });
const FULL_SLOT = hosted({ edition: 'full', platform: 'web', saveSlot: true });
const SMALL = hosted({ edition: 'small', platform: 'web' });
const ids = (rows) => rows.map((r) => r.id);

test('the approved labels, letter for letter', () => {
  assert.deepEqual({ ...MENU_COPY }, {
    continue: 'Continue', endings: 'Endings', options: 'Options', account: 'Account', sealed: '?',
    fuzz: '{n} {cur}', endingsCount: '{got} of {total}', depth: 'Depth {n}',
  });
  assert.deepEqual({ ...MENU_REUSED }, {
    newRun: 'New run', newRunSure: 'Sure. Start over.', stickerBook: 'Sticker Book', renewal: 'Renewal',
    expo: 'Llama Expo 2037', playExpo: 'Play Llama Expo 2037', waiting: 'TRANSMISSION WAITING', moreInFull: 'More in the full game',
  });
});

test('the menu shows from the second launch on, never on a first launch or a dev door', () => {
  assert.equal(menuDue({ life: { contract: false } }), false);
  assert.equal(menuDue({ life: { contract: true } }), true);
  for (const k of ['devDoor', 'newRunReload', 'skyVisit', 'skySleeps', 'skyDemo']) assert.equal(menuDue({ life: { contract: true }, [k]: true }), false, k);
});

test('rows in order: Continue only with a live run, Account only with a save slot, the Expo row last', () => {
  const life = { contract: true };
  assert.deepEqual(ids(menuRows({ life, run: null, ed: FULL_READY })), ['new', 'stickers', 'endings', 'options', 'expo']);
  assert.deepEqual(ids(menuRows({ life, run: { fuzz: 12480.7 }, ed: FULL_SLOT })), ['continue', 'new', 'stickers', 'endings', 'options', 'account', 'expo']);
  assert.deepEqual(ids(menuRows({ life, run: { fuzz: 5, ended: { id: 'drop' } }, ed: FULL_READY }))[0], 'new');
  const c = menuRows({ life, run: { fuzz: 12480.7 }, ed: FULL_READY })[0];
  assert.equal(c.label, 'Continue');
  assert.match(c.meta, /^12,?480 Fuzz$/);   // the approved line "{n} Fuzz": the name comes from data/brand.js
});

test('Renewal: full edition only, once there is a Depth', () => {
  const life = { contract: true, prestige: { depth: 2 } };
  const full = menuRows({ life, ed: FULL_READY });
  assert.deepEqual(ids(full), ['new', 'stickers', 'endings', 'renewal', 'options', 'expo']);
  assert.equal(full[3].meta, 'Depth 2');
  assert.ok(!ids(menuRows({ life, ed: SMALL })).includes('renewal'));
  assert.ok(!ids(menuRows({ life: { contract: true, prestige: { depth: 0 } }, ed: FULL_READY })).includes('renewal'));
});

test('the Expo row: sealed, ready, waiting, the small door', () => {
  const none = { contract: true }, ll = { contract: true, endings: { ll_end: 1 } };
  assert.equal(expoKind(none, FULL_READY), 'sealed');
  assert.equal(expoKind(none, SMALL), 'sealed');
  assert.equal(expoKind(ll, FULL_READY), 'ready');
  assert.equal(expoKind(ll, FULL_ABSENT), 'wait');
  assert.equal(expoKind(ll, SMALL), 'door');
  const last = (life, ed) => menuRows({ life, ed }).at(-1);
  assert.deepEqual(last(none, FULL_READY), { id: 'expo', kind: 'sealed', label: '', aria: '?' });
  assert.deepEqual(last(ll, FULL_READY), { id: 'expo', kind: 'ready', label: 'Play Llama Expo 2037' });
  assert.deepEqual(last(ll, FULL_ABSENT), { id: 'expo', kind: 'wait', label: 'Llama Expo 2037', tag: 'TRANSMISSION WAITING' });
  assert.deepEqual(last(ll, SMALL), { id: 'expo', kind: 'door', label: 'Llama Expo 2037', tag: 'More in the full game' });
});

test('Endings counts the way the end card counts', () => {
  const life = { endings: { drop: 2, ll_end: 1, nope: 0 } };
  const got = new Set(['drop', 'll_end']);
  assert.deepEqual(endingsCount(life, {}), { got: 2, total: galleryIds(Object.keys(ENDINGS), got, {}).length });
  const row = menuRows({ life: { contract: true, ...life }, ed: FULL_READY }).find((r) => r.id === 'endings');
  assert.equal(row.meta, `2 of ${galleryIds(Object.keys(ENDINGS), got, {}).length}`);
});

test('keys: arrows wrap, Home and End jump, Escape does nothing', () => {
  assert.equal(menuKey('ArrowDown', 4, 5), 0);
  assert.equal(menuKey('ArrowUp', 0, 5), 4);
  assert.equal(menuKey('ArrowRight', 1, 5), 2);
  assert.equal(menuKey('Home', 3, 5), 0);
  assert.equal(menuKey('End', 0, 5), 4);
  assert.equal(menuKey('Escape', 2, 5), null);
  assert.equal(menuKey('ArrowDown', 0, 0), null);
});

test('the ready row reads "Play Llama Expo 2037" whatever the ending is called later', () => {
  const ready = menuRows({ life: { contract: true, endings: { ll_end: 1 } }, ed: FULL_READY }).at(-1);
  assert.equal(ready.label, 'Play Llama Expo 2037');
  assert.equal(MENU_REUSED.playExpo, 'Play Llama Expo 2037');
});

/* ---- M3: the collection either side ---- */
import { sideStickers, SPOTS, PORTRAIT_SPOTS, slotsFor } from '../ui/menu-model.js';
import { STICKERS } from '../data/content/stickers.js';
import { earnSticker } from '../sim/stickers.js';
import { herdRoster } from '../data/content/llamas.js';

const lifeWith = (ids, extra = {}) => { const life = { runs: 0, ...extra }; ids.forEach((id, i) => earnSticker(life, id, 1 + Math.floor(i / 3))); return life; };
const plain = STICKERS.filter((s) => s.art.k !== 'gold' && !s.legacy && !s.hidden).map((s) => s.id);

test('the sheet holds the mockup\'s 22 places, and every place stays on screen', () => {
  assert.equal(SPOTS.length + PORTRAIT_SPOTS.length, 22);
  for (const p of [...SPOTS, ...PORTRAIT_SPOTS]) { assert.ok(p.x >= 0 && p.x <= 100 && p.y >= 0 && p.y <= 100); assert.ok(p.s > 0); }
  assert.equal([...SPOTS, ...PORTRAIT_SPOTS].filter((p) => p.m).length, 8);   // the phone's eight
});

test('a near-new player: the few earned stickers, then dashed slots for the next ones', () => {
  const side = sideStickers({ life: lifeWith(plain.slice(0, 2)), ed: FULL_READY });
  assert.deepEqual(side.filter((x) => x.kind === 'got').map((x) => x.id).sort(), plain.slice(0, 2).sort());
  assert.equal(side.filter((x) => x.kind === 'slot').length, slotsFor(2));
  assert.equal(side.filter((x) => x.kind === 'portrait').length, 0);
  for (const x of side.filter((q) => q.kind === 'slot')) assert.ok(!STICKERS.find((s) => s.id === x.id).hidden, 'a slot never shows a secret');
});

test('a veteran with the llama ending: crowded, no slots, the Expo sticker first, the herd in the portrait places', () => {
  const life = lifeWith([...plain.slice(0, 40), 'e_ll_end'], { endings: { ll_end: 1 } });
  const side = sideStickers({ life, ed: FULL_READY, herd: herdRoster(6).map((l) => l.id) });
  assert.equal(side.filter((x) => x.kind === 'got').length, SPOTS.length);
  assert.equal(side.filter((x) => x.kind === 'slot').length, 0);
  assert.equal(side[0].id, 'e_ll_end');
  assert.deepEqual(side.filter((x) => x.kind === 'portrait').map((x) => x.spot), [...PORTRAIT_SPOTS]);
  /* No herd before the llama ending. */
  assert.equal(sideStickers({ life: lifeWith(plain.slice(0, 40)), ed: FULL_READY, herd: herdRoster(6).map((l) => l.id) }).filter((x) => x.kind === 'portrait').length, 0);
});

test('the sheet is stable: the same save gives the same sheet, newest first', () => {
  const life = lifeWith(plain.slice(0, 30));
  const a = sideStickers({ life, ed: FULL_READY }).map((x) => x.id), b = sideStickers({ life, ed: FULL_READY }).map((x) => x.id);
  assert.deepEqual(a, b);
  assert.ok(life.stickers[a[0]] >= life.stickers[a.at(-1)]);
});

test('the small edition never shows a sticker its book does not show', () => {
  const side = sideStickers({ life: lifeWith(plain.slice(0, 60)), ed: SMALL });
  const shown = new Set(inBookIds(SMALL));
  for (const x of side) assert.ok(shown.has(x.id), x.id);
});
import { shown as inBookShown } from '../sim/stickers.js';
function inBookIds(ed) { return inBookShown({}, ed).map((s) => s.id); }
