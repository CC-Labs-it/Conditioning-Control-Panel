/* render/materials/colossus.js - soft shading for the colossi and the floating scatter.
 * Unlit node material (no light pass) built from the glTF material it replaces: its colour, vertex
 * paint (COLOR_0, the approved chess paint) and emissive are kept. Shading:
 *   - an optional saturated tint over the paint's luminance (polish 3: every prop and colossus wears a
 *     spectrum colour; metal trims and screens keep their own),
 *   - soft wrap light from the lamp (LOOK.lightDir), the shaded side cooled toward violet instead of
 *     going grey,
 *   - a broad sheen (wider and stronger on metal),
 *   - a rim (the tint lifted toward white, or the sky palette), so silhouettes pop off the sky,
 *   - no seams or flutes: lines running up an upright body read as ribbing (polish 3 review),
 *   - aerial perspective: full colour inside hazeNearM, a light haze toward the sky colour beyond it.
 * Everything moves only with the palette mix and the camera; no time term, nothing to flicker.
 */
import { MeshBasicNodeMaterial, Color, DoubleSide } from 'three/webgpu';
import { vec3, float, vertexColor, normalWorld, positionWorld, cameraPosition, normalize, dot, max, abs, pow, mix, clamp,
  smoothstep, texture, uv, uniform, faceDirection, reflect, attribute, positionGeometry } from 'three/tsl';
import { LOOK } from '../look.js';
import { worldLook } from '../colossiMath.js';
import { currentArt } from '../art.js';
import { surfaceFinish } from '../materialDetailMath.js';
import { VERTEX_SHADE_ATTRIBUTE } from '../vertexShadeMath.js';

/** The sky's colour toward world direction d (a cheap stand-in for the dome under the haze). */
export function skyToward(pal, d) {
  const up = d.y.mul(0.5).add(0.5);
  const ground = mix(pal.col(2), pal.col(0), up).mul(LOOK.skyGapDim);
  const bands = mix(pal.col(0), pal.col(1), up);
  return mix(ground, bands, 0.45).mul(LOOK.skyDim);
}

/** Parts that keep their own colour under a tint: metal trims, screens, lamps and the credit slot. */
const KEEP = ['screen', 'lamp', 'credit', 'gold', 'silver', 'rim'];
export function keepsOwnColour(src) {
  const name = String((src && src.name) || '').toLowerCase();
  return ((src && src.metalness) || 0) > 0.5 || KEEP.some((k) => name.includes(k));
}

/**
 * src: the glTF material to replace (MeshStandardMaterial / MeshPhysicalMaterial).
 * opts.haze (default 1) scales the aerial haze. opts.tint: a vec3 node (a uniform, or an instanced
 * attribute) the body takes on; opts.rimGain / rimPow override the rim. Polish 4 (hero props): opts.rim, a
 * vec3 node for the rim colour (a contrasting sheen, like the chess game's silicone); opts.chrome, a 0..1
 * float node mixing in a mirror of the sky; opts.tintMix overrides the tint share (0 keeps the paint);
 * opts.ink (cards): the paint's light becomes a white porcelain face and its dark marks take the tint;
 * opts.fill lifts the shaded side to at least fill times the base colour (white silicone stays white).
 * Returns { material, uniforms: { haze } }.
 */
export function createSoftMaterial(src, pal, { haze = 1, tint = null, rimGain = null, rimPow = null, rim = null, chrome = null, tintMix = null, fill = 0, ink = false } = {}) {
  const W = worldLook();
  const u = { haze: uniform(haze) };
  const c = new Color().copy((src && src.color) || new Color(1, 1, 1));
  // Colours are data, so differently painted props can share a compiled shader.
  let base = uniform(c);
  if (src && src.vertexColors) base = base.mul(vertexColor().rgb);
  if (src && src.map) base = base.mul(texture(src.map, uv()).rgb);
  const finish = surfaceFinish(src || {});
  const metal = uniform(finish.metalness), specExponent = uniform(finish.exponent), specGain = uniform(finish.gain);
  // Only validated source primitives carry this attribute. Dynamic faces remain neutral.
  const vertexShade = src?.userData?.ccpVertexShade ? attribute(VERTEX_SHADE_ATTRIBUTE, 'vec2') : null;
  const ao = vertexShade ? vertexShade.x : float(1);
  const curve = vertexShade ? clamp(vertexShade.y.mul(255).sub(128).div(127), -1, 1) : float(0);
  const cavity = mix(float(0.55), float(1), ao);
  // the tint keeps the paint's light and dark (symbols, panel lines) and swaps its hue
  if (tint && ink && !keepsOwnColour(src)) {
    const lum = dot(base, vec3(0.3, 0.59, 0.11));
    base = mix(tint, vec3(1, 0.97, 0.98), smoothstep(0.3, 0.7, lum));
  } else if (tint && !keepsOwnColour(src)) {
    const lum = dot(base, vec3(0.3, 0.59, 0.11));
    base = mix(base, tint.mul(lum.mul(W.tintGain).add(W.tintFloor)), tintMix ?? W.tintMix);
  }

  const N = normalize(normalWorld);
  const Lv = normalize(vec3(...LOOK.lightDir));
  const toCam = cameraPosition.sub(positionWorld), dist = toCam.length(), V = toCam.div(max(dist, 1e-3));
  // double-sided parts: light the face the camera sees
  const Nf = N.mul(faceDirection);
  const wrap = clamp(dot(Nf, Lv).add(W.wrap).div(1 + W.wrap), 0, 1);
  const diffuse = wrap.mul(wrap).mul(float(3).sub(wrap.mul(2)));          // smooth, no hard terminator
  // the shaded side cools toward a fixed violet (a palette colour can be cream, which would lift it)
  const st = new Color(W.shadeTint);
  const shadeTint = mix(vec3(1, 1, 1), vec3(st.r, st.g, st.b).mul(1.5), 0.6);
  const lit = mix(base.mul(shadeTint).mul(W.shadeFloor), base, diffuse).mul(curve.mul(0.06).add(1));
  const sky = Nf.y.mul(0.5).add(0.5).mul(0.12);                          // a little sky fill from above
  const H = normalize(Lv.add(V));
  const wearMap = finish.metalness > 0.5 ? currentArt()?.detail('detail-gold-wear') : null;
  // Source-local scalar variation needs no UV changes on the immutable watch/chess models.
  const wear = wearMap ? texture(wearMap, positionGeometry.xy.div(0.22)).r : float(0);
  const glazeMap = finish.metalness < .5 && !keepsOwnColour(src)
    ? currentArt()?.detail('detail-porcelain-roughness') : null;
  // Diagonal source projection gives curved porcelain a quiet fired-glaze variation.
  // Existing mipmapped art keeps the glaze smooth at a distance, without a new texture owner.
  const glazeUV = positionGeometry.xy.add(positionGeometry.zz.mul(.37)).div(.24);
  const glaze = glazeMap ? texture(glazeMap, glazeUV).r.sub(.5) : float(0);
  const specPower = specExponent.mul(wear.mul(-0.28).add(1)).mul(glaze.mul(.38).add(1));
  const spec = pow(max(dot(Nf, H), 0), specPower).mul(specGain.mul(W.specGain))
    .mul(wear.mul(-0.22).add(1)).mul(glaze.mul(-.20).add(1));
  const edge = max(float(1).sub(abs(dot(Nf, V))), 0);   // never negative under pow (NaN guard)
  let col = lit.add(base.mul(pal.col(1)).mul(sky));
  if (fill > 0) col = max(col, base.mul(fill));
  col = col.mul(cavity).add(mix(vec3(1, 0.95, 0.92), base, metal.mul(0.65)).mul(spec).mul(ao.mul(0.3).add(0.7)));
  if (rim) {
    col = col.add(rim.mul(pow(edge, rimPow ?? W.propRimPow)).mul(rimGain ?? W.propRimGain));
  } else if (tint) {
    // a bright rim in the body's own hue, lifted toward white: reads against any sky colour
    col = col.add(mix(tint, vec3(1, 1, 1), W.propRimWhite).mul(pow(edge, rimPow ?? W.propRimPow)).mul(rimGain ?? W.propRimGain));
  } else {
    col = col.add(mix(pal.col(0), pal.col(1), 0.35).mul(pow(edge, W.rimPow)).mul(rimGain ?? W.rimGain));
  }
  if (src && src.emissive) {
    const e = src.emissive, k = src.emissiveIntensity ?? 1;
    if (e.r + e.g + e.b > 0) col = col.add(vec3(e.r * k, e.g * k, e.b * k));
  }
  if (chrome && !keepsOwnColour(src)) {
    // chrome: the sky mirrored about the normal, lifted by the tint and a hard highlight
    const R = reflect(V.negate(), Nf);
    const mirror = skyToward(pal, R).mul(1.6).add(base.mul(0.25)).add(vec3(1, 1, 1).mul(pow(max(dot(Nf, H), 0), 60).mul(1.4)));
    col = mix(col, mirror.add(rim ? rim.mul(pow(edge, 1.5)).mul(0.8) : vec3(0, 0, 0)), clamp(chrome, 0, 1));
  }
  // aerial perspective: nothing inside hazeNearM, a light haze toward the sky beyond it
  const far = smoothstep(W.hazeNearM, W.hazeFarM, dist).mul(W.hazeMax);
  let h = far;
  if (W.hazeLowMax > 0) {
    const low = smoothstep(0, W.hazeLowM, positionWorld.y.negate()).mul(W.hazeLowMax);
    h = float(1).sub(float(1).sub(far).mul(float(1).sub(low)));
  }
  col = mix(col, skyToward(pal, V.negate()), clamp(h.mul(u.haze), 0, 1));

  const m = new MeshBasicNodeMaterial({ side: DoubleSide, fog: false });
  m.name = (src && src.name) || 'colossus';
  m.colorNode = col;
  if (src && (src.transparent || src.transmission > 0)) {
    m.transparent = true; m.depthWrite = false;
    m.opacity = src.transmission > 0 ? 0.35 : src.opacity;
  }
  return { material: m, uniforms: u };
}

/** Swap every mesh material under root for its soft twin (one twin per source material). */
export function softenAll(root, pal, cache = new Map(), opts = {}) {
  root.traverse((o) => {
    if (!o.isMesh || !o.material || Array.isArray(o.material)) return;
    let twin = cache.get(o.material);
    if (!twin) { twin = createSoftMaterial(o.material, pal, opts).material; cache.set(o.material, twin); }
    o.material = twin;
  });
  return cache;
}
