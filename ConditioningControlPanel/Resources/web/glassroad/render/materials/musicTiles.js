/* Nodes for fixed, shallow glazed panes. Built with the road before warming, never a new draw pass. */
import { float, vec3, fract, floor, sin, min, max, abs, clamp, mix, smoothstep, step, fwidth, normalize, dot, pow, normalWorld } from 'three/tsl';
import { MUSIC_TILES as T, TILE_COLOURS, musicTileLayout } from '../musicTilesMath.js';
import { LOOK } from '../look.js';

const TAU = Math.PI * 2;
const oneMinus = (v) => float(1).sub(v);
const aaIndex = (x, px) => {
  const n = floor(x.add(0.5)), h = clamp(px.mul(0.75), 1e-4, 0.5);
  return n.sub(1).add(smoothstep(h.negate(), h, x.sub(n)));
};
const line = (d, width, px, period) => {
  const w = max(max(float(width), px), 1e-6);
  const coverage = oneMinus(smoothstep(0, w, d)).mul(float(width).div(w));
  return mix(coverage, width / period, smoothstep(T.meltFrom, T.meltTo, px.div(period)));
};

export function createMusicTiles({ track, p, trueEdge, music: mu, view, distance }) {
  const layout = musicTileLayout(track);
  if (!layout) return { mask: float(0), colour: vec3(...TILE_COLOURS[0]), relief: float(1), glaze: float(0), opacity: float(T.opacity) };
  const s = fract(p.y.sub(layout.startS).div(layout.lapM).add(0.5)).sub(0.5).mul(layout.lapM);
  let mask = smoothstep(layout.fromM, layout.fromM + T.fadeM, s)
    .mul(oneMinus(smoothstep(layout.toM - T.fadeM, layout.toM, s)))
    .mul(smoothstep(T.shoulderM, T.shoulderM + T.shoulderFadeM, trueEdge));
  for (const pad of layout.pads) {
    const a = oneMinus(smoothstep(pad.len / 2 + T.padMarginM, pad.len / 2 + T.padMarginM + T.padFadeM, abs(s.sub(pad.s))));
    const b = oneMinus(smoothstep(pad.w / 2 + T.padMarginM, pad.w / 2 + T.padMarginM + T.padFadeM, abs(p.x.sub(pad.x))));
    mask = mask.mul(oneMinus(a.mul(b)));
  }

  // Only these raw metre coordinates define the panes. Neither song time nor road flow moves a joint.
  const x = p.x.div(T.cellX).add(0.5), y = s.div(T.cellS).add(0.5);
  const fx = fract(x), fy = fract(y), px = fwidth(p.x), py = fwidth(s);
  const cxPx = px.div(T.cellX), cyPx = py.div(T.cellS);
  const detail = oneMinus(smoothstep(T.meltFrom, T.meltTo, max(cxPx, cyPx)));
  const near = smoothstep(T.nearFromM, T.nearToM, distance);
  const dx = min(fx, oneMinus(fx)).mul(T.cellX), dy = min(fy, oneMinus(fy)).mul(T.cellS);
  const joint = max(line(dx, T.jointM, px, T.cellX), line(dy, T.jointM, py, T.cellS).mul(near));

  // Adjacent colours share one luminance, with soft pixel joins and a flat average in the distance.
  const phase = aaIndex(x, cxPx).mul(0.17).add(aaIndex(y, cyPx).mul(0.11));
  const slow = sin(phase.add(mu.tileBar.mul(T.barTurns).mul(mu.tileOn)).mul(TAU)).mul(0.5).add(0.5);
  const fast = sin(mu.tileBeat.mul(T.beatTurns).sub(phase).mul(TAU)).mul(T.beatShare).mul(mu.tileOn).mul(oneMinus(mu.tileGentle));
  const hue = clamp(slow.mul(0.64).add(0.18).add(fast), 0, 1);
  const pearl = oneMinus(slow).mul(mix(0.18, 0.32, mu.tileGentle)).add(0.12);
  const coloured = mix(mix(vec3(...TILE_COLOURS[0]), vec3(...TILE_COLOURS[1]), hue), vec3(...TILE_COLOURS[2]), pearl);
  const mean = vec3(...TILE_COLOURS[0].map((v, i) => (v + TILE_COLOURS[1][i]) * 0.395 + TILE_COLOURS[2][i] * 0.21));
  const colour = mix(mean, coloured, detail);

  // A shallow bevel normal catches the same light as the road. Narrow joints never become hard bands.
  const bevel = (d, f, pixel) => smoothstep(0, T.bevelM, d).mul(oneMinus(smoothstep(T.bevelM, T.bevelM * 2, d)))
    .mul(oneMinus(step(0.5, f).mul(2))).mul(float(T.bevelM).div(max(float(T.bevelM), pixel))).mul(T.bevelSlope).mul(detail);
  const bx = bevel(dx, fx, px), by = bevel(dy, fy, py).mul(near);
  const N = normalize(normalWorld.sub(vec3(...layout.lateral).mul(bx)).sub(vec3(...layout.forward).mul(by)));
  const light = normalize(vec3(...LOOK.sheenDir)), H = normalize(light.add(view));
  // A specular lobe alone misses the usual driving view. Fixed opposing light/shadow on the bevel
  // makes the shallow moulding readable there; it fades with the existing pixel/near attenuation.
  const moulding = dot(N, light).sub(dot(normalWorld, light)).mul(T.bevelLight).mul(mix(1, 0.5, mu.tileGentle));
  const glaze = pow(max(dot(N, H), 0), T.glazePower).mul(T.glazeGain)
    .add(pow(oneMinus(abs(dot(N, view))), 5).mul(T.glazeFresnel));
  return { mask, colour, relief: oneMinus(joint.mul(T.jointDark)).add(moulding), glaze, opacity: float(T.opacity) };
}
