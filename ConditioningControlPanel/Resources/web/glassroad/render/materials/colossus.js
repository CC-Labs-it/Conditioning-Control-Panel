/* render/materials/colossus.js - soft shading for the colossi and the floating scatter.
 * Unlit node material (no light pass) built from the glTF material it replaces: its colour, vertex
 * paint (COLOR_0, the approved chess paint) and emissive are kept. Shading:
 *   - soft wrap light from the lamp (LOOK.lightDir), the shaded side cooled toward violet instead of
 *     going grey,
 *   - a broad sheen (wider and stronger on metal),
 *   - a rim from the sky palette, so silhouettes glow against the spiral,
 *   - aerial perspective: distance haze toward the sky colour along the view, and a low haze that
 *     swallows whatever hangs below the road, so a 250 m body reads as huge and far.
 * Everything moves only with the palette mix and the camera; no time term, nothing to flicker.
 */
import { MeshBasicNodeMaterial, Color, DoubleSide } from 'three/webgpu';
import { vec3, float, vertexColor, normalWorld, positionWorld, cameraPosition, normalize, dot, max, abs, pow, mix, clamp,
  exp, smoothstep, texture, uv, uniform, faceDirection } from 'three/tsl';
import { LOOK } from '../look.js';
import { worldLook } from '../colossiMath.js';

/** The sky's colour toward world direction d (a cheap stand-in for the dome under the haze). */
export function skyToward(pal, d) {
  const up = d.y.mul(0.5).add(0.5);
  const ground = mix(pal.col(2), pal.col(0), up).mul(LOOK.skyGapDim);
  const bands = mix(pal.col(0), pal.col(1), up);
  return mix(ground, bands, 0.45).mul(LOOK.skyDim);
}

/**
 * src: the glTF material to replace (MeshStandardMaterial / MeshPhysicalMaterial).
 * opts.haze (default 1) scales the aerial haze; opts.instanced is informational (instancing works
 * through normalWorld either way). Returns { material, uniforms: { haze } }.
 */
export function createSoftMaterial(src, pal, { haze = 1 } = {}) {
  const W = worldLook();
  const u = { haze: uniform(haze) };
  const c = new Color().copy((src && src.color) || new Color(1, 1, 1));
  let base = vec3(c.r, c.g, c.b);
  if (src && src.vertexColors) base = base.mul(vertexColor().rgb);
  if (src && src.map) base = base.mul(texture(src.map, uv()).rgb);
  const metal = float(Math.min(1, (src && src.metalness) || 0));

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
  const lit = mix(base.mul(shadeTint).mul(W.shadeFloor), base, diffuse);
  const sky = Nf.y.mul(0.5).add(0.5).mul(0.12);                          // a little sky fill from above
  const H = normalize(Lv.add(V));
  const spec = pow(max(dot(Nf, H), 0), mix(float(W.specPow * 0.4), float(W.specPow), metal)).mul(W.specGain).mul(metal.mul(0.8).add(0.35));
  const rim = pow(float(1).sub(abs(dot(Nf, V))), W.rimPow).mul(W.rimGain);
  let col = lit.add(base.mul(pal.col(1)).mul(sky)).add(vec3(1, 0.95, 0.92).mul(spec)).add(mix(pal.col(0), pal.col(1), 0.35).mul(rim));
  if (src && src.emissive) {
    const e = src.emissive, k = src.emissiveIntensity ?? 1;
    if (e.r + e.g + e.b > 0) col = col.add(vec3(e.r * k, e.g * k, e.b * k));
  }
  // aerial perspective: far haze along the view, low haze under the road plane
  const far = float(1).sub(exp(dist.div(-W.hazeM))).mul(W.hazeMax);
  const low = smoothstep(0, W.hazeLowM, positionWorld.y.negate()).mul(W.hazeLowMax);
  const h = float(1).sub(float(1).sub(far).mul(float(1).sub(low))).mul(u.haze);
  col = mix(col, skyToward(pal, V.negate()), clamp(h, 0, 1));

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
