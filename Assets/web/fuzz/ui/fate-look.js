/* ui/fate-look.js - the fate card look in the shop, its hover card and the guide map (owner 2026-11-10,
 * handoff-1110/TC-FATE-CARDS-BRIEF.md). A fate card (an item with `fate`: the four cards of a set and its ending) is the same
 * on all eight theme pages: a black notched card, a white frame, a dashed inner frame; the ending adds a red END seal.
 * Never the theme fill and never the gold "can afford" fill: a fate card that can be bought brightens its frame, turns its
 * price white and lifts a little. Before a set is picked the seeds sit together on one shelf with a one-line caption; when a
 * seed is bought the others take a red NOT THIS RUN stamp and leave. State always comes from the sim (`fateOf`), so a reload
 * mid-stamp just shows the shop without them.
 * Pure helpers first (node tests in tests/fate-look.test.mjs), then the style block the shop and the tip share. */

import { isSeed } from '../data/fate.js';

/** The shelf caption over the seeds. Plain, dry, no spoiler of which ending. */
export const FATE_CAPTION = 'Pick one. It decides how this ends.';
/** The stamp on a seed that was not picked. */
export const FATE_STAMP = 'NOT THIS RUN';
/** How long a stamped seed stays before it leaves (ms), and the stagger between seeds. */
export const STAMP_MS = 1000, STAMP_STAGGER_MS = 90;

/** True for a row that wears the fate look: a card of a set or its ending. */
export const isFate = (item) => Boolean(item?.fate);

/** The extra classes a shop tile (or guide token) of this item wears. Empty for any other row. */
export function fateClasses(item) {
  if (!isFate(item)) return [];
  const out = ['fate'];
  if (item.kind === 'end') out.push('fate-end');
  if (item.step === 1) out.push('fate-seed');
  return out;
}

/**
 * Split a shop window for the seed shelf. Before a pick every seed on offer (from `pool`, the whole offer, so a small window
 * cannot cut one off) leaves the goal slot and the rows and sits on the shelf, in a fixed order. After a pick the shelf is
 * empty and the window is untouched.
 * @param {{goal, rows, more}} win the window the shop shows
 * @param {string|null} picked the run's fate set (sim/economy.js fateOf)
 * @param {{goal, rows}} [pool] the whole offer; defaults to `win`
 * @param {string[]} [order] seed order on the shelf (data/fate.js SEEDS)
 * @returns {{seeds: object[], goal, rows: object[], more: number}}
 */
export function fateShelf(win, picked, pool = win, order = null) {
  if (picked) return { seeds: [], goal: win.goal, rows: win.rows, more: win.more };
  const all = [pool.goal, ...(pool.rows || [])].filter((it) => it && isSeed(it.id));
  const seen = new Set(), seeds = [];
  for (const it of all) if (!seen.has(it.id)) { seen.add(it.id); seeds.push(it); }
  if (order) seeds.sort((a, b) => order.indexOf(a.id) - order.indexOf(b.id));
  if (!seeds.length) return { seeds, goal: win.goal, rows: win.rows, more: win.more };
  const inWin = [win.goal, ...win.rows].filter((it) => it && seen.has(it.id)).length;
  return {
    seeds,
    goal: win.goal && seen.has(win.goal.id) ? null : win.goal,
    rows: win.rows.filter((it) => !seen.has(it.id)),
    more: Math.max(0, (win.more || 0) - (seeds.length - inWin)),
  };
}

/**
 * Which seed tiles still on the shelf take the stamp now: the ones no longer offered because a set was picked, except the
 * seed that picked it (that one was bought and leaves like any bought tile). Nothing is stamped without a pick.
 * @param {string[]} shown seed ids on the shelf before this draw
 * @param {string[]} offered seed ids offered now
 * @param {string|null} pickedSeed the bought seed (the picked set's card 1), or null
 */
export function stampedSeeds(shown, offered, pickedSeed) {
  if (!pickedSeed) return [];
  const keep = new Set(offered);
  return shown.filter((id) => id !== pickedSeed && !keep.has(id));
}

/* The notched card. The tile itself is not clipped (its badges and the guide's tick sit outside it); the card is ::before. */
const NOTCH = (n) => `polygon(${n}px 0,calc(100% - ${n}px) 0,100% ${n}px,100% calc(100% - ${n}px),calc(100% - ${n}px) 100%,${n}px 100%,0 calc(100% - ${n}px),0 ${n}px)`;
export const FATE_CARD = '#0b0b0d', FATE_CHALK = '#ffffff', FATE_RED = '#d2281e';

/** Shop tiles, the shelf and the hover card. Every colour is fixed: the same card on every theme page. */
export const FATE_CSS = `
#fate-shelf { flex: 0 0 100%; display: flex; flex-direction: column; gap: 6px; min-width: 0; pointer-events: none; }
#fate-shelf[hidden] { display: none; }
#fate-shelf .fs-cap { margin: 0; font-size: 14px; font-weight: 700; line-height: 1.2; color: var(--ink); text-shadow: 0 0 3px var(--page), 0 0 6px var(--page); }
#fate-shelf .fs-cards { display: flex; flex-wrap: wrap; gap: 12px; }
#fate-shelf .fs-cards .row { flex: none; width: 100px; }
#fate-shelf.fs-leaving .fs-cap { opacity: 0; transition: opacity .4s ease-in; }

#shop .row.fate { --fc: ${FATE_CARD}; --fk: ${FATE_CHALK}; --frame: color-mix(in srgb, var(--fk) 62%, transparent);
  background: transparent !important; color: var(--fk) !important; border: 0 !important; border-radius: 0 !important;
  box-shadow: none !important; outline: none !important; overflow: visible; isolation: isolate;
  transition: translate .15s ease-out, rotate .15s ease-out; }
#shop .row.fate::before { content: ''; position: absolute; inset: 0; z-index: -1; background: var(--fc);
  clip-path: ${NOTCH(10)}; outline: 2px solid var(--frame); outline-offset: -7px; transition: outline-color .2s; }
#shop .row.fate::after { content: ''; position: absolute; inset: 11px; z-index: 0; right: 11px; top: 11px; width: auto; height: auto;
  border: 1px dashed var(--fk); border-radius: 0; background: none; opacity: .32; pointer-events: none; transition: opacity .2s; }
#shop .row.fate .bar { inset: 15px auto 15px 6px; width: 2px; height: auto; background: var(--fk) !important; opacity: .55 !important; }
#shop .row.fate .ico { opacity: 1; filter: drop-shadow(1px 0 0 var(--fk)) drop-shadow(-1px 0 0 var(--fk)) drop-shadow(0 1px 0 var(--fk)) drop-shadow(0 -1px 0 var(--fk)); }
#shop .row.fate.short .ico { opacity: .62; }
#shop .row.fate .rep-ico { z-index: 1; }
#shop .row.fate .cost { z-index: 1; color: var(--fk); font-weight: 700; padding: 0 5px; background: none; box-shadow: none; border-radius: 0; }
#shop .row.fate.short .cost { color: color-mix(in srgb, var(--fk) 70%, transparent); }
#shop .row.fate .tag { z-index: 1; color: color-mix(in srgb, var(--fk) 80%, transparent) !important; background: none !important; padding: 0; text-transform: uppercase; font-size: 10px; font-weight: 900; letter-spacing: .07em; }
#shop .row.fate.buyable { --frame: var(--fk); translate: 0 -3px; color: var(--fk) !important; background: transparent !important; box-shadow: none !important; }
#shop .row.fate.buyable::after { opacity: .6; }
#shop .row.fate.buyable .ico { opacity: 1; filter: drop-shadow(1px 0 0 var(--fk)) drop-shadow(-1px 0 0 var(--fk)) drop-shadow(0 1px 0 var(--fk)) drop-shadow(0 -1px 0 var(--fk)); }
#shop .row.fate.buyable .cost { background: var(--fk); color: var(--fc); box-shadow: none; border-radius: 0; padding: 1px 7px; }
#shop .row.fate.buyable .tag { color: var(--fk) !important; background: none !important; }
#shop .row.fate:not(.short):hover, #shop .row.fate.peek { rotate: -1.5deg; outline: none; }
#shop #goal .row.fate.ready { animation: none; }
#shop .row.fate .tb-word { background: var(--fk) !important; color: var(--fk) !important; }
.row.fate .fate-seal { position: absolute; right: 6px; top: 6px; z-index: 2; width: 30px; height: 30px; border-radius: 50%;
  display: grid; place-items: center; background: ${FATE_RED}; color: #fff; font-size: 9px; font-weight: 900; letter-spacing: .05em;
  line-height: 1; rotate: 12deg; box-shadow: 0 0 0 2px ${FATE_CARD}; font-style: normal; }
#shop .row.fate-end .tag { display: none; }   /* the seal says it; the tip still carries the tag */
#goal .row.fate .fate-seal { width: 36px; height: 36px; font-size: 10px; right: 7px; top: 7px; }
.row.fate .fate-no { display: none; position: absolute; left: 50%; top: 50%; z-index: 3; translate: -50% -50%; rotate: -14deg;
  border: 2px solid ${FATE_RED}; color: ${FATE_RED}; background: #fff; font-size: 9px; font-weight: 900; letter-spacing: .05em;
  line-height: 1.1; padding: 2px 4px; white-space: nowrap; font-style: normal; pointer-events: none; }
#shop .row.fate.fate-out { pointer-events: none; }
#shop .row.fate.fate-out .fate-no { display: block; }

.row.tip.fate { --fk: ${FATE_CHALK}; background: ${FATE_CARD} !important; color: var(--fk) !important;
  border: 2px solid var(--fk) !important; border-radius: 0 !important; box-shadow: 3px 3px 0 rgba(0, 0, 0, .45) !important; font-family: var(--shop-font); }
.row.tip.fate::before { content: ''; position: absolute; inset: 4px; border: 1px dashed var(--fk); opacity: .4; pointer-events: none; }
.row.tip.fate::after { display: none; }
.row.tip.fate > * { position: relative; }
.row.tip.fate b { color: var(--fk) !important; }
.row.tip.fate small, .row.tip.fate .cost i, .row.tip.fate .tag, .row.tip.fate .rep, .row.tip.fate .also { color: color-mix(in srgb, var(--fk) 72%, transparent) !important; }
.row.tip.fate .tag { text-transform: uppercase; font-size: 10px; font-weight: 900; letter-spacing: .07em; }
.row.tip.fate .cost > span { color: var(--fk) !important; font-weight: 700; }
.row.tip.fate.tip-short .cost > span { color: #ff8a80 !important; }
.row.tip.fate .tb-word { background: var(--fk) !important; color: var(--fk) !important; }
.row.tip.fate .tip-buy { background: var(--fk); color: ${FATE_CARD}; border: 0; font-weight: 700; }
.row.tip.fate .tip-buy:disabled { background: color-mix(in srgb, var(--fk) 35%, transparent); color: ${FATE_CARD}; }
.row.tip.fate-end::after { display: grid; content: 'END'; position: absolute; right: -10px; top: -12px; width: 34px; height: 34px;
  border-radius: 50%; place-items: center; background: ${FATE_RED}; color: #fff; font-size: 9px; font-weight: 900;
  letter-spacing: .05em; rotate: 12deg; box-shadow: 0 0 0 2px ${FATE_CARD}; pointer-events: none; }
.row.tip.fate .tip-demo { position: relative; }

@media (max-width: 759px) {
  #fate-shelf .fs-cards .row { width: 80px; }
  #fate-shelf .fs-cap { font-size: 13px; }
  #shop .row.fate::after { inset: 9px; }
  #shop .row.fate .bar { inset: 12px auto 12px 5px; }
}
@media (prefers-reduced-motion: reduce) { #shop .row.fate:not(.short):hover, #shop .row.fate.peek, #shop .row.fate.buyable { rotate: none; translate: none; } }
body.gentle #shop .row.fate:not(.short):hover, body.gentle #shop .row.fate.peek, body.gentle #shop .row.fate.buyable { rotate: none; translate: none; }
`;

/** The guide map's fate tokens (ui/guide-map.js): the same card, the picked set lit, the others dimmed, unpicked seeds stamped. */
export const FATE_GUIDE_CSS = `
.gr .tk.fate { background: transparent; border: 0; border-radius: 0; box-shadow: none; isolation: isolate; }
.gr .tk.fate::before { content: ''; position: absolute; inset: 0; z-index: -1; background: ${FATE_CARD}; clip-path: ${NOTCH(9)};
  outline: 2px solid color-mix(in srgb, ${FATE_CHALK} 55%, transparent); outline-offset: -6px; }
.gr .tk.fate::after { content: ''; position: absolute; inset: 10px; z-index: 0; border: 1px dashed ${FATE_CHALK}; opacity: .3; pointer-events: none; }
.gr .tk.fate .ico { position: relative; z-index: 1; width: 42px; height: 42px; filter: drop-shadow(1px 0 0 #fff) drop-shadow(-1px 0 0 #fff) drop-shadow(0 1px 0 #fff) drop-shadow(0 -1px 0 #fff); }
.gr .tk.fate.end { width: 80px; height: 80px; margin: -40px 0 0 -40px; box-shadow: none; border-radius: 0; }
.gr .tk.fate.end .ico { width: 48px; height: 48px; }
.gr .tk.fate:hover, .gr .tk.fate.sel { box-shadow: none; }
.gr .tk.fate.own, .gr .tk.fate.offer, .gr .tk.fate.skip, .gr .tk.fate.later, .gr .tk.fate.off { background: transparent; border: 0; box-shadow: none; }
.gr .tk.fate.offer::before, .gr .tk.fate.own::before, .gr .tk.fate.fate-lit::before { outline-color: ${FATE_CHALK}; }
.gr .tk.fate.fate-lit::after { opacity: .65; }
.gr .tk.fate.fate-lit { filter: drop-shadow(0 0 6px rgba(255, 255, 255, .55)); }
.gr .tk.fate.off { opacity: .42; }
.gr .tk.fate.off .ico { filter: grayscale(1); opacity: .5; }
.gr .tk.fate .fate-seal { position: absolute; right: -6px; top: -8px; z-index: 3; width: 28px; height: 28px; border-radius: 50%;
  display: grid; place-items: center; background: ${FATE_RED}; color: #fff; font: 900 8px/1 system-ui, sans-serif; letter-spacing: .05em;
  rotate: 12deg; box-shadow: 0 0 0 2px ${FATE_CARD}; font-style: normal; pointer-events: none; }
.gr .tk.fate .fate-no { display: none; position: absolute; left: 50%; top: 50%; z-index: 4; translate: -50% -50%; rotate: -14deg;
  border: 2px solid ${FATE_RED}; color: ${FATE_RED}; background: #fff; font: 900 8px/1.1 system-ui, sans-serif; letter-spacing: .04em;
  padding: 2px 3px; white-space: nowrap; font-style: normal; pointer-events: none; }
.gr .tk.fate.fate-stamp .fate-no { display: block; }
.gr .tk.fate.fate-stamp { opacity: .7; }
`;

/** One style block per document, by id. */
export function fateCss(doc, id = 'fate-look-css', css = FATE_CSS) {
  if (doc.getElementById(id)) return;
  const st = doc.createElement('style');
  st.id = id;
  st.textContent = css;
  doc.head.appendChild(st);
}

/** The seal and the stamp spans a fate tile carries (both hidden by CSS until their class asks). */
export function fateMarks(doc, item) {
  const out = [];
  if (!isFate(item)) return out;
  if (item.kind === 'end') { const s = doc.createElement('i'); s.className = 'fate-seal'; s.setAttribute('aria-hidden', 'true'); s.textContent = 'END'; out.push(s); }
  if (item.step === 1) { const n = doc.createElement('i'); n.className = 'fate-no'; n.setAttribute('aria-hidden', 'true'); n.textContent = FATE_STAMP; out.push(n); }
  return out;
}
