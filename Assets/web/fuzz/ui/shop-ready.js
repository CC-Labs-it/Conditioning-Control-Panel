/* ui/shop-ready.js - a shop tile that becomes buyable says so (owner 2026-11-08): one jump with a squash and wobble the moment
 * its fill tops up, a warm gold fill while it stays buyable (owner: the red and theme fills clashed with the pink art; the
 * twitching side lines were removed), and a small hop every 6 to 8 seconds until it is bought, never two tiles at once. Motion is transform-only (translate, scale, rotate,
 * which compose with any transform an unlock puts on a tile), so the shop never reflows. Gentle and reduced motion keep the
 * colour, and nothing moves. Tiles already buyable when they first draw never jump. */

export const SHOP_READY_CSS = `
#shop { width: calc(100% + 24px); max-width: 784px; margin-inline: -12px; padding-inline: 12px; margin-top: -16px; padding-top: 20px; }
#shop .row.buyable { --buy-a: #f6c544; --buy-b: #ffe48a; z-index: 1; overflow: visible; color: var(--ink); border-color: transparent;
  background: linear-gradient(160deg, var(--buy-b), var(--buy-a)); color: #3b2a06;
  box-shadow: 0 0 0 2px #c8901a, 0 3px 0 2px #8a5c08, 0 0 14px 2px #ffd65a99; }
#shop #goal .row.buyable { animation: none; }
#shop .row.buyable .bar { opacity: 0; }
#shop .row.buyable .ico { opacity: 1; filter: drop-shadow(0 2px 0 color-mix(in srgb, var(--buy-a) 45%, #0007)); }
#shop .row.buyable .cost { padding: 1px 7px; border-radius: 9px; background: #fffbea; color: #3b2a06; font-weight: 700;
  box-shadow: 0 1px 0 color-mix(in srgb, var(--buy-a) 60%, #000); }
#shop .row.buyable .tag { padding: 1px 6px; border-radius: 9px; background: #fffbeaE6; color: #3b2a06; font-weight: 700; }
`;

/* The first jump: squash down, stretch up, land squashed, wobble out. About 450 ms. */
const JUMP = [
  { translate: '0 0', scale: '1 1', rotate: '0deg', easing: 'ease-out' },
  { translate: '0 1px', scale: '1.07 .9', rotate: '0deg', offset: .14, easing: 'cubic-bezier(.2,.7,.4,1)' },
  { translate: '0 -10px', scale: '.94 1.09', rotate: '0deg', offset: .4, easing: 'ease-in' },
  { translate: '0 0', scale: '1.08 .91', rotate: '0deg', offset: .62, easing: 'ease-out' },
  { translate: '0 0', scale: '.98 1.02', rotate: '-3.5deg', offset: .76 },
  { translate: '0 0', scale: '1 1', rotate: '2deg', offset: .88 },
  { translate: '0 0', scale: '1 1', rotate: '0deg' },
];
/* The reminder: a smaller hop with one wiggle. */
const NUDGE = [
  { translate: '0 0', scale: '1 1', rotate: '0deg', easing: 'ease-out' },
  { translate: '0 1px', scale: '1.04 .95', rotate: '0deg', offset: .16, easing: 'cubic-bezier(.2,.7,.4,1)' },
  { translate: '0 -5px', scale: '.97 1.04', rotate: '0deg', offset: .42, easing: 'ease-in' },
  { translate: '0 0', scale: '1.04 .96', rotate: '0deg', offset: .64, easing: 'ease-out' },
  { translate: '0 0', scale: '1 1', rotate: '-2deg', offset: .8 },
  { translate: '0 0', scale: '1 1', rotate: '0deg' },
];

export const NUDGE_MIN_MS = 6000, NUDGE_SPREAD_MS = 2000, NUDGE_GAP_MS = 900;

/** When a buyable tile next hops: 6 to 8 seconds out. */
export const nextNudge = (now, rnd = Math.random()) => now + NUDGE_MIN_MS + rnd * NUDGE_SPREAD_MS;

/**
 * What a tile does on this paint, from whether it was buyable before (undefined on its first paint).
 * 'jump' only on the change from short to buyable, never on a first paint, so a boot or a fresh tile is quiet.
 */
export function readyStep(was, buyable) {
  if (!buyable) return 'none';
  return was === false ? 'jump' : was === undefined ? 'arm' : 'stay';
}

/** Play one hop on a tile, replacing any hop still running. */
export function hop(el, kind = 'jump') {
  if (!el.animate) return null;
  el.__hop?.cancel();
  const a = el.animate(kind === 'jump' ? JUMP : NUDGE, { duration: kind === 'jump' ? 460 : 380, easing: 'linear' });
  el.__hop = a;
  a.onfinish = () => { if (el.__hop === a) el.__hop = null; };
  return a;
}
