/* Fixed musical panes on the starting terrace. Distances are road metres, colours linear RGB. */
import { hexRgb, roadLevel } from './spectrum.js';
import { smoothstep, aaLine } from './roadAaMath.js';

// Shared by width texture encode/decode, including the broad platform approaches.
export const ROAD_HALF_WIDTH_RANGE = 120;
export const encodeRoadHalfWidth = (half) => Math.round(Math.max(0, Math.min(1, half / ROAD_HALF_WIDTH_RANGE)) * 255);

export const MUSIC_TILES = Object.freeze({
  fromM: -48, toM: 84, fadeM: 10, sampleM: 2,
  cellX: 3, cellS: 6, jointM: 0.045, bevelM: 0.16, bevelSlope: 0.22,
  bevelLight: 0.7, glazePower: 10, glazeGain: 0.16, glazeFresnel: 0.09,
  shoulderM: 1.85, shoulderFadeM: 0.5, padMarginM: 0.7, padFadeM: 1.2,
  jointDark: 0.12, courtJointDark: 0.38, opacity: 0.92, luminance: 0.29,
  meltFrom: 0.22, meltTo: 0.7, nearFromM: 3, nearToM: 9,
  barTurns: 1 / 8, beatTurns: 1 / 2, beatShare: 0.24,
});

const linear = (v) => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
export const TILE_COLOURS = Object.freeze(['#ee6eaa', '#5bcfdc', '#e8dfe6']
  .map((h) => Object.freeze(roadLevel(hexRgb(h), MUSIC_TILES.luminance, 0).map(linear))));

/** Signed distance around a closed lap, continuous across the start line. */
export function tileDistance(s, startS, lapM) {
  return ((s - startS + lapM / 2) % lapM + lapM) % lapM - lapM / 2;
}

/** Compile a small static layout once. A different track must also have a safe starting straight. */
export function musicTileLayout(track) {
  if (!track || !(track.lengthM > 2 * (MUSIC_TILES.toM - MUSIC_TILES.fromM)) || !track.nodeFlags) return null;
  let start = -1;
  for (let i = 0; i < track.nodeCount; i++) if (track.nodeFlags(i).includes('start')) { start = i; break; }
  if (start < 0) return null;
  const startS = track.nodeS[start], origin = track.frameAt(startS);
  const safe = (d) => {
    const f = track.frameAt(startS + d), flags = track.nodeFlags(f.seg);
    const alignment = f.t.reduce((v, n, i) => v + n * origin.t[i], 0);
    const normal = f.n.reduce((v, n, i) => v + n * origin.n[i], 0);
    return alignment >= 0.993 && normal >= 0.985 && f.w >= 13
      && Math.abs(f.p[1] - origin.p[1]) < 0.8 && !flags.some((v) => ['open', 'jump', 'magnet'].includes(v));
  };
  const reach = (limit) => {
    const sign = Math.sign(limit);
    let result = 0;
    for (let d = MUSIC_TILES.sampleM; d <= Math.abs(limit); d += MUSIC_TILES.sampleM) {
      if (!safe(d * sign)) break;
      result = d * sign;
    }
    return result;
  };
  const fromM = reach(MUSIC_TILES.fromM), toM = reach(MUSIC_TILES.toM);
  if (toM - fromM < MUSIC_TILES.fadeM * 3) return null;
  const layout = { startS, lapM: track.lengthM, fromM, toM, lateral: origin.b.slice(), forward: origin.t.slice(), pads: [] };
  for (const pad of track.boosts || []) {
    const s = tileDistance(pad.s, startS, track.lengthM);
    if (s + pad.len / 2 >= fromM - 3 && s - pad.len / 2 <= toM + 3) layout.pads.push({ s, x: pad.x, w: pad.w, len: pad.len });
  }
  return layout;
}

/** Court tiles share the song uniforms and shader, but use their own transported bevel frame. */
export function courtTileLayouts(track) {
  return (track?.courts || []).map((c) => {
    const startS = (c.fromS + c.toS) / 2, f = track.frameAt(startS);
    return { startS, lapM: track.lengthM, fromM: c.fromS - startS, toM: c.toS - startS,
      lateral: [...f.b], forward: [...f.t], pads: (track.boosts || [])
        .filter((p) => p.s + p.len / 2 >= c.fromS && p.s - p.len / 2 <= c.toS)
        .map((p) => ({ s: p.s - startS, x: p.x, w: p.w, len: p.len })) };
  });
}

/** CPU reference for the shader's soft mask, including the quiet shoulder and pad underlay. */
export function musicTileMask(layout, s, x, halfWidth) {
  if (!layout) return 0;
  const T = MUSIC_TILES, d = tileDistance(s, layout.startS, layout.lapM);
  let mask = smoothstep(layout.fromM, layout.fromM + T.fadeM, d)
    * (1 - smoothstep(layout.toM - T.fadeM, layout.toM, d))
    * smoothstep(T.shoulderM, T.shoulderM + T.shoulderFadeM, halfWidth - Math.abs(x));
  for (const pad of layout.pads) {
    const insideS = 1 - smoothstep(pad.len / 2 + T.padMarginM, pad.len / 2 + T.padMarginM + T.padFadeM, Math.abs(d - pad.s));
    const insideX = 1 - smoothstep(pad.w / 2 + T.padMarginM, pad.w / 2 + T.padMarginM + T.padFadeM, Math.abs(x - pad.x));
    mask *= 1 - insideS * insideX;
  }
  return mask;
}

/** Colour coefficients, not brightness. No song is static; Gentle retains only the slow bar colour. */
export function musicTileColour(beat, bar, on, gentle, cellX = 0, cellS = 0) {
  const T = MUSIC_TILES, phase = cellX * 0.17 + cellS * 0.11;
  const slow = Math.sin((phase + (on ? bar * T.barTurns : 0)) * Math.PI * 2) * 0.5 + 0.5;
  const fast = on && !gentle ? Math.sin((beat * T.beatTurns - phase) * Math.PI * 2) * T.beatShare : 0;
  const hue = Math.max(0, Math.min(1, 0.18 + slow * 0.64 + fast));
  const pearl = 0.12 + (1 - slow) * (gentle ? 0.32 : 0.18);
  return TILE_COLOURS[0].map((v, i) => (v + (TILE_COLOURS[1][i] - v) * hue) * (1 - pearl) + TILE_COLOURS[2][i] * pearl);
}

/** Reference joint coverage for distance/readability checks, with the same pixel-energy rule as the road. */
export function musicTileJoint(x, s, pxX, pxS, distance) {
  const T = MUSIC_TILES, settings = { lineMul: 1, meltFrom: T.meltFrom, meltTo: T.meltTo };
  const edge = (v, cell) => { const f = v / cell + 0.5 - Math.floor(v / cell + 0.5); return Math.min(f, 1 - f) * cell; };
  return Math.max(aaLine(edge(x, T.cellX), T.jointM, pxX, T.cellX, settings),
    aaLine(edge(s, T.cellS), T.jointM, pxS, T.cellS, settings) * smoothstep(T.nearFromM, T.nearToM, distance));
}
