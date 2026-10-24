/* Static Boudoir finishes in road metres. Pattern detail filters out before it can crawl. */
import { Color } from 'three/webgpu';
import { float, vec3, sin, abs, fract, min, max, mix, smoothstep, fwidth, normalize, dot, pow } from 'three/tsl';
import { getBoudoirLayout } from '../track/boudoir-layout.js';

export const BOUDOIR_SURFACES = Object.freeze({
  vanity: Object.freeze({ name: 'rosewood', base: '#874b53', dark: '#542935', light: '#ab7273', border: '#542935', sheen: .025 }),
  gallery: Object.freeze({ name: 'ivory parquet', base: '#d4c3a4', dark: '#bba27b', light: '#eadbc0', border: '#322538', sheen: .035 }),
  wardrobe: Object.freeze({ name: 'stitched satin', base: '#7f294a', dark: '#55192f', light: '#9f4968', border: '#42182e', sheen: .035 }),
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
const one = () => float(1);
const TAU = Math.PI * 2;
/** No textures, clocks or external resources. Water and music composite after this dry base. */
export function createBoudoirRoadSurface({ track, p, trueEdge, distance, view, normal }) {
  const zones = boudoirSurfaceZones(track); if (!zones.length) return null;
  const lap = track.lengthM, fit = desired => lap / Math.max(1, Math.round(lap / desired));
  const x = p.x, s = p.y, far = one().sub(smoothstep(BOUDOIR_FINISH.detailNearM, BOUDOIR_FINISH.detailFarM, distance));
  const detail = (coord, period) => one().sub(smoothstep(BOUDOIR_FINISH.footprintFrom, BOUDOIR_FINISH.footprintTo,
    fwidth(coord).div(period))).mul(far);
  const wave = (coord, period) => sin(coord.mul(TAU / period)).mul(detail(coord, period));
  const seam = (coord, period, width) => {
    const d = min(fract(coord.div(period)), one().sub(fract(coord.div(period)))).mul(period);
    const px = max(fwidth(coord).mul(1.15), width);
    return one().sub(smoothstep(0, px, d)).mul(float(width).div(px)).mul(detail(coord, period));
  };
  const tint = (kind, factor) => mix(rgb(BOUDOIR_SURFACES[kind].dark), rgb(BOUDOIR_SURFACES[kind].light), factor);
  const gold = rgb('#c6a168');

  // Long staggered boards have broad grain and narrow recessed joints.
  const boardShift = sin(x.mul(TAU / 5.6)).mul(1.1);
  const grainCoord = x.add(sin(s.mul(TAU / fit(44))).mul(.12));
  const grain = wave(grainCoord, .62).mul(.045).add(wave(grainCoord, 1.8).mul(.06));
  const boards = seam(x, 2.8, .035).add(seam(s.add(boardShift), fit(9.5), .028)).min(1);
  const wood = tint('vanity', grain.add(.52)).mul(one().sub(boards.mul(.35)));

  // Diagonal parquet blocks use two broad grain directions and restrained gold inlay.
  const diagonalA = s.add(x), diagonalB = s.sub(x), parquetPeriod = fit(6);
  const parquet = wave(diagonalA, parquetPeriod * 2).mul(wave(diagonalB, parquetPeriod * 2));
  const parquetJoints = max(seam(diagonalA, parquetPeriod, .045), seam(diagonalB, parquetPeriod, .045));
  const parquetGrain = wave(x.add(s.mul(6 / fit(6))), .8).mul(.022);
  const gallery = mix(tint('gallery', parquet.mul(.14).add(parquetGrain).add(.62)), gold, parquetJoints.mul(.65));

  // Fabric keeps a broad velvet nap. Small weave and stitched borders fade by footprint.
  const nap = wave(x.add(sin(s.mul(TAU / fit(84))).mul(.3)), 8).mul(.12);
  const weave = wave(x.add(s.mul(.6 / fit(.6))), .6).mul(.026);
  const stitchDash = wave(s, fit(.9)).mul(.5).add(.5);
  const borderStitch = seam(trueEdge.sub(.65), 1.1, .045).mul(one().sub(smoothstep(1.5, 2.5, trueEdge)));
  const satin = mix(tint('wardrobe', nap.add(weave).add(.52)), gold, borderStitch.mul(stitchDash).mul(.8));

  // Porcelain stays ivory on the racing line with teal shoulder tiles and quiet mint inserts.
  const tilePeriod = fit(4.5), tileJoints = max(seam(x, 4.5, .04), seam(s, tilePeriod, .04));
  const tileTone = wave(x, 9).mul(wave(s, fit(9))).mul(.06).add(.88);
  const porcelain = mix(rgb('#b5d0c3'), rgb(BOUDOIR_SURFACES.spa.light), tileTone)
    .mul(one().sub(tileJoints.mul(.22)));

  // Wide stone slabs and faint continuous mineral veins, independent of scrolling effects.
  const veinCoord = x.add(s.mul(18 / fit(230))).add(sin(s.mul(TAU / fit(73))).mul(1.7));
  const veins = seam(veinCoord, 18, .18).mul(.22);
  const stoneJoints = max(seam(x, 8, .025), seam(s, fit(12), .025));
  const stone = tint('mirror', wave(s.add(x), fit(38)).mul(.035).add(.83))
    .mul(one().sub(veins).sub(stoneJoints.mul(.18)));

  const patterns = { vanity: wood, gallery, wardrobe: satin, spa: porcelain, mirror: stone };
  const shoulder = one().sub(smoothstep(.55, BOUDOIR_FINISH.borderM, trueEdge));
  const h = BOUDOIR_FINISH.transitionM / 2, roadS = fract(s.div(lap)).mul(lap);
  let colour = vec3(0), sheenGain = float(0);
  for (const [i, zone] of zones.entries()) {
    const weight = (i ? smoothstep(zone.s0 - h, zone.s0 + h, roadS) : one())
      .mul(i < zones.length - 1 ? one().sub(smoothstep(zone.s1 - h, zone.s1 + h, roadS)) : one());
    const border = zone.kind === 'spa' ? one().sub(smoothstep(3.5, 5.5, trueEdge)) : shoulder;
    colour = colour.add(mix(patterns[zone.kind], rgb(BOUDOIR_SURFACES[zone.kind].border), border.mul(.78)).mul(weight));
    sheenGain = sheenGain.add(weight.mul(BOUDOIR_SURFACES[zone.kind].sheen));
  }
  const H = normalize(normalize(vec3(.35, .8, -.3)).add(view));
  const broadSheen = pow(max(dot(normal, H), 0), 18).mul(.7)
    .add(pow(one().sub(abs(dot(normal, view))), 4).mul(.3));
  return { colour, sheen: rgb('#f7e8d1').mul(broadSheen).mul(sheenGain), zones };
}
