// Rain V2 bake on Apply (lane R6, RAIN-V2-PLAN 2.3 / 2.5). The tube never runs live 3D: when the
// host sends {type:'rain-bake', lookHash, clips, fps, w, h} the page renders each clip of the
// CURRENT look offscreen at 2x, runs the tube clips' pixel pass (a port of
// blender-scripting/rain-tube-1024/scripts/pixel.py) and streams PNG frames to the host:
//   rain-bake-frame {clip, index, png}   rain-bake-progress {clip, frame, of}
//   rain-bake-done {clip, frames}        rain-bake-done {all:true}   (plus lookHash on each)
// A clip the GLB does not carry yet answers rain-bake-done {clip, frames:0, skipped:true}.
// rain-bake-cancel (or a newer rain-bake) stops at the next frame: rain-bake-done {all:true, cancelled:true}.
// A readback that is all opaque or all black posts boot-error and rain-bake-done {all:true, error}.
// The visible view keeps running: the bake draws through its own renderer and its own mixer,
// and every bake frame hands the pose back before the next paint.

export const BAKE = Object.freeze({ scale: 2, grain: 2, colors: 48, alphaCut: 128, fps: 15, w: 192, h: 309, toneMapping: 'AgXToneMapping', exposure: 1 });
const BAKE_TONE = { AgXToneMapping: 1, ACESFilmicToneMapping: 1, NeutralToneMapping: 1 };

/** Opaque colours of the shipped idle.webp (Resources/rain_emotes, 44 of its 48 median-cut
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

function toTubeFrame(raw, w, h) {
  const factor = Math.max(1, Math.round(raw.width / w));
  const down = factor > 1 ? boxDownscale(raw.data, raw.width, raw.height, factor) : { data: raw.data, width: raw.width, height: raw.height };
  if (down.width !== w || down.height !== h) throw new Error(`Bake frame ${down.width}x${down.height}, expected ${w}x${h}`);
  return { data: unpremultiply(down.data), width: w, height: h };
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
    const palette = request.palette === 'shipped' ? SHIPPED_IDLE_PALETTE : medianCut(toTubeFrame(check.raw, w, h).data);
    const baked = {};
    for (const clip of clips) {
      if (cancelled()) break;
      if (!source.hasClip(clip)) { send({ type: 'rain-bake-done', clip, frames: 0, skipped: true }); continue; }
      const of = frameCount(source.duration(clip), fps);
      for (let index = 0; index < of; index++) {
        if (cancelled()) break;
        const frame = pixelate(toTubeFrame(source.render(clip, index / fps), w, h), { palette });
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

/** The three.js source: its own renderer (alpha, premultipliedAlpha false, preserveDrawingBuffer)
 *  on a detached canvas, its own mixer on the shared avatar. ctx = { THREE, scene, avatar,
 *  getClip, sync() (expansion updates + world matrices), restore() (visible pose back),
 *  hide: [objects kept out of the bake] }. options.toneMapping / options.exposure come from a desk
 *  check's request.render only; the host never sends them. */
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
      try {
        renderer.setRenderTarget(null); renderer.clear(); renderer.render(ctx.scene, camera);
        gl.readPixels(0, 0, W, H, gl.RGBA, gl.UNSIGNED_BYTE, buffer);
      } finally {
        for (const o of hidden) o.visible = true;
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
      mixer.stopAllAction(); mixer.uncacheRoot(ctx.avatar); ctx.restore();
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
    try { source = makeSource(ctx, w, h, request.render || {}); } catch (error) {
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
