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
 */
import { WebGPURenderer, Group, Mesh, Sprite, PlaneGeometry } from 'three/webgpu';
import { createPost } from './post.js';
import { Q, TIERS, GOVERNOR, setTier, pickTier, createGovernor, createDynRes, dynLines, shortGpu } from './quality.js';
import { createFrameGate } from '../core/clock.js';
import { loadOptions, cleanOptions } from '../hud/options.js';
import { WARM, warmGroups } from './warmGroups.js';

export async function createRenderer(canvas, { forceRenderer = null, maxDpr = 2, options = null } = {}) {
  const qs = new URLSearchParams(globalThis.location ? location.search : '');
  const force = forceRenderer || (/^(webgl2|webgpu)$/.test(qs.get('renderer') || '') ? qs.get('renderer') : null);
  const override = TIERS[qs.get('quality')] ? qs.get('quality') : null;
  const coarse = !!(globalThis.matchMedia && matchMedia('(pointer: coarse)').matches);
  let opts = cleanOptions(options || loadOptions());
  // ?quality= wins (dev), then a pinned Quality option, else the auto pick
  const pinOf = () => override ? null : opts.quality !== 'auto' ? opts.quality : null;
  // ask for the fast GPU on both paths; no adapter means the WebGL2 fallback, with a context made here.
  // Firefox runs WebGL2 unless ?renderer=webgpu: its WebGPU drew every lit prop as a black silhouette for the
  // owner (polish 4, Firefox 158) while its WebGL2 draws them right; a chosen WebGL2 still starts on high
  const firefox = !force && !!globalThis.navigator && (navigator.userAgent || '').includes('Firefox/');
  let webgl2 = force === 'webgl2' || firefox, gpu = '', context = null;
  if (!webgl2) {
    try {
      const ad = globalThis.navigator && navigator.gpu ? await navigator.gpu.requestAdapter({ powerPreference: 'high-performance', featureLevel: 'compatibility' }) : null;
      if (!ad) webgl2 = true;
      else { const i = ad.info || {}; gpu = shortGpu([i.vendor, i.architecture, i.description].filter(Boolean).join(' ')); }
    } catch (e) { webgl2 = true; }
  }
  if (webgl2) context = canvas.getContext('webgl2', { antialias: false, alpha: true, depth: true, stencil: false, powerPreference: 'high-performance' });
  setTier(pinOf() || pickTier({ override, coarse, webgl2: webgl2 && !firefox }));

  const three = new WebGPURenderer({ canvas, antialias: false, forceWebGL: webgl2, powerPreference: 'high-performance', ...(context ? { context } : {}) });
  await three.init();
  three.info.autoReset = false;                    // reset once per frame here, so stats() is one whole frame
  const backend = three.backend.isWebGPUBackend ? 'webgpu' : 'webgl2';
  if (backend === 'webgl2') {
    try { const gl = three.backend.gl, ext = gl.getExtension('WEBGL_debug_renderer_info'); gpu = shortGpu(gl.getParameter(ext ? ext.UNMASKED_RENDERER_WEBGL : gl.RENDERER)); } catch (e) { /* hidden */ }
  }
  const autoTier = pickTier({ override, coarse, webgl2: backend === 'webgl2' && !firefox, gpu });
  setTier(pinOf() || autoTier);
  // graceS: boot, art decode and the first shader compiles never cost a tier
  const govRules = { ...GOVERNOR, graceS: 6 };
  let gov = pinOf() ? null : createGovernor({ tier: Q.name, ceiling: autoTier, rules: govRules });
  const gate = createFrameGate({ cap: opts.frameCap });
  const dyn = createDynRes();
  const tierFns = new Set();
  let post = null, postKey = null, last = 0, gated = false;
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
    if (!post || postKey[0] !== scene || postKey[1] !== camera || post.antialias !== Q.antialias) {
      if (post) post.dispose();
      post = createPost(three, scene, camera); postKey = [scene, camera];
    }
    return post;
  }
  function retier() {
    resize();
    if (post && post.antialias === Q.antialias) post.retier();   // an MSAA change rebuilds on the next frame
    for (const fn of tierFns) fn(Q.name);
  }
  function setQuality() {
    const pin = pinOf(), name = pin || autoTier;
    gov = pin ? null : createGovernor({ tier: name, ceiling: autoTier, rules: govRules });
    if (name !== Q.name) { setTier(name); retier(); }
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
    get tier() { return Q.name; },
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
    post: postFor,
    compile(scene, camera, extras, opts) { return warmAll(api, scene, camera, extras, opts); },
    resize,
    /** dt (frame seconds) reaches post.update for the item screen effect (CONTRACT-B1). */
    render({ scene, camera, fx, dt = 0 }) {
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
      p.update(fx, dt, Math.max(1, canvas.clientWidth) / Math.max(1, canvas.clientHeight));
      p.render();
    },
    stats() {
      const r = three.info.render;
      return { backend, tier: Q.name, drawCalls: r.drawCalls, triangles: r.triangles, perFrame: true,
        renderScale: Math.round(scale() * 100) / 100, renderFps: meter.fps, displayMs: Math.round(gate.displayMs * 100) / 100, gpu, art: meter.art };
    },
    dispose() { if (ro) ro.disconnect(); if (post) post.dispose(); readout(false); three.dispose(); },
  };
  return api;
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
 * @returns {Promise<{ ms: number, objects: number, groups: number }>}
 */
export { WARM, warmGroups };

// a painted frame between warm passes (the card and its bar redraw), falling back to a plain task
const paintGap = () => new Promise((r) => (globalThis.requestAnimationFrame ? requestAnimationFrame(() => setTimeout(r, 0)) : setTimeout(r, 0)));

export async function warmAll(renderer, scene, camera, extras = [], { onProgress = null } = {}) {
  const t0 = performance.now();
  const post = renderer.post(scene, camera);
  const temp = new Group(), plane = new PlaneGeometry(0.01, 0.01);
  temp.name = 'warm-extras';
  for (const x of [].concat(extras || [])) {
    const o = !x ? null : x.isObject3D ? x : x.object && x.object.isObject3D ? x.object
      : x.isMaterial ? (x.isSpriteNodeMaterial || x.type === 'SpriteNodeMaterial' ? new Sprite(x) : new Mesh(plane, x)) : null;
    if (o && !o.parent) temp.add(o);
  }
  scene.add(temp);
  const saved = [], mats = [], items = [];
  let objects = 0, groups = [];
  scene.traverse((o) => {
    saved.push([o, o.visible, o.frustumCulled, o.count]);
    o.visible = true; o.frustumCulled = false;
    if (o.count === 0 && (o.isInstancedMesh || o.isSprite || o.isBatchedMesh)) o.count = 1;
    for (const m of [].concat(o.material || [])) if (m && m.visible === false) { mats.push(m); m.visible = true; }
    if (o.material && (o.isMesh || o.isSprite || o.isPoints || o.isLine)) items.push(o);
    objects++;
  });
  scene.updateMatrixWorld(true);
  // every layer too: the shadow catchers live on their own layer, which the camera only joins in the first
  // world.update; without them the warm frame never runs the shadow pass, and every caster's depth program
  // then links in the first menu frame (a 1.5 s stall in Firefox, which has no parallel shader compile)
  const layers = camera && camera.layers ? camera.layers.mask : null;
  if (layers !== null) camera.layers.enableAll();
  try {
    // one group shown per pass: its programs (and its casters' shadow programs) link in their own task
    // shadow receivers (road, pads, catchers) stay in every pass: the shadow pass only runs in a frame that
    // draws a receiver, so without them each group's casters linked their depth programs in the last pass
    const receivers = items.filter((o) => o.receiveShadow), casting = items.filter((o) => !o.receiveShadow);
    groups = warmGroups(casting);
    for (const o of casting) o.visible = false;
    // pass 0: the post chain and the receivers alone (the road's programs are big), then a painted frame
    await post.compileAsync();
    post.render();
    await paintGap();
    const shown = [];
    for (let g = 0; g < groups.length; g++) {
      for (const o of groups[g]) for (let p = o; p && p !== scene; p = p.parent) if (!p.visible) { p.visible = true; shown.push(p); }
      await post.compileAsync();
      post.render();
      for (const p of shown) p.visible = false;
      shown.length = 0;
      if (onProgress) { try { onProgress(g + 1, groups.length + 1); } catch { /* the card only */ } }
      await paintGap();
    }
    // the whole scene once: anything only a full frame builds (everything above is cached by now)
    for (const o of items) o.visible = true;
    await post.compileAsync();
    post.render();
    if (onProgress) { try { onProgress(groups.length + 1, groups.length + 1); } catch { /* the card only */ } }
  } catch (e) {
    console.warn('[glassroad/renderer] warm: ' + (e && e.message));
  } finally {
    if (layers !== null) camera.layers.mask = layers;
    for (const [o, v, f, n] of saved) { o.visible = v; o.frustumCulled = f; if (o.count !== n) o.count = n; }
    for (const m of mats) m.visible = false;
    for (const o of [...temp.children]) temp.remove(o);
    scene.remove(temp);
    plane.dispose();
    scene.updateMatrixWorld(true);
  }
  post.render();
  return { ms: Math.round(performance.now() - t0), objects, groups: groups.length };
}
