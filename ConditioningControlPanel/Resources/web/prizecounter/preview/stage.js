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

const FOV = 28;

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

  // What each kind builds: { root, owned[] } (owned = trees loaded for this item only).
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
      for (const n of [...(spec.hide || []), ...(spec.hideNamed || [])]) { const o = root.getObjectByName(n); if (o) o.visible = false; }
      for (const n of spec.show || []) { const o = lib && lib.getObjectByName(n); if (o) rain.add(o.clone(true)); }
      if (wear) rain.add(wear);
      return { root, owned: wear ? [wear] : [] };
    },
  };

  return {
    canvas,
    async open(spec) {
      if (dead || !build[spec.kind]) throw new Error(`no stage for ${spec.kind}`);
      const { root, owned } = await build[spec.kind](spec);
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
        },
        hide() { if (current === root) canvas.classList.remove('pc-pv-on'); },
        dispose() {
          if (gone) return;
          gone = true;
          if (current === root) { current = null; turn.remove(root); if (raf) { cancelAnimationFrame(raf); raf = 0; } }
          for (const o of owned) disposeTree(o);
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
