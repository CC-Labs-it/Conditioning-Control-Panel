/* Static Boudoir finishes in road metres. Pattern detail filters out before it can crawl. */
import { Color } from 'three/webgpu';
import { float, vec3, sin, abs, fract, min, max, mix, smoothstep, fwidth, normalize, dot, pow } from 'three/tsl';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { reliefNormal } from './surfaceRelief.js';

export const BOUDOIR_SURFACES = Object.freeze({
  vanity: Object.freeze({ name: 'rosewood', base: '#874b53', dark: '#542935', light: '#ab7273', border: '#542935', sheen: .045 }),
  gallery: Object.freeze({ name: 'ivory parquet', base: '#d4c3a4', dark: '#bba27b', light: '#eadbc0', border: '#322538', sheen: .035 }),
  wardrobe: Object.freeze({ name: 'stitched satin', base: '#7f294a', dark: '#55192f', light: '#9f4968', border: '#42182e', sheen: .055 }),
  spa: Object.freeze({ name: 'porcelain', base: '#e1ddce', dark: '#1f6d70', light: '#f0e5d0', border: '#1f6d70', sheen: .07 }),
  mirror: Object.freeze({ name: 'pale stone', base: '#d2cbd1', dark: '#aaa5b1', light: '#eae2da', border: '#9e929e', sheen: .06 }),
});
export const BOUDOIR_FINISH = Object.freeze({ transitionM: 8, detailNearM: 38, detailFarM: 115,
  footprintFrom: .18, footprintTo: .65, borderM: 1.8 });

/** Contiguous districts follow authored landmarks, including all ramps and new connecting pieces. */
export function boudoirSurfaceZones(track) {
  const layout = getBoudoirLayout(track); if (!layout) return [];
  const starts = [
    ['vanity', 'vanity-grid'], ['mirror', 'drawer-hall'], ['gallery', 'shelf-approach'],
    ['wardrobe', 'wardrobe-bridge'], ['spa', 'bath-approach'], ['mirror', 'mirror-approach'], ['vanity', 'home-turn'],
  ];
  return starts.map(([kind, landmark], i) => ({ kind, landmark,
    s0: layout.landmarks.find(row => row.id === landmark).s0,
    s1: i + 1 < starts.length ? layout.landmarks.find(row => row.id === starts[i + 1][1]).s0 : track.lengthM }));
}
const clamp01 = n => Math.max(0, Math.min(1, n));
const smooth = (a, b, v) => { const t = clamp01((v - a) / (b - a)); return t * t * (3 - 2 * t); };
/** Numeric counterpart of district mixing for authoring checks and map legends. */
export function boudoirSurfaceWeights(zones, distanceS) {
  if (!zones.length) return {};
  const lap = zones.at(-1).s1, s = ((distanceS % lap) + lap) % lap, h = BOUDOIR_FINISH.transitionM / 2;
  const weights = Object.fromEntries(Object.keys(BOUDOIR_SURFACES).map(k => [k, 0]));
  zones.forEach((zone, i) => { weights[zone.kind] += (i ? smooth(zone.s0 - h, zone.s0 + h, s) : 1)
    * (i < zones.length - 1 ? 1 - smooth(zone.s1 - h, zone.s1 + h, s) : 1); });
  return weights;
}
/** Fine detail loses contrast before its cell becomes smaller than a pixel. */
export function boudoirDetailWeight(footprintM, distanceM, periodM) {
  return (1 - smooth(BOUDOIR_FINISH.footprintFrom, BOUDOIR_FINISH.footprintTo, Math.abs(footprintM) / periodM))
    * (1 - smooth(BOUDOIR_FINISH.detailNearM, BOUDOIR_FINISH.detailFarM, distanceM));
}

const rgb = hex => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
const unit = float(1), one = () => unit;
const TAU = Math.PI * 2;
/** No textures, clocks or external resources. Water and music composite after this dry base. */
export function createBoudoirRoadSurface({ track, p, trueEdge, distance, view, normal }) {
  const zones = boudoirSurfaceZones(track); if (!zones.length) return null;
  const lap = track.lengthM, fit = desired => lap / Math.max(1, Math.round(lap / desired));
  const x = p.x, s = p.y, far = one().sub(smoothstep(BOUDOIR_FINISH.detailNearM, BOUDOIR_FINISH.detailFarM, distance));
  const footprints = new Map(), details = new Map(), seams = new Map();
  const footprint = coord => {
    if (!footprints.has(coord)) footprints.set(coord, fwidth(coord));
    return footprints.get(coord);
  };
  const detail = (coord, period) => {
    if (!details.has(coord)) details.set(coord, new Map());
    const cache = details.get(coord);
    if (!cache.has(period)) cache.set(period, one().sub(smoothstep(BOUDOIR_FINISH.footprintFrom,
      BOUDOIR_FINISH.footprintTo, footprint(coord).div(period))).mul(far));
    return cache.get(period);
  };
  const wave = (coord, period) => sin(coord.mul(TAU / period)).mul(detail(coord, period));
  const seam = (coord, period, width) => {
    if (!seams.has(coord)) seams.set(coord, new Map());
    const cache = seams.get(coord);
    if (!cache.has(period)) {
      const phase = fract(coord.div(period));
      cache.set(period, min(phase, one().sub(phase)).mul(period));
    }
    const d = cache.get(period), px = max(footprint(coord).mul(1.15), width);
    return one().sub(smoothstep(0, px, d)).mul(float(width).div(px)).mul(detail(coord, period));
  };
  const tint = (kind, factor) => mix(rgb(BOUDOIR_SURFACES[kind].dark), rgb(BOUDOIR_SURFACES[kind].light), factor);
  const gold = rgb('#c6a168');

  // Rosewood has long flame grain, a quiet pore layer and wax gathered at the joints.
  const boardShift = sin(x.mul(TAU / 5.6)).mul(1.1);
  const grainCoord = x.add(sin(s.mul(TAU / fit(44))).mul(.12));
  const flameCoord = grainCoord.add(sin(s.mul(TAU / fit(15))).mul(.19));
  const grain = wave(flameCoord, .62).mul(.11).add(wave(grainCoord, 1.8).mul(.095));
  const pores = wave(flameCoord.add(s.mul(.15 / fit(6))), .15).mul(.018);
  const boards = seam(x, 2.8, .035).add(seam(s.add(boardShift), fit(9.5), .028)).min(1);
  const boardWax = seam(x, 2.8, .16).sub(boards).max(0);
  const wood = tint('vanity', grain.add(pores).add(.52)).mul(one().sub(boards.mul(.46)))
    .add(rgb('#bc8680').mul(boardWax).mul(.065));

  // Diagonal parquet blocks use two broad grain directions and restrained gold inlay.
  const diagonalA = s.add(x), diagonalB = s.sub(x), parquetPeriod = fit(6);
  const parquet = wave(diagonalA, parquetPeriod * 2).mul(wave(diagonalB, parquetPeriod * 2));
  const parquetJoints = max(seam(diagonalA, parquetPeriod, .045), seam(diagonalB, parquetPeriod, .045));
  const parquetGrain = mix(wave(diagonalA, .7), wave(diagonalB, .7), parquet.mul(.5).add(.5)).mul(.065);
  const parquetBevel = max(seam(diagonalA, parquetPeriod, .15), seam(diagonalB, parquetPeriod, .15))
    .sub(parquetJoints).max(0);
  const gallery = mix(tint('gallery', parquet.mul(.17).add(parquetGrain).add(.62)), gold, parquetJoints.mul(.65))
    .mul(one().sub(parquetBevel.mul(.075)));

  // Fabric keeps a broad velvet nap. Small weave and stitched borders fade by footprint.
  const nap = wave(x.add(sin(s.mul(TAU / fit(84))).mul(.3)), 8).mul(.19);
  const threadAlong = wave(x.add(s.mul(.6 / fit(.6))), .6);
  const threadAcross = wave(x.sub(s.mul(.6 / fit(.6))), .6);
  const weave = threadAlong.mul(threadAcross).mul(.05);
  const stitchDash = wave(s, fit(.9)).mul(.5).add(.5);
  const borderStitch = seam(trueEdge.sub(.65), 1.1, .045).mul(one().sub(smoothstep(1.5, 2.5, trueEdge)));
  const satin = mix(tint('wardrobe', nap.add(weave).add(.52)), gold, borderStitch.mul(stitchDash).mul(.8));

  // Porcelain stays ivory on the racing line with teal shoulder tiles and quiet mint inserts.
  const tilePeriod = fit(4.5), tileJoints = max(seam(x, 4.5, .04), seam(s, tilePeriod, .04));
  const tileTone = wave(x, 9).mul(wave(s, fit(9))).mul(.06).add(.88);
  const tileBevel = max(seam(x, 4.5, .16), seam(s, tilePeriod, .16)).sub(tileJoints).max(0);
  const porcelain = mix(rgb('#b5d0c3'), rgb(BOUDOIR_SURFACES.spa.light), tileTone)
    .mul(one().sub(tileJoints.mul(.36)).sub(tileBevel.mul(.085)));

  // Wide stone slabs and faint continuous mineral veins, independent of scrolling effects.
  const veinCoord = x.add(s.mul(18 / fit(230))).add(sin(s.mul(TAU / fit(73))).mul(1.7));
  const mineralCoord = veinCoord.add(sin(s.mul(TAU / fit(21))).mul(.22));
  const veins = seam(mineralCoord, 18, .18).mul(.31);
  const mineralHalo = seam(mineralCoord, 18, .65).sub(veins).max(0).mul(.025);
  const stoneJoints = max(seam(x, 8, .025), seam(s, fit(12), .025));
  const stone = tint('mirror', wave(s.add(x), fit(38)).mul(.035).add(.83))
    .mul(one().sub(veins).sub(mineralHalo).sub(stoneJoints.mul(.18)));

  const patterns = { vanity: wood, gallery, wardrobe: satin, spa: porcelain, mirror: stone };
  const shoulder = one().sub(smoothstep(.55, BOUDOIR_FINISH.borderM, trueEdge));
  const h = BOUDOIR_FINISH.transitionM / 2, roadS = fract(s.div(lap)).mul(lap);
  const weights = zones.map((zone, i) => (i ? smoothstep(zone.s0 - h, zone.s0 + h, roadS) : one())
    .mul(i < zones.length - 1 ? one().sub(smoothstep(zone.s1 - h, zone.s1 + h, roadS)) : one()));
  // Wide relief changes the real lighting normal: waxed boards, soft nap and convex glazed tiles.
  // The height has no derivative filtering inside it, so the normal calculation remains defined.
  const tileX = fract(x.div(4.5)), tileS = fract(s.div(tilePeriod));
  const tileEdge = min(min(tileX, one().sub(tileX)).mul(4.5), min(tileS, one().sub(tileS)).mul(tilePeriod));
  let height = sin(flameCoord.mul(TAU / 1.8)).mul(.012);
  for (const [i, zone] of zones.entries()) {
    if (zone.kind === 'wardrobe') height = mix(height, sin(x.mul(TAU / 8)).mul(.075), weights[i]);
    if (zone.kind === 'spa') height = mix(height, smoothstep(.035, .25, tileEdge).mul(.055), weights[i]);
  }
  const finishNormal = reliefNormal(normal, height, max(footprint(x), footprint(s)), { slope: .3 });
  // Different highlight widths give wax, fibres and glaze their own finish, without extra lights.
  const H = normalize(normalize(vec3(.35, .8, -.3)).add(view));
  const halfLight = max(dot(finishNormal, H), 0), grazing = pow(one().sub(abs(dot(finishNormal, view))), 4);
  const reliefLight = max(dot(finishNormal, normalize(vec3(.35, .8, -.3))), 0).mul(.28).add(.76);
  const finishes = {
    vanity: pow(halfLight, 22).mul(one().sub(boards.mul(.8))).mul(.8).add(grazing.mul(.2)),
    gallery: pow(halfLight, 30).mul(one().sub(parquetJoints.mul(.85))).mul(.8).add(grazing.mul(.2)),
    wardrobe: pow(halfLight, 8).mul(nap.add(.88)).mul(.35).add(grazing.mul(.65)),
    spa: pow(halfLight, 48).mul(one().sub(tileJoints.mul(.9))).mul(.85).add(grazing.mul(.15)),
    mirror: pow(halfLight, 36).mul(one().sub(stoneJoints.mul(.8))).mul(.8).add(grazing.mul(.2)),
  };
  let colour = vec3(0), sheenGain = float(0);
  for (const [i, zone] of zones.entries()) {
    const weight = weights[i];
    const border = zone.kind === 'spa' ? one().sub(smoothstep(3.5, 5.5, trueEdge)) : shoulder;
    colour = colour.add(mix(patterns[zone.kind], rgb(BOUDOIR_SURFACES[zone.kind].border), border.mul(.78)).mul(weight));
    sheenGain = sheenGain.add(weight.mul(BOUDOIR_SURFACES[zone.kind].sheen).mul(finishes[zone.kind]));
  }
  return { colour: colour.mul(reliefLight), sheen: rgb('#f7e8d1').mul(sheenGain), zones };
}
