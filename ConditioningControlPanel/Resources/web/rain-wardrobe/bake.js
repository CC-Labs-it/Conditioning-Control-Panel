// Rain V2 bake on Apply (lane R6, RAIN-V2-PLAN 2.3 / 2.5). The tube never runs live 3D: when the
// host sends {type:'rain-bake', lookHash, clips, fps, w, h} the page renders each clip of the
// CURRENT look offscreen at 2x, box-filters it down to the tube size (2x the tube's on-screen size, so
// it stays crisp at 200% DPI), maps the colours onto one shared 256-colour palette (soft alpha kept:
// a smaller pack, no visible banding) and streams PNG frames to the host. The old pixel pass (a port of
// blender-scripting/rain-tube-1024/scripts/pixel.py, 2x2 grain, 48 colours, hard alpha) stays for
// request.pixel / request.palette === 'shipped' (the side-by-side gate against the shipped clips):
//   rain-bake-frame {clip, index, png}   rain-bake-progress {clip, frame, of}
//   rain-bake-done {clip, frames}        rain-bake-done {all:true}   (plus lookHash on each)
// A clip the GLB does not carry yet answers rain-bake-done {clip, frames:0, skipped:true}.
// rain-bake-cancel (or a newer rain-bake) stops at the next frame: rain-bake-done {all:true, cancelled:true}.
// A readback that is all opaque or all black posts boot-error and rain-bake-done {all:true, error}.
// The visible view keeps running: the bake draws through its own renderer and its own mixer,
// and every bake frame hands the pose back before the next paint.

export const BAKE = Object.freeze({ scale: 2, grain: 2, colors: 48, smoothColors: 256, alphaCut: 128, fps: 15, w: 384, h: 618, toneMapping: 'AgXToneMapping', exposure: 1 });
const BAKE_TONE = { AgXToneMapping: 1, ACESFilmicToneMapping: 1, NeutralToneMapping: 1 };

/** Opaque colours of the October 24 shipped idle.webp (the 192x309 pixel pass, 44 of its 48 median-cut
 *  entries are used). Only for request.palette === 'shipped' (the default-look side-by-side gate);
 *  a custom look gets its own median cut from its idle rest frame, as pixel.py builds one. */
export const SHIPPED_IDLE_PALETTE = Object.freeze(['561b30', '854747', 'bb2c7a', 'ab5861', 'd45095', 'b26f5d', '9a7a68', 'b67767', 'd75c9c', 'bb7a67', 'd8619d', 'd962a0', 'da64a1', 'be7f67', 'be817a', 'dc6aa4', 'd5709d', 'c18472', 'c58875', 'dd72a8', 'b7927d', 'c98f7e', 'e28299', 'cc9482', 'd19787', 'cc9a89', 'bda28c', 'd39c89', 'd29d8d', 'd2a08f', 'd5a18f', 'b9b095', 'd4a395', 'd8a595', 'c8b29d', 'd7ac9e', 'ccb49f', 'ccb8a4', 'e6aea1', 'd0b9a6', 'cdbca8', 'd0beac', 'd7bfae', 'dac9b8']
  .map(hex => [parseInt(hex.slice(0, 2), 16), parseInt(hex.slice(2, 4), 16), parseInt(hex.slice(4, 6), 16)]));

/** The shipped clips' camera (rain-tube-1024 run_all.sh + render_clips.py camera() + out/crop.json):
 *  orthographic, 2.52 m over a 440x708 film, aimed at (0, 1.16) from -20 degrees, then the crop box
 *  that pixel.py cut down to 192x309. The page's "Tube size" preview (perspective, 1.04 m) cuts the
 *  boots off at this size, so the bake keeps the Blender framing the tube already plays. */
export const TUBE_CAMERA = Object.freeze({ ortho: 2.52, film: [440, 708], crop: [21.87, 41, 425.13, 690], yawDeg: -20, aim: [0, 1.16, 0], distance: 4, lift: 0.01 });

/** The four ortho frustum edges (metres from the aim point) the crop box leaves. */
export function tubeFrustum(spec = TUBE_CAMERA) {
  const perMetre = spec.film[1] / spec.ortho, cx = spec.film[0] / 2, cy = spec.film[1] / 2;
  return { left: (spec.crop[0] - cx) / perMetre, right: (spec.crop[2] - cx) / perMetre, top: (cy - spec.crop[1]) / perMetre, bottom: (cy - spec.crop[3]) / perMetre };
}

export function makeTubeCamera(THREE, spec = TUBE_CAMERA) {
  const f = tubeFrustum(spec), a = spec.yawDeg * Math.PI / 180;
  const camera = new THREE.OrthographicCamera(f.left, f.right, f.top, f.bottom, 0.01, 100);
  // Blender (x, y, z) is glTF (x, z, -y): the Blender camera sits at aim + (4 sin a, -4 cos a, .01).
  camera.position.set(spec.aim[0] + spec.distance * Math.sin(a), spec.aim[1] + spec.lift, spec.aim[2] + spec.distance * Math.cos(a));
  camera.lookAt(spec.aim[0], spec.aim[1], spec.aim[2]);
  camera.updateProjectionMatrix();
  return camera;
}

export function frameCount(duration, fps) { return Math.max(1, Math.round(duration * fps)); }

/** Box (area) downscale by an integer factor. Premultiplied RGBA in and out, top-left aligned. */
export function boxDownscale(src, width, height, factor) {
  const w = Math.floor(width / factor), h = Math.floor(height / factor), n = factor * factor;
  const out = new Uint8ClampedArray(w * h * 4);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    let r = 0, g = 0, b = 0, a = 0;
    for (let dy = 0; dy < factor; dy++) {
      let i = ((y * factor + dy) * width + x * factor) * 4;
      for (let dx = 0; dx < factor; dx++, i += 4) { r += src[i]; g += src[i + 1]; b += src[i + 2]; a += src[i + 3]; }
    }
    const o = (y * w + x) * 4;
    out[o] = Math.round(r / n); out[o + 1] = Math.round(g / n); out[o + 2] = Math.round(b / n); out[o + 3] = Math.round(a / n);
  }
  return { data: out, width: w, height: h };
}

export function premultiply(src) {
  const out = new Uint8ClampedArray(src.length);
  for (let i = 0; i < src.length; i += 4) {
    const a = src[i + 3];
    out[i] = Math.round(src[i] * a / 255); out[i + 1] = Math.round(src[i + 1] * a / 255); out[i + 2] = Math.round(src[i + 2] * a / 255); out[i + 3] = a;
  }
  return out;
}

export function unpremultiply(src) {
  const out = new Uint8ClampedArray(src.length);
  for (let i = 0; i < src.length; i += 4) {
    const a = src[i + 3];
    if (a) { out[i] = Math.round(src[i] * 255 / a); out[i + 1] = Math.round(src[i + 1] * 255 / a); out[i + 2] = Math.round(src[i + 2] * 255 / a); }
    out[i + 3] = a;
  }
  return out;
}

/** Deterministic median cut over the opaque pixels (alpha >= cut) of a straight RGBA buffer.
 *  pixel.py also counts transparent pixels (as black); skipping them spends all 48 on Rain. */
export function medianCut(rgba, colors = BAKE.colors, cut = BAKE.alphaCut) {
  const counts = new Map();
  for (let i = 0; i < rgba.length; i += 4) {
    if (rgba[i + 3] < cut) continue;
    const key = (rgba[i] << 16) | (rgba[i + 1] << 8) | rgba[i + 2];
    counts.set(key, (counts.get(key) || 0) + 1);
  }
  const all = [...counts].sort((p, q) => p[0] - q[0]).map(([key, n]) => [(key >> 16) & 255, (key >> 8) & 255, key & 255, n]);
  if (!all.length) return [[0, 0, 0]];
  const range = box => {
    let best = -1, axis = 0;
    for (let c = 0; c < 3; c++) {
      let lo = 255, hi = 0;
      for (const p of box) { if (p[c] < lo) lo = p[c]; if (p[c] > hi) hi = p[c]; }
      if (hi - lo > best) { best = hi - lo; axis = c; }
    }
    return { spread: best, axis };
  };
  const boxes = [all];
  while (boxes.length < colors) {
    let pick = -1, spread = 0, axis = 0;
    boxes.forEach((box, index) => { const r = range(box); if (box.length > 1 && r.spread > spread) { pick = index; spread = r.spread; axis = r.axis; } });
    if (pick < 0) break;
    const box = boxes[pick].slice().sort((p, q) => p[axis] - q[axis] || p[0] - q[0] || p[1] - q[1] || p[2] - q[2]);
    const total = box.reduce((s, p) => s + p[3], 0);
    let acc = 0, split = 1;
    for (let i = 0; i < box.length - 1; i++) { acc += box[i][3]; if (acc * 2 >= total) { split = i + 1; break; } split = i + 1; }
    boxes.splice(pick, 1, box.slice(0, split), box.slice(split));
  }
  return boxes.map(box => {
    let r = 0, g = 0, b = 0, n = 0;
    for (const p of box) { r += p[0] * p[3]; g += p[1] * p[3]; b += p[2] * p[3]; n += p[3]; }
    return [Math.round(r / n), Math.round(g / n), Math.round(b / n)];
  });
}

/** Nearest palette colour (squared RGB distance, no dither, first entry wins a tie). */
export function nearestColor(palette) {
  const cache = new Map();
  return (r, g, b) => {
    const key = (r << 16) | (g << 8) | b;
    let hit = cache.get(key);
    if (hit) return hit;
    if (cache.size >= 1 << 20) cache.clear();   // one lookup serves a whole bake: keep it bounded
    let best = Infinity;
    for (const p of palette) {
      const d = (p[0] - r) ** 2 + (p[1] - g) ** 2 + (p[2] - b) ** 2;
      if (d < best) { best = d; hit = p; }
    }
    cache.set(key, hit);
    return hit;
  };
}

/** pixel.py pixelate(): area-downscale by grain (premultiplied, no dark fringe), hard alpha,
 *  one shared palette, nearest-neighbour back up, pasted bottom-centre on a clear canvas. */
export function pixelate(frame, { grain = BAKE.grain, palette, alphaCut = BAKE.alphaCut } = {}) {
  const { width: W, height: H } = frame;
  const small = boxDownscale(premultiply(frame.data), W, H, grain);
  const straight = unpremultiply(small.data);
  const pick = palette ? nearestColor(palette) : null;
  const out = new Uint8ClampedArray(W * H * 4);
  const x0 = Math.floor((W - small.width * grain) / 2), y0 = H - small.height * grain;
  for (let y = 0; y < small.height; y++) for (let x = 0; x < small.width; x++) {
    const i = (y * small.width + x) * 4;
    if (straight[i + 3] < alphaCut) continue;
    const c = pick ? pick(straight[i], straight[i + 1], straight[i + 2]) : straight.subarray(i, i + 3);
    for (let dy = 0; dy < grain; dy++) for (let dx = 0; dx < grain; dx++) {
      const o = ((y0 + y * grain + dy) * W + x0 + x * grain + dx) * 4;
      out[o] = c[0]; out[o + 1] = c[1]; out[o + 2] = c[2]; out[o + 3] = 255;
    }
  }
  return { data: out, width: W, height: H };
}

/** The tube pass: every visible pixel's colour snaps to the shared palette (pick = nearestColor),
 *  its alpha is kept as is, so edges stay soft and nothing is cut to blocks. Straight RGBA in and out. */
export function smoothPass(frame, pick) {
  const src = frame.data, out = new Uint8ClampedArray(src.length);
  for (let i = 0; i < src.length; i += 4) {
    const a = src[i + 3];
    if (!a) continue;
    const c = pick(src[i], src[i + 1], src[i + 2]);
    out[i] = c[0]; out[i + 1] = c[1]; out[i + 2] = c[2]; out[i + 3] = a;
  }
  return { data: out, width: frame.width, height: frame.height };
}

/** The alpha / premultiply self-check: a usable readback holds both clear and solid pixels
 *  and is not all black. */
export function checkReadback(data, cut = BAKE.alphaCut) {
  let transparent = 0, opaque = 0, lit = 0;
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] === 0) transparent++; else if (data[i + 3] >= cut) opaque++;
    if (data[i] || data[i + 1] || data[i + 2]) lit++;
  }
  const reason = !lit ? 'all black' : !transparent ? 'all opaque' : !opaque ? 'nothing drawn' : '';
  return { ok: !reason, transparent, opaque, reason };
}

/** Render idle frame 0 (or the first clip the GLB has) and check the readback. */
export function selfCheck(source) {
  const clip = source.hasClip('idle') ? 'idle' : source.firstClip?.();
  if (!clip) return { ok: false, reason: 'no clips' };
  const raw = source.render(clip, 0);
  return { ...checkReadback(raw.data), clip, raw };
}

/** The tube look (owner 2026-10-30: the HD clips felt plasticky and blurry, the fishnets blended
 *  into the legs; "black outlines on some hairs and the dress, the fishnets, the shoes", colours that
 *  pop). Two halves, the same in both pipelines (shipped clips: rain-tube-1024 toon_render.py +
 *  palette_pass.mjs; in-app bake: createThreeSource + tubeFrame):
 *  - at render time, INK: an inverted hull per outlined mesh (pushed out along its normals by
 *    INK.width metres, hair INK.hairWidth, head INK.headWidth; back faces only, #1b0f19), and the
 *    fishnet grid darker, more opaque and a little wider (INK.fishnet*);
 *  - after the 2x -> 1x box filter, the shared post pass (tubeFrame): a mild unsharp mask on the
 *    premultiplied colour (alpha untouched, colour never above alpha) and the pop: chroma x
 *    (1 + saturation + vibrance * (1 - sat)), skin-hued pixels get the plain saturation step only
 *    (no orange skin), capped so no channel clips (the pink dress stays pink, never neon).
 *  1 px of the 384x618 tube = 3.74 mm at the tube camera, so width 5.2 mm = about 1.4 px. */
export const INK = Object.freeze({
  color: 0x0b040a /* the #1b0f19 emission as Blender's AgX draws it */, width: 0.0052, hairWidth: 0.0038, headWidth: 0.003,
  skip: /eye|lash|brow|gum|teeth|tongue|mascara|hoop|zip|seam|clasp|cornea|dot|tip|welt/i,
  hair: /hair/i, head: /GEO-rain-head/,
  fishnetDarken: 0.3, fishnetOpacity: 0.95, fishnetStrand: 0.115
});
export const TOON = Object.freeze({ sharpen: 0.6, sharpenSigma: 0.7, saturation: 0.22, vibrance: 0.25 });

/** Unsharp mask, 3x3 Gaussian (sigma ~0.7 px), on PREMULTIPLIED RGBA: colour sharpened and kept
 *  within [0, alpha], alpha untouched (no halo past the silhouette). */
export function sharpenPass(frame, opts = TOON) {
  const t = { ...TOON, ...opts }, { width: W, height: H } = frame, src = frame.data;
  const out = new Uint8ClampedArray(src);
  if (!(t.sharpen > 0)) return { data: out, width: W, height: H };
  const e = Math.exp(-1 / (2 * t.sharpenSigma * t.sharpenSigma)), k0 = 1 / (1 + 2 * e), k1 = e / (1 + 2 * e);
  const n = W * H, tmp = new Float32Array(n * 3), blur = new Float32Array(n * 3);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const p = y * W + x, l = x > 0 ? p - 1 : p, r = x < W - 1 ? p + 1 : p;
    for (let c = 0; c < 3; c++) tmp[p * 3 + c] = k1 * src[l * 4 + c] + k0 * src[p * 4 + c] + k1 * src[r * 4 + c];
  }
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const p = y * W + x, u = y > 0 ? p - W : p, d = y < H - 1 ? p + W : p;
    for (let c = 0; c < 3; c++) blur[p * 3 + c] = k1 * tmp[u * 3 + c] + k0 * tmp[p * 3 + c] + k1 * tmp[d * 3 + c];
  }
  for (let p = 0; p < n; p++) {
    const i = p * 4, a = src[i + 3];
    if (!a) continue;
    for (let c = 0; c < 3; c++) {
      const v = src[i + c] + t.sharpen * (src[i + c] - blur[p * 3 + c]);
      out[i + c] = Math.round(v < 0 ? 0 : v > a ? a : v);
    }
  }
  return { data: out, width: W, height: H };
}

/** The pop pass. Straight RGBA in and out, alpha untouched. */
export function popPass(frame, opts = TOON) {
  const t = { ...TOON, ...opts }, src = frame.data, out = new Uint8ClampedArray(src.length);
  for (let i = 0; i < src.length; i += 4) {
    const r = src[i], g = src[i + 1], b = src[i + 2];
    out[i + 3] = src[i + 3];
    const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
    if (!src[i + 3] || mx === mn) { out[i] = r; out[i + 1] = g; out[i + 2] = b; continue; }
    const sat = (mx - mn) / mx;
    // Skin: red leads, hue 5-45 degrees (the orange side), not already strongly saturated.
    let skin = 0;
    if (mx === r && g >= b) {
      const hue = 60 * (g - b) / (mx - mn);
      skin = Math.max(0, Math.min(1, (hue - 5) / 8, (45 - hue) / 10)) * Math.max(0, Math.min(1, (0.75 - sat) / 0.15));
    }
    let s = 1 + t.saturation + t.vibrance * (1 - sat) * (1 - skin);
    const Y = 0.299 * r + 0.587 * g + 0.114 * b;
    for (const c of [r, g, b]) {
      if (c > Y) s = Math.min(s, (255 - Y) / (c - Y));
      else if (c < Y) s = Math.min(s, Y / (Y - c));
    }
    if (s < 1) s = 1;
    out[i] = Math.round(Y + (r - Y) * s); out[i + 1] = Math.round(Y + (g - Y) * s); out[i + 2] = Math.round(Y + (b - Y) * s);
  }
  return { data: out, width: frame.width, height: frame.height };
}

/** Render (premultiplied, an integer multiple of w x h) -> the tube frame (straight, w x h):
 *  box down, then (toon, default) sharpenPass + popPass; toon false = the plain October downscale. */
export function tubeFrame(raw, w, h, { toon = true, opts = TOON } = {}) {
  const factor = Math.max(1, Math.round(raw.width / w));
  let down = factor > 1 ? boxDownscale(raw.data, raw.width, raw.height, factor) : { data: raw.data, width: raw.width, height: raw.height };
  if (down.width !== w || down.height !== h) throw new Error(`Bake frame ${down.width}x${down.height}, expected ${w}x${h}`);
  if (toon) down = sharpenPass(down, opts);
  const straight = { data: unpremultiply(down.data), width: w, height: h };
  return toon ? popPass(straight, opts) : straight;
}

/** The bake loop. source = { hasClip(name), duration(name), render(name, t) -> premultiplied
 *  RGBA {data, width, height} at scale x (w, h), encode({data, width, height}) -> base64 PNG,
 *  dispose() }. Frame i of a clip samples t = i / fps, round(duration * fps) frames. */
export async function runBake({ request, source, post, yieldFrame = () => new Promise(r => setTimeout(r, 0)), cancelled = () => false }) {
  const fps = Number(request.fps) > 0 ? Number(request.fps) : BAKE.fps;
  const w = Number(request.w) > 0 ? Number(request.w) : BAKE.w, h = Number(request.h) > 0 ? Number(request.h) : BAKE.h;
  const lookHash = typeof request.lookHash === 'string' ? request.lookHash : '';
  const clips = [...new Set((Array.isArray(request.clips) ? request.clips : []).filter(c => typeof c === 'string' && c))];
  const send = message => post({ ...message, lookHash });
  try {
    const check = selfCheck(source);
    if (!check.ok) {
      post({ type: 'boot-error', msg: `Rain bake readback failed: ${check.reason}` });
      send({ type: 'rain-bake-done', all: true, error: check.reason });
      return { ok: false, reason: check.reason };
    }
    const pixel = request.pixel === true || request.palette === 'shipped';
    // The smooth (tube) pass inks and pops each frame before its palette, which comes from the INKED
    // idle rest frame; the pixel / shipped-palette gate stays the plain October 24 pass.
    const toon = { toon: !pixel };
    const palette = request.palette === 'shipped' ? SHIPPED_IDLE_PALETTE
      : medianCut(tubeFrame(check.raw, w, h, toon).data, pixel ? BAKE.colors : BAKE.smoothColors);
    const pick = nearestColor(palette);
    const pass = pixel ? frame => pixelate(frame, { palette }) : frame => smoothPass(frame, pick);
    const baked = {};
    for (const clip of clips) {
      if (cancelled()) break;
      if (!source.hasClip(clip)) { send({ type: 'rain-bake-done', clip, frames: 0, skipped: true }); continue; }
      const of = frameCount(source.duration(clip), fps);
      for (let index = 0; index < of; index++) {
        if (cancelled()) break;
        const frame = pass(tubeFrame(source.render(clip, index / fps), w, h, toon));
        send({ type: 'rain-bake-frame', clip, index, png: await source.encode(frame) });
        send({ type: 'rain-bake-progress', clip, frame: index + 1, of });
        await yieldFrame();
      }
      if (cancelled()) break;
      baked[clip] = of;
      send({ type: 'rain-bake-done', clip, frames: of, fps, w, h });
    }
    if (cancelled()) { send({ type: 'rain-bake-done', all: true, cancelled: true }); return { ok: false, cancelled: true, baked }; }
    send({ type: 'rain-bake-done', all: true });
    return { ok: true, baked };
  } catch (error) {
    const msg = error?.message || String(error);
    post({ type: 'boot-error', msg: `Rain bake failed: ${msg}` });
    send({ type: 'rain-bake-done', all: true, error: msg });
    return { ok: false, reason: msg };
  } finally {
    source.dispose?.();
  }
}

/** The bake-only layer: the ink hulls live on it, the bake camera sees it, the visible view never does. */
export const INK_LAYER = 5;

/** Hull width (metres) for one mesh, or 0 = no hull (INK.skip: eyes, lashes, mouth, zips, seams...). */
export function inkWidth(mesh, ink = INK) {
  const mats = (Array.isArray(mesh.material) ? mesh.material : [mesh.material]).filter(Boolean);
  const names = [mesh.name, mesh.parent?.name || '', ...mats.map(m => m.name || '')].join(' ');
  if (ink.skip.test(names)) return 0;
  return ink.head.test(names) ? ink.headWidth : ink.hair.test(names) ? ink.hairWidth : ink.width;
}

/** The inverted-hull material for one mesh material: the same program (skinning, morphs, the
 *  wardrobe's own discards via its onBeforeCompile), pushed out along the skinned normal by width
 *  (object units), back faces only, painted INK.color. */
export function inkMaterial(THREE, base, width, { skinned = true, color = INK.color } = {}) {
  const m = base.clone();
  m.name = `${base.name}.ink`; m.side = THREE.BackSide;
  const ink = new THREE.Color(color), rgb = [ink.r, ink.g, ink.b].map(v => v.toFixed(4)).join(', ');   // sRGB, written after the output transform
  const prior = base.onBeforeCompile, key = base.customProgramCacheKey?.call(base) || '';
  const normal = base.isMeshBasicMaterial && !skinned ? 'normal' : 'objectNormal';
  m.onBeforeCompile = (shader, renderer) => {
    prior?.call(base, shader, renderer);
    shader.uniforms.rainInkWidth = { value: width };
    shader.vertexShader = 'uniform float rainInkWidth;\n' + shader.vertexShader.replace('#include <skinning_vertex>', `#include <skinning_vertex>\ntransformed += normalize(${normal}) * rainInkWidth;`);
    shader.fragmentShader = shader.fragmentShader.replace('#include <dithering_fragment>', `#include <dithering_fragment>\ngl_FragColor.rgb = vec3(${rgb});`);
  };
  m.customProgramCacheKey = () => `${key}|rain-ink-${width.toFixed(5)}-${normal}`;
  return m;
}

/** Add an ink hull child to every outlined mesh under root (layer INK_LAYER only). Returns the undo. */
export function addInkHulls(THREE, root, { ink = INK, skip = [] } = {}) {
  const meshes = [];
  root.updateMatrixWorld(true);
  root.traverse(o => { if (o.isMesh && !o.userData.rainInk && !skip.includes(o)) meshes.push(o); });
  const added = [];
  for (const mesh of meshes) {
    const width = inkWidth(mesh, ink);
    if (!width) continue;
    const local = width / (mesh.matrixWorld.getMaxScaleOnAxis() || 1);
    const skinned = Boolean(mesh.isSkinnedMesh);
    const mats = Array.isArray(mesh.material) ? mesh.material.map(m => inkMaterial(THREE, m, local, { skinned, color: ink.color })) : inkMaterial(THREE, mesh.material, local, { skinned, color: ink.color });
    const hull = skinned ? new THREE.SkinnedMesh(mesh.geometry, mats) : new THREE.Mesh(mesh.geometry, mats);
    if (skinned) { hull.bindMode = mesh.bindMode; hull.bind(mesh.skeleton, mesh.bindMatrix); }
    if (mesh.morphTargetInfluences) { hull.morphTargetInfluences = mesh.morphTargetInfluences; hull.morphTargetDictionary = mesh.morphTargetDictionary; }
    hull.name = `${mesh.name}.inkHull`; hull.userData.rainInk = true;
    hull.frustumCulled = false; hull.castShadow = false; hull.receiveShadow = false;
    hull.layers.set(INK_LAYER);
    mesh.add(hull); added.push(hull);
  }
  return {
    count: added.length,
    remove() { for (const hull of added) { hull.removeFromParent(); for (const m of [].concat(hull.material)) m.dispose(); } added.length = 0; }
  };
}

/** Fishnets read in the bake (INK.fishnet*, the same numbers as toon_render.py): while the bake
 *  draws, the grid colour darkens, its opacity rises and its strands widen. Returns the undo. */
export function inkFishnet(garment, ink = INK) {
  if (!garment || garment.mode?.value !== 1) return () => {};
  const color = garment.color.value.clone(), opacity = garment.opacity.value, strand = garment.strand?.value;
  garment.color.value.multiplyScalar(ink.fishnetDarken);
  garment.opacity.value = Math.max(opacity, ink.fishnetOpacity);
  if (garment.strand) garment.strand.value = Math.max(strand, ink.fishnetStrand);
  return () => { garment.color.value.copy(color); garment.opacity.value = opacity; if (garment.strand) garment.strand.value = strand; };
}

/** The three.js source: its own renderer (alpha, premultipliedAlpha false, preserveDrawingBuffer)
 *  on a detached canvas, its own mixer on the shared avatar. ctx = { THREE, scene, avatar,
 *  getClip, sync() (expansion updates + world matrices), restore() (visible pose back),
 *  hide: [objects kept out of the bake], garment: the legwear uniforms }. options.toneMapping /
 *  options.exposure come from a desk check's request.render only; the host never sends them.
 *  options.ink === false bakes without the ink hulls and the fishnet tweak (the pixel gate). */
export function createThreeSource(ctx, w, h, options = {}) {
  const scale = BAKE.scale;
  const { THREE } = ctx;
  const W = w * scale, H = h * scale;
  const canvas = document.createElement('canvas');
  canvas.width = W; canvas.height = H;
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, premultipliedAlpha: false, preserveDrawingBuffer: true });
  renderer.setPixelRatio(1); renderer.setSize(W, H, false);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE[options.toneMapping in BAKE_TONE ? options.toneMapping : BAKE.toneMapping];
  renderer.toneMappingExposure = Number(options.exposure) > 0 ? Number(options.exposure) : BAKE.exposure;
  renderer.setClearColor(0x000000, 0);
  const camera = makeTubeCamera(THREE);
  const inked = options.ink !== false;
  const hulls = inked ? addInkHulls(THREE, ctx.avatar, { skip: ctx.hide || [] }) : { count: 0, remove() {} };
  if (inked) camera.layers.enable(INK_LAYER);
  const mixer = new THREE.AnimationMixer(ctx.avatar);
  const gl = renderer.getContext();
  const buffer = new Uint8Array(W * H * 4);
  const pngCanvas = document.createElement('canvas');
  pngCanvas.width = w; pngCanvas.height = h;
  const png2d = pngCanvas.getContext('2d');
  return {
    hasClip: name => Boolean(ctx.getClip(name)),
    firstClip: () => ctx.clips?.[0]?.name,
    duration: name => ctx.getClip(name).duration,
    render(name, t) {
      const action = mixer.clipAction(ctx.getClip(name));
      action.reset().setLoop(THREE.LoopRepeat, Infinity).setEffectiveTimeScale(1).setEffectiveWeight(1).play();
      mixer.setTime(t);
      ctx.sync();
      const hidden = (ctx.hide || []).filter(o => o.visible);
      for (const o of hidden) o.visible = false;
      const fishnet = inked ? inkFishnet(ctx.garment) : () => {};
      try {
        renderer.setRenderTarget(null); renderer.clear(); renderer.render(ctx.scene, camera);
        gl.readPixels(0, 0, W, H, gl.RGBA, gl.UNSIGNED_BYTE, buffer);
      } finally {
        for (const o of hidden) o.visible = true;
        fishnet();
        action.stop(); // deactivating restores the pose the bindings saw on play()
        ctx.restore();
      }
      // GL rows run bottom-up. Blending onto a clear (0,0,0,0) leaves the buffer premultiplied.
      const data = new Uint8ClampedArray(W * H * 4), row = W * 4;
      for (let y = 0; y < H; y++) data.set(buffer.subarray((H - 1 - y) * row, (H - y) * row), y * row);
      return { data, width: W, height: H };
    },
    encode(frame) {
      png2d.putImageData(new ImageData(frame.data, frame.width, frame.height), 0, 0);
      return pngCanvas.toDataURL('image/png').slice('data:image/png;base64,'.length);
    },
    dispose() {
      mixer.stopAllAction(); mixer.uncacheRoot(ctx.avatar); hulls.remove(); ctx.restore();
      renderer.dispose(); renderer.forceContextLoss();
    }
  };
}

/** Wire the host messages. ctx as createThreeSource plus post (postHost) and prepare()
 *  (finish any swap transition so the bake sees the settled look). start(request, sink) lets a
 *  desk check outside the app collect the messages (window.wardrobeDebug.bake). */
export function installBaker(ctx, { makeSource = createThreeSource, webview = globalThis.window?.chrome?.webview } = {}) {
  let current = null;
  const cancel = () => { if (current) current.cancelled = true; };
  async function start(request, post = ctx.post) {
    cancel();
    const run = { cancelled: false };
    current = run;
    ctx.prepare?.();
    const w = Number(request.w) > 0 ? Number(request.w) : BAKE.w, h = Number(request.h) > 0 ? Number(request.h) : BAKE.h;
    let source;
    const plain = request.pixel === true || request.palette === 'shipped';   // the October pixel gate stays un-inked
    try { source = makeSource(ctx, w, h, { ...(request.render || {}), ...(plain ? { ink: false } : {}) }); } catch (error) {
      post({ type: 'boot-error', msg: `Rain bake renderer: ${error?.message || error}` });
      post({ type: 'rain-bake-done', all: true, error: 'renderer', lookHash: request.lookHash || '' });
      return { ok: false };
    }
    try { return await runBake({ request, source, post, cancelled: () => run.cancelled }); }
    finally { if (current === run) current = null; }
  }
  webview?.addEventListener?.('message', event => {
    const data = event.data;
    if (data?.type === 'rain-bake') start(data);
    else if (data?.type === 'rain-bake-cancel') cancel();
  });
  return { start, cancel, busy: () => Boolean(current) };
}
