/* preview/fit.js - the pure half of wearing things in the preview box (lane F5). No DOM, no three.
 *
 * Rain: coverage (which default groups an item hides) and the body cut under a suit, the same rules as the Rain
 * wardrobe (rain-wardrobe/viewer.js applyLook + wardrobe-foundation/jewelry coverage), over the preview avatar's
 * groups (assets/preview/index.json rain.slots).
 * Kart: the shell mask (vertices EMI_root carries, as glassroad render/emiSkin.js paints them), the bone check an
 * outfit must pass before it is bound to the kart, and the paint walk law. */

/** The default groups an item covers: its own `hide` plus every group of each slot its coverage hides. */
export function coveredGroups(spec, slots = {}) {
  const out = new Set(spec.hide || []);
  for (const slot of (spec.coverage && spec.coverage.hideSlots) || []) for (const n of slots[slot] || []) out.add(n);
  return [...out];
}

/** The wardrobe clamps a suit's hideBodyBelow to 0.86 (viewer.js applyLook): the neckline always stays. */
export const BODY_BELOW_MAX = 0.86;
/** The lower-body region in the preview pose: hips and legs sit inside this half width, the hands outside it. */
export const BODY_HALF_WIDTH = 0.19;

/**
 * The triangles of a body that stay drawn under a suit hiding the body below `below` metres (lower body only: the
 * hands keep theirs). `at(i)` gives vertex i as [x, y, z] in the avatar's space; `index` is the triangle list (null =
 * unindexed, `count` vertices). A triangle goes only when all three corners are covered, so the suit hem hides the cut.
 */
export function cutBelow(index, count, at, below, { halfWidth = BODY_HALF_WIDTH, centerX = 0 } = {}) {
  const cut = Math.min(BODY_BELOW_MAX, below);
  const n = index ? index.length : count;
  const covered = new Uint8Array(count);
  for (let i = 0; i < count; i++) { const [x, y] = at(i); covered[i] = y < cut && Math.abs(x - centerX) < halfWidth ? 1 : 0; }
  const keep = [];
  for (let t = 0; t + 2 < n; t += 3) {
    const a = index ? index[t] : t, b = index ? index[t + 1] : t + 1, c = index ? index[t + 2] : t + 2;
    if (!(covered[a] && covered[b] && covered[c])) keep.push(a, b, c);
  }
  return count > 65535 ? Uint32Array.from(keep) : Uint16Array.from(keep);
}

/** The bones an outfit or umbrella may bind to: Astra's registry joints for the accepted kart (15, by name). */
export const KART_JOINTS = Object.freeze(['kart_root', 'cup_body', 'rider_mount', 'EMI_root', 'ant0', 'ant1', 'ant2',
  'ballpiv', 'button', 'EMI_glass', 'footL', 'footR', 'shoulderL', 'shoulderR', 'saucer_body']);

/** A piece binds only when every bone it names is one of the kart's (exact names, nothing extra). */
export function bindsTo(pieceBones, kartBones) {
  const have = new Set(kartBones);
  return pieceBones.length > 0 && pieceBones.every((n) => have.has(n));
}

/** 1 per vertex the EMI_root bone carries (weight >= .5): the shell a paint covers. Face, hands, feet and antenna keep theirs. */
export function shellMask(joints, weights, boneNames, count) {
  const out = new Float32Array(count), root = boneNames.indexOf('EMI_root');
  if (root < 0) return out;
  for (let v = 0; v < count; v++) for (let k = 0; k < 4; k++) if (joints[v * 4 + k] === root && weights[v * 4 + k] >= 0.5) { out[v] = 1; break; }
  return out;
}

/** The pattern ids the shell shader knows (glassroad render/emiSkin.js PAINT_PATTERN and its three native ones). */
export const PATTERN_ID = Object.freeze({ stripes: 1, dots: 2, checks: 3, hearts: 4, spiral: 5, 'host-gold': 6 });

/**
 * How a paint with several patterns walks them: { holdMs, blendMs } or null (hold the first). Motion off and
 * reduced motion hold still; Gentle keeps the walk but lengthens each blend past the 300 ms floor (never a cut).
 */
export function paintWalk(count, { motion = 'full', reducedMotion = false, gentle = false } = {}) {
  if (count < 2 || motion === 'off' || reducedMotion) return null;
  return { holdMs: 1600, blendMs: gentle ? 600 : motion === 'less' ? 450 : 320 };
}
