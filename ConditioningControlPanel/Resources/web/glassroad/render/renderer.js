/* render/renderer.js - WebGPURenderer with the automatic WebGL2 fallback (CONTRACT.md section 6).
 * `?renderer=webgl2` (or forceRenderer) forces WebGL2. `?quality=high|mid|phone` pins the start tier.
 * The DPR cap is min(maxDpr, tier dprMax); the frame-time governor steps the tier and re-applies
 * DPR and bloom. compile() warms every scene material and the post chain behind the start card.
 */
import { WebGPURenderer } from 'three/webgpu';
import { createPost } from './post.js';
import { Q, setTier, pickTier, createGovernor } from './quality.js';

export async function createRenderer(canvas, { forceRenderer = null, maxDpr = 2 } = {}) {
  const qs = new URLSearchParams(globalThis.location ? location.search : '');
  const force = forceRenderer || (/^(webgl2|webgpu)$/.test(qs.get('renderer') || '') ? qs.get('renderer') : null);
  const override = qs.get('quality');
  const coarse = !!(globalThis.matchMedia && matchMedia('(pointer: coarse)').matches);
  setTier(pickTier({ override, coarse, webgl2: force === 'webgl2' }));

  const three = new WebGPURenderer({ canvas, antialias: Q.antialias, forceWebGL: force === 'webgl2', powerPreference: 'high-performance' });
  await three.init();
  const backend = three.backend.isWebGPUBackend ? 'webgpu' : 'webgl2';
  setTier(pickTier({ override, coarse, webgl2: backend === 'webgl2' }));
  const gov = createGovernor({ tier: Q.name });
  const tierFns = new Set();
  let post = null, postKey = null, last = 0;

  function resize() {
    const w = Math.max(1, canvas.clientWidth), h = Math.max(1, canvas.clientHeight);
    three.setPixelRatio(Math.min(globalThis.devicePixelRatio || 1, maxDpr, Q.dprMax));
    three.setSize(w, h, false);
  }
  const ro = globalThis.ResizeObserver ? new ResizeObserver(resize) : null;
  if (ro) ro.observe(canvas);
  resize();

  function postFor(scene, camera) {
    if (!post || postKey[0] !== scene || postKey[1] !== camera) {
      if (post) post.dispose();
      post = createPost(three, scene, camera); postKey = [scene, camera];
    }
    return post;
  }

  return {
    backend,
    three,
    get tier() { return Q.name; },
    get aspect() { return Math.max(1, canvas.clientWidth) / Math.max(1, canvas.clientHeight); },
    /** fn(tierName) after every governor step (scene budgets read Q themselves). */
    onTier(fn) { tierFns.add(fn); return () => tierFns.delete(fn); },
    async compile(scene, camera) {
      const p = postFor(scene, camera);
      await three.compileAsync(scene, camera);
      p.render(); // builds the bloom and composite pipelines while the start card is up
    },
    resize,
    /** dt (frame seconds) reaches post.update for the item screen effect (CONTRACT-B1). */
    render({ scene, camera, fx, dt = 0 }) {
      const now = performance.now();
      const step = last ? gov.sample(now - last) : null;
      last = now;
      const p = postFor(scene, camera);
      if (step) { setTier(step); resize(); p.retier(); for (const fn of tierFns) fn(step); }
      p.update(fx, dt, Math.max(1, canvas.clientWidth) / Math.max(1, canvas.clientHeight));
      p.render();
    },
    stats() {
      const r = three.info.render;
      return { backend, tier: Q.name, drawCalls: r.drawCalls, triangles: r.triangles };
    },
    dispose() { if (ro) ro.disconnect(); if (post) post.dispose(); three.dispose(); },
  };
}
