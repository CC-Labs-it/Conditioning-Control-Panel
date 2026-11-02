/* Close, recognizable dressing-table clutter. The fitter reserves the full motion envelope. */
import { getBoudoirSetpieces } from '../track/boudoir-setpieces.js';
const TAU = Math.PI * 2;
const add = (a, b) => a.map((v, i) => v + b[i]);
const mul = (a, n) => a.map(v => v * n);
const at = (f, x, y, z = 0) => add(add(add(f.p, mul(f.b, x)), mul(f.n, y)), mul(f.t, z));
const axesAt = f => [[...f.b], [...f.n], f.t.map(v => -v)];
const WORLD = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];

export function dressBoudoirProps(track, layout, { put, clear, rows, particles, heroes }) {
  const clusters = [], motionEnvelopes = [], animated = [], features = [];
  const setpieces = getBoudoirSetpieces(track);
  const isHall = s => layout.halls.some(h => s >= h.s0 - 20 && s <= h.s1 + 20);
  const isFlight = s => track.routes.some(r => s > r.openS0 - 65 && s < r.land.toS + 70);
  const isSetpiece = s => [...setpieces.presses, ...setpieces.sweepers].some(h => Math.abs(s - h.s) < 30)
    || setpieces.conveyors.some(h => s > h.s0 - 30 && s < h.s1 + 10);
  function part(ctx, name, material, x, y, z, size, shape = 'box', rotation = 0, move = false) {
    const { f, offset } = ctx, axes = axesAt(f);
    if (rotation) {
      const c = Math.cos(rotation), s = Math.sin(rotation), a = axes[0], b = axes[1];
      axes[0] = a.map((v, i) => v * c + b[i] * s); axes[1] = b.map((v, i) => v * c - a[i] * s);
    }
    const row = put(name, material, at(f, offset + x, y, z), size, { axes, shape });
    if (row && move && ctx.motion) { row.motion = ctx.motion; animated.push(row); }
    return row;
  }
  function site(s, side, width = 18, height = 25, depth = 20, moving = false, gap = 7, outsideSpiral = false) {
    const f = track.frameAt(s);
    if (f.surface === 'stream' || f.surface === 'pool' || isHall(s) || isSetpiece(s)) return null;
    const axes = axesAt(f);
    for (let tries = 0; tries < 6; tries++) {
      const offset = side * (f.w / 2 + width / 2 + gap + tries * 5);
      const envelope = { p: at(f, offset, height / 2 - 2), size: [width + 3, height + 6, depth + 3], axes };
      if (!clear(envelope)) continue;
      const motion = moving && (!isFlight(s) || outsideSpiral) ? { pivot: at(f, offset, 0), axis: [...f.t], amp: .12,
        speed: .55 + s % 7 * .035, phase: s * .019, rise: .7, envelope } : null;
      const ctx = { s, f, offset, motion, envelope };
      if (motion) motionEnvelopes.push(envelope);
      clusters.push({ s, side, p: at(f, offset, 0), width, height, motion: !!motion });
      particles.push({ p: at(f, offset, 4), phase: s * .03 }); return ctx;
    }
    return null;
  }
  function bow(ctx, x, y, z, scale = 1, material = 'rose', moving = false) {
    for (const side of [-1, 1]) {
      part(ctx, 'satin-bow-loop', material, x + side * 2.3 * scale, y, z, [5.8 * scale, 3.5 * scale, 2.4 * scale], 'sphere', side * .3, moving);
      part(ctx, 'satin-bow-loop-recess', 'velvet', x + side * 2.4 * scale, y + .1 * scale, z - 1.08 * scale,
        [3.7 * scale, 1.5 * scale, .2 * scale], 'sphere', side * .3, moving);
      part(ctx, 'satin-bow-tail', material, x + side * 1.5 * scale, y - 3.7 * scale, z + .1 * scale,
        [1.9 * scale, 6.6 * scale, .5 * scale], 'box', side * -.2, moving);
    }
    part(ctx, 'satin-bow-knot', 'gold', x, y, z - .5 * scale, [2.5 * scale, 3.2 * scale, 2.7 * scale], 'sphere', 0, moving);
  }
  function brush(ctx, dark = false) {
    part(ctx, 'brush-handle', dark ? 'velvet' : 'rose', 0, 5, 0, [2.8, 13, 2.8], 'cylinder', 0, true);
    part(ctx, 'brush-ferrule', 'gold', 0, 12.2, 0, [3.8, 4.7, 3.8], 'cylinder', 0, true);
    part(ctx, 'brush-bristle-mass', dark ? 'velvet' : 'cream', 0, 17, 0, [8.8, 8.5, 8.8], 'sphere', 0, true);
    for (let n = 0; n < 16; n++) {
      const a = n / 16 * TAU;
      part(ctx, 'brush-individual-bristles', n % 3 ? 'cream' : 'gold', Math.cos(a) * 3.5, 18.2, Math.sin(a) * 3.5,
        [.38, 7 + n % 3 * .45, .38], 'cone', Math.sin(a) * .16, true);
    }
    part(ctx, 'brush-stand', 'gold', 0, -.5, 0, [11, 2, 11], 'cylinder');
    bow(ctx, 0, 7, -2.2, .7, 'lavender', true);
  }
  function compact(ctx, hue = 'rose') {
    part(ctx, 'powder-compact-bottom', 'gold', 0, .3, 0, [17, 3.4, 17], 'cylinder');
    part(ctx, 'powder-compact-powder', hue, 0, 2.1, 0, [14.6, .7, 14.6], 'cylinder');
    part(ctx, 'powder-compact-press', 'cream', 0, 2.5, 0, [8, .18, 8], 'cylinder');
    part(ctx, 'powder-compact-hinge', 'gold', 0, 1.8, 7, [8, 2.2, 2.2], 'cylinder');
    part(ctx, 'powder-compact-lid', 'gold', 0, 10, 7, [17, 17, 1.6], 'disc', .1, true);
    part(ctx, 'powder-compact-mirror', 'mirror', 0, 10, 6, [14.7, 14.7, .3], 'disc', .1, true);
    part(ctx, 'powder-puff', 'pearl', -5, 4, -6, [8, 3.5, 7], 'sphere');
    part(ctx, 'powder-puff-satin-strap', 'lavender', -5, 5.6, -6, [2.2, .5, 6.7]);
    for (let n = 0; n < 8; n++) part(ctx, 'spilled-powder', 'cream', 7 + Math.sin(n * 2) * 3, .5, -2 - n, [1.8, .3, 1.8], 'sphere');
  }
  function spool(ctx, hue = 'lavender') {
    part(ctx, 'thread-spool-body', hue, 0, 5.2, 0, [11, 11, 11], 'cylinder', 0, true);
    for (const y of [0, 10.8]) {
      part(ctx, 'thread-spool-flange', 'cream', 0, y, 0, [15, 1.8, 15], 'cylinder', 0, true);
      part(ctx, 'thread-spool-edge', 'gold', 0, y + .8, 0, [15.3, .35, 15.3], 'cylinder', 0, true);
    }
    for (let n = 0; n < 9; n++) part(ctx, 'thread-winding', n % 2 ? hue : 'gold', 0, .9 + n * 1.12, 0,
      [11.25, .34, 11.25], 'cylinder', 0, true);
    part(ctx, 'thread-spool-hole', 'velvet', 0, 12, 0, [3.1, .4, 3.1], 'cylinder', 0, true);
    for (let n = 0; n < 8; n++) part(ctx, 'unfurled-satin-ribbon', hue, Math.sin(n * .8) * 2, 1 + Math.sin(n * .7), -6 - n * 1.5,
      [4, .28, 1.8], 'box', Math.sin(n) * .08, true);
    bow(ctx, 0, 14, 0, .8, 'rose', true);
  }
  function polish(ctx, index) {
    const hue = ['rose', 'lavender', 'mint'][index % 3];
    part(ctx, 'nail-polish-bottle', hue, 0, 3.5, 0, [9, 9, 7], 'sphere');
    part(ctx, 'nail-polish-foot', 'gold', 0, -.2, 0, [7.5, .8, 6]);
    part(ctx, 'nail-polish-neck', 'gold', 0, 8, 0, [4, 3, 4], 'cylinder');
    part(ctx, 'nail-polish-cap', 'velvet', 0, 13, 0, [4.8, 8.4, 4.8], 'cylinder', 0, true);
    for (let n = 0; n < 8; n++) {
      const a = n / 8 * TAU;
      part(ctx, 'nail-polish-cap-rib', 'gold', Math.cos(a) * 2.4, 13, Math.sin(a) * 2.4, [.2, 7.4, .2], 'cylinder', 0, true);
    }
    part(ctx, 'nail-polish-label', 'cream', 0, 3.5, -3.5, [5.6, 3.8, .35]);
    part(ctx, 'nail-polish-seal', 'gold', 0, 3.5, -3.8, [2.2, 2.2, .25], 'disc');
  }
  function gift(ctx, index) {
    part(ctx, 'jewel-gift-box', index % 2 ? 'rose' : 'lavender', 0, 4, 0, [16, 10, 13]);
    part(ctx, 'jewel-gift-lid', 'cream', 0, 9.5, 0, [17, 2.1, 14]);
    part(ctx, 'jewel-gift-vertical-ribbon', 'gold', 0, 4, -6.7, [2, 11, .5]);
    part(ctx, 'jewel-gift-top-ribbon', 'gold', 0, 10.7, 0, [2, .3, 14]);
    part(ctx, 'jewel-gift-cross-ribbon', 'gold', 0, 10.8, 0, [17, .3, 2]);
    bow(ctx, 0, 13, 0, 1.3, index % 2 ? 'lavender' : 'rose', true);
    for (let n = 0; n < 12; n++) part(ctx, 'spilled-pearl-necklace', 'pearl', Math.cos(n / 12 * TAU) * 8, 1, Math.sin(n / 12 * TAU) * 6 - 7, [1.4, 1.4, 1.4], 'sphere');
  }
  function comb(ctx) {
    part(ctx, 'hair-comb-spine', 'gold', 0, 8, 0, [19, 4, 2], 'sphere', .12, true);
    for (let n = 0; n < 13; n++) part(ctx, 'hair-comb-teeth', 'cream', (n - 6) * 1.25, 4, 0,
      [.7, 7 + Math.sin(n / 12 * Math.PI) * 3, .9], 'cylinder', .12, true);
    for (let n = 0; n < 7; n++) part(ctx, 'hair-comb-jewel', n % 2 ? 'mint' : 'rose', (n - 3) * 2.4, 9.5, -1.3, [1.8, 2.5, 1], 'sphere', 0, true);
    bow(ctx, 0, 12, 0, .75, 'lavender', true);
  }
  const kinds = [brush, compact, spool, polish, gift, comb];
  // Alternating combinations establish a near, middle and far scale, instead of props only on the horizon.
  for (let s = 18, i = 0; s < track.lengthM; s += 57, i++) {
    const side = i % 2 ? 1 : -1;
    if (isFlight(s)) continue;
    const ctx = site(s, side, 23, 28, 38, i % 3 !== 0); if (!ctx) continue;
    const k = i % kinds.length;
    if (k === 0) brush(ctx, i % 4 === 0); else if (k === 1) compact(ctx, i % 3 ? 'rose' : 'lavender');
    else if (k === 2) spool(ctx); else if (k === 3) polish(ctx, i); else if (k === 4) gift(ctx, i); else comb(ctx);
    if (i % 4 === 0) { const pair = site(s + 15, -side, 17, 24, 23, false); if (pair) polish(pair, i + 1); }
  }
  // The inside of the wardrobe carries the drop shortcuts. Its outside shoulder stays dressed
  // at each elevation, close enough for the driving camera to read individual bristles and labels.
  for (const core of layout.furniture) {
    const span = layout.landmarks.find(l => l.id === core.id);
    for (let s = span.s0 + 15, i = 0; s < span.s1 - 15; s += 31, i++) {
      const f = track.frameAt(s), radial = [f.p[0] - core.x, 0, f.p[2] - core.z];
      const side = f.b.reduce((sum, v, k) => sum + v * radial[k], 0) > 0 ? 1 : -1;
      const ctx = site(s, side, 14, 25, 17, i % 2 === 0, 3.5, true); if (!ctx) continue;
      if (i % 3 === 0) brush(ctx, true); else if (i % 3 === 1) polish(ctx, i); else {
        part(ctx, 'spiral-jewel-plinth', 'cream', 0, 1, 0, [11, 4, 9]);
        bow(ctx, 0, 8, 0, 1.3, i % 2 ? 'rose' : 'lavender', true);
        for (let n = 0; n < 7; n++) part(ctx, 'spiral-jewel-drop', n % 2 ? 'gold' : 'mint', (n - 3) * 1.5, 3.7, -4.8, [1, 2.5, 1], 'sphere');
      }
      clusters.at(-1).spiral = core.id;
    }
  }
  // Giant centerpiece gowns and hatbox stacks replace the empty middle of the two helices.
  for (const core of layout.furniture) {
    const isWardrobe = core.id === 'wardrobe-descent', radius = core.radiusM - 10;
    const f = { p: [core.x, core.baseY, core.z], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1] }, ctx = { f, offset: 0 };
    if (isWardrobe) {
      for (let i = rows.length - 1; i >= 0; i--) if (rows[i].name === 'wardrobe-descent-shelf' &&
        Math.hypot(rows[i].p[0] - core.x, rows[i].p[2] - core.z) < radius + 30) rows.splice(i, 1);
      part(ctx, 'hero-gown-skirt', 'rose', 0, 49, 0, [radius * 1.65, 98, radius * 1.4], 'cone');
      part(ctx, 'hero-gown-bodice', 'lavender', 0, 110, 0, [35, 38, 25], 'sphere');
      part(ctx, 'hero-gown-waist', 'gold', 0, 93, 0, [29, 7, 25], 'cylinder');
      part(ctx, 'hero-gown-hanger', 'gold', 0, 135, 0, [66, 3, 5]);
      for (const side of [-1, 1]) part(ctx, 'hero-gown-strap', 'gold', side * 10, 128, 0, [3.5, 23, 3]);
      for (let n = 0; n < 20; n++) {
        const a = n / 20 * TAU;
        part(ctx, 'hero-gown-gathered-pleat', n % 3 ? 'rose' : 'lavender', Math.cos(a) * radius * .52, 45, Math.sin(a) * radius * .43,
          [7, 90, 7], 'cone', Math.cos(a) * -.12);
        part(ctx, 'hero-gown-pearl-hem', 'pearl', Math.cos(a) * radius * .81, 4, Math.sin(a) * radius * .68, [3, 3, 3], 'sphere');
      }
      bow(ctx, 0, 96, -17, 3, 'cream'); features.push('central-gown');
    } else {
      for (let level = 0; level < 3; level++) {
        const diameter = radius * 1.8 - level * 14, y = level * 42 + 16;
        part(ctx, 'hero-hatbox', level % 2 ? 'lavender' : 'rose', 0, y, 0, [diameter, 34, diameter], 'cylinder');
        part(ctx, 'hero-hatbox-lid', 'cream', 0, y + 19, 0, [diameter + 5, 5, diameter + 5], 'cylinder');
        part(ctx, 'hero-hatbox-gold-edge', 'gold', 0, y + 22, 0, [diameter + 6, 1.1, diameter + 6], 'cylinder');
        for (let n = 0; n < 18; n++) {
          const a = n / 18 * TAU;
          part(ctx, 'hero-hatbox-piping', 'gold', Math.cos(a) * (diameter / 2 + .1), y, Math.sin(a) * (diameter / 2 + .1), [.7, 31, .7], 'cylinder');
        }
      }
      bow(ctx, 0, 133, 0, 4, 'lavender'); features.push('hatbox-stack');
    }
    heroes.push({ id: isWardrobe ? 'central-satin-gown' : 'stacked-hatboxes', p: [...f.p], width: radius * 2, height: 145 });
  }
  // Suspended pearl chandeliers and curved ribbon garlands mark each major environment change.
  for (const [id, share] of [['vanity-grid', .58], ['powder-return', .4], ['shelf-gallery', .6],
    ['upper-gallery', .55], ['wardrobe-out', .5], ['bath-approach', .35], ['mirror-landing', .6]]) {
    const l = layout.landmarks.find(v => v.id === id); if (!l) continue;
    const s = l.s0 + (l.s1 - l.s0) * share, f = track.frameAt(s), width = f.w + 34;
    const envelope = { p: at(f, 0, 38), size: [width + 8, 35, 46], axes: axesAt(f) };
    if (!clear(envelope)) continue;
    const ctx = { f, offset: 0, motion: { pivot: at(f, 0, 53), axis: [...f.t], amp: .035, speed: .24, phase: s, rise: 0, envelope } };
    motionEnvelopes.push(envelope);
    part(ctx, 'chandelier-crown', 'gold', 0, 50, 0, [16, 6, 16], 'cylinder', 0, true);
    for (let n = 0; n < 28; n++) {
      const a = n / 28 * TAU, x = Math.cos(a) * width * .38, z = Math.sin(a) * 17;
      part(ctx, 'chandelier-ring-bead', 'gold', x, 35, z, [2.6, 2.6, 2.6], 'sphere', 0, true);
      if (n % 2 === 0) for (let k = 0; k < 5; k++) {
        const u = k / 5;
        part(ctx, 'chandelier-pearl-chain', 'pearl', x * (1 - u), 35 + u * 14, z * (1 - u), [1.8, 1.8, 1.8], 'sphere', 0, true);
        part(ctx, 'chandelier-pearl-drop', 'pearl', x, 33 - k * 1.6, z, [1.4, 1.7, 1.4], 'sphere', 0, true);
      }
    }
    for (let n = 0; n < 24; n++) {
      const x = (n / 23 - .5) * width, u = x / (width / 2), y = 27 + u * u * 13;
      const tilt = Math.atan(52 * x / (width * width));
      part(ctx, 'draped-wide-satin-ribbon', n % 6 < 3 ? 'rose' : 'lavender', x, y, -18,
        [width / 23 + .25, .5, 7], 'box', tilt, true);
      part(ctx, 'ribbon-gold-selvedge', 'gold', x, y + .2, -21.5,
        [width / 23 + .3, .25, .5], 'box', tilt, true);
    }
    for (const side of [-1, 1]) bow(ctx, side * (width / 2 - 10), 42, -18, 2.2, 'cream', true);
    features.push('pearl-chandelier');
  }
  // Small raised scrollwork breaks the naked cabinet slabs without using additional texture maps.
  for (const core of layout.furniture) for (const side of [-1, 1]) {
    const width = core.radiusM * 2 + 130, x = core.x + side * (width / 2 + 1), z = core.z + width / 2 + 5;
    for (let level = 0; level < 4; level++) {
      const y = core.baseY + 20 + level * 35;
      for (let n = 0; n < 12; n++) {
        const a = n / 12 * TAU;
        put('cabinet-carved-flower', n % 3 ? 'gold' : 'pearl', [x + Math.cos(a) * 7, y + Math.sin(a) * 10, z], [3, 5, 2], { shape: 'sphere', axes: WORLD });
      }
      put('cabinet-flower-center', 'rose', [x, y, z - 1], [6, 6, 3], { shape: 'sphere', axes: WORLD });
    }
  }
  return { clusters, motionEnvelopes, animated, features };
}
