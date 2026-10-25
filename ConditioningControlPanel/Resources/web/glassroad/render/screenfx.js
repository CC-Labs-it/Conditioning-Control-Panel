/* render/screenfx.js - the local player's screen effect (CONTRACT-B1 section 3): one TSL node chain
 * driven by fx.state.screen (fx/effects.js already asked photosafe and Gentle, and holds one effect
 * at a time; the fading previous kind adds its share so a replace never hard cuts).
 *
 * Flash a rounded card over cover of the view (B1: the spiral icon on paper); Spiral turning arms
 * over the middle, alpha capped; Brain Drain edges blur and grey, centre sharp; Freeze frost on the
 * edges; Blackout an iris closing to a circle; Pink drifting layered fog; Melt the whole
 * view blurred, wobbling and running down like wet paint; Shatter a soft radial smear and a tilt.
 * Blackout keeps a small circle and a dim surround. Fog and the tunnel attenuate clear-scene
 * highlights and bloom so they can obscure the driving line. Gentle halves amounts and slows motion.
 * Look numbers in render/items/look.js; the uniform-gated branch costs one sample when idle.
 * Wave 2 masks (art/ART.md, each optional with the procedural look as its fallback): Freeze reads
 * fx-frost-edge, Melt runs down along fx-melt-flow, Shatter lays fx-crack lines over the glass (no
 * flash), Blackout takes its soft edge from fx-iris, and fx-noise dithers every fade edge and the
 * output so the soft gradients never band.
 * Polish 2, each with a readable IN and OUT driven by its amount (fxmath helpers): Melt adds a chromatic
 * smear down the view and drips that run from the top; Spiral is a two-band spiral over the whole view that
 * opens from the centre and twirls the middle; Freeze is a frost front that crystallises in from the edges
 * with facets glinting on the front; Shatter cracks into cells that grow out from the middle, then the
 * pieces slide apart and home again; Blackout's iris wears a soft rim; Pink washes in from the edges round
 * a heart in the historical version; Brain Drain's grey creeps inward. Gentle halves every amount.
 * Polish 4, bolder and clear about when it ends: a frame meter (a band round the whole view edge in the
 * effect's colour, draining clockwise from the top as the effect runs out) and a badge low in the middle (the
 * effect's icon in a disc, its ring draining the same way), both over the never-blind floor. The hit itself
 * tints the view toward the kind's colour for a beat (fx/effects.js screen.hit): the hue moves, the
 * luminance of every pixel stays, strongest at the edges.
 * The historical clear-lane mask remains for Spiral and the shader Flash fallback. Pink and
 * Blackout now cross the driving line. Legacy Flash: light blooms
 * in from the frame and the picture shows on two soft panes at the sides (one small pane in the Gentle corner),
 * with feathered edges, an ink glow just inside and a faint halo of paper light; no hard edge, no drop shadow.
 */
import { TextureLoader, SRGBColorSpace, Color, NearestFilter, CanvasTexture, Vector2 } from 'three/webgpu';
import {
  Fn, If, uniform, screenUV, vec2, vec3, float, texture, mix, smoothstep, sin, cos, atan, log, length, abs, max, min,
  luminance, mx_noise_float, mx_worley_noise_float, clamp, normalize, screenCoordinate, floor, fract, dot, sqrt, sign, select, step,
} from 'three/tsl';
import { currentArt } from './art.js';
import { SCREEN_LOOK as L } from './items/look.js';
import { screenAmounts, flashPanes, shatterTilt, frostThreshold, shatterPhase, spiralOpen, meterLook, SCREEN_KINDS } from './items/fxmath.js';
import { HIT_LOOK } from './items/look.js';
import { ITEMS } from '../sim/itemTable.js';
import { maskLook } from './atlas.js';
import { ATMOSPHERE as A, tunnelRadius, createAtmosphereClock } from './screen-atmosphere.js';
import { acquireLoomAtlas, loomAtlasUv } from './loomAtlas.js';
import { createSpiralBeatClock } from './spiralBeat.js';

const rgb = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
const hash = (p, a, b) => fract(sin(dot(p, vec2(a, b))).mul(43758.5453));
const ICON = new URL('../art/fx/spiral.png', import.meta.url).href;

/** The badge atlas: a 4 x 2 canvas of BADGE_PX cells, each screen kind's icon drawn in as it loads. */
const BADGE_PX = 128;
function badgeAtlas() {
  const cv = document.createElement('canvas');
  cv.width = BADGE_PX * 4; cv.height = BADGE_PX * 2;
  const g = cv.getContext('2d'), t = new CanvasTexture(cv);
  t.colorSpace = SRGBColorSpace;
  SCREEN_KINDS.forEach((kind, i) => {
    const img = new Image();
    img.onload = () => { g.drawImage(img, (i % 4) * BADGE_PX, ((i / 4) | 0) * BADGE_PX, BADGE_PX, BADGE_PX); t.needsUpdate = true; };
    img.src = new URL('../' + ITEMS[kind].icon, import.meta.url).href;
  });
  return t;
}

export function createScreenFx(three, art = currentArt()) {
  const tex = (id) => (art && art.tex ? art.tex(id) : null);
  const frostT = tex('fx-frost-edge'), flowT = tex('fx-melt-flow'), crackT = tex('fx-crack'), irisT = tex('fx-iris'), noiseT = tex('fx-noise');
  if (noiseT) { noiseT.minFilter = NearestFilter; noiseT.magFilter = NearestFilter; }   // a dither threshold: base mip, nearest
  const X = maskLook();
  const icon = new TextureLoader().load(ICON);
  icon.colorSpace = SRGBColorSpace;
  // the badge's icons: the eight screen kinds' art drawn into one 4 x 2 canvas atlas (row 0 on top) as each loads
  const badgeTex = badgeAtlas();
  const loom = acquireLoomAtlas(), beat = createSpiralBeatClock();
  const spiralAngle = uniform(0), spiralStyle = uniform(0);
  let wasSpiral = false, spiralSerial = 0;
  const u = {
    any: uniform(0), blurOn: uniform(0), t: uniform(0), runT: uniform(0), aspect: uniform(16 / 9), blurMul: uniform(1),
    flash: uniform(0), spiral: uniform(0), drain: uniform(0), freeze: uniform(0), black: uniform(0), pink: uniform(0),
    melt: uniform(0), shatter: uniform(0), dimOnly: uniform(0), tilt: uniform(0), iris: uniform(L.irisFrom),
    rx: uniform(0.5), ry: uniform(0.5), rw: uniform(0.2), rh: uniform(0.2),
    frostThr: uniform(1), crack: uniform(0), slide: uniform(0), spiralR: uniform(0),
    meterK: uniform(0), remain: uniform(0), meterRgb: uniform(new Color(1, 1, 1)), badgeS: uniform(1),
    hitK: uniform(0), hitRgb: uniform(new Color(1, 1, 1)), badgeCell: uniform(new Vector2(0, 0)), mirror: uniform(1), fogFill: uniform(0),
  };
  const K = L.lane;
  const amt = {};
  let clock = 0;
  const atmosphere = createAtmosphereClock();

  // The tunnel is the only clear opening. Bloom must respect the same opening.
  function tunnelShut() {
    const p = screenUV.sub(vec2(...L.irisAt)).mul(vec2(u.aspect, 1));
    return smoothstep(u.iris.sub(A.tunnelSoft), u.iris.add(A.tunnelSoft), length(p)).mul(u.black);
  }

  function bloomGate() {
    return mix(float(1), tunnelShut().mul(1 - A.tunnelFloor).oneMinus().mul(u.fogFill.mul(0.9).oneMinus()), u.any);
  }

  function node(sceneTex) {
    return Fn(() => {
      const uv0 = screenUV;
      const out = sceneTex.sample(uv0).rgb.toVar();
      If(u.any.greaterThan(0.0005), () => {
        const asp = u.aspect, toH = vec2(asp, 1);
        const c = uv0.sub(0.5).mul(toH);
        const rad = length(c);
        // polish 5: the clear lane (fxmath.laneMask): keep = the share of an effect left at this pixel
        const laneT = clamp(c.y.sub(K.topY).div(K.bottomY - K.topY), 0, 1);
        const laneD = length(vec2(c.x, c.y.sub(laneT.mul(K.bottomY - K.topY).add(K.topY))));
        const laneR = laneT.mul(K.rBottom - K.rTop).add(K.rTop);
        const keep = smoothstep(laneR, laneR.add(K.soft), laneD).oneMinus().mul(K.clear).oneMinus();
        // where to read: Shatter tilts about the centre, Spiral twirls the middle (not under Gentle's dim, and not in
        // the lane), Melt runs down (reads from above) and wobbles
        const twirl = u.spiral.mul(keep).mul(u.dimOnly.oneMinus()).mul(L.spiralTwirlRad).mul(smoothstep(0, max(u.spiralR, 0.001), rad).oneMinus());
        const ct = cos(u.tilt.add(twirl)), st = sin(u.tilt.add(twirl));
        const rc = vec2(c.x.mul(ct).sub(c.y.mul(st)), c.x.mul(st).add(c.y.mul(ct)));
        // blue noise per pixel (-0.5..0.5) jitters every fade edge below
        const jit = noiseT ? texture(noiseT, screenCoordinate.div(X.noisePx)).level(0).r.sub(0.5) : null;
        const dz = (x) => (jit ? clamp(x.add(jit.mul(X.ditherEdge).mul(x).mul(x.oneMinus()).mul(4)), 0, 1) : x);
        // the run: fx-melt-flow scrolls down the view (0.5 neutral), long vertical runs, else noise columns
        const drip = flowT
          ? texture(flowT, vec2(uv0.x.mul(X.meltFlowTile[0]), uv0.y.mul(X.meltFlowTile[1]).sub(u.t.mul(X.meltFlowUvps)))).r
          : mx_noise_float(vec2(uv0.x.mul(L.meltCols), u.t.mul(L.meltHz))).mul(0.5).add(0.5);
        const run = u.melt.mul(smoothstep(0, 1.5, u.runT).mul(L.meltRunUv).add(drip.mul(drip).mul(L.meltDripUv))).mul(A.meltWarp);
        const wob = u.melt.mul(L.meltWobbleUv * A.meltWarp);
        const suv = rc.div(toH).add(0.5).add(vec2(
          sin(uv0.y.mul(14).add(u.t.mul(2.3))).mul(wob),
          run.negate().add(sin(uv0.x.mul(9).sub(u.t.mul(1.7))).mul(wob)),
        )).toVar();
        // Shatter: the glass breaks into cells round the middle; cracks grow out (crack), then each piece
        // slides away from the centre a little (slide) and the OUT slides it home
        const crackLine = float(0).toVar();
        If(u.shatter.greaterThan(0), () => {
          // cells in log-polar space: long shards fanning out from the middle, smaller near it
          const g = vec2(atan(c.y, c.x).mul(L.shatterSpokes / (2 * Math.PI)), log(rad.add(0.04)).mul(L.shatterRings));
          const gi = floor(g), f1 = float(9).toVar(), f2 = float(9).toVar(), id = vec2(0).toVar();
          for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) {
            const cp = gi.add(vec2(ox, oy)), pt = cp.add(vec2(hash(cp, 127.1, 311.7), hash(cp, 269.5, 183.3)).mul(0.8).add(0.1));
            const d = length(g.sub(pt));
            If(d.lessThan(f1), () => { f2.assign(f1); f1.assign(d); id.assign(cp); }).ElseIf(d.lessThan(f2), () => { f2.assign(d); });
          }
          // each shard slides out along its own spoke, some further than others
          const sa = id.x.add(0.5).mul(2 * Math.PI / L.shatterSpokes), away = vec2(cos(sa), sin(sa)).mul(hash(id, 41.3, 17.9).mul(0.7).add(0.5));
          suv.addAssign(away.div(toH).mul(L.shatterSlideUv).mul(u.slide));
          // a crack line is where the two nearest cell points are equally near; it widens as the pieces part
          const grown = smoothstep(u.crack.mul(1.1).sub(0.12), u.crack.mul(1.1).add(0.001), rad).oneMinus();
          // log-polar cells grow with the radius: divide the width back out so a seam keeps its screen width
          const perG = float(L.shatterRings).div(max(rad, 0.04)), w = perG.mul(L.shatterCrackW);
          crackLine.assign(smoothstep(0, w, f2.sub(f1).sub(u.slide.mul(perG).mul(L.shatterGapW))).oneMinus().mul(grown).mul(u.shatter));
        });
        const col = sceneTex.sample(suv).rgb.toVar();
        // Melt: the colour channels run apart down the view (read from further up)
        If(u.melt.greaterThan(0), () => {
          const ch = vec2(0, u.melt.mul(L.meltChromaUv * A.meltChroma));
          col.assign(vec3(col.r, sceneTex.sample(suv.sub(ch)).g, sceneTex.sample(suv.sub(ch.mul(2))).b));
        });
        // blur: Brain Drain at the edges (creeping in with the amount), Freeze under the frost, Melt everywhere
        const creep = u.drain.oneMinus().mul(L.drainCreep);
        const edge = dz(smoothstep(creep.add(L.drainEdge[0]), creep.add(L.drainEdge[1]), rad));
        const frostM = frostT ? texture(frostT, uv0).r : null;
        // the frost front: how far in from the edge this pixel is (0 middle, 1 edge), frozen past the threshold
        const fe = frostT ? smoothstep(X.frostMask[0], X.frostMask[1], frostM)
          : smoothstep(L.frostEdge[0], L.frostEdge[1], rad.add(mx_noise_float(c.mul(L.frostNoise)).mul(0.18)));
        const frost = dz(smoothstep(u.frostThr, u.frostThr.add(L.frostFrontSoft), fe));
        If(u.blurOn.greaterThan(0), () => {
          const r = u.drain.mul(edge).mul(L.drainBlur).add(u.melt.mul(L.meltBlur)).add(u.freeze.mul(frost).mul(L.frostBlur))
            .mul(L.blurMaxUv).mul(u.blurMul);
          const sum = col.toVar();
          for (let i = 0; i < L.blurTaps; i++) {
            const a = i * 2.399963, d = Math.sqrt((i + 0.5) / L.blurTaps);
            // Melt stretches the taps down the screen: paint runs, it does not just go soft
            const o = vec2(Math.cos(a) * d, u.melt.mul(L.meltStreak * A.meltStreak).add(1).mul(Math.sin(a) * d));
            sum.addAssign(sceneTex.sample(suv.add(o.mul(r).div(toH))).rgb);
          }
          col.assign(sum.div(L.blurTaps + 1));
        });
        If(u.shatter.greaterThan(0), () => {
          // the glass cracks (fx-crack, fixed to the screen): the lines bend the smear taps a little, then
          // darken with a faint cool edge; never a flash
          const crack = crackT ? texture(crackT, c.mul(X.crackScale).add(0.5)).r.mul(u.shatter) : null;
          if (crack) suv.addAssign(normalize(c.add(0.0001)).div(toH).mul(crack.mul(X.crackRefractUv)));
          const sum = col.toVar(), toC = suv.sub(0.5).mul(u.shatter.mul(L.shatterSmear));
          for (let i = 1; i <= L.shatterTaps; i++) sum.addAssign(sceneTex.sample(suv.sub(toC.mul(i / L.shatterTaps))).rgb);
          col.assign(sum.div(L.shatterTaps + 1));
          if (crack) col.assign(mix(col, col.mul(X.crackDark).add(rgb(X.crackTint).mul(X.crackLight)), crack.mul(u.crack)));
          // the cell cracks: dark seams with a faint cool lip, never brighter than the glass beside them
          col.assign(mix(col, col.mul(L.shatterCrackDark).add(rgb(X.crackTint).mul(X.crackLight)), crackLine));
        });
        If(u.melt.greaterThan(0), () => {
          // drips run down from the top of the view, one per column, each its own speed and width
          const cx = uv0.x.mul(L.meltDripCols), ci = floor(cx), h = hash(vec2(ci, 3.1), 12.9898, 78.233);
          const head = u.runT.mul(L.meltDripSpeed * 0.6).mul(h.add(0.4)).add(h.mul(0.25)).sub(0.1).mul(u.melt);
          const wd = h.mul(0.18).add(0.1), dx = abs(fract(cx).sub(0.5)), live = smoothstep(0.35, 0.45, hash(vec2(ci, 7.7), 39.3, 11.1));
          const above = smoothstep(head.sub(0.01), head.add(0.0001), uv0.y).oneMinus();
          const body = smoothstep(wd.mul(0.6), wd, dx).oneMinus().mul(smoothstep(head.sub(L.meltDripLen), head.add(0.0001), uv0.y).mul(0.6).add(0.4)).mul(above);
          const tip = smoothstep(wd.mul(0.9), wd.mul(1.35), length(vec2(dx, head.sub(uv0.y).mul(L.meltDripCols).div(u.aspect)))).oneMinus();
          const dr = max(body, tip).mul(u.melt).mul(live);
          col.assign(mix(col, col.mul(rgb(L.meltDripTint)).mul(1.15).add(rgb(L.meltDripTint).mul(0.06)), dr.mul(0.45)));
          // a wet highlight down each drip's left side
          col.addAssign(vec3(L.meltDripGloss).mul(dr).mul(smoothstep(0, 0.06, abs(fract(cx).sub(0.5).add(wd.mul(0.5)))).oneMinus()));
        });
        const lum = luminance(col);
        col.assign(mix(col, vec3(lum), u.drain.mul(edge).mul(L.drainGrey)));
        col.assign(mix(vec3(lum), col, u.melt.mul(L.meltSat - 1).add(1)));
        // Pink is opaque drifting mist, with several cloud depths, shadowed bellies and bright
        // crests. The front rolls in over seconds and eventually crosses the driving line.
        const fogOcclusion = float(0).toVar();
        If(u.fogFill.greaterThan(0.0005), () => {
          const edgeDistance = min(min(uv0.x.mul(asp), uv0.x.oneMinus().mul(asp)), min(uv0.y, uv0.y.oneMinus()));
          for (let layer = 0; layer < A.fogLayers; layer++) {
            const q = layer / (A.fogLayers - 1), drift = u.t.mul(A.fogDrift * (0.7 + q));
            const p = c.mul(2.5 + q * 3).add(vec2(drift.mul(layer % 2 ? -1 : 1), drift.mul(-0.55))).add(vec2(layer * 7.3, layer * 3.9));
            const curl = mx_noise_float(p.mul(0.65)).mul(0.45);
            const billow = mx_noise_float(p.add(vec2(curl, curl.mul(0.7)))).mul(0.5).add(0.5);
            const front = smoothstep(u.fogFill.mul(0.8).sub(0.2), u.fogFill.mul(0.8).add(0.08), edgeDistance.add(billow.mul(0.13))).oneMinus();
            const density = smoothstep(0.2, 0.72, billow).mul(front).mul(u.fogFill).mul(A.fogDepth);
            const alpha = density.negate().exp().oneMinus();
            const cloud = mix(rgb(A.fogFar), rgb(A.fogNear), billow.mul(0.65).add(q * 0.25));
            col.assign(mix(col, cloud, alpha));
            fogOcclusion.assign(fogOcclusion.oneMinus().mul(alpha.oneMinus()).oneMinus());
          }
        });
        // Freeze: frost crystallises in from the edges; facets glint along the advancing front
        const ice = frostT ? smoothstep(X.frostVeinBand[0], X.frostVeinBand[1], frostM) : abs(mx_noise_float(c.mul(L.frostNoise * 4))).oneMinus().pow(10);
        const facet = mx_worley_noise_float(c.mul(L.frostCells)), front = frost.mul(frost.oneMinus()).mul(4);
        const glint = smoothstep(0.02, 0.14, facet).oneMinus().mul(front).mul(L.frostGlint);
        col.assign(mix(col, rgb(L.frostColour).add(ice.mul(L.frostVeins)).add(facet.mul(0.12)), frost.mul(min(u.freeze.mul(3), 1)).mul(L.frostAlpha)));
        col.addAssign(vec3(glint).mul(u.freeze));
        // Spiral: a hypnotic two-band spiral over the whole view, opening from the centre (a soft dim under Gentle)
        const reveal = smoothstep(u.spiralR.sub(0.2), u.spiralR.add(0.001), rad).oneMinus().mul(smoothstep(0, L.spiralHole, rad));
        const ca = cos(spiralAngle), sa = sin(spiralAngle);
        const spiralUv = vec2(c.x.mul(ca).sub(c.y.mul(sa)), c.x.mul(sa).add(c.y.mul(ca))).mul(.7).add(.5);
        const bands = texture(loom.texture, loomAtlasUv(spiralUv, spiralStyle)).rgb;
        const spun = mix(col, bands, reveal.mul(u.spiral).mul(keep).mul(L.spiralFullAlpha));
        const dim = col.mul(reveal.mul(u.spiral).mul(keep).mul(L.spiralDim).oneMinus());
        col.assign(mix(spun, dim, u.dimOnly));
        // Flash (polish 5): light blooms in from the frame (not in the Gentle corner mode), the picture turns slowly
        // on soft panes at the sides (the second mirrored), and the lane to the kart and the bend stays clear
        const fk = u.flash.mul(keep), F = L.flashFeather;
        const radE = length(c.div(vec2(asp.mul(0.5), 0.5)));
        const frame = smoothstep(L.flashFrame[0], L.flashFrame[1], radE).mul(u.mirror);
        col.assign(mix(col, rgb(L.flashPaper), frame.mul(fk).mul(L.flashFrameAlpha)));
        const wx = mix(u.rx, select(uv0.x.lessThan(0.5), u.rx, u.rx.oneMinus()), u.mirror);
        const half = vec2(u.rw, u.rh).mul(toH), p = uv0.sub(vec2(wx, u.ry)).mul(toH);
        const q = abs(p).sub(half).add(L.flashRound);
        const d = length(max(q, 0)).add(min(max(q.x, q.y), 0)).sub(L.flashRound);
        const inside = dz(smoothstep(-F, F, d).oneMinus());
        const cu = p.div(half.y.mul(2)), ca = u.t.mul(0.5);
        const iuv = vec2(cu.x.mul(cos(ca)).sub(cu.y.mul(sin(ca))), cu.x.mul(sin(ca)).add(cu.y.mul(cos(ca)))).mul(0.95).add(0.5);
        const ic = texture(icon, clamp(iuv, 0, 1));
        // an ink glow just inside the feathered edge, a faint halo of paper light just outside it
        const rim = smoothstep(-F * 3, -F * 0.5, d).mul(inside);
        const pic = mix(mix(rgb(L.flashPaper), ic.rgb, ic.a), rgb(L.flashInk), rim.mul(L.flashRim));
        const halo = smoothstep(0, F * 4, d).oneMinus().mul(inside.oneMinus());
        col.assign(mix(col, rgb(L.flashPaper), halo.mul(fk).mul(L.flashHalo)));
        col.assign(mix(col, pic, inside.mul(fk).mul(L.flashAlpha)));
        // Blackout: everything outside the iris goes dim
        const ic0 = uv0.sub(vec2(...L.irisAt)).mul(toH);
        // fx-iris: white is the aperture (radius irisMaskR of the mask), scaled so it sits at u.iris
        const open = irisT ? texture(irisT, ic0.mul(X.irisMaskR).div(u.iris).add(0.5)).r.oneMinus()
          : smoothstep(u.iris.sub(L.irisSoft), u.iris.add(L.irisSoft), length(ic0));
        // A circular tunnel has no road-shaped exemption and no sharp highlights outside it.
        const shut = tunnelShut();
        col.assign(mix(col, col.mul(A.tunnelFloor), shut));
        // a soft rim of light rides the closing iris edge
        col.addAssign(rgb(L.irisRimColour).mul(open.mul(open.oneMinus()).mul(4).pow(L.irisRimPow).mul(u.black).mul(L.irisRimAlpha * 0.4)));
        // the hit: the view's hue swings toward the kind's colour, each pixel keeping its own luminance
        If(u.hitK.greaterThan(0.001), () => {
          const tl = luminance(col), hl = max(luminance(u.hitRgb), 0.05);
          const hued = min(u.hitRgb.mul(tl.div(hl)), vec3(1.2));
          const tw = smoothstep(HIT_LOOK.tintEdge[0], HIT_LOOK.tintEdge[1], rad).mul(1 - HIT_LOOK.tintCentre).add(HIT_LOOK.tintCentre);
          col.assign(mix(col, hued, u.hitK.mul(tw)));
        });
        // never blind: the highlights (rails, racing line) come through sharp, read where Melt and
        // Shatter moved the view so even Melt has no still centre
        const clear = sceneTex.sample(suv).rgb;
        const bright = clear.mul(smoothstep(L.keepBright[0], L.keepBright[1], luminance(clear)).mul(L.keepBrightGain))
          .mul(shut.mul(1 - A.tunnelFloor).oneMinus()).mul(fogOcclusion.oneMinus());
        if (jit) col.addAssign(jit.mul(X.ditherOut));
        out.assign(max(col, bright));
        If(u.meterK.greaterThan(0.001), () => {
          const K = u.meterK;
          // the frame meter: lit clockwise from the top centre up to remain over a dark track, so it reads on any sky
          const ex = min(uv0.x, uv0.x.oneMinus()).mul(asp), ey = min(uv0.y, uv0.y.oneMinus()), de = min(ex, ey);
          const p = fract(atan(c.x, c.y.negate()).div(2 * Math.PI).add(1));
          const lit = smoothstep(u.remain.add(0.004), u.remain.sub(0.004), p).mul(step(0.0005, u.remain));
          out.assign(mix(out, out.mul(L.meterShade), smoothstep(L.meterW * 1.9, L.meterW * 1.3, de).mul(K)));
          const band = smoothstep(L.meterW, L.meterW * 0.6, de);
          out.assign(mix(out, mix(rgb(L.meterTrack), u.meterRgb, lit), band.mul(K).mul(L.meterAlpha)));
          out.addAssign(u.meterRgb.mul(smoothstep(L.meterGlowW, L.meterW * 1.9, de).mul(lit).mul(K).mul(L.meterGlow)));
          // the badge: the icon in a disc, a ring round it draining the same way, a dark outline under both
          const bc = uv0.sub(vec2(...L.badgeAt)).mul(toH), br = length(bc), R = u.badgeS.mul(L.badgeR), RW = R.add(L.badgeRingW);
          out.assign(mix(out, out.mul(L.meterShade), smoothstep(RW.add(0.007), RW.add(0.004), br).mul(K)));
          const disc = smoothstep(R, R.sub(0.003), br);
          const ring = smoothstep(R.sub(0.001), R.add(0.002), br).mul(smoothstep(RW.add(0.002), RW, br));
          const pb = fract(atan(bc.x, bc.y.negate()).div(2 * Math.PI).add(1));
          const rl = smoothstep(u.remain.add(0.006), u.remain.sub(0.006), pb).mul(step(0.0005, u.remain));
          // screen y runs down and the canvas texture is flipped, so the icon's v counts up from its cell's bottom
          const lu = clamp(bc.div(R.mul(1.8)).add(0.5), 0.004, 0.996);
          const bi = texture(badgeTex, vec2(lu.x.add(u.badgeCell.x).div(4), u.badgeCell.y.oneMinus().add(lu.y.oneMinus()).div(2)));
          out.assign(mix(out, mix(rgb(L.badgePaper), bi.rgb, bi.a), disc.mul(K).mul(L.badgeAlpha)));
          out.assign(mix(out, mix(rgb(L.meterTrack), u.meterRgb, rl), ring.mul(K).mul(L.badgeAlpha)));
        });
      });
      return out;
    })();
  }

  function update(screen, dt, aspect, view = {}) {
    const a = screenAmounts(screen, amt), m = meterLook(screen);
    const activeSpiral = a.spiral > 0;
    if (activeSpiral && !wasSpiral) spiralStyle.value = spiralSerial++ % 6;
    wasSpiral = activeSpiral;
    if (!view.paused) spiralAngle.value += Math.max(0, dt || 0) * .45 * a.timeMul * beat.update(dt, view, a.gentle);
    u.fogFill.value = atmosphere.update(screen, a.pink, dt, a.timeMul);
    u.hitK.value = m.hitK; u.meterK.value = m.meterK; u.remain.value = m.remain; u.badgeS.value = m.badgeS;
    if (m.hitK > 0) u.hitRgb.value.set(m.hitHex);
    if (m.meterK > 0) {
      u.meterRgb.value.set(m.meterHex);
      const ci = Math.max(0, SCREEN_KINDS.indexOf(screen.kind));
      u.badgeCell.value.set(ci % 4, (ci / 4) | 0);
    }
    const any = a.any || m.hitK > 0 || m.meterK > 0;
    u.any.value = any ? 1 : 0;
    if (!any) { clock = 0; return; }
    clock += (dt || 0) * a.timeMul;
    u.t.value = clock;
    u.runT.value = (screen.t || 0) * a.timeMul;
    u.aspect.value = aspect > 0 ? aspect : 16 / 9;
    u.blurMul.value = a.blurMul;
    u.flash.value = screen.params?.mediaFlash ? 0 : a.flash; u.spiral.value = a.spiral; u.drain.value = a.brainDrain; u.freeze.value = a.freeze;
    u.black.value = a.blackout; u.pink.value = a.pink; u.melt.value = a.melt; u.shatter.value = a.shatter;
    u.blurOn.value = a.brainDrain + a.melt + a.freeze > 0.001 ? 1 : 0;
    u.dimOnly.value = a.dimOnly ? 1 : 0;
    u.iris.value = tunnelRadius(a.blackout);
    u.tilt.value = a.gentle ? 0 : shatterTilt(screen.kind === 'shatter' ? screen.t || 0 : 9, a.shatter);
    // polish 2: the IN and OUT of each effect follow its amount (fronts, reveals, cracks then slides)
    const sp = shatterPhase(screen.kind === 'shatter' ? (screen.t || 0) * a.timeMul : 9, a.shatter);
    u.crack.value = sp.crack; u.slide.value = sp.slide;
    u.frostThr.value = frostThreshold(a.freeze);
    u.spiralR.value = spiralOpen(a.spiral);
    const r = flashPanes(a.cover, u.aspect.value, { corner: a.corner, scale: a.cornerScale, seed: a.seed });
    u.mirror.value = r.mirror ? 1 : 0;
    const pop = 1 - L.flashPop * (1 - Math.min(1, a.flash));
    // the card rises a little as it pops in and sinks as it goes
    u.rx.value = r.cx; u.ry.value = r.cy + L.flashPop * 0.8 * (1 - Math.min(1, a.flash)); u.rw.value = r.hw * pop; u.rh.value = r.hh * pop;
  }

  return { node, update, bloomGate, dispose() { icon.dispose(); badgeTex.dispose(); loom.dispose(); } };
}
