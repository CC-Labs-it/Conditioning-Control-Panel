/* render/media.js - the media layer (MEDIA lane, polish 3; MEDIA.md is the slot API and the rules).
 *
 * Where Scrolller media will show: trackside billboards on glass stalks, a halo of screens orbiting
 * over the start/finish, and a glass tunnel lined with screens (tracks/<id>.media.json, fitted by
 * render/mediaMath.js so nothing enters any road stretch). This pass shows ANIMATED PLACEHOLDERS only
 * (render/mediaPlaceholders.js): nothing is downloaded, nothing asks Scrolller.
 *
 * Draw cost: the placeholders are a few shared canvases (Q tier: 6 at 256 px, phone 4 at 128 px); every
 * slot showing one is an instance of that program's InstancedMesh, cropped and hue-turned per instance,
 * so the whole layer is about a dozen draws. Canvas uploads share one budget (at most 2 a frame, phone 1),
 * nearest and most overdue first, and only for programs with a slot within drawM. Loom spirals are drawn
 * once and turn in the shader, so they move smoothly at any redraw rate.
 * Each frame: a soft glow border cycling through the spectrum, a dark back plate, and the credit strip
 * along the bottom of the picture (empty until a source with a credit arrives).
 * Gentle: half the motion (placeholder time, halo orbit, Loom turn, hue drift) and half the contrast
 * and border glow. Nothing here changes brightness fast: hue cycles, positions move, luminance holds.
 */
import {
  Group, Mesh, InstancedMesh, PlaneGeometry, CylinderGeometry, BufferGeometry, Float32BufferAttribute,
  InstancedBufferAttribute, CanvasTexture, Texture, SRGBColorSpace, MeshBasicNodeMaterial, AdditiveBlending,
  DoubleSide, FrontSide, BackSide, Matrix4, Vector3, Color, MirroredRepeatWrapping,
} from 'three/webgpu';
import {
  texture, uv, vec2, vec3, float, uniform, instancedBufferAttribute, attribute, cos, sin, hue, mix, smoothstep,
  max, abs, fract, pow,
} from 'three/tsl';
import { Q } from './quality.js';
import {
  mediaLook, tierOf, roadSamples, placeBillboard, haloSlots, haloPoseInto, tunnelLayout, spinSafeCrop, fitCover,
  createUploadBudget, urlAllowed, creditText, colourOf, slotColour,
} from './mediaMath.js';
import { createPlaceholder, programFor, spinOf, warpOf, releaseLoom, PROGRAMS } from './mediaPlaceholders.js';

// the shared colour system (render/spectrum.js); the local ring stays the fallback for a missing hue
import * as SPECTRUM from './spectrum.js';

const LAYOUTS = new Map();
/** Fetch tracks/<id>.media.json once; resolves the layout or null (never rejects). */
export async function loadMediaLayout(trackId) {
  if (LAYOUTS.has(trackId)) return LAYOUTS.get(trackId);
  let data = null;
  try {
    const r = await fetch(new URL('../tracks/' + trackId + '.media.json', import.meta.url));
    if (r.ok) data = await r.json();
  } catch { /* no layout: no media */ }
  LAYOUTS.set(trackId, data);
  return data;
}
if (globalThis.fetch && globalThis.location) await loadMediaLayout('threshold');

const TAU = Math.PI * 2;
const M4 = new Matrix4(), VX = new Vector3(), VY = new Vector3(), VZ = new Vector3(), VP = new Vector3(), EYE = new Vector3();
const SX = new Vector3(), SY = new Vector3();
const ZERO = new Matrix4().makeScale(0, 0, 0);

/** A slot's matrix: picture axes, w x h scale, pushed along its face by `lift`. */
function slotMatrix(sl, out, { w = sl.w, h = sl.h, lift = 0, down = 0 } = {}) {
  VX.set(sl.X[0], sl.X[1], sl.X[2]); VY.set(sl.Y[0], sl.Y[1], sl.Y[2]); VZ.set(sl.Z[0], sl.Z[1], sl.Z[2]);
  out.makeBasis(SX.copy(VX).multiplyScalar(w), SY.copy(VY).multiplyScalar(h), VZ);
  VP.set(sl.C[0], sl.C[1], sl.C[2]).addScaledVector(VZ, lift).addScaledVector(VY, -down);
  out.setPosition(VP);
  return out;
}

function placeholderColours() {
  const secs = ['dawn', 'deep', 'afterglow'], out = [];
  for (let i = 0; i < 12; i++) out.push(slotColour(i, SPECTRUM, secs[i % 3]));
  return out;
}

/** The base border colour for a slot: the spectrum's hue at its s when there is one, else the ring. */
function borderColour(sl, i) {
  try { if (SPECTRUM && typeof SPECTRUM.hueAt === 'function') return colourOf(SPECTRUM.hueAt(sl.s || 0, 0), slotColour(i)); } catch { /* ring */ }
  return slotColour(i);
}

/**
 * createMediaSlots(scene, track, opts) -> { object, slots, setSource, update, dispose, stats }.
 * opts: tier (Q tier name at build, default Q.name), layout (default the fetched tracks/<id>.media.json),
 * samples (road samples), feel (reads gentle.* when update's view does not say), make (canvas factory).
 */
export function createMediaSlots(scene, track, opts = {}) {
  const M = mediaLook(), layout = opts.layout || LAYOUTS.get(track.id);
  const buildTier = opts.tier || Q.name, T = tierOf(buildTier, M);
  const group = new Group();
  group.name = 'media';
  const empty = { object: group, slots: [], setSource: () => false, update() {}, dispose() { if (scene) scene.remove(group); }, stats: () => ({ slots: 0, draws: 0, uploads: 0 }) };
  if (!layout) { if (scene) scene.add(group); return empty; }
  const samples = opts.samples || roadSamples(track, M.checkStepM);

  // ---- slots: billboards, the halo, the tunnel ----
  const slots = [];
  for (const b of layout.billboards || []) {
    const sl = placeBillboard(b, track, samples, M);
    if (sl) slots.push(sl); else console.warn('[glassroad/media] billboard ' + b.id + ' found no clear spot');
  }
  const haloSpec = layout.halo || null, haloFrame = haloSpec ? track.frameAt(((haloSpec.f || 0) % 1) * track.lengthM) : null;
  const haloN = haloSpec ? Math.max(3, Math.round((haloSpec.count || 12) * T.haloShare)) : 0;
  let orbit = 0;
  const haloStart = slots.length;
  if (haloSpec) for (const sl of haloSlots(haloSpec, haloFrame, haloN, orbit, M)) slots.push(sl);
  const tun = layout.tunnel ? tunnelLayout(layout.tunnel, track, M, T.tunnelStepMul) : null;
  if (tun) for (const sl of tun.slots) slots.push(sl);

  // ---- shared placeholder programs ----
  const P = Math.min(T.programs, PROGRAMS.length), cols = placeholderColours();
  const progs = [];
  for (let p = 0; p < P; p++) {
    const ph = createPlaceholder(p, PROGRAMS[p].loom ? T.loomPx || T.canvasPx : T.canvasPx, cols, { make: opts.make, animate: T.placeholderFps > 0 });
    const tex = new CanvasTexture(ph.canvas);
    tex.colorSpace = SRGBColorSpace; tex.anisotropy = 4;
    if (warpOf(p)) { tex.wrapS = tex.wrapT = MirroredRepeatWrapping; }
    ph.draw(0);
    progs.push({ ph, tex, spin: uniform(spinOf(p)), warp: warpOf(p), slots: [], near: Infinity });
  }
  releaseLoom();
  const budget = createUploadBudget();
  progs.forEach((pg, p) => { pg.src = budget.add({ every: pg.ph.static ? Infinity : 1 / T.placeholderFps, kind: 'program', p, on: !pg.ph.static }); });

  // shared uniforms: time (Gentle slows it), contrast and border gain (Gentle halves them)
  const U = { time: uniform(0), contrast: uniform(1), border: uniform(M.borderGain), rib: uniform(M.ribGain) };

  // slot -> program and per-instance data
  slots.forEach((sl, i) => {
    sl.index = i;
    sl.p = programFor(sl.program ?? i, P);
    sl.hue0 = ((i * 2.399) % TAU);                   // golden-angle spread: neighbours never share a hue
    sl.spinDir = (i % 2 ? -1 : 1) * (sl.frame === 'tunnel' ? 0.7 : 1);
    sl.base = borderColour(sl, i);
    progs[sl.p].slots.push(sl);
  });

  const plane = new PlaneGeometry(1, 1);
  const owned = [plane];
  const screenMeshes = progs.map((pg) => {
    const n = Math.max(1, pg.slots.length), crop = new Float32Array(n * 4), info = new Float32Array(n * 4);
    pg.slots.forEach((sl, k) => {
      const c = pg.spin.value ? spinSafeCrop(sl.w / sl.h) : fitCover(1, sl.w / sl.h);
      crop.set([c.cx, c.cy, c.sx, c.sy], k * 4);
      info.set([sl.hue0, sl.spinDir, (sl.index * 0.618) % 1, 0], k * 4);
      sl.k = k;
    });
    const m = screenMaterial(texture(pg.tex), instancedBufferAttribute(new InstancedBufferAttribute(crop, 4), 'vec4'),
      instancedBufferAttribute(new InstancedBufferAttribute(info, 4), 'vec4'), pg.spin, pg.warp, U, M);
    const im = new InstancedMesh(plane, m, n);
    im.frustumCulled = false; im.name = 'media-' + pg.ph.program;
    if (!pg.slots.length) im.count = 0;
    group.add(im);
    return im;
  });

  // borders (additive glow round each picture), back plates, credit strips: one instanced draw each
  const N = slots.length;
  const bSize = new Float32Array(N * 2), bCol = new Float32Array(N * 4);
  slots.forEach((sl, i) => { bSize.set([sl.w, sl.h], i * 2); bCol.set([...sl.base, sl.hue0], i * 4); });
  const borderMat = borderMaterial(instancedBufferAttribute(new InstancedBufferAttribute(bSize, 2), 'vec2'),
    instancedBufferAttribute(new InstancedBufferAttribute(bCol, 4), 'vec4'), U, M);
  const borders = new InstancedMesh(plane, borderMat, Math.max(1, N));
  const backMat = new MeshBasicNodeMaterial({ side: BackSide });
  backMat.colorNode = vec3(...new Color(M.backColour).toArray());
  const backs = new InstancedMesh(plane, backMat, Math.max(1, N));
  const stripMat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: FrontSide });
  stripMat.colorNode = vec3(...new Color(M.stripColour).toArray());
  stripMat.opacityNode = float(M.stripAlpha);
  const strips = new InstancedMesh(plane, stripMat, Math.max(1, N));
  borders.name = 'media-border'; backs.name = 'media-back'; strips.name = 'media-strip';
  for (const im of [borders, backs, strips]) { im.frustumCulled = false; group.add(im); }
  borders.renderOrder = 2; strips.renderOrder = 3;

  // glass stalks under the billboards
  const bbs = slots.filter((s) => s.stalk);
  let stalks = null;
  if (bbs.length) {
    const cyl = new CylinderGeometry(M.stalkR, M.stalkR * 0.6, 1, 10, 1, true).translate(0, -0.5, 0);
    owned.push(cyl);
    const sm = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
    sm.colorNode = vec3(...new Color(M.stalkColour).toArray());
    // clamped: under MSAA the interpolated uv can dip below 0 at an edge and pow() of a negative is NaN,
    // which the bloom chain spread over the whole WebGL2 frame (integrator fix, polish 3)
    sm.opacityNode = pow(max(uv().y, float(0)), float(1.6)).mul(M.stalkAlpha);
    stalks = new InstancedMesh(cyl, sm, bbs.length);
    bbs.forEach((sl, i) => {
      const top = sl.stalk.top, bot = sl.stalk.bottom;
      VY.set(top[0] - bot[0], top[1] - bot[1], top[2] - bot[2]);
      const L = VY.length(); VY.normalize();
      VX.set(1, 0, 0); if (Math.abs(VX.dot(VY)) > 0.9) VX.set(0, 0, 1);
      VZ.crossVectors(VX, VY).normalize(); VX.crossVectors(VY, VZ).normalize();
      M4.makeBasis(VX, VY.clone().multiplyScalar(L), VZ).setPosition(top[0], top[1], top[2]);
      stalks.setMatrixAt(i, M4);
    });
    stalks.frustumCulled = false; stalks.name = 'media-stalk'; group.add(stalks);
  }

  // the tunnel: a glass arch with lines along it, and glowing ribs every half ring
  let shell = null, ribs = null;
  if (tun) {
    const g = new BufferGeometry();
    g.setAttribute('position', new Float32BufferAttribute(tun.shell.positions, 3));
    g.setAttribute('uv', new Float32BufferAttribute(tun.shell.uvs, 2));
    g.setIndex(tun.shell.indices);
    g.computeBoundingSphere();
    owned.push(g);
    const shm = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
    const base = vec3(...new Color(M.shellColour).toArray());
    // lines along the tunnel, each its own drifting hue; the glass between them is a faint tint
    const lane = fract(uv().x.mul(12)), line = smoothstep(0.86, 1, abs(lane.sub(0.5)).mul(2));
    const lineHue = hue(vec3(1, 0.25, 0.7), uv().x.mul(TAU).add(U.time.mul(M.borderHueRadps * 1.5)));
    shm.colorNode = mix(base, lineHue, line);
    shm.opacityNode = mix(float(M.shellAlpha), float(0.75), line).mul(U.contrast.mul(0.5).add(0.5));
    shell = new Mesh(g, shm); shell.renderOrder = 1; shell.name = 'media-tunnel';
    group.add(shell);

    const rg = new BufferGeometry();
    rg.setAttribute('position', new Float32BufferAttribute(tun.ribs.positions, 3));
    const rc = [];
    for (const r of tun.ribs.hues) rc.push(...slotColour(r * 5, SPECTRUM, ['dawn', 'deep', 'afterglow'][r % 3]));
    rg.setAttribute('aCol', new Float32BufferAttribute(rc, 3));
    rg.setIndex(tun.ribs.indices);
    rg.computeBoundingSphere();
    owned.push(rg);
    const rm = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
    rm.colorNode = hue(attribute('aCol', 'vec3'), U.time.mul(M.borderHueRadps)).mul(U.rib);
    ribs = new Mesh(rg, rm); ribs.renderOrder = 2; ribs.name = 'media-ribs';
    group.add(ribs);
  }

  // ---- own sources (setSource) and credits ----
  const own = new Map();     // slot index -> { mesh, mat, tex, el, kind, src, credit }
  let creditTex = null;
  function creditTexture(text) {
    if (creditTex && creditTex.text === text) return creditTex.tex;
    const cv = opts.make ? opts.make(256, 40) : Object.assign(document.createElement('canvas'), { width: 256, height: 40 });
    const g = cv.getContext('2d');
    g.clearRect(0, 0, 256, 40);
    g.fillStyle = 'rgba(255,255,255,0.92)'; g.font = '600 24px system-ui, sans-serif';
    g.textBaseline = 'middle'; g.textAlign = 'left';
    g.fillText(text, 10, 21);
    const tex = new CanvasTexture(cv); tex.colorSpace = SRGBColorSpace;
    if (creditTex) creditTex.tex.dispose();
    creditTex = { text, tex };
    return tex;
  }

  // ---- placement of every instance (halo slots move, tier changes hide or show) ----
  let shownTier = null;
  const hidden = (sl) => (sl.frame === 'billboard' && !sl.phone && tierOf(Q.name, M).phoneOnly);
  function placeSlot(sl) {
    const off = hidden(sl), o = own.get(sl.index), stripH = sl.h * M.stripShare;
    const im = screenMeshes[sl.p];
    if (off || o) im.setMatrixAt(sl.k, ZERO); else im.setMatrixAt(sl.k, slotMatrix(sl, M4));
    if (off) { for (const x of [borders, backs, strips]) x.setMatrixAt(sl.index, ZERO); }
    else {
      borders.setMatrixAt(sl.index, slotMatrix(sl, M4, { w: sl.w + M.borderM * 2, h: sl.h + M.borderM * 2, lift: -0.06 }));
      backs.setMatrixAt(sl.index, slotMatrix(sl, M4, { lift: -0.03 }));
      strips.setMatrixAt(sl.index, slotMatrix(sl, M4, { h: stripH, lift: 0.04, down: sl.h / 2 - stripH / 2 }));
    }
    if (o) {
      o.mesh.visible = !off;
      slotMatrix(sl, o.mesh.matrix, { lift: 0.01 });
      o.mesh.matrixWorldNeedsUpdate = true;
      if (o.credit) {
        o.credit.visible = !off;
        slotMatrix(sl, o.credit.matrix, { w: stripH * 6.4, h: stripH, lift: 0.06, down: sl.h / 2 - stripH / 2 });
        placeCreditLeft(sl, o.credit, stripH);
        o.credit.matrixWorldNeedsUpdate = true;
      }
    }
  }
  function placeCreditLeft(sl, mesh, stripH) {
    // shift the credit to the strip's left end (picture axis X), a small margin in
    const dx = -(sl.w / 2) + stripH * 3.4;
    mesh.matrix.elements[12] += sl.X[0] * dx; mesh.matrix.elements[13] += sl.X[1] * dx; mesh.matrix.elements[14] += sl.X[2] * dx;
  }
  function flush() {
    for (const im of [...screenMeshes, borders, backs, strips]) im.instanceMatrix.needsUpdate = true;
  }
  function placeAll() { for (const sl of slots) placeSlot(sl); flush(); shownTier = Q.name; }
  placeAll();
  if (scene) scene.add(group);

  /**
   * setSource(slotId, { kind: 'image'|'video'|'canvas', url | el, credit }) shows a real source on one
   * slot (null puts the placeholder back). url must be local or same origin (mediaMath.urlAllowed):
   * the host hands a cached copy, never a remote Scrolller url. credit 'scrolller' writes "via Scrolller"
   * on the slot's strip. Returns true when taken.
   */
  function setSource(slotId, src) {
    const sl = slots.find((s) => s.id === slotId);
    if (!sl) return false;
    drop(sl.index);
    if (!src) { placeSlot(sl); flush(); return true; }
    const origin = globalThis.location ? location.origin : '';
    let el = src.el || null;
    if (!el && src.url) {
      if (!urlAllowed(src.url, origin)) { console.warn('[glassroad/media] refused a non-local media url'); placeSlot(sl); flush(); return false; }
      if (src.kind === 'video') el = Object.assign(document.createElement('video'), { muted: true, loop: true, playsInline: true, src: src.url, crossOrigin: 'anonymous' });
      else if (src.kind === 'image') el = Object.assign(new Image(), { crossOrigin: 'anonymous', decoding: 'async', src: src.url });
    }
    if (!el || !['image', 'video', 'canvas'].includes(src.kind)) { placeSlot(sl); flush(); return false; }
    const tex = src.kind === 'canvas' ? new CanvasTexture(el) : new Texture(el);
    tex.colorSpace = SRGBColorSpace; tex.anisotropy = 4;
    const crop = { cx: uniform(0.5), cy: uniform(0.5), sx: uniform(1), sy: uniform(1) };
    const mat = ownMaterial(texture(tex), crop, U, M);
    const mesh = new Mesh(plane, mat);
    mesh.matrixAutoUpdate = false; mesh.frustumCulled = false; mesh.visible = false;
    group.add(mesh);
    const o = { mesh, mat, tex, el, kind: src.kind, crop, ready: false, credit: null, src: -1 };
    const text = creditText(src.credit);
    if (text) {
      const cm = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
      const ct = texture(creditTexture(text));
      cm.colorNode = ct.rgb; cm.opacityNode = ct.a;
      o.credit = new Mesh(plane, cm); o.credit.matrixAutoUpdate = false; o.credit.frustumCulled = false; o.credit.renderOrder = 4;
      group.add(o.credit);
    }
    const ready = () => {
      if (!own.has(sl.index) || own.get(sl.index) !== o) return;
      const w = el.videoWidth || el.naturalWidth || el.width || 1, h = el.videoHeight || el.naturalHeight || el.height || 1;
      const c = fitCover(w / h, sl.w / sl.h);
      crop.cx.value = c.cx; crop.cy.value = c.cy; crop.sx.value = c.sx; crop.sy.value = c.sy;
      o.ready = true; tex.needsUpdate = true;
      placeSlot(sl); flush();
    };
    own.set(sl.index, o);
    if (src.kind === 'image') { if (el.complete && el.naturalWidth) ready(); else el.addEventListener('load', ready, { once: true }); }
    else if (src.kind === 'video') {
      el.addEventListener('loadeddata', ready, { once: true });
      if (el.readyState >= 2) ready();
      try { const pr = el.play(); if (pr && pr.catch) pr.catch(() => {}); } catch { /* paused until a gesture */ }
    } else ready();
    if (src.kind !== 'image') o.src = budget.add({ every: 1 / (src.kind === 'video' ? T.videoFps : T.sourceFps), kind: 'own', slot: sl.index });
    placeSlot(sl); flush();
    return true;
  }
  function drop(i) {
    const o = own.get(i);
    if (!o) return;
    own.delete(i);
    if (o.src >= 0) budget.list[o.src].on = false;
    group.remove(o.mesh); o.mat.dispose(); o.tex.dispose();
    if (o.credit) { group.remove(o.credit); o.credit.material.dispose(); }
    if (o.kind === 'video' && o.el && o.el.pause) { try { o.el.pause(); } catch { /* gone */ } }
  }

  // ---- per frame ----
  let clock = 0, uploads = 0;
  const picks = [];
  const haloSlotsOf = haloSpec ? slots.slice(haloStart, haloStart + haloN) : [];
  function update(dt, view = {}) {
    const feel = opts.feel;
    const gentle = view.gentle ?? (feel ? !!feel.get('gentle.on') : false);
    const scroll = view.scrollMul ?? (gentle ? (feel ? feel.get('gentle.scrollMul') : 0.5) : 1);
    const contrast = view.contrastMul ?? (gentle ? (feel ? feel.get('gentle.contrastMul') : 0.5) : 1);
    clock += dt * scroll;
    U.time.value = clock;
    U.contrast.value = contrast;
    U.border.value = M.borderGain * (gentle ? M.borderGentle : 1);
    U.rib.value = M.ribGain * (gentle ? M.ribGentle : 1);

    // the halo orbits (its slot objects are updated in place so ids and indices hold)
    if (haloSpec && haloSlotsOf.length) {
      orbit = (orbit + dt * (haloSpec.orbitRadps ?? 0.05) * scroll) % TAU;
      // in place (perf, polish 6): a fresh haloSlots() a frame was steady race garbage
      for (let k = 0; k < haloSlotsOf.length; k++) placeSlot(haloPoseInto(haloSpec, haloFrame, haloN, orbit, k, haloSlotsOf[k]));
    }
    if (shownTier !== Q.name) placeAll(); else if (haloSlotsOf.length) flush();

    // the shared upload budget: nearest overdue programs and live own sources within drawM
    const cam = view.camera;
    if (cam && cam.position) EYE.copy(cam.position); else if (view.eye) EYE.set(view.eye[0], view.eye[1], view.eye[2]);
    const Tl = tierOf(Q.name, M);
    for (const pg of progs) {
      let d = Infinity;
      for (const sl of pg.slots) if (!own.has(sl.index) && !hidden(sl)) d = Math.min(d, Math.hypot(sl.C[0] - EYE.x, sl.C[1] - EYE.y, sl.C[2] - EYE.z));
      const x = budget.list[pg.src];
      x.near = d; x.every = pg.ph.static || !(Tl.placeholderFps > 0) ? Infinity : 1 / Tl.placeholderFps;
    }
    for (const [i, o] of own) if (o.src >= 0) { const sl = slots[i]; budget.list[o.src].near = Math.hypot(sl.C[0] - EYE.x, sl.C[1] - EYE.y, sl.C[2] - EYE.z); }
    budget.pick(clock, Tl.maxUploads, Tl.drawM, picks);
    uploads = 0;
    for (const i of picks) {
      const x = budget.list[i];
      if (x.kind === 'program') { const pg = progs[x.p]; if (pg.ph.draw(clock)) { pg.tex.needsUpdate = true; uploads++; } }
      else { const o = own.get(x.slot); if (o && o.ready) { o.tex.needsUpdate = true; uploads++; } }
      budget.done(i, clock);
    }
  }

  function dispose() {
    for (const i of [...own.keys()]) drop(i);
    for (const pg of progs) pg.tex.dispose();
    if (creditTex) creditTex.tex.dispose();
    group.traverse((o) => { if (o.material) o.material.dispose(); });
    for (const g of owned) g.dispose();
    if (scene) scene.remove(group);
  }

  const pub = slots.map((sl) => ({ id: sl.id, frame: sl.frame, kind: sl.kind, s: sl.s, w: sl.w, h: sl.h, side: sl.side || 0, phone: sl.phone,
    get C() { return sl.C; }, get Z() { return sl.Z; } }));
  return {
    object: group,
    slots: pub,
    tunnel: tun ? { fromS: tun.fromS, toS: tun.toS } : null,
    setSource,
    update,
    dispose,
    stats: () => ({ slots: slots.length, programs: progs.length, draws: group.children.filter((c) => c.visible).length, uploads, canvasPx: T.canvasPx }),
  };
}

/** The placeholder screen: per-instance crop and hue, the program's turn or warp, a slow breathing zoom, Gentle contrast. */
function screenMaterial(tex, crop, info, spin, warp, U, M) {
  const m = new MeshBasicNodeMaterial({ side: FrontSide });
  const ang = spin.mul(U.time).mul(info.y);
  const breathe = float(1).sub(sin(U.time.mul(TAU * M.breatheHz).add(info.z.mul(TAU))).mul(0.5).add(0.5).mul(M.breatheAmp));
  const off = uv().sub(0.5).mul(crop.zw).mul(breathe);
  const c = cos(ang), s = sin(ang);
  let tuv = crop.xy.add(vec2(off.x.mul(c).sub(off.y.mul(s)), off.x.mul(s).add(off.y.mul(c))));
  if (warp) {
    // a slow lava warp (mirrored edges): position only, so no brightness steps
    const w = U.time.mul(0.35).add(info.z.mul(TAU));
    tuv = tuv.add(vec2(sin(tuv.y.mul(5.1).add(w)), cos(tuv.x.mul(4.3).sub(w.mul(0.8)))).mul(warp));
  }
  const col = hue(tex.sample(tuv).rgb, info.x.add(U.time.mul(M.screenHueRadps)));
  m.colorNode = mix(vec3(0.42, 0.36, 0.48), col, U.contrast).mul(M.screenGain);
  return m;
}

/** A real source on one slot: the cover crop from its aspect, same contrast and gain as the placeholders. */
function ownMaterial(tex, crop, U, M) {
  const m = new MeshBasicNodeMaterial({ side: FrontSide });
  const tuv = vec2(crop.cx, crop.cy).add(uv().sub(0.5).mul(vec2(crop.sx, crop.sy)));
  m.colorNode = mix(vec3(0.42, 0.36, 0.48), tex.sample(tuv).rgb, U.contrast).mul(M.screenGain);
  return m;
}

/** The glow border: a bright rim at the picture edge falling off over borderM, hue cycling per slot. */
function borderMaterial(size, col, U, M) {
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const B = M.borderM, full = size.add(B * 2);
  const p = uv().sub(0.5).mul(full);
  const d = max(abs(p.x).sub(size.x.mul(0.5)), abs(p.y).sub(size.y.mul(0.5)));
  const outside = smoothstep(B, 0, d).pow(2.2), rim = smoothstep(-0.35, 0, d).mul(smoothstep(0.25, 0, d));
  const glow = max(outside.mul(0.85), rim);
  m.colorNode = hue(col.xyz, col.w.add(U.time.mul(M.borderHueRadps))).mul(glow).mul(U.border);
  return m;
}
