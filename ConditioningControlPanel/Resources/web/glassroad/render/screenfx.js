/* render/screenfx.js - the local player's screen effect (CONTRACT-B1 section 3): one TSL node chain
 * driven by fx.state.screen (fx/effects.js already asked photosafe and Gentle, and holds one effect
 * at a time; the fading previous kind adds its share so a replace never hard cuts).
 *
 * Flash a rounded card over cover of the view (B1: the spiral icon on paper); Spiral turning arms
 * over the middle, alpha capped; Brain Drain edges blur and grey, centre sharp; Freeze frost on the
 * edges; Blackout an iris closing to a circle; Pink a wash with the contrast down; Melt the whole
 * view blurred, wobbling and running down like wet paint; Shatter a soft radial smear and a tilt.
 * NEVER BLIND: a floor of the clear image's highlights survives every effect (rails and the racing
 * line are the brightest things on screen), Blackout keeps a circle and a dim floor, and post.js
 * adds bloom from the clear scene after this node. Gentle halves the amounts and slows the motion.
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
 * a heart; Brain Drain's grey creeps inward. Gentle halves every amount, so fronts stop halfway.
 */
import { TextureLoader, SRGBColorSpace, Color, NearestFilter } from 'three/webgpu';
import {
  Fn, If, uniform, screenUV, vec2, vec3, float, texture, mix, smoothstep, sin, cos, atan, log, length, abs, max, min,
  luminance, mx_noise_float, mx_worley_noise_float, clamp, normalize, screenCoordinate, floor, fract, dot, sqrt, sign, select,
} from 'three/tsl';
import { currentArt } from './art.js';
import { SCREEN_LOOK as L } from './items/look.js';
import { screenAmounts, irisRadius, flashRect, shatterTilt, frostThreshold, shatterPhase, spiralOpen, pinkHeart } from './items/fxmath.js';
import { maskLook } from './atlas.js';

const rgb = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
const hash = (p, a, b) => fract(sin(dot(p, vec2(a, b))).mul(43758.5453));
/** Signed distance to a heart of size 1 (fxmath.heartSd, the same formula). */
function heartSd(p) {
  const x = abs(p.x), y = p.y, m = max(x.add(y), 0).mul(0.5);
  const a = length(vec2(x.sub(0.25), y.sub(0.75))).sub(Math.SQRT2 / 4);
  const d1 = vec2(x, y.sub(1)), d2 = vec2(x.sub(m), y.sub(m));
  return select(x.add(y).greaterThan(1), a, sqrt(min(dot(d1, d1), dot(d2, d2))).mul(sign(x.sub(y))));
}
const ICON = new URL('../art/fx/spiral.png', import.meta.url).href;

export function createScreenFx(three, art = currentArt()) {
  const tex = (id) => (art && art.tex ? art.tex(id) : null);
  const frostT = tex('fx-frost-edge'), flowT = tex('fx-melt-flow'), crackT = tex('fx-crack'), irisT = tex('fx-iris'), noiseT = tex('fx-noise');
  if (noiseT) { noiseT.minFilter = NearestFilter; noiseT.magFilter = NearestFilter; }   // a dither threshold: base mip, nearest
  const X = maskLook();
  const icon = new TextureLoader().load(ICON);
  icon.colorSpace = SRGBColorSpace;
  const u = {
    any: uniform(0), blurOn: uniform(0), t: uniform(0), runT: uniform(0), aspect: uniform(16 / 9), blurMul: uniform(1),
    flash: uniform(0), spiral: uniform(0), drain: uniform(0), freeze: uniform(0), black: uniform(0), pink: uniform(0),
    melt: uniform(0), shatter: uniform(0), dimOnly: uniform(0), tilt: uniform(0), iris: uniform(L.irisFrom),
    rx: uniform(0.5), ry: uniform(0.5), rw: uniform(0.2), rh: uniform(0.2),
    frostThr: uniform(1), crack: uniform(0), slide: uniform(0), spiralR: uniform(0), heartR: uniform(L.pinkHeartR[0]),
  };
  const amt = {};
  let clock = 0;

  function node(sceneTex) {
    return Fn(() => {
      const uv0 = screenUV;
      const out = sceneTex.sample(uv0).rgb.toVar();
      If(u.any.greaterThan(0.0005), () => {
        const asp = u.aspect, toH = vec2(asp, 1);
        const c = uv0.sub(0.5).mul(toH);
        const rad = length(c);
        // where to read: Shatter tilts about the centre, Spiral twirls the middle (not under Gentle's dim),
        // Melt runs down (reads from above) and wobbles
        const twirl = u.spiral.mul(u.dimOnly.oneMinus()).mul(L.spiralTwirlRad).mul(smoothstep(0, max(u.spiralR, 0.001), rad).oneMinus());
        const ct = cos(u.tilt.add(twirl)), st = sin(u.tilt.add(twirl));
        const rc = vec2(c.x.mul(ct).sub(c.y.mul(st)), c.x.mul(st).add(c.y.mul(ct)));
        // blue noise per pixel (-0.5..0.5) jitters every fade edge below
        const jit = noiseT ? texture(noiseT, screenCoordinate.div(X.noisePx)).level(0).r.sub(0.5) : null;
        const dz = (x) => (jit ? clamp(x.add(jit.mul(X.ditherEdge).mul(x).mul(x.oneMinus()).mul(4)), 0, 1) : x);
        // the run: fx-melt-flow scrolls down the view (0.5 neutral), long vertical runs, else noise columns
        const drip = flowT
          ? texture(flowT, vec2(uv0.x.mul(X.meltFlowTile[0]), uv0.y.mul(X.meltFlowTile[1]).sub(u.t.mul(X.meltFlowUvps)))).r
          : mx_noise_float(vec2(uv0.x.mul(L.meltCols), u.t.mul(L.meltHz))).mul(0.5).add(0.5);
        const run = u.melt.mul(smoothstep(0, 1.5, u.runT).mul(L.meltRunUv).add(drip.mul(drip).mul(L.meltDripUv)));
        const wob = u.melt.mul(L.meltWobbleUv);
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
          const ch = vec2(0, u.melt.mul(L.meltChromaUv));
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
            const o = vec2(Math.cos(a) * d, u.melt.mul(L.meltStreak).add(1).mul(Math.sin(a) * d));
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
        // Pink: the wash closes in from the edges round a heart, full outside it and softer within
        const hp = vec2(c.x, c.y.negate().add(0.06)).div(u.heartR).add(vec2(0, 0.55));
        const hsd = heartSd(hp), outHeart = smoothstep(L.pinkHeartSoft * -0.3, L.pinkHeartSoft, hsd);
        const low = mix(vec3(0.5), col, u.pink.mul(L.pinkContrast).oneMinus()).mul(vec3(...L.pinkTint)).add(L.pinkLift);
        col.assign(mix(col, low, u.pink.mul(outHeart.mul(0.75).add(0.25))));
        col.assign(mix(col, rgb(L.pinkEdgeTint), u.pink.mul(outHeart).mul(L.pinkEdgeAlpha)));
        // a soft glowing line traces the heart
        col.addAssign(rgb(L.pinkEdgeTint).mul(smoothstep(0, L.pinkRimW, abs(hsd)).oneMinus().mul(u.pink).mul(L.pinkRimAlpha)));
        // Freeze: frost crystallises in from the edges; facets glint along the advancing front
        const ice = frostT ? smoothstep(X.frostVeinBand[0], X.frostVeinBand[1], frostM) : abs(mx_noise_float(c.mul(L.frostNoise * 4))).oneMinus().pow(10);
        const facet = mx_worley_noise_float(c.mul(L.frostCells)), front = frost.mul(frost.oneMinus()).mul(4);
        const glint = smoothstep(0.02, 0.14, facet).oneMinus().mul(front).mul(L.frostGlint);
        col.assign(mix(col, rgb(L.frostColour).add(ice.mul(L.frostVeins)).add(facet.mul(0.12)), frost.mul(min(u.freeze.mul(3), 1)).mul(L.frostAlpha)));
        col.addAssign(vec3(glint).mul(u.freeze));
        // Spiral: a hypnotic two-band spiral over the whole view, opening from the centre (a soft dim under Gentle)
        const reveal = smoothstep(u.spiralR.sub(0.2), u.spiralR.add(0.001), rad).oneMinus().mul(smoothstep(0, L.spiralHole, rad));
        const arm = sin(atan(c.y, c.x).mul(L.spiralArms).add(log(rad.add(0.02)).mul(L.spiralTwist)).sub(u.t.mul(L.spiralTurnRadps)));
        const bands = mix(rgb(L.spiralBands[0]), rgb(L.spiralBands[1]), smoothstep(-L.spiralEdge, L.spiralEdge, arm));
        const spun = mix(col, bands, reveal.mul(u.spiral).mul(L.spiralFullAlpha));
        const dim = col.mul(reveal.mul(u.spiral).mul(L.spiralDim).oneMinus());
        col.assign(mix(spun, dim, u.dimOnly));
        // Flash: a rounded card with a soft shadow; the picture turns slowly
        const half = vec2(u.rw, u.rh).mul(toH), p = uv0.sub(vec2(u.rx, u.ry)).mul(toH);
        const q = abs(p).sub(half).add(L.flashRound);
        const d = length(max(q, 0)).add(min(max(q.x, q.y), 0)).sub(L.flashRound);
        const inside = smoothstep(0, 0.004, d).oneMinus().mul(u.flash);
        col.assign(col.mul(smoothstep(0, 0.04, d).oneMinus().mul(L.flashShadow).mul(u.flash).oneMinus()));
        const cu = p.div(half.y.mul(2)), ca = u.t.mul(0.5);
        const iuv = vec2(cu.x.mul(cos(ca)).sub(cu.y.mul(sin(ca))), cu.x.mul(sin(ca)).add(cu.y.mul(cos(ca)))).mul(0.95).add(0.5);
        const ic = texture(icon, clamp(iuv, 0, 1));
        const ink = smoothstep(-0.012, -0.006, d);
        const pic = mix(mix(rgb(L.flashPaper), ic.rgb, ic.a), rgb(L.flashInk), ink);
        col.assign(mix(col, pic, inside.mul(L.flashAlpha)));
        // Blackout: everything outside the iris goes dim
        const ic0 = uv0.sub(vec2(...L.irisAt)).mul(toH);
        // fx-iris: white is the aperture (radius irisMaskR of the mask), scaled so it sits at u.iris
        const open = irisT ? texture(irisT, ic0.mul(X.irisMaskR).div(u.iris).add(0.5)).r.oneMinus()
          : smoothstep(u.iris.sub(L.irisSoft), u.iris.add(L.irisSoft), length(ic0));
        const shut = dz(open).mul(min(u.black.mul(3), 1));
        col.assign(mix(col, col.mul(L.keepImage), shut));
        // a soft rim of light rides the closing iris edge
        col.addAssign(rgb(L.irisRimColour).mul(open.mul(open.oneMinus()).mul(4).pow(L.irisRimPow).mul(u.black).mul(L.irisRimAlpha)));
        // never blind: the highlights (rails, racing line) come through sharp, read where Melt and
        // Shatter moved the view so even Melt has no still centre
        const clear = sceneTex.sample(suv).rgb;
        const bright = clear.mul(smoothstep(L.keepBright[0], L.keepBright[1], luminance(clear)).mul(L.keepBrightGain));
        if (jit) col.addAssign(jit.mul(X.ditherOut));
        out.assign(max(col, bright));
      });
      return out;
    })();
  }

  function update(screen, dt, aspect) {
    const a = screenAmounts(screen, amt);
    u.any.value = a.any ? 1 : 0;
    if (!a.any) { clock = 0; return; }
    clock += (dt || 0) * a.timeMul;
    u.t.value = clock;
    u.runT.value = (screen.t || 0) * a.timeMul;
    u.aspect.value = aspect > 0 ? aspect : 16 / 9;
    u.blurMul.value = a.blurMul;
    u.flash.value = a.flash; u.spiral.value = a.spiral; u.drain.value = a.brainDrain; u.freeze.value = a.freeze;
    u.black.value = a.blackout; u.pink.value = a.pink; u.melt.value = a.melt; u.shatter.value = a.shatter;
    u.blurOn.value = a.brainDrain + a.melt + a.freeze > 0.001 ? 1 : 0;
    u.dimOnly.value = a.dimOnly ? 1 : 0;
    u.iris.value = irisRadius(a.blackout);
    u.tilt.value = a.gentle ? 0 : shatterTilt(screen.kind === 'shatter' ? screen.t || 0 : 9, a.shatter);
    // polish 2: the IN and OUT of each effect follow its amount (fronts, reveals, cracks then slides)
    const sp = shatterPhase(screen.kind === 'shatter' ? (screen.t || 0) * a.timeMul : 9, a.shatter);
    u.crack.value = sp.crack; u.slide.value = sp.slide;
    u.frostThr.value = frostThreshold(a.freeze);
    u.spiralR.value = spiralOpen(a.spiral);
    u.heartR.value = pinkHeart(a.pink);
    const r = flashRect(a.cover, u.aspect.value, { corner: a.corner, scale: a.cornerScale, seed: a.seed });
    const pop = 1 - L.flashPop * (1 - Math.min(1, a.flash));
    // the card rises a little as it pops in and sinks as it goes
    u.rx.value = r.cx; u.ry.value = r.cy + L.flashPop * 0.8 * (1 - Math.min(1, a.flash)); u.rw.value = r.hw * pop; u.rh.value = r.hh * pop;
  }

  return { node, update, dispose() { icon.dispose(); } };
}
