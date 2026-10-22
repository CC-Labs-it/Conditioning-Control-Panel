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
 */
import { TextureLoader, SRGBColorSpace, Color, NearestFilter } from 'three/webgpu';
import {
  Fn, If, uniform, screenUV, vec2, vec3, float, texture, mix, smoothstep, sin, cos, atan, log, length, abs, max, min,
  luminance, mx_noise_float, clamp, normalize, screenCoordinate,
} from 'three/tsl';
import { currentArt } from './art.js';
import { SCREEN_LOOK as L } from './items/look.js';
import { screenAmounts, irisRadius, flashRect, shatterTilt } from './items/fxmath.js';
import { maskLook } from './atlas.js';

const rgb = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
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
        // where to read: Shatter tilts about the centre, Melt runs down (reads from above) and wobbles
        const ct = cos(u.tilt), st = sin(u.tilt);
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
        const col = sceneTex.sample(suv).rgb.toVar();
        // blur: Brain Drain at the edges, Freeze under the frost, Melt everywhere
        const edge = dz(smoothstep(L.drainEdge[0], L.drainEdge[1], rad));
        const frostM = frostT ? texture(frostT, uv0).r : null;
        const frost = dz(frostT ? smoothstep(X.frostMask[0], X.frostMask[1], frostM)
          : smoothstep(L.frostEdge[0], L.frostEdge[1], rad.add(mx_noise_float(c.mul(L.frostNoise)).mul(0.18))));
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
          if (crack) col.assign(mix(col, col.mul(X.crackDark).add(rgb(X.crackTint).mul(X.crackLight)), crack));
        });
        const lum = luminance(col);
        col.assign(mix(col, vec3(lum), u.drain.mul(edge).mul(L.drainGrey)));
        col.assign(mix(vec3(lum), col, u.melt.mul(L.meltSat - 1).add(1)));
        const low = mix(vec3(0.5), col, u.pink.mul(L.pinkContrast).oneMinus()).mul(vec3(...L.pinkTint)).add(L.pinkLift);
        col.assign(mix(col, low, u.pink));
        const ice = frostT ? smoothstep(X.frostVeinBand[0], X.frostVeinBand[1], frostM) : abs(mx_noise_float(c.mul(L.frostNoise * 4))).oneMinus().pow(10);
        col.assign(mix(col, rgb(L.frostColour).add(ice.mul(L.frostVeins)), frost.mul(u.freeze).mul(L.frostAlpha)));
        // Spiral: arms over the middle (or a soft dim under Gentle)
        const ring = smoothstep(L.spiralR[0] * 0.4, L.spiralR[0], rad).mul(smoothstep(L.spiralR[1] * 0.7, L.spiralR[1], rad).oneMinus());
        const arm = smoothstep(0.35, 0.65, sin(atan(c.y, c.x).mul(L.spiralArms).add(log(rad.add(0.02)).mul(L.spiralTwist)).sub(u.t.mul(L.spiralTurnRadps))).mul(0.5).add(0.5));
        const spun = mix(col, rgb(L.spiralColour), arm.mul(ring).mul(u.spiral).mul(L.spiralAlpha));
        const dim = col.mul(ring.mul(u.spiral).mul(L.spiralDim).oneMinus());
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
    const r = flashRect(a.cover, u.aspect.value, { corner: a.corner, scale: a.cornerScale, seed: a.seed });
    const pop = 1 - L.flashPop * (1 - Math.min(1, a.flash));
    u.rx.value = r.cx; u.ry.value = r.cy; u.rw.value = r.hw * pop; u.rh.value = r.hh * pop;
  }

  return { node, update, dispose() { icon.dispose(); } };
}
