/* preview/stage.js - the 3D half of the preview box (lane B3-W5): one small WebGL canvas, a turntable, Astra's GLBs.
 * Made on the first 3D item; preview/index.js disposes it with the counter.
 *
 *   const stage = createStage(box, { base, motion, reducedMotion, gentle });
 *   const h = await stage.open(spec); h.show(); h.hide(); h.dispose(); stage.release(kind); stage.dispose();
 *
 * Per-item GLBs are loaded for the focused item and disposed when it leaves. What a kind shares between its items
 * (the Rain avatar, the knight) stays parsed until stage.release(kind); compositions are clones of it, so disposing an
 * item never frees the shared geometry. */
import * as T from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { HOME_YAW, fitDistance, spinRate, viewBox } from './frame.js';
import { KART_JOINTS, bindsTo, cutBelow, paintWalk, shellMask } from './fit.js';

const FOV = 28;
const I4 = new T.Matrix4();

/** A clone of a skinned tree with its own skeletons on the cloned bones (three's SkeletonUtils.clone, small). */
export function cloneSkinned(src) {
  const root = src.clone(true), a = [], b = [], skeletons = [];
  src.traverse((o) => a.push(o)); root.traverse((o) => b.push(o));
  const map = new Map(a.map((o, i) => [o, b[i]]));
  a.forEach((o, i) => {
    if (!o.isSkinnedMesh) return;
    const s = new T.Skeleton(o.skeleton.bones.map((x) => map.get(x)), o.skeleton.boneInverses);
    b[i].bind(s, o.bindMatrix);
    skeletons.push(s);
  });
  return { root, skeletons };
}

/** The shell mask attribute on a kart geometry, made once per shared geometry (the other clones never read it). */
const shellDone = new WeakSet();
function withShell(mesh) {
  const g = mesh.geometry;
  if (shellDone.has(g)) return;
  const j = g.getAttribute('skinIndex'), w = g.getAttribute('skinWeight'), n = g.getAttribute('position').count;
  const flat = (a) => { const o = new Float32Array(a.count * 4); for (let i = 0; i < a.count; i++) for (let k = 0; k < 4; k++) o[i * 4 + k] = a.getComponent(i, k); return o; };
  g.setAttribute('pcShell', new T.BufferAttribute(j && w ? shellMask(flat(j), flat(w), mesh.skeleton.bones.map((x) => x.name), n) : new Float32Array(n), 1));
  shellDone.add(g);
}

// Glassroad's shell paint (render/emiSkin.js, TSL there) in GLSL for r169: two layers so a walk blends, never cuts.
const PAINT_GLSL = `
float pcPat(float id, vec2 p, sampler2D mask) {
  float stripes = step(.5, fract((p.x + p.y * 1.2) * 14.));
  float dots = 1. - step(.23, length(fract(p * 17.) - .5));
  float checks = fract((floor(p.x * 14.) + floor(p.y * 14.)) * .5) * 2.;
  float m = texture2D(mask, vec2((p.x + .255) / .511, (p.y - .427) / .52)).r;
  return id < .5 ? 0. : id < 1.5 ? stripes : id < 2.5 ? dots : id < 3.5 ? checks : m;
}`;
function paintMaterial(src, u) {
  const m = src.clone();
  m.onBeforeCompile = (sh) => {
    Object.assign(sh.uniforms, u);
    // Declarations go after the material's own #defines (behind <common>), never in front of them.
    sh.vertexShader = sh.vertexShader
      .replace('#include <common>', '#include <common>\nattribute float pcShell;\nvarying float vPcShell;\nvarying vec2 vPcP;\n')
      .replace('#include <begin_vertex>', '#include <begin_vertex>\nvPcShell = pcShell;\nvPcP = position.xy;');
    sh.fragmentShader = sh.fragmentShader
      .replace('#include <common>', '#include <common>\nvarying float vPcShell;\nvarying vec2 vPcP;\n'
        + 'uniform vec3 pcPrimaryA, pcSecondaryA, pcPrimaryB, pcSecondaryB;\nuniform float pcPatternA, pcPatternB, pcBlend;\n'
        + 'uniform sampler2D pcMaskA, pcMaskB;\n' + PAINT_GLSL + '\n')
      .replace('#include <map_fragment>', `#include <map_fragment>
  {
    vec3 pcBase = diffuseColor.rgb;
    float pcShade = min(dot(pcBase, vec3(.299, .587, .114)) * 1.5 + .42, 1.2);
    vec3 pcA = mix(pcPrimaryA, pcSecondaryA, pcPat(pcPatternA, vPcP, pcMaskA));
    vec3 pcB = mix(pcPrimaryB, pcSecondaryB, pcPat(pcPatternB, vPcP, pcMaskB));
    diffuseColor.rgb = mix(pcBase, mix(pcA, pcB, pcBlend) * pcShade, vPcShell);
  }`);
  };
  m.customProgramCacheKey = () => 'prizecounter-kart-paint';
  return m;
}

/** Free a loaded tree's geometry, materials and textures. Only for trees this stage owns, never for clones. */
export function disposeTree(root) {
  root.traverse((o) => {
    if (o.geometry) o.geometry.dispose();
    for (const m of [].concat(o.material || [])) {
      for (const v of Object.values(m)) if (v && v.isTexture) v.dispose();
      m.dispose();
    }
    if (o.skeleton) o.skeleton.dispose();
  });
}

export function createStage(box, { base = './assets/', motion = 'full', reducedMotion = false, gentle = false } = {}) {
  const doc = box.ownerDocument;
  const canvas = doc.createElement('canvas');
  canvas.className = 'pc-pv pc-pv-3d';
  box.append(canvas);
  const renderer = new T.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'low-power' });
  renderer.setPixelRatio(Math.min(2, (doc.defaultView && doc.defaultView.devicePixelRatio) || 1));
  renderer.outputColorSpace = T.SRGBColorSpace;
  renderer.toneMapping = T.NeutralToneMapping;
  renderer.setClearColor(0x000000, 0);
  const scene = new T.Scene(), camera = new T.PerspectiveCamera(FOV, 1, 0.01, 50);
  // The counter's studio, small: a few glowing panels baked once so lacquer, satin and gold have something to show.
  const pm = new T.PMREMGenerator(renderer), studio = new T.Scene();
  studio.background = new T.Color('#3a2a40');
  for (const [p, c] of [[[0, 6, 4], [5, 4.4, 4.4]], [[-6, 2, 3], [2.4, 1.2, 2]], [[6, 2, 3], [1.6, 1.4, 2.6]]]) {
    const panel = new T.Mesh(new T.PlaneGeometry(5, 3), new T.MeshBasicMaterial({ color: new T.Color(...c), side: T.DoubleSide }));
    panel.position.set(...p); panel.lookAt(0, 0, 0); studio.add(panel);
  }
  const envTex = pm.fromScene(studio, 0.04).texture;
  scene.environment = envTex;
  studio.traverse((x) => { x.geometry?.dispose(); x.material?.dispose(); });
  pm.dispose();
  scene.add(new T.HemisphereLight(0xfff2f8, 0x3a2440, 0.6));
  const key = new T.DirectionalLight(0xfff0e0, 2.2); key.position.set(-2, 4, 5); scene.add(key);
  const turn = new T.Group();
  scene.add(turn);

  const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  const shared = new Map();   // file -> { kind, gltf: Promise }
  const keep = (file, kind) => {
    if (!shared.has(file)) {
      const gltf = loader.loadAsync(base + file);
      gltf.catch(() => { if (shared.get(file)?.gltf === gltf) shared.delete(file); });
      shared.set(file, { kind, gltf });
    }
    return shared.get(file).gltf;
  };
  const own = (file) => loader.loadAsync(base + file).then((g) => g.scene);

  const rate = spinRate({ motion, reducedMotion, gentle });
  let current = null, raf = 0, last = 0, dead = false;
  const draw = () => { if (!dead) renderer.render(scene, camera); };
  const tick = (now) => {
    raf = 0;
    if (dead || !current) return;
    const dt = last ? Math.min(0.1, (now - last) / 1000) : 0;
    last = now;
    turn.rotation.y += rate * dt;
    draw();
    raf = requestAnimationFrame(tick);
  };
  function size() {
    const w = Math.max(1, box.clientWidth || 250), h = Math.max(1, box.clientHeight || 150);
    renderer.setSize(w, h, false);
    camera.aspect = w / h;
    return w / h;
  }
  // Centre the model on the turntable and back the camera off until it fits while it turns.
  function frame(root, view) {
    root.updateMatrixWorld(true);
    const b = new T.Box3().setFromObject(root);
    if (b.isEmpty()) return;
    const v = viewBox(b.min.toArray(), b.max.toArray(), view);
    root.position.x -= v.center[0]; root.position.z -= v.center[2];
    const aspect = size(), dist = fitDistance(v.size, FOV, aspect);
    camera.position.set(0, v.center[1] + v.size[1] * 0.12, dist);
    camera.near = Math.max(0.01, dist / 100); camera.far = dist * 4;
    camera.lookAt(0, v.center[1], 0);
    camera.updateProjectionMatrix();
  }

  // What each kind builds: { root, owned[], free[], live? } (owned = trees loaded for this item only, free = what this
  // item made from shared art, live = { show(), hide() } for an item that moves on its own, the paint walk).
  const build = {
    async model(spec) { const s = await own(spec.file); return { root: s, owned: [s] }; },
    async chess(spec) {
      const [piece, att] = await Promise.all([keep(spec.piece, 'chess'), own(spec.file)]);
      const root = piece.scene.clone(true), knight = root.children[0] || root;
      knight.add(att);   // identity under the approved piece root (Astra's registry)
      return { root, owned: [att] };
    },
    async rain(spec) {
      const gltf = await keep(spec.base, 'rain'), wear = spec.wear ? await own(spec.wear) : null;
      const root = gltf.scene.clone(true), rain = root.getObjectByName(spec.root) || root;
      const lib = gltf.scenes.find((s) => s.name === spec.library);
      // spec.hide already carries the slots the item covers (catalog.js coveredGroups): boots under the coverall...
      for (const n of [...(spec.hide || []), ...(spec.hideNamed || [])]) { const o = root.getObjectByName(n); if (o) o.visible = false; }
      for (const n of spec.show || []) { const o = lib && lib.getObjectByName(n); if (o) rain.add(o.clone(true)); }
      if (wear) rain.add(wear);
      // ...and a suit's hideBodyBelow cuts the legs out of the body it covers (the wardrobe's lower-body mask).
      const free = [], below = spec.coverage && spec.coverage.hideBodyBelow;
      if (Number.isFinite(below)) {
        root.updateMatrixWorld(true);
        const inv = rain.matrixWorld.clone().invert(), m4 = new T.Matrix4(), v = new T.Vector3();
        for (const n of spec.body || []) {
          const g = root.getObjectByName(n);
          if (!g || !g.visible) continue;
          g.traverse((mesh) => {
            if (!mesh.isMesh) return;
            m4.multiplyMatrices(inv, mesh.matrixWorld);
            const src = mesh.geometry, pos = src.getAttribute('position');
            const at = (i) => v.fromBufferAttribute(pos, i).applyMatrix4(m4).toArray();
            const geo = src.clone();
            geo.setIndex(new T.BufferAttribute(cutBelow(src.index ? src.index.array : null, pos.count, at, below), 1));
            mesh.geometry = geo; free.push(geo);
          });
        }
      }
      return { root, owned: wear ? [wear] : [], free };
    },
    // EMI's rigged kart: outfits and the umbrella bound to its own bones (glassroad render/wardrobe.js rebind), a paint
    // on its shell. The base stays parsed while the focus walks the kart shelf.
    async kart(spec) {
      const [gltf, ...wears] = await Promise.all([keep(spec.base, 'kart'), ...spec.wear.map(own)]);
      const { root, skeletons } = cloneSkinned(gltf.scene), free = [...skeletons];
      const meshes = [];
      root.traverse((o) => { if (o.isSkinnedMesh) meshes.push(o); });
      const kart = meshes.find((m) => m.material && m.material.name === 'kart_surface') || meshes[0];
      if (!kart) throw new Error('kart base has no skinned mesh');
      const byName = new Map(kart.skeleton.bones.map((b) => [b.name, b]));
      if (!bindsTo(KART_JOINTS, [...byName.keys()])) throw new Error('kart base bones are not the accepted set');
      // Hold the accepted idle pose (time zero), as the garage shows EMI.
      const idle = gltf.animations.find((c) => c.name === 'idle_bob');
      const mixer = idle ? new T.AnimationMixer(root) : null;
      if (mixer) { mixer.clipAction(idle).play(); mixer.update(0); free.push({ dispose: () => { mixer.stopAllAction(); mixer.uncacheRoot(root); } }); }
      for (const w of wears) {
        const parts = [];
        w.traverse((o) => { if (o.isSkinnedMesh) parts.push(o); });
        for (const s of parts) {
          const names = s.skeleton.bones.map((b) => b.name);
          if (!bindsTo(names, KART_JOINTS)) throw new Error('outfit bones are not the accepted kart set');
          const m = new T.SkinnedMesh(s.geometry, s.material), sk = new T.Skeleton(names.map((n) => byName.get(n)), s.skeleton.boneInverses);
          m.name = s.name; m.frustumCulled = false;
          m.bind(sk, I4);   // identity local transform and bind: the kart mesh's own space
          kart.parent.add(m);
          free.push(sk);
        }
      }
      let live = null;
      if (spec.layers && spec.layers.length) {
        const blank = new T.DataTexture(new Uint8Array(4), 1, 1), masks = new Map(), tl = new T.TextureLoader();
        blank.needsUpdate = true;
        free.push(blank);
        await Promise.all([...new Set(spec.layers.map((l) => l.mask).filter(Boolean))].map(async (f) => {
          const t = await tl.loadAsync(base + f);
          t.wrapS = t.wrapT = T.RepeatWrapping; t.colorSpace = T.NoColorSpace;
          masks.set(f, t); free.push(t);
        }));
        const u = { pcBlend: { value: 0 } };
        for (const s of ['A', 'B']) Object.assign(u, { ['pcPrimary' + s]: { value: new T.Color() }, ['pcSecondary' + s]: { value: new T.Color() },
          ['pcPattern' + s]: { value: 0 }, ['pcMask' + s]: { value: blank } });
        const put = (s, l) => {
          u['pcPrimary' + s].value.set(l.primary); u['pcSecondary' + s].value.set(l.secondary);
          u['pcPattern' + s].value = l.pattern; u['pcMask' + s].value = (l.mask && masks.get(l.mask)) || blank;
        };
        put('A', spec.layers[0]); put('B', spec.layers[0]);
        withShell(kart);
        kart.material = paintMaterial(kart.material, u);
        free.push(kart.material);
        const walk = paintWalk(spec.layers.length, { motion, reducedMotion, gentle });
        if (walk) live = paintWalker(spec.layers, walk, u, put);
      }
      return { root, owned: wears, free, live };
    },
  };

  // A paint with several patterns: hold one, then blend to the next over blendMs (each pattern's OUT is the next one's IN).
  function paintWalker(layers, { holdMs, blendMs }, u, put) {
    let i = 0, timer = 0, anim = 0, t0 = 0;
    const step = (now) => {
      anim = 0;
      if (!t0) t0 = now;
      const k = Math.min(1, (now - t0) / blendMs);
      u.pcBlend.value = k * k * (3 - 2 * k);
      if (!raf) draw();   // the turntable draws while it runs
      if (k < 1) { anim = requestAnimationFrame(step); return; }
      put('A', layers[i]); u.pcBlend.value = 0;
      timer = setTimeout(next, holdMs);
    };
    const next = () => { timer = 0; i = (i + 1) % layers.length; put('B', layers[i]); t0 = 0; anim = requestAnimationFrame(step); };
    const stop = () => { clearTimeout(timer); cancelAnimationFrame(anim); timer = anim = 0; };
    return { show() { stop(); put('A', layers[i]); u.pcBlend.value = 0; timer = setTimeout(next, holdMs); }, hide: stop };
  }

  return {
    canvas,
    async open(spec) {
      if (dead || !build[spec.kind]) throw new Error(`no stage for ${spec.kind}`);
      const { root, owned, free = [], live = null } = await build[spec.kind](spec);
      root.traverse((o) => { if (o.isSkinnedMesh) o.frustumCulled = false; });
      let gone = false;
      return {
        show() {
          if (gone || dead) return;
          if (current && current !== root) turn.remove(current);
          current = root;
          turn.rotation.y = HOME_YAW;
          turn.add(root);
          frame(root, spec.view);
          canvas.classList.add('pc-pv-on');
          last = 0;
          draw();
          if (rate && !raf) raf = requestAnimationFrame(tick);
          if (live) live.show();
        },
        hide() { if (live) live.hide(); if (current === root) canvas.classList.remove('pc-pv-on'); },
        dispose() {
          if (gone) return;
          gone = true;
          if (live) live.hide();
          if (current === root) { current = null; turn.remove(root); if (raf) { cancelAnimationFrame(raf); raf = 0; } }
          for (const o of owned) disposeTree(o);
          for (const x of free) x.dispose();
        },
      };
    },
    /** Forget what a kind keeps between its items (the focus left that kind). */
    release(kind) {
      for (const [file, s] of shared) if (s.kind === kind) { shared.delete(file); s.gltf.then((g) => g.scenes.forEach(disposeTree), () => {}); }
    },
    resize() { if (current) { size(); draw(); } },
    dispose() {
      if (dead) return;
      dead = true;
      if (raf) cancelAnimationFrame(raf);
      if (current) turn.remove(current);
      current = null;
      for (const kind of new Set([...shared.values()].map((s) => s.kind))) this.release(kind);
      envTex.dispose();
      renderer.dispose();
      renderer.forceContextLoss();
      canvas.remove();
    },
  };
}
