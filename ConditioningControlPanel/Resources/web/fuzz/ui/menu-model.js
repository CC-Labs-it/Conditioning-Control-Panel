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
