/* Room-scale architecture and metre-based finishes, shared by the dressing and media fitter. */
import { getBoudoirSetpieces } from '../track/boudoir-setpieces.js';

export const BOUDOIR_FINISH = Object.freeze({ wood: 0, tile: 1, velvet: 2, lacquer: 3, ivory: 4 });
export const BOUDOIR_DISTRICTS = Object.freeze({
  vanity: { finish: 'wood', base: '#542935', accent: '#9b5362', structure: 'rosewood-arcade' },
  gallery: { finish: 'lacquer', base: '#211726', accent: '#d7ad62', structure: 'stepped-display-arcade' },
  wardrobe: { finish: 'velvet', base: '#55192f', accent: '#8d3354', structure: 'gathered-velvet-bay' },
  spa: { finish: 'tile', base: '#1f6d70', accent: '#f0e5d0', structure: 'tiled-spa-colonnade' },
  mirror: { finish: 'ivory', base: '#e9e2dc', accent: '#baa1cf', structure: 'mirror-hall' },
});

export function boudoirDistrictFor(id) {
  if (/drawer-hall|mirror-/.test(id)) return 'mirror';
  if (/bath|compacts/.test(id)) return 'spa';
  if (/wardrobe|pearl-slalom/.test(id)) return 'wardrobe';
  if (/shelf|upper/.test(id)) return 'gallery';
  return 'vanity';
}

const add = (a, b) => a.map((v, i) => v + b[i]);
const mul = (a, n) => a.map(v => v * n);
const at = (f, x, y, z = 0) => add(add(add(f.p, mul(f.b, x)), mul(f.n, y)), mul(f.t, z));
const axesAt = f => [[...f.b], [...f.n], f.t.map(v => -v)];

/** Finish coordinates use the actual instance dimensions, so large walls never stretch a tiny motif. */
export function finishBoudoirRow(row, district, finish = BOUDOIR_DISTRICTS[district].finish, tone = 0) {
  if (!row) return null;
  row.material = 'district'; row.district = district;
  row.finish = [BOUDOIR_FINISH[finish], tone, ((row.p[0] * .071 + row.p[2] * .037) % 1 + 1) % 1];
  return row;
}

export function dressBoudoirDistricts(track, layout, { put, clear, rows, heroes }) {
  const stations = [], setpieces = getBoudoirSetpieces(track);
  const samples = [];
  for (const span of layout.landmarks) for (let s = span.s0; s < span.s1; s += 18)
    samples.push({ s, p: track.frameAt(s).p, district: boudoirDistrictFor(span.id), id: span.id });
  const nearest = p => samples.reduce((best, sample) => {
    const d = sample.p.reduce((sum, v, i) => sum + (v - p[i]) ** 2 * (i === 1 ? 2 : 1), 0);
    return !best || d < best.d ? { ...sample, d } : best;
  }, null);

  // Existing large slabs belong to a room, instead of repeating the same pink dotted wallpaper.
  for (const row of rows) {
    if (row.shape !== 'box' || row.motion || ['gold', 'mirror', 'pearl', 'glass', 'lip'].includes(row.material)) continue;
    const large = Math.max(...row.size) > 28 || /drawer-|vanity-table|vanity-drawer|jewel-gift-box|spiral-jewel-plinth/.test(row.name);
    if (!large) continue;
    const place = nearest(row.p), district = /wardrobe-descent|wardrobe-folded/.test(row.name) ? 'wardrobe'
      : /shelf-climb/.test(row.name) ? 'gallery' : /vanity-table|vanity-drawer/.test(row.name) ? 'vanity' : place.district;
    let finish = BOUDOIR_DISTRICTS[district].finish, tone = 0;
    if (/dressing-room-wall|dressing-room-parquet/.test(row.name)) { finish = 'wood'; tone = .35; }
    if (/ceiling|underside/.test(row.name)) { finish = district === 'wardrobe' ? 'velvet' : 'ivory'; tone = .25; }
    if (district === 'gallery' && /shelf$/.test(row.name)) { finish = 'ivory'; tone = .15; }
    if (district === 'spa' && row.material === 'cream') tone = 1;
    finishBoudoirRow(row, district, finish, tone);
  }
  // The central gown is deep wine satin. Display boxes stay dark against their ivory shelves.
  for (const row of rows) {
    if (/hero-gown-skirt|hero-gown-gathered-pleat|wardrobe-gown-skirt/.test(row.name)) row.material = 'velvet';
    if (row.name === 'hero-hatbox') row.material = 'velvet';
  }

  const isProtected = (s, outsideSpiral = false) => layout.halls.some(h => s > h.s0 - 24 && s < h.s1 + 24)
    || (!outsideSpiral && track.routes.some(r => s > r.openS0 - 85 && s < r.land.toS + 85))
    || [...setpieces.presses, ...setpieces.sweepers].some(h => Math.abs(s - h.s) < 35)
    || setpieces.conveyors.some(h => s > h.s0 - 32 && s < h.s1 + 18);
  const isMediaBay = (s, span) => [.3, .72].some(u => Math.abs(s - (span.s0 + (span.s1 - span.s0) * u)) < 24);
  function piece(name, district, f, x, y, z, size, finish, tone = 0, turn = 0, material = null) {
    const axes = axesAt(f);
    if (turn) {
      const c = Math.cos(turn), s = Math.sin(turn), a = axes[0], b = axes[1];
      axes[0] = a.map((v, i) => v * c + b[i] * s); axes[1] = b.map((v, i) => v * c - a[i] * s);
    }
    const row = put(name, material || 'district', at(f, x, y, z), size, { axes });
    if (row && !material) finishBoudoirRow(row, district, finish, tone);
    return row;
  }
  function gold(name, district, f, x, y, z, size, turn = 0) {
    return piece(name, district, f, x, y, z, size, 'wood', 0, turn, 'gold');
  }

  function arcade(span, s, district, side, serial) {
    const f = track.frameAt(s), edge = f.w / 2, x = side * (edge + 12), zSpan = 27;
    // A side arcade stays open at eye level. No opaque wall is placed across a picture's approach.
    const envelope = { p: at(f, x, 13), size: [13, 31, zSpan + 8], axes: axesAt(f) };
    if (!clear(envelope)) return;
    const finish = BOUDOIR_DISTRICTS[district].finish;
    const mark = (name, dx, y, z, size, type = finish, tone = 0, turn = 0) =>
      piece('district-' + name, district, f, x + dx, y, z, size, type, tone, turn);
    const trim = (name, dx, y, z, size, turn = 0) => gold('district-' + name, district, f, x + dx, y, z, size, turn);
    if (district === 'vanity') {
      // Turned, layered uprights and a shallow rosewood cornice read as a dressing-table arcade.
      for (const end of [-1, 1]) {
        mark('rosewood-pier', 0, 10, end * zSpan / 2, [5.2, 24, 5.2]);
        mark('rosewood-inset', -side * 2.8, 10, end * zSpan / 2, [.5, 18, 3.5], 'wood', 1);
        for (const y of [0, 2, 20, 22]) trim('arcade-capital', 0, y, end * zSpan / 2, [6.8, 1.2, 6.8]);
      }
      mark('rosewood-cornice', 0, 25, 0, [7, 5.5, zSpan + 10]);
      trim('rosewood-cornice-bead', -side * 3.7, 23, 0, [.7, 1, zSpan + 11]);
      for (const end of [-1, 1]) {
        // Four thick stepped corbels form the round shoulder of each open bay.
        for (let k = 0; k < 4; k++) mark('carved-arcade-corbel', 0, 18 + k * 1.4, end * (zSpan / 2 - k * 1.1), [5, 1.6, 3.5]);
      }
      mark('rosewood-wainscot', side * 2.2, .2, 0, [2.6, 5, zSpan - 5]);
      trim('rosewood-chair-rail', side * 1.1, 3, 0, [3.2, 1.1, zSpan - 2]);
    } else if (district === 'gallery') {
      // Black stepped Art Deco pylons, broad cream inset fields and a gold ziggurat crown.
      for (const end of [-1, 1]) {
        mark('lacquer-pylon', 0, 10, end * zSpan / 2, [7, 25, 5]);
        mark('gallery-ivory-inset', -side * 3.7, 11, end * zSpan / 2, [.5, 16, 2.4], 'ivory');
        trim('gallery-pylon-edge', -side * 4, 10, end * zSpan / 2 - 2.1, [.4, 24, .4]);
      }
      for (let k = 0; k < 3; k++) {
        mark('deco-crown', 0, 23 + k * 2.1, 0, [8 - k * 1.1, 2.8, zSpan + 8 - k * 6]);
        trim('deco-crown-inlay', -side * (4.1 - k * .55), 23 + k * 2.1, 0, [.45, .65, zSpan + 8 - k * 6]);
      }
      mark('gallery-plinth', 0, -.4, 0, [12, 4, zSpan + 6]);
      mark('gallery-plinth-marble', 0, 1.9, 0, [12.5, .7, zSpan + 6.5], 'ivory');
    } else if (district === 'wardrobe') {
      // An open fitting bay with dimensional gathered folds at the sides and a scalloped pelmet.
      for (const end of [-1, 1]) for (let k = 0; k < 5; k++) {
        const z = end * (zSpan / 2 - 1.1 * k), dx = Math.sin(k * 1.7) * 1.1;
        mark('gathered-velvet-curtain', dx, 10.2, z, [2.7, 25, 1.8], 'velvet', k % 2 * .35);
        if (k === 2) trim('curtain-tieback', -side * 1.8, 7, z, [1.1, 1.4, 7]);
      }
      for (let k = 0; k < 11; k++) {
        const z = (k / 10 - .5) * zSpan, scallop = Math.sin(k / 10 * Math.PI) * 3;
        mark('velvet-scalloped-pelmet', Math.cos(k) * .3, 24 - scallop, z, [4.2, 5, 3], 'velvet', .2);
        trim('velvet-pelmet-braid', -side * 2.3, 21.3 - scallop, z, [.6, .8, 3.1]);
      }
      mark('wardrobe-valance-crown', 0, 27, 0, [6.5, 2.8, zSpan + 6], 'wood');
      mark('velvet-upholstered-bench', side * 1.7, .7, 0, [8, 5, zSpan - 6], 'velvet', .3);
      trim('wardrobe-bench-piping', -side * 2.4, 3.2, 0, [.5, .5, zSpan - 5]);
    } else if (district === 'spa') {
      // Tile towers and broad stepped porcelain capitals make the bath an ivory and teal colonnade.
      for (const end of [-1, 1]) {
        mark('spa-tiled-pier', 0, 9.5, end * zSpan / 2, [6, 23, 6], 'tile');
        mark('spa-porcelain-foot', 0, -.5, end * zSpan / 2, [9, 3, 9], 'ivory');
        for (let k = 0; k < 3; k++) mark('spa-stepped-capital', 0, 20.5 + k * 1.5, end * zSpan / 2, [6.5 + k * 1.8, 1.7, 6.5 + k * 1.8], 'ivory');
        trim('spa-brass-collar', 0, 4, end * zSpan / 2, [6.6, .7, 6.6]);
      }
      mark('spa-architrave', 0, 26, 0, [9, 3.8, zSpan + 11], 'ivory');
      mark('spa-tile-frieze', -side * 4.7, 26, 0, [.5, 2.5, zSpan + 10], 'tile');
      mark('spa-tiled-dado', side * 1.5, .2, 0, [3.8, 5.5, zSpan - 5], 'tile');
      mark('spa-dado-coping', side * .7, 3.5, 0, [6, 1.2, zSpan - 3], 'ivory');
    }
    stations.push({ district, landmark: span.id, s, side, p: envelope.p, size: envelope.size, structure: BOUDOIR_DISTRICTS[district].structure });
  }

  for (const span of layout.landmarks) {
    const district = boudoirDistrictFor(span.id);
    if (district === 'mirror' || /chute|gap|ramp|compacts$/.test(span.id)) continue;
    let serial = 0;
    const core = layout.furniture.find(c => c.id === span.id);
    for (let s = span.s0 + 18; s < span.s1 - 12; s += district === 'wardrobe' ? 42 : 48, serial++) {
      if (isProtected(s, !!core) || isMediaBay(s, span)) continue;
      const f = track.frameAt(s);
      if (core) {
        const radial = [f.p[0] - core.x, 0, f.p[2] - core.z];
        const side = f.b.reduce((sum, v, i) => sum + v * radial[i], 0) > 0 ? 1 : -1;
        arcade(span, s, district, side, serial);
      } else for (const side of [-1, 1]) arcade(span, s, district, side, serial);
    }
  }

  // Strong entry silhouettes span the road only above the reserved driving and hazard envelope.
  for (const [id, district] of [['vanity-grid', 'vanity'], ['shelf-gallery', 'gallery'], ['wardrobe-out', 'wardrobe'], ['bath-approach', 'spa']]) {
    const span = layout.landmarks.find(l => l.id === id);
    const s = span.s0 + 12, f = track.frameAt(s), edge = f.w / 2 + 9;
    for (const side of [-1, 1]) {
      piece('district-entry-pier', district, f, side * edge, 12, 0, [5.5, 28, 8]);
      gold('district-entry-pier-inlay', district, f, side * (edge - 3), 12, 0, [.6, 26, 8.5]);
    }
    for (let layer = 0; layer < 3; layer++) {
      const y = 27 + layer * 2.4;
      piece('district-entry-crown', district, f, 0, y, 0, [edge * 2 + 12 - layer * 5, 2.7, 9 - layer], district === 'spa' ? 'ivory' : undefined);
      gold('district-entry-crown-inlay', district, f, 0, y - 1.3, -4.7, [edge * 2 + 10 - layer * 5, .55, .7]);
    }
    heroes.push({ id: district + '-entry-portal', s, p: at(f, 0, 26), width: edge * 2 + 12, height: 32 });
  }
  return { stations, finishes: Object.keys(BOUDOIR_FINISH), districts: Object.keys(BOUDOIR_DISTRICTS) };
}
