/* data/folds.js - bundles. Plain stretches of one lane are sold together: the first row of a stretch (the lead) brings the
 * next ones (its riders) in the same purchase, so a run has about half the purchases and each one does more. Built from
 * data/items.js at load, so a regenerated catalog folds itself. Rows that each change the button, forks, endings, the
 * stacking button, hubs and the arcade toys always stay single: each of those is its own moment.
 * An old save that owns a lead without its riders sees each rider as a single row again, at its own price. */

import { ITEMS, BY_ID } from './items.js';
import { CONFIG } from './config.js';

/** Lanes that never fold: the main path, crossovers, exits, the free rows and the fate cards (each card is one step
 *  of the run's ending, owner 2026-11-10). */
const NEVER_LANES = new Set(['trunk', 'combo', 'exit', 'meta', 'after', 'fate']);
/** Rows that stay single wherever they are. Button skins: one tap shows one new button. Side hubs and toys: windows. */
const SINGLE = new Set([
  'rounded', 'bubble', 'bigbtn', 'tinybtn',
  'gold', 'screen', 'levels', 'boxes', 'wheel', 'guide', 'panel', 'lofi_radio',
  'starburst', 'word_comet', 'score_heat', 'dualwield', 'em_head', 'quiz', 'd_autoscroll',
  'go_tx', 'go_will', 'go_corp', 'go_hive',   // EMI Takeover: each row is one step of her story (owner 2026-11-10)
  'toy_box', 'toy', 'mini_breakout', 'mini_race', 'mini_deep', 'mini_slots', 'dispenser', 'dispenser_fast', 'dispenser_faster',
]);

const plain = (it) => it && it.group === 'run' && it.kind === 'item' && !it.grow && it.cost > 0
  && !NEVER_LANES.has(it.lane) && !SINGLE.has(it.id) && it.req.length === 1 && !it.any;

export function buildFolds(items = ITEMS, size = CONFIG.FOLD_SIZE) {
  const by = Object.fromEntries(items.map((i) => [i.id, i]));
  /* Explicit bundles (dev/gen-items.mjs INTAKE_STAGES, the three question stages): a row with `fold` brings exactly
   * those rows, and none of them folds any other way. */
  const fixed = Object.fromEntries(items.filter((i) => Array.isArray(i.fold)).map((i) => [i.id, i.fold.filter((r) => by[r])]));
  const pinned = new Set(Object.entries(fixed).flatMap(([l, r]) => [l, ...r]));
  const foldable = (it) => plain(it) && !pinned.has(it.id);
  const kids = {};
  // A separately sold optional child doesn't split its parent's existing bundle.
  for (const it of items) {
    if (SINGLE.has(it.id)) continue;
    for (const q of it.req) if (by[q] && by[q].lane === it.lane) (kids[q] ??= []).push(it.id);
  }
  /* x links to its parent when the parent is foldable in the same lane and x is that parent's only child there. */
  const linked = (it) => {
    const p = by[it.req[0]];
    return foldable(it) && foldable(p) && p.lane === it.lane && (kids[p.id] || []).length === 1;
  };
  const lead = {}, of = {};
  if (size < 2) return { FOLD: lead, LEAD_OF: of };
  for (const [l, riders] of Object.entries(fixed)) {
    if (!riders.length) continue;
    lead[l] = Object.freeze(riders);
    for (const r of riders) of[r] = l;
  }
  for (const head of items) {
    if (!foldable(head) || linked(head)) continue;
    const chain = [head.id];
    let cur = head;
    for (;;) {
      const k = kids[cur.id] || [];
      if (k.length !== 1 || !linked(by[k[0]])) break;
      cur = by[k[0]];
      chain.push(cur.id);
    }
    const groups = [];
    for (let i = 0; i < chain.length; i += size) groups.push(chain.slice(i, i + size));
    if (groups.length > 1 && groups[groups.length - 1].length === 1) groups[groups.length - 2].push(...groups.pop());
    for (const g of groups) {
      if (g.length < 2) continue;
      lead[g[0]] = Object.freeze(g.slice(1));
      for (const r of g.slice(1)) of[r] = g[0];
    }
  }
  return { FOLD: Object.freeze(lead), LEAD_OF: Object.freeze(of) };
}

const built = buildFolds();
/** lead id -> its rider ids, in purchase order. */
export const FOLD = built.FOLD;
/** rider id -> its lead id. */
export const LEAD_OF = built.LEAD_OF;
export const ridersOf = (id) => FOLD[id] || [];
export const isRider = (id) => !!LEAD_OF[id] && !!BY_ID[id];
