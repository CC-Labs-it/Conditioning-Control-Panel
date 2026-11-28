/* ui/repeat-badge.js - one small sticker on an icon that is sold more than once (owner 2026-11-08: "Add 1-2-3 etc, levels to
 * the same power ups, otherwise it reads as the same icon. And a + next to another unit of the thing").
 * Three families share a parent's icon today:
 *   plus  - another unit of the same thing: a repeat buy of a growing item (the Bouncing Button, up to 12 copies).
 *   level - a Journey rank row (rank_<parent>_<n>): the parent pays more. It buys level rank + 1, the same number the row's
 *           name already gives ("Bouncing Button · Rank 2" shows 2).
 *   pick  - a Journey specialty (spec_*): pick one of two upgrades for the parent.
 * The shop tile, its hover card and the guide map all read this one module, so they always agree. Pure data first (node
 * tests in tests/repeat-badge.test.mjs), then a small DOM painter with its own injected style block. Prices, effects,
 * ids and save keys are never touched here. */

import { BY_ID } from '../data/items.js';
import { brandText } from '../data/brand.js';

const nameOf = (id) => brandText(BY_ID[id]?.name || id);
const owned = (run, id) => (run?.owned?.[id] || 0);

/** What kind of repeat a sold row is, from the item alone: 'plus' | 'level' | 'pick' | null. */
export function repeatKind(item) {
  if (!item) return null;
  if (item.rank) return 'level';
  if (item.options && item.iconId) return 'pick';
  if (item.grow) return 'plus';
  return null;
}

/** The sticker for a shop offer: { kind, n, text, label } or null. A first copy of a growing item gets none. */
export function offerBadge(item, run) {
  const kind = repeatKind(item);
  if (kind === 'level') {
    const n = item.rank + 1;
    return { kind, n, text: String(n), label: `Level ${n} of ${nameOf(item.iconId)}` };
  }
  if (kind === 'pick') return { kind, n: 0, text: 'or', label: `Pick one upgrade for ${nameOf(item.iconId)}` };
  if (kind === 'plus') {
    const have = owned(run, item.id);
    if (have < 1) return null;
    return { kind, n: have + 1, text: '+', label: `One more ${nameOf(item.id)}. You have ${have}.` };
  }
  return null;
}

/** The stickers for a map node: what the run already has of that thing. A level from 2 up, a count from 2 copies up. */
export function standingBadges(item, run) {
  if (!item) return [];
  const out = [];
  const rank = run?.store?.journey?.ranks?.[item.id] || 0;
  if (rank >= 1) out.push({ kind: 'level', n: rank + 1, text: String(rank + 1), label: `Level ${rank + 1}` });
  const have = item.grow ? owned(run, item.id) : 0;
  if (have >= 2) out.push({ kind: 'plus', n: have, text: `x${have}`, label: `You have ${have}` });
  return out;
}

/* ---- DOM ---- */

const CSS = `
.rep-ico { position: relative; display: inline-flex; flex: none; line-height: 0; }
.rep-badge { position: absolute; z-index: 3; box-sizing: border-box; min-width: 21px; height: 21px; padding: 0 4px;
  display: grid; place-items: center; font: 800 13px/1 var(--shop-font, inherit); font-variant-numeric: tabular-nums; letter-spacing: 0;
  color: #2a2031; background: color-mix(in srgb, var(--accent, #ff6fb5) 55%, #fffaf0); border: 2px solid #2a2031;
  border-radius: 10px 8px 11px 7px / 8px 11px 7px 10px; pointer-events: none; white-space: nowrap; }
.rep-badge.plus { background: color-mix(in srgb, var(--accent-2, #8fd0ff) 45%, #fffaf0); font-size: 16px; }
.rep-badge.pick { font-size: 10px; }
.rep-ico > .rep-badge { right: -8px; bottom: -6px; }
.rep-ico > .rep-badge.second { right: auto; left: -7px; }
.row.short .rep-badge { opacity: .85; }
.row .rep { display: block; font-style: normal; font-weight: 700; font-size: 13px; line-height: 1.2; color: var(--ink); }
.row .rep:empty { display: none; }
.gr .tk > .rep-badge { right: -8px; bottom: -8px; min-width: 24px; height: 24px; font-size: 14px; }
.gr .tk > .rep-badge.plus { font-size: 13px; }
.gr .tk > .rep-badge.second { right: auto; left: -8px; }
@media (max-width: 759px) { .rep-badge { min-width: 19px; height: 19px; font-size: 12px; } .rep-badge.plus { font-size: 15px; } }
`;

const styled = new WeakSet();
export function repeatBadgeCss(doc) {
  if (styled.has(doc)) return;
  styled.add(doc);
  const s = doc.createElement('style');
  s.id = 'repeat-badge-css';
  s.textContent = CSS;
  doc.head.appendChild(s);
}

/** Puts the stickers (a badge, a list of them, or nothing) on host; only touches the DOM when something changed. */
export function paintBadges(host, badges) {
  if (!host) return;
  const list = (Array.isArray(badges) ? badges : [badges]).filter(Boolean).slice(0, 2);
  const key = list.map((b) => b.kind + ':' + b.text).join('|');
  if (host.dataset.rep === key || (!key && host.dataset.rep === undefined)) return;
  host.dataset.rep = key;
  for (const old of [...host.children]) if (old.classList.contains('rep-badge')) old.remove();
  const doc = host.ownerDocument;
  repeatBadgeCss(doc);
  list.forEach((b, i) => {
    const el = doc.createElement('span');
    el.className = `rep-badge ${b.kind}${i ? ' second' : ''}`;
    el.setAttribute('aria-hidden', 'true');
    el.textContent = b.text;
    host.appendChild(el);
  });
}
