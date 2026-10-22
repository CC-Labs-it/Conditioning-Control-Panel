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
  normalize, dot, pow, abs, clamp, max, min, reflect, floor, hash, length, atan, step } from 'three/tsl';
import { LOOK } from '../look.js';
import { SPECTRUM_LOOK, patternZones, patternBytes, fitPeriod } from '../spectrum.js';

/** Road polish numbers; LOOK.roadPolish overrides any key. */
export const ROAD_POLISH = Object.freeze({
  depthTile: 2.2, depthGain: 0.14, depthLaneM: 4,
  reflGain: 0.35, reflF0: 0.04, reflBump: 0.12,
  glintCellM: 0.38, glintDensity: 0.07, glintSizeM: 0.03, glintTwinkle: 4, glintPow: 6, glintGain: 0.45,
  glintNearM: [3, 7], glintFarM: [22, 34],
  rimInM: 1.4, rimGain: 0.3, halfWidthM: 7,
});

const TAU = Math.PI * 2;

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
  const edgeLine = (f, w) => float(1).sub(smoothstep(0, w, min(f, float(1).sub(f))));
  // 0 streaming lanes: hue flows down each lane; steady lane lines; light packets run toward you
  const lu = u.div(S.laneW), k = floor(lu), e = fract(lu);
  const line0 = float(1).sub(smoothstep(0, S.laneLineW, min(e, float(1).sub(e)).mul(S.laneW)));
  const pk = float(1).sub(fract(a.div(per.packet).add(hash(k.add(11.3)))));
  const packet = pow(smoothstep(1 - S.packetLen, 1, pk), 2).mul(float(1).sub(smoothstep(0.18, 0.42, abs(e.sub(0.5)))))
    .mul(step(1 - S.packetShare, hash(k.add(3.7))));
  const p0 = { x: k.mul(S.laneHueStep).add(a.div(per.ramp)), l: max(line0.mul(0.8), packet.mul(S.packetGain / S.lineGain)) };
  // 1 chevrons pointing up the road: a solid hue per chevron (chevronHueSteps whole chevron lengths of
  // ramp apart, so the wrap stays seamless), a hot leading edge, a soft smear behind
  const cv = a.add(au.mul(S.chevronSlope)).div(per.chevron), cf = fract(cv), cm = cf.mul(per.chevron);
  const p1 = { x: floor(cv).add(smoothstep(0, 1, cf).mul(0.4)).mul(S.chevronHueSteps * per.chevron / per.ramp).add(au.mul(0.01)),
    l: max(float(1).sub(smoothstep(0, S.chevronEdgeM, cm)), float(1).sub(smoothstep(0, per.chevron * 0.5, cm)).mul(0.3)).mul(calm) };
  // 2 warped checker: two hue families half a ramp apart, swapping cell by cell
  const uw = u.add(sin(a.mul(TAU / per.warpA).add(u.mul(0.3))).mul(0.9));
  const aw = a.add(sin(u.mul(0.55).add(a.mul(TAU / per.warpB))).mul(1.4));
  const ci = floor(uw.div(S.checkerM)), cj = floor(aw.div(per.checker));
  const par = fract(ci.add(cj).mul(0.5)).mul(2);
  const p2 = { x: par.mul(0.5).add(cj.mul(per.checker / per.ramp)),
    l: max(edgeLine(fract(uw.div(S.checkerM)), 0.03), edgeLine(fract(aw.div(per.checker)), 0.03).mul(calm)) };
  // 3 spiral rings: a two-armed spiral every ring cell, turning inward (clockwise from above)
  const cc = fract(a.div(per.ring)), cvr = cc.sub(0.5).mul(per.ring);
  const r = length(vec2(u, cvr)), ang = atan(u, cvr);
  const sp = r.div(S.ringW).sub(ang.mul(S.ringArms / TAU)).add(spin);
  const seamFade = float(1).sub(smoothstep(0.3, 0.48, abs(cc.sub(0.5))));
  // colour: the plain flow plus a bounded spiral swing (a sine of the spiral phase, seamless across the
  // atan cut, faded out at the cell seams; never a mix of two far apart ramp coordinates, which would
  // sweep the whole ramp in a few metres); the lines keep to a disc of ringLineR so no flat arcs strobe past
  const swing = sin(r.mul(TAU / S.ringHueM).sub(ang)).mul(S.ringSwing).mul(seamFade);
  const p3 = { x: a.div(per.ramp).add(au.mul(0.05)).add(swing), l: edgeLine(fract(sp), 0.09).mul(smoothstep(0.3, 1, r)).mul(smoothstep(S.ringLineR[0], S.ringLineR[1], r)).mul(seamFade).mul(calm) };
  // 4 kaleidoscope weave: mirrored across the centre, two diagonal strand sets going over and under
  const q1 = au.add(a).div(per.weave), q2 = a.sub(au).div(per.weave);
  const over = fract(floor(q1).add(floor(q2)).mul(0.5)).mul(2);
  const strand = mix(fract(q2), fract(q1), over);
  const p4 = { x: over.mul(0.33).add(a.div(per.ramp)).add(au.mul(0.02)), l: edgeLine(strand, 0.08).mul(calm) };
  return [p0, p1, p2, p3, p4];
}

export function createRoadMaterial(pal, art = null, track = null) {
  const P = { ...ROAD_POLISH, ...(LOOK.roadPolish || {}) };
  const S = { ...SPECTRUM_LOOK, ...(LOOK.roadSpectrum || {}) };
  const u = { phase: uniform(0), contrast: uniform(1), bright: uniform(0), sheen: uniform(1) };
  const lapM = track && track.lengthM > 0 ? track.lengthM : 0;
  const fit = (b) => (lapM ? fitPeriod(lapM, b) : b);
  const per = { ramp: fit(S.rampM), packet: fit(S.packetM), chevron: fit(S.chevronM), checker: fit(S.checkerM),
    ring: fit(S.ringCellM), weave: fit(S.weaveM), warpA: fit(30), warpB: fit(54), spin: fit(12) };
  if (pal.flow) { if (lapM) pal.flowWrap = lapM; pal.motionSource = u.contrast; }
  const flow = pal.flow || u.phase;
  const p = uv(), uL = p.x, au = abs(uL);
  const a = p.y.add(flow);
  const toCam = cameraPosition.sub(positionWorld), dist = toCam.length(), V = normalize(toCam);
  const calm = smoothstep(S.nearCalmM[0], S.nearCalmM[1], dist);
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

  // edges and centre: a steady bright edge lane and a calm centre line frame the racing line
  const half = track ? texture(widthTexture(track), vec2(p.y.div(lapM), 0.5)).r.mul(16) : float(P.halfWidthM);
  const toEdge = half.sub(au);
  const edgeW = float(1).sub(smoothstep(S.edgeLaneM * 0.6, S.edgeLaneM, toEdge));
  const centre = float(1).sub(smoothstep(S.centreLineW * 0.5, S.centreLineW, au));

  const patCol = mix(ramp(xa), ramp(xb), tB);
  const edgeCol = ramp(a.div(per.ramp).add(0.5));
  const avg = pal.col(0).mul(0.5).add(pal.col(1).mul(0.3)).add(pal.col(2).mul(0.2));
  const body = mix(avg, mix(patCol, edgeCol.mul(1 + S.edgeGain), edgeW), u.contrast);
  const lineV = mix(la, lb, tB).mul(float(1).sub(edgeW)).mul(u.contrast);
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

  const rim = float(1).sub(smoothstep(0, P.rimInM, toEdge)).mul(P.rimGain);
  const polish = refl.add(edgeCol.mul(0.6).add(0.4).mul(rim)).mul(u.sheen).add(vec3(1, 0.97, 0.99).mul(glint));
  const surface = body.mul(S.baseGain).add(hot.mul(lineV.mul(S.lineGain))).add(vec3(1, 0.95, 0.98).mul(centre.mul(S.centreGain).mul(u.contrast)))
    .mul(u.bright.add(1)).mul(haze).mul(LOOK.roadGain);
  const m = new MeshBasicNodeMaterial({ transparent: true, side: DoubleSide });
  m.colorNode = surface.add(deepCol).add(vec3(1, 0.96, 0.98).mul(sheen)).add(polish);
  m.opacityNode = clamp(float(LOOK.roadAlpha).add(lineV.mul(0.25)).add(edgeW.mul(0.2)).add(centre.mul(0.2)).add(sheen.mul(0.5))
    .add(schlick.mul(0.25)).add(rim.mul(0.4)).add(glint.mul(0.5)), 0, 1);
  return { material: m, uniforms: u };
}
