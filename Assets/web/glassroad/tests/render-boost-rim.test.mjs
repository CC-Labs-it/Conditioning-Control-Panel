import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { BOOST_RIM as L, FLOW_HZ_MAX, flowHz, overTop, rimTarget, rimFlowMul, rimTimes, createRimState, rimHues, hexToHsl, hueDelta, rimMask } from '../render/boostRimMath.js';
import { SPEED_FEEL, speedShare, speedFeelMul } from '../render/cameraMath.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');
const cap = SPEED_FEEL.boostDeg / SPEED_FEEL.topDeg;
// the settled widen share the camera reads, as fx/reactions.js computes it (before the settle)
const feelAt = (pct, motion = 'full', gentle = false) => speedShare(pct, 1.3, SPEED_FEEL) * speedFeelMul(motion, gentle, SPEED_FEEL);

test('boost rim: the browser module and its math parse as ES modules', () => {
  for (const f of ['render/boostRim.js', 'render/boostRimMath.js']) {
    assert.doesNotThrow(() => execFileSync(process.execPath, ['--input-type=module', '--check'], { input: src(f), encoding: 'utf8' }), f);
  }
});

test('boost rim: nothing at or under top speed without a boost; the part past top speed counts under every option', () => {
  for (const pct of [0, 0.5, 0.9, 1]) assert.equal(rimTarget({ speedFeel: feelAt(pct), speedLines: 0, boostKind: 0 }), 0, 'pct ' + pct);
  assert.equal(overTop(feelAt(1.3)), 1);
  // Gentle and Motion less halve the camera share; the rim undoes that, so the same speed is the same share
  assert.ok(Math.abs(overTop(feelAt(1.15, 'full', true), 'full', true) - overTop(feelAt(1.15))) < 1e-9);
  assert.ok(Math.abs(overTop(feelAt(1.15, 'less'), 'less') - overTop(feelAt(1.15))) < 1e-9);
  assert.equal(overTop(cap * 2), 1, 'clamped');
});

test('boost rim: a weak drift boost is a light rim, a pad or Surge is full', () => {
  const mini = rimTarget({ speedFeel: feelAt(1.06), speedLines: 0.5, boostKind: 1 });
  const tier1 = rimTarget({ speedFeel: feelAt(1.1), speedLines: 0.7, boostKind: 1 });
  const surge = rimTarget({ speedFeel: feelAt(1.25), speedLines: 0.85, boostKind: 4 });
  const pad = rimTarget({ speedFeel: feelAt(1.3), speedLines: 1, boostKind: 4 });
  assert.ok(mini > 0 && mini < 0.25, 'mini ' + mini);
  assert.ok(tier1 > mini && tier1 < surge, 'tier1 ' + tier1);
  assert.ok(surge > 0.8, 'surge ' + surge);
  assert.equal(pad, 1);
});

test('boost rim: an item throw flicking the speed lines at cruise never lights it', () => {
  assert.equal(rimTarget({ speedFeel: feelAt(0.95), speedLines: 0.6, boostKind: 0 }), 0);
});

test('boost rim: Motion off is no rim and no flow; Motion less and Gentle slow the flow', () => {
  assert.equal(rimTarget({ speedFeel: cap, speedLines: 1, boostKind: 4, motion: 'off' }), 0);
  assert.equal(rimTarget({ speedFeel: 0, speedLines: 1, boostKind: 4, motion: 'off', force: 1 }), 0, 'even the dev force');
  assert.equal(rimFlowMul('off'), 0);
  assert.equal(rimFlowMul('less'), L.lessMul);
  assert.equal(rimFlowMul('full', true), L.gentleFlowMul);
  assert.ok(flowHz(1) < FLOW_HZ_MAX, 'no pixel pulses at 3 Hz or more: ' + flowHz(1));
  assert.ok(flowHz(rimFlowMul('full', true)) <= FLOW_HZ_MAX / 2);
});

test('boost rim: eases in and out (never a pop), Gentle rises over at least riseMs at half the level', () => {
  const run = (view, s, dt = 1 / 60) => { const st = createRimState(); const lv = []; for (let t = 0; t < s; t += dt) lv.push(st.update(view, dt).level); return { st, lv }; };
  const pad = { enabled: true, speedFeel: cap, speedLines: 1, boostKind: 4 };
  const { lv } = run(pad, 1);
  for (let i = 1; i < lv.length; i++) assert.ok(lv[i] - lv[i - 1] < 0.12, 'step ' + i + ' ' + (lv[i] - lv[i - 1]));
  assert.ok(lv[0] < 0.05, 'starts soft (zero slope at rest)');
  assert.ok(lv[lv.length - 1] > 0.98, 'reaches full');
  const g = run({ ...pad, gentle: true, riseMs: 300 }, 1);
  const at = (arr, s) => arr[Math.round(s * 60) - 1];
  assert.ok(at(g.lv, 0.15) < 0.5 * 0.7, 'Gentle still rising at 150 ms');
  assert.ok(Math.abs(g.lv[g.lv.length - 1] - L.gentleMul) < 0.01, 'Gentle level is gentleMul');
  assert.ok(rimTimes({ gentle: true, riseMs: 450 }).rise >= 0.45);
  assert.ok(rimTimes({ gentle: true, fadeMs: 900 }).fall >= 0.9);
  // the boost ends: it eases out over fallS, no step
  const st = run(pad, 1).st, out = [];
  for (let i = 0; i < 60; i++) out.push(st.update({ enabled: true, speedFeel: 1, speedLines: 0, boostKind: 0 }, 1 / 60).level);
  for (let i = 1; i < out.length; i++) assert.ok(out[i - 1] - out[i] < 0.1, 'fall step ' + i);
  assert.ok(out[out.length - 1] < 0.1);
});

test('boost rim: pause freezes it; off eases out quickly; leaving the race clears it', () => {
  const st = createRimState(), pad = { enabled: true, speedFeel: cap, speedLines: 1, boostKind: 4 };
  for (let i = 0; i < 60; i++) st.update(pad, 1 / 60);
  const held = st.frame.level, flow = st.frame.flowS;
  for (let i = 0; i < 30; i++) st.update(pad, 0);
  assert.equal(st.frame.level, held); assert.equal(st.frame.flowS, flow);
  for (let i = 0; i < 18; i++) st.update({ ...pad, off: true }, 1 / 60);
  assert.ok(st.frame.level < 0.1 && st.frame.level >= 0, 'hit cam / finish eases out in ~0.2 s');
  st.update({ enabled: false }, 1 / 60);
  assert.equal(st.frame.level, 0);
});

test('boost rim: strict photosafe lowers the level', () => {
  const st = createRimState(), v = { enabled: true, speedFeel: cap, speedLines: 1, boostKind: 4, photosafe: true };
  for (let i = 0; i < 90; i++) st.update(v, 1 / 60);
  assert.ok(Math.abs(st.frame.level - L.photosafeMul) < 0.01);
});

test('boost rim: the hue set is a rainbow skewed toward the course palette, in place', () => {
  const out = new Float32Array(L.hueStops * 3);
  assert.equal(rimHues('#ff4fa3', '#ffffff', out), out, 'fills the given array');
  const pinkH = hexToHsl('#ff4fa3')[0], cyanH = hexToHsl('#00e5ff')[0], greenH = hexToHsl('#39ff88')[0];
  const huesOf = (arr) => Array.from({ length: L.hueStops }, (_, i) => {
    const r = arr[i * 3], g = arr[i * 3 + 1], b = arr[i * 3 + 2];
    const hex = '#' + [r, g, b].map((v) => Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, '0')).join('');
    return hexToHsl(hex);
  });
  const mean = (hs, h) => hs.reduce((s, x) => s + Math.abs(hueDelta(x[0], h)), 0) / hs.length;
  // Threshold-like pink: the stops sit nearer pink than a plain rainbow would (mean distance of a rainbow is 0.25)
  const pink = huesOf(rimHues('#ff4fa3', '#ff9fd6', out));
  assert.ok(mean(pink, pinkH) < 0.15, 'pink skew ' + mean(pink, pinkH));
  // Mainframe-like cyan / green: near those two hues, far from pink
  const mf = huesOf(rimHues('#00e5ff', '#39ff88', out));
  const mid = cyanH + hueDelta(cyanH, greenH) / 2;
  assert.ok(mean(mf, mid) < 0.15, 'cyan/green skew ' + mean(mf, mid));
  assert.ok(mean(mf, pinkH) > 0.3);
  // still a rainbow: the stops are not all one hue
  const spread = Math.max(...mf.map((x) => Math.abs(hueDelta(mid, x[0]))));
  assert.ok(spread > 0.05, 'keeps a hue sweep');
  // saturated, never black, never past the light cap
  for (const [, s, l] of [...pink, ...mf]) { assert.ok(s > 0.5, 'sat ' + s); assert.ok(l >= L.lightMin - 0.02 && l <= L.lightMax + 0.02, 'light ' + l); }
  // a bad colour falls back without throwing
  assert.doesNotThrow(() => rimHues('nope', null, out));
});

test('boost rim: the centre 60% stays clear, the corners carry the most', () => {
  for (const [u, v] of [[0.5, 0.5], [0.21, 0.5], [0.79, 0.5], [0.5, 0.21], [0.5, 0.79], [0.25, 0.25], [0.76, 0.76]]) assert.equal(rimMask(u, v), 0, u + ',' + v);
  const edge = rimMask(1, 0.5), corner = rimMask(1, 1), top = rimMask(0.5, 0);
  assert.ok(edge > 0.2 && top > 0.2, 'edges show');
  assert.ok(corner > edge && corner > 0.95, 'densest in the corners');
});

test('boost rim: wired into the post composite and boot, screen-space only', () => {
  const post = src('render/post.js'), boot = src('boot.js'), rim = src('render/boostRim.js').replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/.*$/gm, '');
  assert.match(post, /import\('\.\/boostRim\.js'\)/);
  assert.match(post, /rim\.apply\(out\)/);
  assert.match(post, /rim\.update\(fx, dt, aspect, view && view\.boostRim\)/);
  assert.ok(post.indexOf('rim.apply(out)') < post.indexOf('const inset = insetOver(out)'), 'under the hit cam inset');
  assert.match(boot, /boostRim: rimView/);
  assert.match(boot, /rimView\.enabled = state === 'race'/);
  // never the camera: no camera import, no fov, no pose
  assert.doesNotMatch(rim, /camera|fov|cameraMath/i);
  assert.doesNotMatch(src('render/boostRimMath.js').replace(/^ \*.*$/gm, ''), /\.fov\b|createCamRig|camera\.js/);
  // no allocation per frame in the hot update: no array / object literals after `function update`
  const upd = rim.slice(rim.indexOf('function update('), rim.indexOf('return { apply'));
  assert.doesNotMatch(upd, /new |=\s*\[|=\s*\{|\(\s*\{/, 'update allocates nothing');
});

test('boost rim: the node graph builds to WGSL behind its level gate, and update drives the uniforms', async () => {
  const { registerHooks } = await import('node:module');
  const hooks = registerHooks({ resolve(s, c, next) {
    const files = { 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
    return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
  } });
  const T = await import('three/webgpu');
  const { vec3, vec4, context } = await import('three/tsl');
  const { createBoostRim } = await import('../render/boostRim.js');
  hooks.deregister();
  const rim = createBoostRim();
  const m = new T.NodeMaterial();
  m.fragmentNode = vec4(rim.apply(vec3(0.2, 0.3, 0.4)), 1);
  const renderer = { backend: { isWebGPUBackend: true, capabilities: { getUniformBufferLimit: () => 65536 } },
    getMRT: () => null, getRenderTarget: () => null, getOutputRenderTarget: () => null, contextNode: context({}),
    shadowMap: { enabled: false }, lighting: { getNode: () => null }, debug: { checkShaderErrors: false, diagnostics: { keywords: new Set() } },
    library: new T.StandardNodeLibrary(), nodes: { getCacheKey: () => 0 }, toneMapping: 0, outputColorSpace: 'srgb', getColorSpace: () => 'srgb', hasFeature: () => false };
  const b = new T.WGSLNodeBuilder(new T.Mesh(new T.PlaneGeometry(), m), renderer);
  b.scene = new T.Scene(); b.camera = new T.PerspectiveCamera(); b.material = m;
  b.build();
  const frag = b.fragmentShader;
  assert.match(frag, /if \( \( object\.nodeUniform\d+ > 0\.001 \) \)/, 'idle costs one uniform test');
  assert.match(frag, /atan\(/);
  const fx = { speedFeel: SPEED_FEEL.boostDeg / SPEED_FEEL.topDeg, speedLines: 1 };
  const view = { enabled: true, off: false, boostKind: 4, motion: 'full', gentle: false, palette: () => ['#00e5ff', '#39ff88'] };
  for (let i = 0; i < 60; i++) rim.update(fx, 1 / 60, 16 / 9, view);
  assert.ok(rim.uniforms.level.value > 0.98 && rim.uniforms.flow.value > 0, 'pad boost: full rim, flowing');
  assert.equal(rim.stats.primary, '#00e5ff', 'reads the course palette');
  const lv = rim.uniforms.level.value, fl = rim.uniforms.flow.value;
  rim.update(fx, 0, 16 / 9, view);
  assert.equal(rim.uniforms.level.value, lv); assert.equal(rim.uniforms.flow.value, fl, 'pause freezes');
  rim.update(fx, 1 / 60, 16 / 9, undefined);
  assert.equal(rim.uniforms.level.value, 0, 'no view (garage, warm-up): no rim');
});
