/* ============================================================================
 * sim/prestige-looks.js - the Renewal Sky's Looks shelf (brief handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md, wave 2 Finish).
 * After the core star (Fully Conditioned) Stardust buys looks the run shops already sell: Button Shop styles and Wave
 * ribbons (ui/button-shop.js, ui/wave-ribbon.js), Cursor Shop skins and trail families (ui/cursor-shop.js). A look bought
 * here is owned from the first second of every later run: applyLooks writes the same ownership flags the run shops write
 * (store.bubble.faces / waveStyles, store.cursorKit.skins / stages). It never equips anything and never lowers what a run
 * already owns. Pure: no DOM, no timers. Ownership lives in life.prestige.store.looks = { own: { [lookId]: 1 } }.
 *
 * Prices are the run shop's price, in Stardust (a trail family costs its three stages together). The ids and prices are
 * pinned here on purpose (the ui shops pull in the DOM); tests/prestige-looks.test.mjs checks them against the shops.
 * ==========================================================================*/

const L = (kind, key, name, cost, art = null) => Object.freeze({ id: `${kind}:${key}`, kind, key, name, cost, art });

export const LOOK_KINDS = Object.freeze([
  Object.freeze({ id: 'face', name: 'Button styles', icon: 'button' }),
  Object.freeze({ id: 'wave', name: 'Wave ribbons', icon: 'pulse' }),
  Object.freeze({ id: 'cursor', name: 'Cursors', icon: 'cursor' }),
  Object.freeze({ id: 'family', name: 'Trail families', icon: 'cursors' }),
]);

export const LOOKS = Object.freeze([
  L('face', 'human-check', 'Human Check', 1500),
  L('face', 'emi', 'EMI', 3000),
  L('wave', 'aurora', 'Velvet Aurora', 1200),
  L('wave', 'loom', 'Phase Loom', 2800),
  L('wave', 'mercury', 'Mercury Tide', 4800),
  L('wave', 'siri9', 'iOS 9 SiriWave', 6000),
  L('wave', 'mycelium', 'Neon Mycelium', 7500),
  L('wave', 'harp', 'Resonance Harp', 9000),
  L('wave', 'prism', 'Spectral Shear', 12000),
  L('wave', 'chroma', 'Chroma Cycle', 15000),
  L('wave', 'pyramid', 'Swimming Pyramid', 18000),
  L('cursor', 'cursor_prism', 'Prism Cursor', 3600),
  L('cursor', 'cursor_nova', 'Nova Cursor', 5400),
  ...[['aurora', 'Aurora Ribbon'], ['circuit', 'Neon Circuit'], ['ink', 'Ink Bloom'], ['atlas', 'Star Atlas'],
    ['fireflies', 'Firefly Swarm'], ['ripples', 'Ripple Glass'], ['phoenix', 'Phoenix Ash'], ['glasswing', 'Glasswing']]
    .map(([key, name]) => L('family', key, name, 30000, `assets/shop/trails/${key}-1.webp`)),
]);
export const LOOK_BY_ID = Object.freeze(Object.fromEntries(LOOKS.map((l) => [l.id, l])));
/** Stages a trail family has (data/trail-collection.js levels): a family look owns all of them. */
export const FAMILY_STAGES = 3;

/** One plain line per kind: what owning it means in a run. */
export const LOOK_LINES = Object.freeze({
  face: 'Owned in the Button Shop from the start of every run.',
  wave: 'Owned in the Button Shop from the start of every run.',
  cursor: 'Owned in the Cursor Shop from the start of every run.',
  family: 'All three stages, owned in the Cursor Shop from the start of every run. Worn once the run has a cursor trail.',
});
export const LOCKED_LINE = 'Light the core, Fully Conditioned, to spend Stardust on looks.';

/** The looks bag, cleaned: unknown ids dropped. Never throws. */
export function parseLooks(bag) {
  const own = {};
  const src = bag && typeof bag === 'object' && bag.own && typeof bag.own === 'object' ? bag.own : {};
  for (const id of Object.keys(src)) if (LOOK_BY_ID[id] && src[id]) own[id] = 1;
  return { own };
}

/** life.prestige.store.looks, cleaned in place and returned. */
export function looksOf(p) {
  if (!p || typeof p !== 'object') return { own: {} };
  p.store ||= {};
  const clean = parseLooks(p.store.looks);
  p.store.looks = { ...(p.store.looks && typeof p.store.looks === 'object' ? p.store.looks : {}), own: clean.own };
  return p.store.looks;
}

export const ownsLook = (p, id) => !!p?.store?.looks?.own?.[id] && !!LOOK_BY_ID[id];

export function canBuyLook(p, id) {
  const l = LOOK_BY_ID[id];
  if (!l) return { ok: false, reason: 'unknown' };
  if (!p?.core) return { ok: false, reason: 'core' };
  if (ownsLook(p, id)) return { ok: false, reason: 'owned' };
  if ((p.dust || 0) < l.cost) return { ok: false, reason: 'dust' };
  return { ok: true, reason: null };
}

export function buyLook(p, id) {
  const c = canBuyLook(p, id);
  if (!c.ok) return c;
  const cost = LOOK_BY_ID[id].cost;
  p.dust -= cost; p.spent = (p.spent || 0) + cost;
  looksOf(p).own[id] = 1;
  return { ok: true, cost };
}

/** Writes every owned look into the run's shop stores. Idempotent; answers how many flags it set this time. */
export function applyLooks(run, p) {
  if (!run || typeof run !== 'object' || !p?.store?.looks) return 0;
  run.store ||= {};
  let n = 0;
  for (const id of Object.keys(parseLooks(p.store.looks).own)) {
    const l = LOOK_BY_ID[id];
    if (l.kind === 'face' || l.kind === 'wave') {
      const b = (run.store.bubble ||= {});
      const field = l.kind === 'face' ? 'faces' : 'waveStyles';
      if (b[field]?.[l.key] === true) continue;
      b[field] = { ...(b[field] || {}), [l.key]: true }; n += 1;
    } else if (l.kind === 'cursor') {
      const k = (run.store.cursorKit ||= {});
      if (k.skins?.[l.key]) continue;
      k.skins = { ...(k.skins || {}), [l.key]: true }; n += 1;
    } else if (l.kind === 'family') {
      const k = (run.store.cursorKit ||= {});
      if ((k.stages?.[l.key] || 0) >= FAMILY_STAGES) continue;
      k.stages = { ...(k.stages || {}), [l.key]: FAMILY_STAGES }; n += 1;
    }
  }
  return n;
}
