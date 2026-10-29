/* render/renderer.js - WebGPURenderer with the automatic WebGL2 fallback (CONTRACT.md section 6).
 * `?renderer=webgl2` (or forceRenderer) forces WebGL2. `?quality=high|mid|phone` pins the start tier.
 * The DPR cap is min(maxDpr, tier dprMax) times the render scale; the frame-time governor steps the
 * tier and re-applies DPR, MSAA and bloom. compile() is warmAll(): every material that can appear in
 * a race is built before the start card lifts.
 * Polish 2 (perf): the player's display options (hud/options.js) are read at boot and live through
 * setOptions(o): frameCap (frameDue(now) at the top of each rAF says whether that one renders),
 * renderScale ('auto' = dynamic resolution holding the display rate, quality.js createDynRes),
 * quality ('auto' = the governor, or a pinned tier) and showFps (a small corner readout).
 * MSAA lives on the scene pass only; the canvas gets one composite quad.
 * The adapter: high-performance for WebGPU and for the WebGL2 context (three does not pass it to
 * WebGL2, so that context is made here); its name shows in the fps readout and stats().gpu, so an
 * integrated GPU on a two-GPU PC is easy to spot.
 * HIT CAM: setInset({ scene, camera, rect, viewW, viewH }) draws a second, small, shadowless view of the scene
 * into the post chain's inset target right before the post render, every frame it is set (the composite
 * rewrites the whole canvas each frame); the post quad lays it over the frame (render/post.js). Its size is the
 * rect times the pixel ratio times the tier's Q.hitCam (0 = off). warmInset() builds its pipelines behind the
 * loading card (its own target, no shadow map: a different program set from the scene pass).
 */
import { WebGPURenderer, Group, Mesh, Sprite, PlaneGeometry } from 'three/webgpu';
import * as threeWebgpu from 'three/webgpu';
import * as GPU from 'three/webgpu';
import { createPost } from './post.js';
import { releaseSceneResources, trackSceneResources, disposeSceneResources } from './sceneResources.js';
import { skipUnchangedInstanceUploads } from './uniformUploads.js';
import { uploadChangedAttributeBytes } from './attributeUploads.js';
import { uploadVideoElementsDirectly } from './videoUploads.js';
import { uploadCanvasRegions } from './canvasRegions.js';
import { uploadDataTextureRows } from './dataTextureRows.js';
import { logPipelineLinks } from './linkLog.js';
import { isFirefox, concreteFloatConstants, uploadVideoThroughExternalTexture } from './firefoxWebgpu.js';
import { Q, TIERS, ORDER, GOVERNOR, setTier, pickTier, createGovernor, createDynRes, dynLines, shortGpu, qualityLanding } from './quality.js';
import { deviceClass, memoryGuard, setDevice } from './deviceClass.js';
import { createFrameGate } from '../core/clock.js';
import { reasonOf } from '../core/failure.js';
import { loadOptions, cleanOptions } from '../hud/options.js';
import { WARM, warmGroups } from './warmGroups.js';
import { createDeferredWarm } from './stagedWarm.js';
import { shareInstancePrograms } from './instancePrograms.js';
import { lateWarmMaterials } from './warmExtras.js';

/** A canvas can hold one context for life: a failed WebGPU init, or a lost context, needs a new element in its place. */
function replaceCanvas(canvas) {
  const fresh = canvas.cloneNode(false);
  if (canvas.parentNode) canvas.replaceWith(fresh);
  return fresh;
}

/**
 * @param {{ forceRenderer?: string|null, maxDpr?: number, options?: object|null, freshCanvas?: boolean, maxTier?: string|null }} o
 *   freshCanvas: build on a new canvas element in place of the given one (a rebuild after a device loss);
 *   maxTier: a ceiling on the automatic tier (a rebuild after a loss starts one tier lower; a pinned Quality option still wins).
 *   platform: the host caps platform ('desktop', 'web', 'phone'); with the coarse pointer it names the DEVICE
 *     (render/deviceClass.js), which the memory protections read whatever Quality is pinned.
 */
export async function createRenderer(canvas, { forceRenderer = null, maxDpr = 2, options = null, freshCanvas = false, maxTier = null, platform = null } = {}) {
  const qs = new URLSearchParams(globalThis.location ? location.search : '');
  const force = forceRenderer || (/^(webgl2|webgpu)$/.test(qs.get('renderer') || '') ? qs.get('renderer') : null);
  const override = TIERS[qs.get('quality')] ? qs.get('quality') : null;
  const coarse = !!(globalThis.matchMedia && matchMedia('(pointer: coarse)').matches);
  const device = setDevice(deviceClass({ coarse, platform }));
  let opts = cleanOptions(options || loadOptions());
  // ?quality= wins (dev), then a pinned Quality option, else the auto pick
  const pinOf = () => override ? null : opts.quality !== 'auto' ? opts.quality : null;
  const capTier = (name) => maxTier && TIERS[maxTier] && ORDER.indexOf(name) > ORDER.indexOf(maxTier) ? maxTier : name;
  if (freshCanvas) canvas = replaceCanvas(canvas);
  // ask for the fast GPU on both paths; no adapter means the WebGL2 fallback, with a context made here.
  // Firefox runs WebGL2 unless ?renderer=webgpu: its WebGPU drew every lit prop as a black silhouette for the
  // owner (polish 4, Firefox 158) while its WebGL2 draws them right; a chosen WebGL2 still starts on high
  const firefox = !force && !!globalThis.navigator && (navigator.userAgent || '').includes('Firefox/');
  let webgl2 = force === 'webgl2' || firefox, gpu = '', context = null, fallback = null;
  if (!webgl2) {
    try {
      const ad = globalThis.navigator && navigator.gpu ? await navigator.gpu.requestAdapter({ powerPreference: 'high-performance', featureLevel: 'compatibility' }) : null;
      if (!ad) webgl2 = true;
      else { const i = ad.info || {}; gpu = shortGpu([i.vendor, i.architecture, i.description].filter(Boolean).join(' ')); }
    } catch (e) { webgl2 = true; }
  }
  const glContext = (c) => c.getContext('webgl2', { antialias: false, alpha: true, depth: true, stencil: false, powerPreference: 'high-performance' });
  if (webgl2) context = glContext(canvas);
  setTier(pinOf() || capTier(pickTier({ override, coarse, webgl2: webgl2 && !firefox })));

  let three = new WebGPURenderer({ canvas, alpha: true, antialias: false, forceWebGL: webgl2, powerPreference: 'high-performance', ...(context ? { context } : {}) });
  try {
    await three.init();
  } catch (e) {
    // a WebGPU adapter that refuses its device (a phone driver, a blocklist, an out-of-memory device) is not the
    // end: the same page runs on WebGL2, on a fresh canvas since the first one now owns a dead webgpu context
    if (webgl2 || force === 'webgpu') throw e;
    fallback = reasonOf(e);
    console.warn('[glassroad/renderer] WebGPU init failed, running WebGL2: ' + fallback);
    try { three.dispose(); } catch (err) { /* half built */ }
    webgl2 = true; gpu = '';
    canvas = replaceCanvas(canvas);
    context = glContext(canvas);
    if (!context) throw new Error('no WebGL2 context after the WebGPU failure (' + fallback + ')');
    three = new WebGPURenderer({ canvas, alpha: true, antialias: false, forceWebGL: true, powerPreference: 'high-performance', context });
    await three.init();
  }
  trackSceneResources(three);
  shareInstancePrograms(three);
  skipUnchangedInstanceUploads(three);
  uploadChangedAttributeBytes(three);        // WebGL2: attributes re-send only their changed bytes (Firefox command-channel budget)
  uploadVideoElementsDirectly(three);
  uploadCanvasRegions(three);
  uploadDataTextureRows(three);              // WebGL2: a marked DataTexture (the water height atlas) re-sends only its rows
  if (qs.get('census') === '1') { try { (await import('../dev/uploadCensus.js')).installUploadCensus(three); } catch (e) { console.warn('census hook failed', e); } }   // dev: ?census=1 (dev/uploadCensus.js)
  // a lost device or context: three stops drawing for good (render returns at once), so the page must rebuild;
  // onLost(fn) hears it once, with the reason
  const lostFns = new Set();
  let lost = null;
  three.onDeviceLost = (info) => {
    if (lost) return;
    lost = { api: info && info.api, message: info && info.message, reason: info && info.reason, at: performance.now() };
    console.error('[glassroad/renderer] device lost: ' + (lost.api || '') + ' ' + (lost.message || '') + (lost.reason ? ' (' + lost.reason + ')' : ''));
    for (const fn of lostFns) { try { fn(lost); } catch (e) { console.warn('[glassroad/renderer] onLost', e); } }
  };
  let compiledGraphs;
  if (qs.get('logLinks') === '1') logPipelineLinks(three, { Material: GPU.Material });   // dev: every program link, with its phase and pass
  if (qs.get('profileLoad') === '1') {
    compiledGraphs = 0;
    three.debug.onNodeBuilderCreated = () => { compiledGraphs++; };
  }
  three.info.autoReset = false;                    // reset once per frame here, so stats() is one whole frame
  const backend = three.backend.isWebGPUBackend ? 'webgpu' : 'webgl2';
  // Firefox WebGPU (render/firefoxWebgpu.js): naga rejects constant builtin calls on abstract floats, and
  // copyExternalImageToTexture refuses a <video>; concrete f32 constants and an external-texture blit there
  if (backend === 'webgpu' && isFirefox()) { concreteFloatConstants(threeWebgpu.WGSLNodeBuilder); uploadVideoThroughExternalTexture(three); }
  if (backend === 'webgl2') {
    try { const gl = three.backend.gl, ext = gl.getExtension('WEBGL_debug_renderer_info'); gpu = shortGpu(gl.getParameter(ext ? ext.UNMASKED_RENDERER_WEBGL : gl.RENDERER)); } catch (e) { /* hidden */ }
  }
  const autoTier = capTier(pickTier({ override, coarse, webgl2: backend === 'webgl2' && !firefox, gpu }));
  setTier(pinOf() || autoTier);
  // graceS: boot, art decode and the first shader compiles never cost a tier
  const govRules = { ...GOVERNOR, graceS: 6 };
  let gov = pinOf() ? null : createGovernor({ tier: Q.name, ceiling: autoTier, rules: govRules });
  const gate = createFrameGate({ cap: opts.frameCap });
  const dyn = createDynRes();
  const tierFns = new Set();
  let post = null, postKey = null, last = 0, gated = false, inset = null;
  // a tier step rebuilds the post chain and the shadow mode, so every program relinks (Firefox: one by one,
  // a 1 to 3 s freeze mid-race; under the pause card, black frames). While held (a race, its countdown and
  // its pause card) the governor's verdict waits and lands on the next menu or results card; dynamic
  // resolution still carries the load in the race.
  let hold = false, pending = null;
  const meter = { n: 0, t: 0, fps: 0, el: null, art: '' }, LF = String.fromCharCode(10);

  const scale = () => opts.renderScale === 'auto' ? dyn.scale : opts.renderScale;
  function resize() {
    const w = Math.max(1, canvas.clientWidth), h = Math.max(1, canvas.clientHeight);
    three.setPixelRatio(Math.min(globalThis.devicePixelRatio || 1, maxDpr, Q.dprMax) * scale());
    three.setSize(w, h, false);
  }
  const ro = globalThis.ResizeObserver ? new ResizeObserver(resize) : null;
  if (ro) ro.observe(canvas);
  resize();

  function postFor(scene, camera) {
    if (!post || post.antialias !== Q.antialias) {
      if (post) post.dispose();
      post = createPost(three, scene, camera); postKey = [scene, camera];
    } else if (!postKey || postKey[0] !== scene || postKey[1] !== camera) {
      post.setScene(scene,camera);postKey=[scene,camera];
    }
    return post;
  }
  function retier() {
    resize();
    if (post && post.antialias === Q.antialias) post.retier();   // an MSAA change rebuilds on the next frame
    for (const fn of tierFns) fn(Q.name);
  }
  // a Quality change under hold (the pause card mid-race) waits like a governor verdict: applied at once it
  // relinked every program and froze a phone for seconds (owner report 2026-10-26, quality.js qualityLanding)
  function setQuality() {
    const pin = pinOf(), name = pin || autoTier;
    gov = pin ? null : createGovernor({ tier: name, ceiling: autoTier, rules: govRules });
    const land = qualityLanding({ want: name, current: Q.name, hold });
    pending = land.pending;
    if (land.apply) { setTier(land.apply); retier(); }
  }

  function readout(on) {
    const doc = globalThis.document;
    if (!doc) return;
    if (on && !meter.el) {
      const el = meter.el = doc.createElement('div');
      el.className = 'gr-fps'; el.setAttribute('aria-hidden', 'true');
      // under the minimap (top right), clear of the lap and time block in the top left
      el.style.cssText = 'position:fixed;right:8px;top:150px;z-index:5;pointer-events:none;text-align:right;font:600 11px/1.2 ui-monospace,Consolas,monospace;' +
        'color:#fff8ef;background:rgba(8,7,14,0.45);padding:2px 6px;border-radius:4px;letter-spacing:0.04em;white-space:pre';
      doc.body.appendChild(el);
    } else if (!on && meter.el) { meter.el.remove(); meter.el = null; }
  }
  function count(now) {
    meter.n++;
    if (!meter.t) meter.t = now;
    if (now - meter.t < 500) return;
    meter.fps = Math.round(meter.n * 1000 / (now - meter.t)); meter.n = 0; meter.t = now;
    if (meter.el) { const s = scale(); meter.el.textContent = meter.fps + ' fps' + (s < 1 ? '  ' + Math.round(s * 100) + '%' : '') + LF + backend + (gpu ? ', ' + gpu : '') + (meter.art ? LF + meter.art : ''); }
  }
  readout(opts.showFps);

  const api = {
    backend,
    three,
    /** The canvas in use (a new element after a WebGPU init fallback or a rebuild). */
    get canvas() { return canvas; },
    /** Why WebGPU gave way to WebGL2 at init, or null. */
    fallback,
    /** The device loss three reported ({ api, message, reason, at }), or null while the device lives. */
    get lost() { return lost; },
    /** fn(lost) once when the GPU device or the WebGL context goes away. Returns the unsubscribe. */
    onLost(fn) { lostFns.add(fn); if (lost) { try { fn(lost); } catch (e) { /* told */ } } return () => lostFns.delete(fn); },
    get tier() { return Q.name; },
    /** The device behind the tier ('phone' or 'desktop'): no Quality pin changes it (render/deviceClass.js). */
    get device() { return device; },
    /** The tier a MEMORY budget reads: the phone tier on a phone whatever the look, else the live tier. */
    get memoryTier() { return memoryGuard(device, Q.name); },
    /** True while the tier comes from a pinned Quality option (the ?quality= dev flag is not a pin). */
    get pinned() { return !!pinOf(); },
    /** The automatic pick for this device and GPU, whatever is pinned. */
    get autoTier() { return autoTier; },
    get aspect() { return Math.max(1, canvas.clientWidth) / Math.max(1, canvas.clientHeight); },
    get options() { return { ...opts }; },
    /** The adapter in use, short ('nvidia blackwell', 'Intel(R) UHD Graphics 770'), or '' when the browser hides it. */
    get gpu() { return gpu; },
    /** fn(tierName) after every governor step or Quality change (scene budgets read Q themselves). */
    onTier(fn) { tierFns.add(fn); return () => tierFns.delete(fn); },
    /** Hold tier steps (true in a race); letting go applies the last verdict that waited. */
    setHold(on) {
      hold = !!on;
      if (!hold && pending) { const p = pending; pending = null; if (p !== Q.name) { setTier(p); retier(); } }
    },
    get tierPending() { return pending; },
    /** The hit cam inset for the next frames: { scene, camera, rect: { cx, cy, w, h, rotRad } (CSS px), viewW, viewH } or null. */
    setInset(spec) { inset = spec && spec.scene && spec.camera && spec.rect && Q.hitCam > 0 ? spec : null; },
    get insetOn() { return !!inset; },
    /** insetCamera: the hit cam's own camera; mainCamera keeps the post chain on the race view. */
    warmInset(scene, insetCamera, mainCamera) { return warmInset(three, postFor(scene, mainCamera), scene, insetCamera); },
    /** The live display options (frameCap, renderScale, quality, showFps); other keys are ignored here. */
    setOptions(o) {
      const was = opts;
      opts = cleanOptions({ ...opts, ...o });
      if (opts.frameCap !== was.frameCap) gate.setCap(opts.frameCap);
      if (opts.renderScale !== was.renderScale) { dyn.reset(1); resize(); }
      if (opts.quality !== was.quality) setQuality();
      if (opts.showFps !== was.showFps) readout(opts.showFps);
    },
    /** The art status for the fps readout: art.js loadArt's result, or null when the race runs without art. */
    setArt(art) {
      if (!art) { meter.art = 'art off'; return; }
      const failed = art.failed || [];
      meter.art = 'art ' + (art.loaded ?? 0) + '/' + (art.wanted ?? 0) + (failed.length ? ', ' + failed.length + ' failed: ' + failed[0] : '');
    },
    /** Call first thing in every requestAnimationFrame: false = skip this one (the frame cap). */
    frameDue(now) { gated = true; return gate.due(now); },
    /** Loading is not a slow gameplay frame. Keep quality/options, but restart their timing grace. */
    beginCourse(){
      // a pinned Quality that waited lands here, behind the loading card and before the warm; a governor verdict is dropped
      const pin=pinOf();if(pin&&pin!==Q.name){setTier(pin);retier();}
      last=0;gated=false;pending=null;gate.setCap(opts.frameCap);
      meter.n=0;meter.t=0;meter.fps=0;
      gov=pinOf()?null:createGovernor({tier:Q.name,ceiling:autoTier,rules:govRules});
    },
    post: postFor,
    /** Detach a departing course while retaining reusable post targets and the GPU context. */
    releaseScene(scene,camera,beforeRetire){
      if(!post||!postKey||postKey[0]!==scene||(camera&&postKey[1]!==camera))return false;
      post.detachScene();postKey=null;releaseSceneResources(three,scene,beforeRetire);return true;
    },
    compile(scene, camera, extras, opts) { return warmAll(api, scene, camera, extras, opts); },
    /** The phone tier's deferred groups from a staged warm (render/stagedWarm.js); step() it once per frame. */
    deferredWarm(warm, scene) { return warm && warm.staged ? createDeferredWarm(three, () => post, { all: warm.all, groups: warm.staged.groups, scene }) : null; },
    resize,
    /** dt (frame seconds) reaches post.update for the item screen effect (CONTRACT-B1). */
    render({ scene, camera, fx, dt = 0, musicView = {} }) {
      const now = performance.now();
      if (!gated) gate.due(now);                 // unwired: still learn the display rate for the targets
      const gap = last ? now - last : 0;
      last = now;
      if (gap > 0) {
        const disp = gate.displayMs, target = gate.targetMs, capped = disp > 0 && gate.capMs >= disp * 1.1;
        // under a cap the governor sees only the time past the cap; dynamic resolution calls a frame slow
        // when it is most of a display interval late (a 144 Hz to 60 cadence alternates 2 and 3 intervals)
        // uncapped, the thresholds follow the display interval (a vsynced frame is never under it)
        const step = gov ? (capped ? gov.sample(Math.max(0.1, gap - (target - disp))) : gov.sample(gap, disp)) : null;
        if (step) { if (hold) pending = step; else { setTier(step); retier(); } }
        // dynLines clamps the learnt display interval: a slow GPU never teaches dynres that slow is normal
        const dl = opts.renderScale === 'auto' ? dynLines(disp, gate.capMs) : null;
        if (dl && dyn.sample(gap, dl.target, dl.slow) !== null) resize();
      }
      count(now);
      const p = postFor(scene, camera);
      three.info.reset();
      p.update(fx, dt, Math.max(1, canvas.clientWidth) / Math.max(1, canvas.clientHeight), musicView);
      if (inset && p.inset) {
        try { renderInset(three, p.inset.target, inset, three.getPixelRatio() * Q.hitCam); p.setInset(inset.rect, inset.viewW, inset.viewH); }
        catch (e) { console.warn('[glassroad/renderer] hit cam: ' + (e && e.message)); inset = null; p.setInset(null); }
      } else if (p.setInset) p.setInset(null);
      p.render();
    },
    stats() {
      const r = three.info.render;
      return { backend, tier: Q.name, drawCalls: r.drawCalls, triangles: r.triangles, perFrame: true, compiledGraphs,
        renderScale: Math.round(scale() * 100) / 100, renderFps: meter.fps, displayMs: Math.round(gate.displayMs * 100) / 100, gpu, art: meter.art };
    },
    dispose() {
      if (ro) ro.disconnect();
      lostFns.clear();
      // a dead device throws from anywhere: each step on its own, the page is rebuilding anyway
      for (const step of [() => post && post.dispose(), () => readout(false), () => disposeSceneResources(three), () => three.dispose()]) {
        try { step(); } catch (e) { console.warn('[glassroad/renderer] dispose: ' + (e && e.message)); }
      }
    },
  };
  return api;
}

const insetLighting = new WeakMap();

/** Shadowless, MRT-less, straight into the inset target; every renderer switch it touches is put back. */
function withInsetTarget(three, target, fn) {
  const oldTarget = three.getRenderTarget(), mrt = three.getMRT ? three.getMRT() : null;
  const lighting = three.lighting;
  const shadow = three.shadowMap ? three.shadowMap.enabled : false, autoClear = three.autoClear;
  const restore = () => {
    three.setRenderTarget(oldTarget);
    if (three.setMRT) three.setMRT(mrt);
    if (three.shadowMap) three.shadowMap.enabled = shadow;
    if (lighting) three.lighting = lighting;
    three.autoClear = autoClear;
  };
  if (three.shadowMap) three.shadowMap.enabled = false;
  // Cube probes can share this target's render context but see a different set of
  // lights. Give the inset its own stable lights node so neither pass evicts the
  // other's prepared render objects and shaders.
  if (lighting) {
    let scope = insetLighting.get(three);
    if (!scope) { scope = new GPU.Lighting(); insetLighting.set(three, scope); }
    scope.enabled = lighting.enabled;
    three.lighting = scope;
  }
  three.autoClear = true;
  if (three.setMRT) three.setMRT(null);
  three.setRenderTarget(target);
  let out;
  try { out = fn(); } catch (e) { restore(); throw e; }
  // an async compile takes its render list, target and shadow state in this task (three builds the pipelines
  // afterwards): the switches go back at once, so a garage frame drawn while they build lands on the canvas
  restore();
  return out;
}

function renderInset(three, target, spec, pr) {
  const w = Math.max(2, Math.round(spec.rect.w * pr)), h = Math.max(2, Math.round(spec.rect.h * pr));
  if (target.width !== w || target.height !== h) target.setSize(w, h);
  withInsetTarget(three, target, () => three.render(spec.scene, spec.camera));
}

/** Compile the inset's own programs (no shadows, its own target) for everything that can appear in a race. */
export async function warmInset(three, post, scene, camera) {
  if (!post || !post.inset || !scene || !camera) return { ms: 0 };
  const t0 = performance.now(), saved = [], mats = [], lods = [];
  // the materials made only in a race (render/warmExtras.js) get their inset programs too
  const late = new Group();
  for (const m of lateWarmMaterials(GPU)) late.add(m.isSpriteNodeMaterial ? new Sprite(m) : new Mesh(new PlaneGeometry(0.01, 0.01), m));
  scene.add(late);
  scene.traverse((o) => {
    // A hit camera can cut to any rival. Projection must not hide its other distance variants during preparation.
    if (o.isLOD) { lods.push([o, o.autoUpdate]); o.autoUpdate = false; }
    saved.push([o, o.visible, o.frustumCulled, o.count]);
    o.visible = true; o.frustumCulled = false;
    if (o.count === 0 && (o.isInstancedMesh || o.isSprite || o.isBatchedMesh)) o.count = 1;
    for (const m of [].concat(o.material || [])) if (m && m.visible === false) { mats.push(m); m.visible = true; }
  });
  const layers = camera.layers.mask, shadows = three.shadowMap?.enabled;
  // Use the inset's actual layers. Its shadowless pass cannot compile the
  // layer-2 ShadowNodeMaterial road catchers.
  scene.updateMatrixWorld(true);
  try {
    // Shader building can yield after taking the render list. Keep the shadowless
    // policy until it settles; the target itself is still returned before awaiting.
    if (three.shadowMap) three.shadowMap.enabled = false;
    // the first real hit resizes the target; a 2 px one has the same formats, so the programs carry over
    await withInsetTarget(three, post.inset.target, () => three.compileAsync(scene, camera));
    // A real covered draw also prepares draw-time material variants and uploads.
    withInsetTarget(three, post.inset.target, () => three.render(scene, camera));
  } catch (e) {
    console.warn('[glassroad/renderer] hit cam warm: ' + (e && e.message));
  } finally {
    if (three.shadowMap) three.shadowMap.enabled = shadows;
    for (const [o, autoUpdate] of lods) o.autoUpdate = autoUpdate;
    camera.layers.mask = layers;
    for (const [o, v, f, n] of saved) { o.visible = v; o.frustumCulled = f; if (o.count !== n) o.count = n; }
    for (const m of mats) m.visible = false;
    for (const o of [...late.children]) { late.remove(o); if (o.isMesh) o.geometry.dispose(); }
    scene.remove(late);
    scene.updateMatrixWorld(true);
  }
  return { ms: Math.round(performance.now() - t0) };
}

/**
 * Build every pipeline a race can need before the start card lifts (the WebGPU countdown hitch was
 * first-use compiles: rivals, accessories, bubbles, item pools, particles and screen effects).
 * Everything in the scene is shown for the warm, culling off, empty instanced counts at 1; `extras`
 * (detached Object3Ds, or materials, which get a tiny plane or sprite) join for the warm only. The
 * scene pass compiles in parallel (compileAsync for its own target), then one forced frame uploads
 * textures and builds the shadow and screen chains, and a clean frame is drawn over it in the same task.
 * Pass 3 (loading): the warm runs in groups (warmGroups: about WARM.perGroup materials new to the warm
 * each), one group shown per pass and a painted frame between passes, so the warming card's bar moves
 * and the tab never looks hung (Firefox linked every program serially in one ~7 s task). A last pass with
 * everything shown catches anything only a full frame builds. onProgress(done, total) after each group.
 * Garage first: the warm can run behind the live menu. overdraw() is called in the same task as every warm
 * frame (the garage draws its own frame over it before anything is presented, so no warm frame is ever seen)
 * and alive() false after a pass stops the warm (a course switch mid-warm) with everything put back.
 * @returns {Promise<{ ms: number, objects: number, groups: number, stopped: boolean }>}
 */
export { WARM, warmGroups };

// a painted frame between warm passes (the card and its bar redraw), falling back to a plain task
const paintGap = () => new Promise((r) => (globalThis.requestAnimationFrame ? requestAnimationFrame(() => setTimeout(r, 0)) : setTimeout(r, 0)));

export async function warmAll(renderer, scene, camera, extras = [], { onProgress = null, stage = null, alive = null, overdraw = null } = {}) {
  const t0 = performance.now();
  const post = renderer.post(scene, camera);
  const timing = { compileMs: 0, renderMs: 0, gapMs: 0 };
  // compileStart hands the scene pass target back in this task (post.js): a frame drawn while the pipelines
  // build (the garage behind the menu) lands on the canvas, never in the scene pass target
  const compile = async () => { const t = performance.now(); await (post.compileStart ? post.compileStart() : post.compileAsync()); timing.compileMs += performance.now() - t; };
  const render = () => { const t = performance.now(); post.render(); if (overdraw) overdraw(); timing.renderMs += performance.now() - t; };
  const gap = async () => { const t = performance.now(); await paintGap(); timing.gapMs += performance.now() - t; };
  const stopped = () => !!alive && !alive();
  const temp = new Group(), plane = new PlaneGeometry(0.01, 0.01);
  temp.name = 'warm-extras';
  for (const x of [].concat(extras || [], lateWarmMaterials(GPU))) {   // + the materials made only in a race
    const o = !x ? null : x.isObject3D ? x : x.object && x.object.isObject3D ? x.object
      : x.isMaterial ? (x.isSpriteNodeMaterial || x.type === 'SpriteNodeMaterial' ? new Sprite(x) : new Mesh(plane, x)) : null;
    if (o && !o.parent) temp.add(o);
  }
  scene.add(temp);
  const saved = [], mats = [], items = [], lods = [];
  let objects = 0, groups = [];
  scene.traverse((o) => {
    // Projection normally selects one LOD and hides the others, undoing the warm's
    // all-visible state. Prepare every distance variant before an intro cut needs it.
    if (o.isLOD) { lods.push([o, o.autoUpdate]); o.autoUpdate = false; }
    saved.push([o, o.visible, o.frustumCulled, o.count]);
    o.visible = true; o.frustumCulled = false;
    if (o.count === 0 && (o.isInstancedMesh || o.isSprite || o.isBatchedMesh)) o.count = 1;
    for (const m of [].concat(o.material || [])) if (m && m.visible === false) { mats.push(m); m.visible = true; }
    if (o.material && (o.isMesh || o.isSprite || o.isPoints || o.isLine)) items.push(o);
    objects++;
  });
  scene.updateMatrixWorld(true);
  // the phone tier's staged preparation (render/stagedWarm.js): the cover prepares only what the opening needs,
  // the deferred groups are prepared while the menu, the intro and the countdown play. Extras are always now.
  let plan = null, later = null;
  if (stage) {
    try {
      plan = stage({ items, saved, hiddenMats: new Set(mats), scene, camera, pinRoot: temp });
      later = new Set(plan.groups.flatMap((g) => g.items));
    } catch (e) { plan = null; later = null; console.warn('[glassroad/renderer] stage: ' + (e && e.message)); }
  }
  // every layer too: the shadow catchers live on their own layer, which the camera only joins in the first
  // world.update; without them the warm frame never runs the shadow pass, and every caster's depth program
  // then links in the first menu frame (a 1.5 s stall in Firefox, which has no parallel shader compile)
  const layers = camera && camera.layers ? camera.layers.mask : null;
  if (layers !== null) camera.layers.enableAll();
  try {
    // one group shown per pass: its programs (and its casters' shadow programs) link in their own task
    // shadow receivers (road, pads, catchers) stay in every pass: the shadow pass only runs in a frame that
    // draws a receiver, so without them each group's casters linked their depth programs in the last pass
    const receivers = items.filter((o) => o.receiveShadow), casting = items.filter((o) => !o.receiveShadow && !(later && later.has(o)));
    groups = warmGroups(casting, renderer.backend === 'webgpu' ? WARM.webgpuPerGroup : WARM.perGroup);
    for (const o of casting) o.visible = false;
    if (later) for (const o of later) o.visible = false;
    // pass 0: the post chain and the receivers alone (the road's programs are big), then a painted frame
    await compile();
    if (!stopped()) { render(); await gap(); }
    const shown = [];
    for (let g = 0; g < groups.length && !stopped(); g++) {
      for (const o of groups[g]) for (let p = o; p && p !== scene; p = p.parent) if (!p.visible) { p.visible = true; shown.push(p); }
      await compile();
      if (!stopped()) render();
      for (const p of shown) p.visible = false;
      shown.length = 0;
      if (stopped()) break;
      if (onProgress) { try { onProgress(g + 1, groups.length + 1); } catch { /* the card only */ } }
      await gap();
    }
    if (!stopped()) {
      // the whole scene once: anything only a full frame builds (everything above is cached by now)
      for (const o of items) o.visible = !(later && later.has(o));
      await compile();
      if (!stopped()) {
        render();
        if (onProgress) { try { onProgress(groups.length + 1, groups.length + 1); } catch { /* the card only */ } }
      }
    }
  } catch (e) {
    console.warn('[glassroad/renderer] warm: ' + (e && e.message));
  } finally {
    for (const [o, autoUpdate] of lods) o.autoUpdate = autoUpdate;
    if (layers !== null) camera.layers.mask = layers;
    for (const [o, v, f, n] of saved) { o.visible = v; o.frustumCulled = f; if (o.count !== n) o.count = n; }
    for (const m of mats) m.visible = false;
    for (const o of [...temp.children]) temp.remove(o);
    scene.remove(temp);
    plane.dispose();
    scene.updateMatrixWorld(true);
  }
  if (!stopped()) render();
  return { ms: Math.round(performance.now() - t0), objects, groups: groups.length, staged: plan, all: plan ? items : null, stopped: stopped(),
    ...Object.fromEntries(Object.entries(timing).map(([key, value]) => [key, Math.round(value)])) };
}
