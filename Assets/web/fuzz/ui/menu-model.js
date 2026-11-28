/* ui/menu-model.js - the main menu's rules (lane M, owner 2026-11-22, mockup approved the same night). No DOM here:
 * which rows, in which order, in which state, with which counters. ui/main-menu.js draws what this answers.
 *
 *   When: from the second launch on. "Launched before" is life.contract (signed the contract once, sim/save.js): the game
 *   keeps no launch counter, and a player who declined the first contract still lands in it, exactly as today. Never on a
 *   dev door, a New run reload, an open or sleeping Sky visit.
 *   Rows: Continue (a live run only), New run, Sticker Book, Endings, Renewal (full edition, once Depth > 0), Options,
 *   Account (the host keeps a save slot), the Expo row.
 *   Expo row: sealed `?` until the llama ending (lifetime, life.endings.ll_end); then `ready` (the files are here, full),
 *   `wait` (full, the files are not on this PC) or `door` (small edition).
 *
 * COPY: APPROVED AND FROZEN (owner 2026-11-22, the menu dialog), pinned in tests/main-menu.test.mjs. The rows that the
 * game already had reuse its own strings (imported, never retyped). */

import { fmt } from '../sim/format.js';
import { brandText } from '../data/brand.js';
import { galleryIds, runEdition, setRunEdition } from '../sim/economy.js';
import { ENDINGS } from '../data/content/endings.js';
import { shown as inBook } from '../sim/stickers.js';
import { renewalAllowed, saveSlotOn, expoAllowed, isSmall } from './edition.js';
import { SKY_ASLEEP_COPY } from './save-sleep.js';          // 'New run'
import { TRANSMISSION_LINES } from './transmission.js';     // 'TRANSMISSION WAITING'
import { LOCKED_TILE_COPY } from './shop-locked.js';        // 'More in the full game'
import { RENEWAL_LABELS } from './renewal-menu.js';         // 'Renewal'

/** The menu's own words (approved tonight). `{n}`, `{got}`, `{total}` are numbers. */
export const MENU_COPY = Object.freeze({
  continue: 'Continue',
  endings: 'Endings',
  options: 'Options',
  account: 'Account',
  sealed: '?',
  fuzz: '{n} {cur}',   // {cur}: the currency name, data/brand.js
  endingsCount: '{got} of {total}',
  depth: 'Depth {n}',
});

/** The game's own strings the menu reuses. `expo` is the ending's name (data/content/endings.js ll_end); `playExpo` is
 *  the label the Expo's own drives press (dev/play-ending.html), the owner's mockup line. */
export const MENU_REUSED = Object.freeze({
  newRun: SKY_ASLEEP_COPY.newRun,
  newRunSure: 'Sure. Start over.',   // the pause card's second tap (ui/app.js #new-run)
  stickerBook: 'Sticker Book',   // ui/sticker-book.js door and heading (not exported there)
  renewal: RENEWAL_LABELS.door,
  expo: ENDINGS.ll_end.title,
  playExpo: 'Play Llama Expo 2037',   // a literal (owner's mockup line): a renamed ending never renames the row
  waiting: TRANSMISSION_LINES.penSign,
  moreInFull: LOCKED_TILE_COPY.label,
});

/** The menu's sounds, each [files, gain]: files the game already ships, and more than one file means one picked at
 *  random each time. A pressed row is the classic bubble pop (owner 2026-11-22), the same three files the game's own
 *  bubbles use (ui/unlocks/basics/look.js wallpop), whether the row opens a card (`press`) or leaves the menu (`burst`). */
const BUBBLE_POPS = Object.freeze(['assets/audio/pop/Pop.mp3', 'assets/audio/pop/Pop2.mp3', 'assets/audio/pop/Pop3.mp3']);
export const MENU_SOUNDS = Object.freeze({
  land: [['assets/audio/pop/Pop.mp3'], 0.35],
  tick: [['assets/audio/sfx/tally_tick.mp3'], 0.4],
  press: [BUBBLE_POPS, 0.55],
  burst: [BUBBLE_POPS, 0.55],
  denied: [['assets/audio/sfx/ui_denied.mp3'], 0.5],
});
/** The file a sound plays this time, or null for a name the menu does not have. `rnd` is passed in by tests. */
export function menuSoundFile(name, rnd = Math.random) {
  const files = MENU_SOUNDS[name]?.[0];
  return files ? files[Math.min(files.length - 1, Math.floor(rnd() * files.length))] : null;
}

const fill = (tpl, vals) => tpl.replace(/\{(\w+)\}/g, (m, k) => (k in vals ? String(vals[k]) : m));

/** Whether this boot shows the menu. Pure. */
export function menuDue({ life, devDoor = false, newRunReload = false, skyVisit = false, skySleeps = false, skyDemo = false } = {}) {
  if (devDoor || newRunReload || skyVisit || skySleeps || skyDemo) return false;
  return !!life?.contract;
}

/** The llama ending has been reached, ever (the ad card reads it the same way, ui/adcard/model.js chair). */
export const llamaReached = (life) => (Number(life?.endings?.ll_end) || 0) > 0;

/** Which Expo row: 'sealed' | 'ready' | 'wait' | 'door'. */
export function expoKind(life, ed) {
  if (!llamaReached(life)) return 'sealed';
  if (isSmall(ed)) return 'door';
  return expoAllowed(ed) ? 'ready' : 'wait';
}

/** Endings found and on offer, counted the way the end card counts (sim/economy.js galleryIds). */
export function endingsCount(life, run, ed = null) {
  const got = new Set(Object.keys(life?.endings || {}).filter((id) => (Number(life.endings[id]) || 0) > 0));
  const s = run && runEdition(run) ? run : setRunEdition({}, ed);   // no saved run: this edition still decides what is on offer
  return { got: got.size, total: galleryIds(Object.keys(ENDINGS), got, s).length };
}

/** Stickers earned and shown in this edition's book. */
export const stickersEarned = (life, ed) => inBook(life, ed).filter((s) => life?.stickers?.[s.id]).length;

/** The rows, in order: [{ id, label, meta?, tag?, kind?, aria? }]. `run` is the saved run or null. Pure. */
export function menuRows({ life = {}, run = null, ed }) {
  const rows = [];
  if (run && !run.ended) rows.push({ id: 'continue', label: MENU_COPY.continue, meta: fill(brandText(MENU_COPY.fuzz), { n: fmt(Math.floor(run.fuzz || 0)) }) });
  rows.push({ id: 'new', label: MENU_REUSED.newRun });
  rows.push({ id: 'stickers', label: MENU_REUSED.stickerBook, meta: String(stickersEarned(life, ed)) });
  rows.push({ id: 'endings', label: MENU_COPY.endings, meta: fill(MENU_COPY.endingsCount, endingsCount(life, run, ed)) });
  const depth = Number(life?.prestige?.depth) || 0;
  if (renewalAllowed(ed) && depth > 0) rows.push({ id: 'renewal', label: MENU_REUSED.renewal, meta: fill(MENU_COPY.depth, { n: depth }) });
  rows.push({ id: 'options', label: MENU_COPY.options });
  if (saveSlotOn(ed)) rows.push({ id: 'account', label: MENU_COPY.account });
  const kind = expoKind(life, ed);
  if (kind === 'sealed') rows.push({ id: 'expo', kind, label: '', aria: MENU_COPY.sealed });
  else if (kind === 'ready') rows.push({ id: 'expo', kind, label: MENU_REUSED.playExpo });
  else if (kind === 'wait') rows.push({ id: 'expo', kind, label: MENU_REUSED.expo, tag: MENU_REUSED.waiting });
  else rows.push({ id: 'expo', kind, label: MENU_REUSED.expo, tag: MENU_REUSED.moreInFull });
  return rows;
}

/** Arrow keys: the next row index, wrapping. Home/End jump. Anything else: null (Escape never acts here). */
export function menuKey(key, sel, n) {
  if (!n) return null;
  if (key === 'ArrowDown' || key === 'ArrowRight') return (sel + 1) % n;
  if (key === 'ArrowUp' || key === 'ArrowLeft') return (sel - 1 + n) % n;
  if (key === 'Home') return 0;
  if (key === 'End') return n - 1;
  return null;
}

/* ---- the collection either side (slice M3, the owner's "waaay too much neg space") ----
 * Places from the approved mockup (C:/wt-tce/notes/menu-mockup/src.html SIDE): x, y = centre in percent of the screen,
 * s = size in px, r = tilt in degrees, m = [x, y] where it tucks at phone width (only those eight show there).
 * SPOTS fill in order (the mockup's early places first); PORTRAIT_SPOTS take the herd once the llama ending is reached. */
export const SPOTS = Object.freeze([
  { x: 21, y: 13, s: 84, r: 6, m: [3, 38] }, { x: 79, y: 14, s: 86, r: -6, m: [97, 46] }, { x: 5, y: 55, s: 84, r: -12, m: [3, 56] },
  { x: 8, y: 16, s: 100, r: -8, m: [3, 92] }, { x: 14, y: 36, s: 110, r: 4 }, { x: 13, y: 74, s: 106, r: -5, m: [3, 74] },
  { x: 5, y: 90, s: 78, r: 7 }, { x: 87, y: 35, s: 104, r: -4, m: [97, 64] }, { x: 95, y: 56, s: 82, r: 11 },
  { x: 77, y: 54, s: 90, r: -9 }, { x: 24, y: 54, s: 90, r: 9 }, { x: 92, y: 15, s: 96, r: 7 },
  { x: 87, y: 75, s: 104, r: 5, m: [97, 82] }, { x: 74, y: 81, s: 78, r: -11 }, { x: 26, y: 80, s: 76, r: 12 },
  { x: 18, y: 93, s: 70, r: -9 }, { x: 95, y: 91, s: 78, r: -6 }, { x: 82, y: 94, s: 70, r: 8 },
].map(Object.freeze));
export const PORTRAIT_SPOTS = Object.freeze([
  { x: 27, y: 31, s: 72, r: -7 }, { x: 72, y: 33, s: 70, r: 6 }, { x: 4, y: 73, s: 64, r: -5 }, { x: 96, y: 74, s: 64, r: 8, m: [97, 97] },
].map(Object.freeze));
/** Dashed slots for the next stickers while there is room: the mockup's 7 at the start, 4 mid-way, none for a veteran. */
export const slotsFor = (earned) => (earned < 6 ? 7 : earned < 14 ? 4 : 0);
const LLAMA_STICKER = 'e_ll_end';
/* Art the menu can draw at once: the Golden Button stars are drawn by code the book loads on first open, so they stay
 * in the book. */
const drawable = (s) => s.art?.k !== 'gold';

/**
 * Which stickers go on the sheet, where. Pure. `herd`: herd ids in pen order (data/content/llamas.js herdRoster).
 * Earned stickers this edition's book shows, newest first (by the run that earned them, then the book's order, so the
 * sheet only changes when something new is earned); the Llama Expo 2037 sticker first once reached. Then dashed slots
 * for the next not-yet stickers (never a secret one). Answers [{ kind: 'got'|'slot'|'portrait', id, spot, side }].
 */
export function sideStickers({ life = {}, ed, herd = [] } = {}) {
  const book = inBook(life, ed).filter(drawable);
  const got = book.filter((s) => life?.stickers?.[s.id]);
  const order = new Map(book.map((s, i) => [s.id, i]));
  got.sort((a, b) => (a.id === LLAMA_STICKER ? -1 : b.id === LLAMA_STICKER ? 1 : 0)
    || (life.stickers[b.id] - life.stickers[a.id]) || (order.get(a.id) - order.get(b.id)));
  const out = [];
  const side = (spot) => (spot.x < 50 ? 'lf' : 'rt');
  const take = got.slice(0, SPOTS.length);
  take.forEach((s, i) => out.push({ kind: 'got', id: s.id, spot: SPOTS[i], side: side(SPOTS[i]) }));
  const room = SPOTS.length - take.length;
  const next = book.filter((s) => !life?.stickers?.[s.id] && !s.hidden).slice(0, Math.min(room, slotsFor(got.length)));
  next.forEach((s, i) => { const spot = SPOTS[take.length + i]; out.push({ kind: 'slot', id: s.id, spot, side: side(spot) }); });
  if (llamaReached(life)) herd.slice(0, PORTRAIT_SPOTS.length).forEach((id, i) => out.push({ kind: 'portrait', id, spot: PORTRAIT_SPOTS[i], side: side(PORTRAIT_SPOTS[i]) }));
  return out;
}

/** The sheet's scale (slice M4): the mockup's stage is 1040 x 620; a bigger window grows the logo, the rows and the
 *  collection with it, never below today's size (1) and never past 1.6, where a row's text stops reading as a sticker. */
export const SHEET_W = 1040, SHEET_H = 620, SHEET_MIN = 1, SHEET_MAX = 1.6;
export function sheetScale(w, h) {
  const k = Math.min(Number(w) / SHEET_W, Number(h) / SHEET_H);
  return Number.isFinite(k) ? Math.round(Math.min(SHEET_MAX, Math.max(SHEET_MIN, k)) * 100) / 100 : SHEET_MIN;
}
