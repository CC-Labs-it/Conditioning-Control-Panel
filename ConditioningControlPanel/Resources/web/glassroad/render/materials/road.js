/* render/materials/road.js - the glass ribbon. uv: u = lateral metres, v = s metres (CONTRACT.md MeshData).
 *
 * Polish 3 (SPECTRUM lane): the pattern runs ALONG the road and streams toward the player. Colour is the
 * full-spectrum ramp (pal.ramp, render/spectrum.js); the pattern coordinate is a = v + pal.flow, and
 * pal.flow grows with kart speed, so the floor rushes past faster the faster you go. Each section runs
 * its own pattern list (spectrum.js SECTION_PATTERNS: streaming lanes, flowing chevrons, a warped
 * checker, scrolling spiral rings, a kaleidoscope weave); one zone morphs into the next with a V sweep
 * (the edges lead: an arrow pointing up the road) read from a per-metre table (spectrum.js patternBytes). Every along-the-road period is
 * fitted to the lap (fitPeriod) so nothing jumps at the line or when the flow wraps.
 * Readability: a steady centre line and a steady bright edge lane frame every pattern; lines that cross
 * the road fade in from nearCalmM so nothing big strobes right under the kart; the road reads the
 * road-levelled ramp (pal.rampRoad: every hue inside a narrow luminance band, spectrum.js roadLevel), so
 * a chevron, checker or weave block streaming past at any speed is a hue change, never a flash. Gentle: contrast (0.5) flattens toward the palette and
 * pal.motion halves the flow and hue clock.
 *
 * Kept from polish 2, scaled by `sheen` so Gentle halves them: the glass sheen (road-glass-normal), a
 * Schlick sky reflection (now spectrum tinted), glitter glints, the edge rim and a static depth layer
 * under the surface (road-bands-mask turned to run along the road when the art is in).
 */
import { MeshBasicNodeMaterial, DoubleSide, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, NearestFilter, RepeatWrapping, ClampToEdgeWrapping } from 'three/webgpu';
import { uniform, uv, sin, fract, mix, smoothstep, float, vec2, vec3, texture, normalWorld, positionWorld, cameraPosition,
  normalize, dot, pow, abs, clamp, max, min, reflect, floor, hash, length, atan, step, exp, select, fwidth } from 'three/tsl';
import { LOOK } from '../look.js';
import { SPECTRUM_LOOK, patternZones, patternBytes, fitPeriod } from '../spectrum.js';
import { ROAD_AA } from '../roadAaMath.js';

/** Road polish numbers; LOOK.roadPolish overrides any key. */
export const ROAD_POLISH = Object.freeze({
  depthTile: 2.2, depthGain: 0.14, depthLaneM: 4,
  reflGain: 0.35, reflF0: 0.04, reflBump: 0.12,
  glintCellM: 0.38, glintDensity: 0.07, glintSizeM: 0.03, glintTwinkle: 4, glintPow: 6, glintGain: 0.45,
  glintNearM: [3, 7], glintFarM: [22, 34],
  rimInM: 1.4, rimGain: 0.3, halfWidthM: 7,
});

const TAU = Math.PI * 2;

// widen-and-melt (render/roadAaMath.js has the same shapes as numbers, tested): px is always fwidth of the
// coordinate a shape is drawn in, so every hard step and thin line is about a pixel soft and melts to its
// average once its cells pass a pixel. Near the kart (px tiny) each shape is what it was.
const AA = ROAD_AA;
const meltN = (px) => smoothstep(AA.meltFrom, AA.meltTo, px);
/** Line coverage: d distance from the centre, w the half width, px fwidth of d, cell the repeat period (0: none). */
function aaLineN(d, w, px, cell = 0) {
  const wN = float(w), wE = max(max(wN, px.mul(AA.lineMul)), 1e-6);
  const cov = float(1).sub(smoothstep(float(0), wE, d)).mul(wN.div(wE));
  return cell > 0 ? mix(cov, Math.min(1, w / cell), meltN(px.div(cell))) : cov;
}
/** floor(x) with each step blended over about a pixel; m melts it to the ramp x - 0.5. */
function aaFloorN(x, px, m) {
  const n = floor(x.add(0.5)), h = clamp(px.mul(AA.stepMul), 1e-4, 0.5);
  return mix(n.sub(1).add(smoothstep(h.negate(), h, x.sub(n))), x.sub(0.5), m);
}
/** floor(x) mod 2 blended the same way (0.5 once melted). */
function aaParityN(x, px) {
  const m = meltN(px);
  return aaFloorN(x, px, m).sub(aaFloorN(x.mul(0.5), px.mul(0.5), m).mul(2));
}
const xorN = (p, q) => p.add(q).sub(p.mul(q).mul(2));
const cellD = (f) => min(f, float(1).sub(f));

/** Music road shader numbers (the per-frame amounts come from render/musicRoadMath.js). */
export const MUSIC_ROAD = Object.freeze({
  nearM: [4, 14], vibLambdaM: 7, vibHz: 2.2,
  edgeMps: 30, melodyLambdaM: 9, melodyHz: 1.5,
  shardM: 2.8, crackW: [0.02, 0.1], shardHue: 0.45,
  rippleMps: 16, rippleW: 0.7, waveW: 6, waveLight: 0.35,
  calmFlatten: 0.55, calmLines: 0.6, calmSheen: 0.6, bigContrast: 0.12, bigLines: 0.4, bassGlow: 0.5,
});

/** The road edge (owner, pass 2: "make the edges of the track clearer"): a crisp line on the true edge,
 * a dark gutter inside it, the lane colour held on the outer laneHoldM whatever the music does. */
export const ROAD_EDGE = Object.freeze({
  lineM: 0.28, aaPx: 1.5, aaCapMul: 4, lineTint: 0.18, lineGain: 1.15,
  gutterSoftM: 0.06, gutterM: 0.55, gutterDark: 0.5, gutterAlpha: 0.15,
  laneHoldM: [0.35, 0.55],
});

/** The music road's uniforms, every one at rest (no song = the road as it always was). */
function musicUniforms() {
  return { t: uniform(0), kartS: uniform(0), vib: uniform(0), melody: uniform(0), bassW: uniform(0),
    ripAge: uniform(9), ripAmp: uniform(0), waveD: uniform(0), waveAmp: uniform(0), shatter: uniform(0), flash: uniform(0),
    echoL: uniform(0), echoR: uniform(0), calm: uniform(0), big: uniform(0), eFps: uniform(25), eW: uniform(1) };
}

// one silent texel until a song arrives
const ENERGY_REST = (() => {
  const t = new DataTexture(new Uint8Array(4), 1, 1, RGBAFormat, UnsignedByteType);
  t.magFilter = t.minFilter = LinearFilter; t.needsUpdate = true;
  return t;
})();

/** A song's energy curves as one RGBA row (R mix, G drums, B bass, A vocals), at most 8192 texels. */
export function energyTexture(song) {
  const e = song.energy, n = Math.max(e.mix.length, e.vocals.length, 1), w = Math.min(n, 8192), data = new Uint8Array(w * 4);
  const at = (a, i) => (a.length ? a[Math.min(a.length - 1, Math.floor(i * n / w))] : 0);
  for (let i = 0; i < w; i++) { data[i * 4] = at(e.mix, i); data[i * 4 + 1] = at(e.drums, i); data[i * 4 + 2] = at(e.bass, i); data[i * 4 + 3] = at(e.vocals, i); }
  const t = new DataTexture(data, w, 1, RGBAFormat, UnsignedByteType);
  t.wrapS = t.wrapT = ClampToEdgeWrapping; t.magFilter = t.minFilter = LinearFilter; t.generateMipmaps = false; t.needsUpdate = true;
  return { texture: t, width: w, fps: e.fps * w / n };
}

/** Half road width per metre of s as a 1-row texture (R = halfWidth / 16). */
function widthTexture(track) {
  const n = Math.max(2, Math.ceil(track.lengthM)), data = new Uint8Array(n * 4);
  const f = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let i = 0; i < n; i++) {
    const fr = track.frameAt((i + 0.5) * track.lengthM / n, f);
    data[i * 4] = Math.round(Math.min(1, fr.w / 2 / 16) * 255); data[i * 4 + 3] = 255;
  }
  const t = new DataTexture(data, n, 1, RGBAFormat, UnsignedByteType);
  t.wrapS = RepeatWrapping; t.wrapT = ClampToEdgeWrapping; t.magFilter = t.minFilter = LinearFilter; t.needsUpdate = true;
  return t;
}

/** The per-metre pattern table (spectrum.js patternBytes), nearest sampled. */
function patternTexture(track, S) {
  const n = Math.max(2, Math.ceil(track.lengthM));
  const t = new DataTexture(patternBytes(patternZones(track.sections), track.lengthM, S.morphM), n, 1, RGBAFormat, UnsignedByteType);
  t.wrapS = RepeatWrapping; t.wrapT = ClampToEdgeWrapping; t.magFilter = t.minFilter = NearestFilter; t.generateMipmaps = false; t.needsUpdate = true;
  return t;
}

/**
 * The five patterns at lateral u, stream coordinate a (both metres). Returns [{ x, l }] in PATTERNS order:
 * x a ramp coordinate (wraps), l a line / glow share 0..1. calm fades lines that cross the road.
 */
function patterns(u, a, per, S, calm, spin) {
  const au = abs(u);
  // every line (lane, checker, ring, weave) is a cell line: widened to a pixel, melted past one (aaLineN)
  const edgeLine = (f, w, px) => aaLineN(cellD(f), w, px, 1);
  // 0 streaming lanes: hue flows down each lane; steady lane lines; light packets run toward you
  const lu = u.div(S.laneW), k = floor(lu), e = fract(lu), luPx = fwidth(lu);
  const line0 = aaLineN(cellD(e).mul(S.laneW), S.laneLineW, luPx.mul(S.laneW), S.laneW);
  const pa = a.div(per.packet), pf = fract(pa.add(hash(k.add(11.3)))), pk = float(1).sub(pf);
  // the packet's head is the fract wrap (a hard step): blended over a pixel of the stream coordinate
  const head = smoothstep(0, max(fwidth(pa).mul(AA.lineMul * 1.5), 1e-4), pf);
  const packet = pow(smoothstep(1 - S.packetLen, 1, pk), 2).mul(head).mul(float(1).sub(smoothstep(0.18, 0.42, abs(e.sub(0.5)))))
    .mul(step(1 - S.packetShare, hash(k.add(3.7))));
  // the lane hue steps at each lane line (the line covers the join; far off the steps blend and melt)
  const p0 = { x: aaFloorN(lu, luPx, meltN(luPx)).mul(S.laneHueStep).add(a.div(per.ramp)), l: max(line0.mul(0.8), packet.mul(S.packetGain / S.lineGain)) };
  // 1 chevrons pointing up the road: a solid hue per chevron (chevronHueSteps whole chevron lengths of
  // ramp apart, so the wrap stays seamless), a hot leading edge, a soft smear behind
  const cv = a.add(au.mul(S.chevronSlope)).div(per.chevron), cf = fract(cv), cm = cf.mul(per.chevron);
  const cvPx = fwidth(cv), cvMelt = meltN(cvPx), cvF = aaFloorN(cv, cvPx, cvMelt);
  // the hard side of the leading edge (the wrap, cf 1 to 0) blended over a pixel; far off the run melts flat
  const ahead = float(1).sub(smoothstep(0, max(cvPx.mul(AA.lineMul), 1e-4), float(1).sub(cf)));
  const chev = max(max(float(1).sub(smoothstep(0, S.chevronEdgeM, cm)), float(1).sub(smoothstep(0, per.chevron * 0.5, cm)).mul(0.3)), ahead);
  const p1 = { x: cvF.add(smoothstep(0, 1, clamp(cv.sub(cvF), 0, 1)).mul(0.4)).mul(S.chevronHueSteps * per.chevron / per.ramp).add(au.mul(0.01)),
    l: mix(chev, Math.min(1, (S.chevronEdgeM * 0.5 + per.chevron * 0.075) / per.chevron), cvMelt).mul(calm) };
  // 2 warped checker: two hue families half a ramp apart, swapping cell by cell
  const uw = u.add(sin(a.mul(TAU / per.warpA).add(u.mul(0.3))).mul(0.9));
  const aw = a.add(sin(u.mul(0.55).add(a.mul(TAU / per.warpB))).mul(1.4));
  const cx = uw.div(S.checkerM), cy = aw.div(per.checker), cxPx = fwidth(cx), cyPx = fwidth(cy);
  const par = xorN(aaParityN(cx, cxPx), aaParityN(cy, cyPx));
  const p2 = { x: par.mul(0.5).add(aaFloorN(cy, cyPx, meltN(cyPx)).mul(per.checker / per.ramp)),
    l: max(edgeLine(fract(cx), 0.03, cxPx), edgeLine(fract(cy), 0.03, cyPx).mul(calm)) };
  // 3 spiral rings: a two-armed spiral every ring cell, turning inward (clockwise from above)
  const cc = fract(a.div(per.ring)), cvr = cc.sub(0.5).mul(per.ring);
  const r = length(vec2(u, cvr)), ang = atan(u, cvr);
  const sp = r.div(S.ringW).sub(ang.mul(S.ringArms / TAU)).add(spin);
  const seamFade = float(1).sub(smoothstep(0.3, 0.48, abs(cc.sub(0.5))));
  // colour: the plain flow plus a bounded spiral swing (a sine of the spiral phase, seamless across the
  // atan cut, faded out at the cell seams; never a mix of two far apart ramp coordinates, which would
  // sweep the whole ramp in a few metres); the lines keep to a disc of ringLineR so no flat arcs strobe past
  const swing = sin(r.mul(TAU / S.ringHueM).sub(ang)).mul(S.ringSwing).mul(seamFade);
  const p3 = { x: a.div(per.ramp).add(au.mul(0.05)).add(swing), l: edgeLine(fract(sp), 0.09, fwidth(sp)).mul(smoothstep(0.3, 1, r)).mul(smoothstep(S.ringLineR[0], S.ringLineR[1], r)).mul(seamFade).mul(calm) };
  // 4 kaleidoscope weave: mirrored across the centre, two diagonal strand sets going over and under
  const q1 = au.add(a).div(per.weave), q2 = a.sub(au).div(per.weave), qPx = max(fwidth(q1), fwidth(q2));
  const over = xorN(aaParityN(q1, qPx), aaParityN(q2, qPx));
  const strand = mix(fract(q2), fract(q1), over);
  const p4 = { x: over.mul(0.33).add(a.div(per.ramp)).add(au.mul(0.02)), l: edgeLine(strand, 0.08, qPx).mul(calm) };
  return [p0, p1, p2, p3, p4];
}

export function createRoadMaterial(pal, art = null, track = null) {
  const P = { ...ROAD_POLISH, ...(LOOK.roadPolish || {}) };
  const S = { ...SPECTRUM_LOOK, ...(LOOK.roadSpectrum || {}) };
  const u = { phase: uniform(0), contrast: uniform(1), bright: uniform(0), sheen: uniform(1), music: musicUniforms(), energyNode: null };
  const lapM = track && track.lengthM > 0 ? track.lengthM : 0;
  const fit = (b) => (lapM ? fitPeriod(lapM, b) : b);
  const per = { ramp: fit(S.rampM), packet: fit(S.packetM), chevron: fit(S.chevronM), checker: fit(S.checkerM),
    ring: fit(S.ringCellM), weave: fit(S.weaveM), warpA: fit(30), warpB: fit(54), spin: fit(12) };
  if (pal.flow) { if (lapM) pal.flowWrap = lapM; pal.motionSource = u.contrast; }
  const flow = pal.flow || u.phase;
  const p = uv(), uRaw = p.x;
  const a = p.y.add(flow);
  const toCam = cameraPosition.sub(positionWorld), dist = toCam.length(), V = normalize(toCam);
  const calm = smoothstep(S.nearCalmM[0], S.nearCalmM[1], dist);

  // music road (render/musicRoad.js sets these from the song clock; all at rest = the road as before).
  // Paint only: the lines shake and the edges draw, the surface the kart drives on never moves.
  const M = MUSIC_ROAD, mu = u.music, E = { ...ROAD_EDGE, ...(LOOK.roadEdge || {}) };
  const near = smoothstep(M.nearM[0], M.nearM[1], dist);   // right under the kart the lines move least
  const dAhead = lapM ? fract(p.y.sub(mu.kartS).div(lapM).add(0.5)).sub(0.5).mul(lapM) : p.y.sub(mu.kartS);
  const vib = sin(p.y.mul(TAU / M.vibLambdaM).sub(mu.t.mul(TAU * M.vibHz))).mul(mu.vib).mul(near.mul(0.65).add(0.35));
  const uL = uRaw.add(vib), au = abs(uL);
  // the edge lines draw the melody: a wave whose height is the vocals (and the mix) at the song time
  // that will reach the kart from that far ahead
  const fr = mu.t.add(dAhead.div(M.edgeMps)).mul(mu.eFps);
  const env = texture(ENERGY_REST, vec2(max(fr, 0).add(0.5).div(mu.eW), 0.5));
  u.energyNode = env;   // musicRoad.setSong swaps its .value for the song's energy row
  const melody = sin(dAhead.mul(TAU / M.melodyLambdaM).sub(mu.t.mul(TAU * M.melodyHz))).mul(env.a)
    .add(sin(dAhead.mul(TAU / (M.melodyLambdaM * 0.37))).mul(env.r).mul(0.5)).mul(mu.melody);
  // the trigger shatter: glass shards fixed to the road (a cell grid in road metres), hue shifted apart
  const cp = vec2(uRaw, p.y).div(M.shardM), ci = floor(cp), cf = fract(cp);
  let F1 = float(9), F2 = float(9), shardId = float(0);
  for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) {
    const cid = ci.add(vec2(ox, oy));
    const h1 = hash(cid.x.add(cid.y.mul(113.1))), h2 = hash(cid.x.mul(17.3).add(cid.y.mul(29.7)).add(5.1));
    const d = length(vec2(ox, oy).add(vec2(h1, h2)).sub(cf));
    F2 = min(F2, max(F1, d)); shardId = select(d.lessThan(F1), h1, shardId); F1 = min(F1, d);
  }
  const crack = float(1).sub(smoothstep(M.crackW[0], M.crackW[1], F2.sub(F1))).mul(mu.shatter).mul(near);
  const shardHue = shardId.sub(0.5).mul(M.shardHue).mul(mu.shatter);
  const ramp = pal.rampRoad ? (x) => pal.rampRoad(x) : pal.ramp ? (x) => pal.ramp(x) : (x) => mix(pal.col(0), pal.col(1), sin(x.mul(TAU)).mul(0.5).add(0.5));
  const skyRamp = pal.ramp ? (x) => pal.ramp(x) : ramp;

  // which pattern (a before the boundary, b after) and the V sweep between them
  const pats = patterns(uL, a, per, S, calm, flow.div(per.spin));
  let xa = float(0), xb = float(0), la = float(0), lb = float(0), tB = float(0);
  if (track) {
    const n = Math.max(2, Math.ceil(lapM)), step1 = lapM / n;
    const row = texture(patternTexture(track, S), vec2(p.y.div(lapM), 0.5));
    const ia = floor(row.r.mul(255 / 40).add(0.5)), ib = floor(row.g.mul(255 / 40).add(0.5));
    const centre = floor(p.y.div(step1)).add(0.5).mul(step1);
    const edgeS = centre.add(row.b.mul(255).sub(128).mul(0.5));
    tB = smoothstep(0, 1, clamp(p.y.sub(edgeS).add(au.mul(S.sweepSlope)).div(S.sweepSoftM).mul(0.5).add(0.5), 0, 1));
    pats.forEach((q, i) => {
      const wa = clamp(float(1).sub(abs(ia.sub(i))), 0, 1), wb = clamp(float(1).sub(abs(ib.sub(i))), 0, 1);
      xa = xa.add(q.x.mul(wa)); la = la.add(q.l.mul(wa)); xb = xb.add(q.x.mul(wb)); lb = lb.add(q.l.mul(wb));
    });
  } else { xa = xb = pats[0].x; la = lb = pats[0].l; }
  xa = xa.add(shardHue); xb = xb.add(shardHue);

  // edges and centre: a steady bright edge lane and a calm centre line frame the racing line; the music
  // breathes the edge lanes with the bass and moves the edge lines with the melody
  const half = track ? texture(widthTexture(track), vec2(p.y.div(lapM), 0.5)).r.mul(16) : float(P.halfWidthM);
  const toEdge = half.sub(au).sub(melody);
  const laneM = float(S.edgeLaneM).mul(mu.bassW.add(1));
  // the true edge never moves (no vibration, no melody): the lane's outer stretch stays lane colour there
  const trueEdge = half.sub(abs(uRaw));
  const edgeW = max(float(1).sub(smoothstep(laneM.mul(0.6), laneM, toEdge)), float(1).sub(smoothstep(E.laneHoldM[0], E.laneHoldM[1], trueEdge)));
  // the edge line: a crisp bright line ON the true edge with a dark gutter inside it, the same in every palette
  // and music mode; its width grows to at least a pixel and its light shrinks to match, so far off it never
  // shimmers (fwidth keeps the energy per pixel even)
  // capped: over a crest neighbouring pixels jump from the near road to the far road, fwidth explodes and an
  // uncapped line lit the whole strip white across the screen; past the cap the line fades out instead
  const px = fwidth(trueEdge).mul(E.aaPx), cap = E.lineM * E.aaCapMul;
  const lineW = max(float(E.lineM), min(px, cap));
  const crestFade = clamp(float(cap).div(max(px, 1e-4)), 0, 1).pow(2);
  const edgeLine = float(1).sub(smoothstep(lineW.mul(0.55), lineW, trueEdge)).mul(float(E.lineM).div(lineW).min(1).mul(0.6).add(0.4)).mul(crestFade);
  const gutter = smoothstep(lineW, lineW.add(E.gutterSoftM), trueEdge).mul(float(1).sub(smoothstep(lineW.add(E.gutterSoftM), lineW.add(E.gutterM), trueEdge))).mul(crestFade);
  // the centre line widens to a pixel and dims to match (same light per pixel), so far off it never crawls
  const cW = max(float(S.centreLineW), fwidth(au).mul(AA.lineMul * 1.5));
  const centre = float(1).sub(smoothstep(cW.mul(0.5), cW, au)).mul(float(S.centreLineW).div(cW));

  // modes: calm (a break) flattens toward clear glass, big (a drop, a peak) lifts the contrast and lines
  const con = u.contrast.mul(float(1).sub(mu.calm.mul(M.calmFlatten))).mul(mu.big.mul(M.bigContrast).add(1)).min(1.15);
  const patCol = mix(ramp(xa), ramp(xb), tB);
  const edgeCol = ramp(a.div(per.ramp).add(0.5));
  const avg = pal.col(0).mul(0.5).add(pal.col(1).mul(0.3)).add(pal.col(2).mul(0.2));
  const body = mix(avg, mix(patCol, edgeCol.mul(float(1 + S.edgeGain).add(mu.bassW.mul(M.bassGlow))), edgeW), con);
  // events as light in the lines: a kick ring running out to the edges, the drop's pressure wave down the
  // road, the shatter's cracks, an echo's shimmer on the edge of its side
  const ring = exp(au.sub(mu.ripAge.mul(M.rippleMps)).div(M.rippleW).pow(2).negate()).mul(mu.ripAmp);
  const wave = exp(dAhead.sub(mu.waveD).div(M.waveW).pow(2).negate()).mul(mu.waveAmp);
  const echo = mix(mu.echoR, mu.echoL, step(uL, float(0)));
  const shimmer = echo.mul(edgeW).mul(sin(p.y.mul(1.9).sub(mu.t.mul(11))).mul(0.5).add(0.5));
  const music = ring.mul(float(1).sub(edgeW)).add(wave).add(crack).add(shimmer);
  const lineV = mix(la, lb, tB).mul(float(1).sub(edgeW)).mul(con)
    .mul(float(1).sub(mu.calm.mul(M.calmLines))).mul(mu.big.mul(M.bigLines).add(1)).add(music);
  const hot = mix(vec3(1, 0.97, 0.99), body, 0.35);

  const bands = art && art.tex('road-bands-mask'), rough = art && art.tex('road-glass-roughness');
  const [tu, tv] = LOOK.artRoadTileM;
  const haze = rough ? mix(float(1 - LOOK.artHaze), float(1 + LOOK.artHaze), texture(rough, p.div(LOOK.artHazeTileM)).r) : float(1);
  const nrm = art && art.tex('road-glass-normal');
  let sheen = float(0), nt = vec2(0, 0);
  if (nrm) {
    nt = texture(nrm, p.div(LOOK.artHazeTileM)).xy.mul(2).sub(1);
    const H = normalize(normalize(vec3(...LOOK.sheenDir)).add(V));
    const ndh = clamp(dot(normalWorld, H).add(nt.x.add(nt.y).mul(LOOK.sheenBump * 0.5)), 0, 1);
    const fres = pow(float(1).sub(abs(dot(normalWorld, V))), 4).mul(LOOK.sheenFresnel);
    sheen = pow(ndh, LOOK.sheenPow).mul(LOOK.sheenGain).add(fres).mul(u.sheen);
  }

  // depth: a static, broad lane layer under the surface; the streaming top layer slides over it
  const deepF = bands
    ? fract(texture(bands, vec2(uL.div(tu * P.depthTile).add(0.37), p.y.div(tv * P.depthTile))).r.mul(LOOK.artBandSpan).add(0.33))
    : fract(floor(uL.div(P.depthLaneM)).mul(0.29).add(p.y.div(per.ramp * 2)));
  const deepCol = ramp(deepF).mul(P.depthGain).mul(u.contrast);

  // sky reflection along the bumped reflected ray, spectrum tinted
  const N = normalize(normalWorld.add(vec3(nt.x, 0, nt.y).mul(P.reflBump)));
  const R = reflect(V.negate(), N);
  const skyRef = skyRamp(R.y.mul(0.35).add(R.x.mul(0.2)).add(0.5)).mul(LOOK.skyDim);
  const schlick = float(P.reflF0).add(pow(float(1).sub(abs(dot(normalWorld, V))), 5).mul(1 - P.reflF0));
  const refl = skyRef.mul(schlick).mul(P.reflGain);

  // glitter: one candidate glint per cell, a few lit; each catches over a band of view angles
  const cell = floor(p.div(P.glintCellM));
  const h1 = hash(cell.x.add(cell.y.mul(157.31))), h2 = hash(cell.x.mul(7.13).add(cell.y.mul(311.7)).add(19.1));
  const jit = vec2(h1, h2).sub(0.5).mul(0.5);
  const inCell = fract(p.div(P.glintCellM)).sub(0.5).sub(jit).mul(P.glintCellM);
  const spot = float(1).sub(smoothstep(0, P.glintSizeM, length(inCell)));
  const lit = smoothstep(1 - P.glintDensity, 1, h1);
  const seed = vec3(h2.sub(0.5), h1.mul(0.7), float(0.5).sub(h1));
  const catchA = pow(max(sin(dot(V, seed).mul(P.glintTwinkle * TAU).add(h2.mul(TAU))), 0), P.glintPow);
  const range = smoothstep(P.glintNearM[0], P.glintNearM[1], dist).mul(float(1).sub(smoothstep(P.glintFarM[0], P.glintFarM[1], dist)));
  const glint = spot.mul(lit).mul(catchA).mul(range).mul(P.glintGain).mul(u.sheen);

  const rim = float(1).sub(smoothstep(0, P.rimInM, trueEdge)).mul(P.rimGain);
  const polish = refl.add(edgeCol.mul(0.6).add(0.4).mul(rim)).mul(u.sheen).mul(mu.calm.mul(M.calmSheen).add(1)).add(vec3(1, 0.97, 0.99).mul(glint));
  // u.bright carries the beat breath; the drop wave and the shatter add their own light (mu.flash is
  // photosafe gated by the caller, 0 when refused or under Gentle)
  const surface = body.mul(S.baseGain).add(hot.mul(lineV.mul(S.lineGain))).add(vec3(1, 0.95, 0.98).mul(centre.mul(S.centreGain).mul(u.contrast)))
    .mul(u.bright.add(1).add(wave.mul(M.waveLight)).add(mu.flash.mul(crack.add(0.25)))).mul(haze).mul(LOOK.roadGain);
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  // the edge line sits over everything; the gutter darkens what is inside it, so the line reads against any hue
  const litCol = surface.add(deepCol).add(vec3(1, 0.96, 0.98).mul(sheen)).add(polish).mul(float(1).sub(gutter.mul(E.gutterDark)));
  const lineCol = mix(vec3(1, 0.97, 0.99), edgeCol, E.lineTint).mul(E.lineGain);
  m.colorNode = mix(litCol, lineCol, edgeLine);
  m.opacityNode = max(clamp(float(LOOK.roadAlpha).add(lineV.mul(0.25)).add(edgeW.mul(0.2)).add(centre.mul(0.2)).add(sheen.mul(0.5))
    .add(schlick.mul(0.25)).add(rim.mul(0.4)).add(glint.mul(0.5)).add(gutter.mul(E.gutterAlpha)), 0, 1), edgeLine);
  return { material: m, uniforms: u };
}
