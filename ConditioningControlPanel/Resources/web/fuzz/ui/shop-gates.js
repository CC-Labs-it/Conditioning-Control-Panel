/* ui/shop-gates.js - when each part of the Button Shop and the Cursor Shop opens (owner 2026-11-08). A shop part waits
 * for a power-up the player already earns on the map: its parent feature first, the rest spread over the trunk so the
 * shops keep getting something new. Pure: reads the run, writes only run.store.shopSeen through markSeen/noteTold.
 * A save that already owns or wears something in a part keeps that part open (grandfathered). */

import { BY_ID } from '../data/items.js';

/* gate: owning ANY of these opens the part. The last id is the one named on locked rows (the route-neutral one).
 * quiet: the part opens with the shop door itself, so it gets the door's NEW tag but no toast. */
export const SHOP_GATES = Object.freeze({
  styles: { shop: 'button', tab: 'Styles', gate: ['bubble'], name: 'Styles', quiet: true },
  emi: { shop: 'button', tab: 'Styles', gate: ['f5'], name: 'EMI style' },
  wave: { shop: 'button', tab: 'Wave', gate: ['a_hum', 'f3'], name: 'Wave ribbons' },
  kinetic: { shop: 'button', tab: 'Wave', gate: ['glossy'], name: 'Kinetic ribbons' },
  copies: { shop: 'button', tab: 'Bouncing copies', gate: ['depth'], name: 'Bouncing copies' },
  cursors: { shop: 'cursor', tab: 'Cursors', gate: ['cursor'], name: 'Cursors', quiet: true },
  nova: { shop: 'cursor', tab: 'Cursors', gate: ['f2'], name: 'Nova Cursor' },
  families: { shop: 'cursor', tab: 'Trails', gate: ['starburst'], name: 'Trail families' },
  words: { shop: 'cursor', tab: 'Words', gate: ['word_comet'], name: 'Trail words' },
});

const KINETIC = new Set(['chroma', 'siri9', 'harp', 'pyramid']);

/** Which part a shop row belongs to, or null when the row has no gate of its own (its item req or the door covers it). */
export function gateOf(kind, id) {
  if (kind === 'style') return id === 'emi' ? 'emi' : null;
  if (kind === 'wave') return id === 'classic' ? null : KINETIC.has(id) ? 'kinetic' : 'wave';
  if (kind === 'cursor') return id === 'cursor_nova' ? 'nova' : null;
  if (kind === 'family') return 'families';
  if (kind === 'words') return 'words';
  if (kind === 'copies') return 'copies';
  return null;
}

const owns = (run, id) => (run?.owned?.[id] || 0) > 0 || Boolean(run?.picks?.[id]);
const bubble = (run) => run?.store?.bubble || {};
const kit = (run) => run?.store?.cursorKit || {};
const anyTrue = (o, keys) => keys.some((k) => o?.[k] === true);

/** A save already owns or wears something in this part: it stays open whatever the gate says. */
export function grandfathered(run, key) {
  const b = bubble(run), k = kit(run);
  switch (key) {
    case 'emi': return b.faces?.emi === true;
    case 'wave': return Object.entries(b.waveStyles || {}).some(([id, v]) => v === true && id !== 'classic' && !KINETIC.has(id));
    case 'kinetic': return anyTrue(b.waveStyles, [...KINETIC]);
    case 'copies': return Object.values(b.copyOwned || {}).some(Boolean)
      || ![undefined, null, 'auto'].includes(b.bouncer) || ![undefined, null, 'normal'].includes(b.copySize) || ![undefined, null, 'mix'].includes(b.emiPose);
    case 'nova': return k.skins?.cursor_nova === true;
    case 'families': return Object.values(k.stages || {}).some((n) => n > 0);
    case 'words': return k.wordEditor === true;
    default: return false;
  }
}

/** The part is open: its gate is owned, or the save already had something in it. */
export function gateOpen(run, key) {
  const g = SHOP_GATES[key];
  if (!g) return true;
  return g.gate.some((id) => owns(run, id)) || grandfathered(run, key);
}

/** A row is usable when its part is open, or the row itself is already owned. */
export function rowOpen(run, kind, id, owned = false) {
  const key = gateOf(kind, id);
  return owned || !key || gateOpen(run, key);
}

/** The one plain line a locked row shows. */
export function gateLine(key) {
  const g = SHOP_GATES[key];
  const id = g?.gate[g.gate.length - 1];
  return 'Unlocks with ' + (BY_ID[id]?.name || id);
}

/* ---- NEW: a part that opened since the player last looked inside its shop ----
 * rec is the one writable record, kept in the Bubble Button's store as run.store.bubble.shopSeen = { seen, told }. */
function seenBag(run, rec) {
  /* First look at a save from before the gates: everything open now counts as seen, so nothing floods. */
  if (!rec.seen) { rec.seen = Object.fromEntries(Object.keys(SHOP_GATES).filter((k) => gateOpen(run, k)).map((k) => [k, true])); rec.told = { ...rec.seen }; }
  rec.told ??= {};
  return rec;
}

/** Open parts of one shop the player has not yet seen inside it. */
export function freshParts(run, shop, rec) {
  const bag = seenBag(run, rec);
  return Object.keys(SHOP_GATES).filter((k) => SHOP_GATES[k].shop === shop && gateOpen(run, k) && bag.seen[k] !== true);
}

/** Opening a shop meets every fresh part in it. Returns the parts that were fresh, for the in-shop NEW marks. */
export function markSeen(run, shop, rec) {
  const fresh = freshParts(run, shop, rec);
  for (const k of fresh) { rec.seen[k] = true; rec.told[k] = true; }
  return fresh;
}

/** Fresh parts that still owe their one toast; marks them told. Quiet parts are told without a toast. */
export function takeToasts(run, shop, rec) {
  const out = [];
  for (const k of freshParts(run, shop, rec)) {
    if (rec.told[k]) continue;
    rec.told[k] = true;
    if (!SHOP_GATES[k].quiet) out.push(k);
  }
  return out;
}

/** The writable record from a run (engine side; unlock code goes through ctx.store). */
export function seenRecord(run) {
  const b = (run.store ??= {}).bubble ??= {};
  return (b.shopSeen ??= {});
}

/** "New in the Cursor Shop: Trail words" */
export function toastLine(shop, keys) {
  return 'New in the ' + (shop === 'cursor' ? 'Cursor Shop' : 'Button Shop') + ': ' + keys.map((k) => SHOP_GATES[k].name).join(', ');
}

/* The door's NEW state copies the dock's NEW tag (cream paper tag, a sweep, an icon hop); Gentle and reduced motion keep colour only.
 * In-shop rows and tabs wear the small tag alone. */
export const SHOP_NEW_CSS = `
#fz-tools .fz-door[data-new] { border-color:var(--accent); overflow:visible; }
#fz-tools .fz-door .fz-door-new, .shop-new { position:absolute; top:-5px; left:-5px; z-index:2; padding:1px 3px 0; font:700 8.5px/1.1 "Segoe UI",Tahoma,sans-serif;
  letter-spacing:.05em; color:#2b2230; background:#fffbe9; border:1px solid #2b2230; border-radius:2px 3px 2px 3px; transform:rotate(-9deg); pointer-events:none; }
#fz-tools .fz-door:not([data-new]) .fz-door-new { display:none; }
#fz-tools .fz-door[data-new]::before { content:""; position:absolute; inset:0; border-radius:inherit; pointer-events:none;
  background:linear-gradient(105deg,transparent 32%,rgba(255,255,255,.75) 47%,color-mix(in srgb,var(--accent-2) 35%,transparent) 53%,transparent 68%) no-repeat;
  background-size:300% 100%; background-position:120% 0; animation:fz-door-shine 4.6s ease-in-out infinite; }
#fz-tools .fz-door[data-new] svg.ico { animation:fz-door-hop 4.6s ease-in-out infinite; }
@keyframes fz-door-shine { 0%, 60% { background-position:120% 0; } 84%, 100% { background-position:-20% 0; } }
@keyframes fz-door-hop { 0%, 66% { transform:none; } 71% { transform:translateY(-4px) rotate(-7deg); } 77% { transform:scale(1.08,.92); } 82% { transform:translateY(-1.5px) rotate(3deg); } 87%, 100% { transform:none; } }
body.gentle #fz-tools .fz-door[data-new]::before { animation:none; background:none; }
body.gentle #fz-tools .fz-door[data-new] svg.ico { animation:none; }
@media (prefers-reduced-motion: reduce) { #fz-tools .fz-door[data-new]::before { animation:none; background:none; } #fz-tools .fz-door[data-new] svg.ico { animation:none; } }
.cursor-kit-tabs button, .bshop-card, .bshop-choices button, .cursor-kit-row, .cursor-family-tag { position:relative; }
.shop-new { top:2px; left:2px; }
.cursor-kit-tabs .shop-new { top:-9px; left:-8px; }
.shop-gate { margin:18px 0 6px; color:var(--quiet,#6e626a); font:600 15px/1.4 "Segoe UI",sans-serif; }
`;

/** A small NEW tag on a tab or row inside a shop. */
export function newMark(el) {
  if (!el || el.querySelector(':scope > .shop-new')) return;
  const t = el.ownerDocument.createElement('span');
  t.className = 'shop-new';
  t.textContent = 'NEW';
  t.setAttribute('aria-hidden', 'true');
  el.append(t);
}
