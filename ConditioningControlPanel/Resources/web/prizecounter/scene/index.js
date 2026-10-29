/* scene/index.js - the Prize Counter in three.js (lane B3-W2). Loads Astra's GLBs, hangs a shelf per bay, sets the
 * items, runs the camera and turns input into intents. It never decides focus: the state model calls focus().
 *
 *   const scene = await createScene(canvas, { base: './assets/', motion: 'full' });
 *   scene.showTab('kart', shelves); scene.focus(bay, slot); scene.onIntent((i) => ...);
 *
 * slot = the row index in its shelf (0..8, 'r1c2' also accepted). Intents: left right up down tabPrev tabNext
 * select back, and { type:'pick', bay, slot } from a tap. Hooks for the juice lane (B3-W3): scene.hooks.onShelvesSwap
 * ({ tabId, outgoing, incoming }) may return a promise, the outgoing shelves leave when it settles;
 * scene.hooks.onFrame(dt, t) runs every frame. scene.parts exposes the three objects. */
import * as T from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { layoutTab, slotIndex, usedBays } from './layout.js';
import { motionAmp, damp, emptyToCameraQuat, forwardOf, lookPoint, bayView, bandFit, dipFor, bob } from './camera-math.js';
import { keyIntent, padIntent, stickIntent, swipeIntent } from './intents.js';
import { createTags } from './tags.js';

const PINK = 0xff3d9a;
const STAMP_DROP = -0.14;    // owned_stamp's card centre (+0.058) lands on the name tag face (-0.082) under the hinge
// What each camera must keep in frame (width, height in metres around its look point).
const FRAME = { wide: [15.4, 6.2], bay: [3.3, 3.55] };
const WIDE_HOLD = 0.7;       // seconds on cam_wide before the first ease in

export async function createScene(canvas, { base = './assets/', motion = 'full' } = {}) {
  const renderer = new T.WebGLRenderer({ canvas, antialias: true, powerPreference: 'default' });
  renderer.setPixelRatio(Math.min(2, window.devicePixelRatio || 1));
  renderer.outputColorSpace = T.SRGBColorSpace;
  renderer.toneMapping = T.NeutralToneMapping;
  renderer.toneMappingExposure = 1.05;
  const scene = new T.Scene();
  scene.background = new T.Color('#140d19');
  const camera = new T.PerspectiveCamera(30, 1, 0.1, 80);

  // Lacquer and gold need something to reflect: a small studio of glowing panels, baked once to PMREM.
  const pm = new T.PMREMGenerator(renderer), studio = new T.Scene();
  studio.background = new T.Color('#2a1b33');
  for (const [p, c] of [[[0, 6, 4], [5, 4.4, 4.4]], [[-6, 2, 3], [2.4, 1.2, 2]], [[6, 2, 3], [1.6, 1.4, 2.6]], [[0, -3, 5], [0.5, 0.35, 0.5]]]) {
    const panel = new T.Mesh(new T.PlaneGeometry(5, 3), new T.MeshBasicMaterial({ color: new T.Color(...c), side: T.DoubleSide }));
    panel.position.set(...p); panel.lookAt(0, 0, 0); studio.add(panel);
  }
  scene.environment = pm.fromScene(studio, 0.04).texture;
  scene.environmentIntensity = 1.1;
  studio.traverse((x) => { x.geometry?.dispose(); x.material?.dispose(); });
  pm.dispose();
  scene.add(new T.HemisphereLight(0xffe4f2, 0x2a1830, 0.4));
  const key = new T.DirectionalLight(0xfff0e0, 2.6); key.position.set(-3, 7, 9); scene.add(key);
  const rim = new T.DirectionalLight(0xff8acb, 0.8); rim.position.set(5, 4, 3); scene.add(rim);

  // One load per file, a clone per use. Materials are shared between clones on purpose.
  const loader = new GLTFLoader(), cache = new Map();
  const load = (file) => {
    if (!cache.has(file)) cache.set(file, loader.loadAsync(base + file).then((g) => g.scene, (e) => { cache.delete(file); throw e; }));
    return cache.get(file);
  };
  const cloneOf = async (file) => (await load(file)).clone(true);
  const tags = createTags(T);   // prices drawn on the blank price tags (scene/tags.js)
  const manifest = await (await fetch(base + 'manifest.json')).json();
  const items = new Map(manifest.items.map((i) => [i.id, i]));
  // A retired id (catalog revision 2) an account still owns draws its stand-in art: the five patterns live in the bundle.
  const artOf = (id) => items.get(id) || items.get(manifest.retired?.[id]);
  // Hair heads rest on their full model: their LOD1 faces collapse (owner, 2026-10-30), and nine heads stay ~42k tris.
  const restFile = (id, meta) => (id.startsWith('rain.hair.') ? meta.file : meta.lod1 || meta.file);
  // The OWNED stamp hangs from the tag hinge like the name tag does.
  const stampOf = async () => { const s = await cloneOf('props/owned_stamp.glb'); s.position.y = STAMP_DROP; return s; };
  const [counter, shelfProto] = await Promise.all([load(manifest.scene.file), load(manifest.shelf.file)]);
  scene.add(counter);
  counter.updateMatrixWorld(true);
  const node = (root, name) => root.getObjectByName(name);
  const worldPos = (o) => o.getWorldPosition(new T.Vector3());

  // Camera poses from the empties: eye, look point on the shelf plane, fov box.
  const planeZ = node(counter, 'bay_0').position.z;
  const poseOf = (name, frame) => {
    const e = node(counter, name), q = new T.Quaternion(), p = new T.Vector3();
    e.matrixWorld.decompose(p, q, new T.Vector3());
    const eye = p.toArray(), look = lookPoint(eye, forwardOf(emptyToCameraQuat(q.toArray())), planeZ);
    return { eye, look, frame, dist: Math.hypot(look[0] - eye[0], look[1] - eye[1], look[2] - eye[2]) };
  };
  const wide = poseOf('cam_wide', FRAME.wide), bayPose = [0, 1, 2, 3].map((i) => poseOf('cam_bay_' + i, FRAME.bay));

  const amp = () => motionAmp(motion);
  const cam = { eye: new T.Vector3(...wide.eye), look: new T.Vector3(...wide.look), fov: 30, dip: 0, jumping: false,
    pose: wide, eyeShift: [0, 0, 0], aim: new T.Vector3(...wide.look), bay: -1, item: null, bayTop: 0, off: 0 };
  let opened = 0, t = 0;

  // Shelves on the counter now, by bay: { group, slots: Map slot -> { holder, lod, full, id } }.
  let shelves = new Map(), focused = null;
  const hooks = { onShelvesSwap: null, onFrame: null };

  async function buildShelf(bay, places) {
    const group = shelfProto.clone(true);
    group.position.copy(worldPos(node(counter, 'bay_' + bay)));
    group.userData.bay = bay;
    const slots = new Map();
    await Promise.all(places.map(async (p) => {
      const meta = artOf(p.id), anchor = node(group, p.slotNode);
      const holder = new T.Group();
      holder.userData.pick = { bay, slot: p.slot };
      anchor.add(holder);
      const entry = { holder, lod: null, full: null, id: p.id, meta, tagNode: node(group, p.tagNode) };
      slots.set(p.slot, entry);
      const prop = p.tag && entry.tagNode && await (p.tag === 'owned' ? stampOf() : cloneOf('props/price_tag.glb'));
      if (prop) { entry.tagNode.add(prop); if (p.tag === 'price') tags.attach(prop, p.row); }
      if (!meta) { console.warn('prizecounter: no art for', p.id); return; }
      entry.lod = await cloneOf(restFile(p.id, meta));
      holder.add(entry.lod);
    }));
    return { group, slots };
  }

  // `wanted` is the model's last focus. While a tab is still loading, focus() only records it (the old shelves must
  // not take it), and showTab applies it once the new shelves exist.
  let swapSeq = 0, swapping = false, wanted = null;
  async function showTab(tabId, tabShelves) {
    const seq = ++swapSeq, places = layoutTab(tabShelves);
    swapping = true;
    let built;
    try { built = await Promise.all(usedBays(tabShelves).map((b) => buildShelf(b, places.filter((p) => p.bay === b)))); }
    catch (e) { if (seq === swapSeq) swapping = false; throw e; }
    if (seq !== swapSeq) { built.forEach((s) => disposeShelf(s)); return; }     // a newer tab won the race
    swapping = false;
    const outgoing = [...shelves.values()];
    clearFocus();
    shelves = new Map(built.map((s) => [s.group.userData.bay, s]));
    built.forEach((s) => scene.add(s.group));
    if (wanted) focus(wanted.bay, wanted.slot);
    if (!opened) opened = t || 1e-6;
    const leave = () => outgoing.forEach((s) => { scene.remove(s.group); disposeShelf(s); });
    const wait = hooks.onShelvesSwap?.({ tabId, outgoing: outgoing.map((s) => s.group), incoming: built.map((s) => s.group) });
    if (wait && typeof wait.then === 'function') wait.then(leave, leave); else leave();
  }
  // A receipt landed: that item's price tag becomes the OWNED stamp in place (no shelf swap).
  async function stamp(itemId) {
    const hits = [...shelves.values()].flatMap((s) => [...s.slots.values()]).filter((e) => e.id === itemId && e.tagNode);
    for (const e of hits) { tags.drop(e.tagNode); e.tagNode.clear(); e.tagNode.add(await stampOf()); }
  }
  // Clones share geometry and materials with the cache, so a shelf leaving only drops its nodes.
  const disposeShelf = (s) => { tags.drop(s.group); s.group.removeFromParent(); };

  // Focus feedback: an ink loop drawn on the back panel behind the item, plus the bob.
  const ring = makeRing();
  ring.visible = false;
  scene.add(ring);

  function clearFocus() {
    if (!focused) return;
    const e = focused;
    e.holder.position.y = 0; e.holder.rotation.y = 0;
    if (e.full) { e.full.removeFromParent(); e.full = null; }
    if (e.lod) e.lod.visible = true;
    focused = null; ring.visible = false;
  }

  function focus(bay, slot) {
    wanted = bay == null ? null : { bay, slot };
    if (swapping) return false;
    const s = shelves.get(bay), j = slotIndex(slot), e = s?.slots.get(j);
    if (!e) return false;
    if (focused !== e) {
      clearFocus();
      focused = e;
      if (e.meta?.file) cloneOf(e.meta.file).then((full) => {
        if (focused !== e) return;
        e.full = full; e.holder.add(full); if (e.lod) e.lod.visible = false;
      }, () => {});
      fitRing(e);
    }
    // The middle of the slot box, not the base. The frame loop turns it into the look (bayView), so a resize reframes.
    aimAt(bay, worldPos(e.holder).add(new T.Vector3(0, 0.39, 0)).toArray());
    return true;
  }
  function aimAt(bay, item) {
    if (cam.bay !== bay) { if (cam.bay >= 0 && amp() > 0) cam.jumping = true; cam.bay = bay; }
    cam.pose = bayPose[bay];
    cam.item = item;
    cam.bayTop = node(counter, 'bay_' + bay).getWorldPosition(new T.Vector3()).y;
  }
  // Frame a bay with nothing focused (the signed-out window display on a phone).
  function frameBay(bay) {
    if (!bayPose[bay]) return false;
    wanted = null; clearFocus();
    aimAt(bay, bayPose[bay].look);
    return true;
  }

  function fitRing(e) {
    const box = new T.Box3().setFromObject(e.lod || e.holder);
    const size = box.isEmpty() ? new T.Vector3(0.6, 0.7, 0.4) : box.getSize(new T.Vector3());
    const c = worldPos(e.holder);
    ring.scale.set(Math.max(0.5, size.x + 0.2) / 2, Math.max(0.55, size.y + 0.16) / 2, 1);
    ring.position.set(c.x, c.y + Math.max(0.55, size.y + 0.16) / 2 - 0.05, c.z - 0.24);
    ring.visible = true;
  }

  // Where a slot's item sits on screen, in CSS px from the canvas's top left (the HUD flies sparkles there), or null.
  function screenPos(bay, slot) {
    const e = shelves.get(bay)?.slots.get(slotIndex(slot));
    if (!e) return null;
    const v = worldPos(e.holder).add(new T.Vector3(0, 0.39, 0)).project(camera);
    return { x: ((v.x + 1) / 2) * canvas.clientWidth, y: ((1 - v.y) / 2) * canvas.clientHeight };
  }

  // ---- input -> intents
  const listeners = new Set();
  const emit = (i) => { if (i) listeners.forEach((cb) => { try { cb(i); } catch (err) { console.error(err); } }); };
  // A focused HUD control keeps its own Enter and Space; the shelves get everything else.
  const typing = (el) => el && (/^(INPUT|TEXTAREA|SELECT|BUTTON)$/.test(el.tagName) || el.isContentEditable);
  const onKey = (ev) => {
    if (ev.ctrlKey || ev.metaKey || ev.altKey || typing(ev.target)) return;
    if (!keyIntent({ code: ev.code, key: ev.key })) return;
    ev.preventDefault();
    emit(keyIntent(ev));
  };
  window.addEventListener('keydown', onKey);

  const ray = new T.Raycaster(), ndc = new T.Vector2();
  let down = null;
  const onDown = (ev) => { down = { x: ev.clientX, y: ev.clientY, id: ev.pointerId, touch: ev.pointerType !== 'mouse' }; };
  const onUp = (ev) => {
    if (!down || down.id !== ev.pointerId) return;
    const dx = ev.clientX - down.x, dy = ev.clientY - down.y, wasTouch = down.touch;
    down = null;
    const swipe = swipeIntent(dx, dy);
    if (swipe) { if (wasTouch) emit(swipe); return; }
    const r = canvas.getBoundingClientRect();
    ndc.set(((ev.clientX - r.left) / r.width) * 2 - 1, -((ev.clientY - r.top) / r.height) * 2 + 1);
    ray.setFromCamera(ndc, camera);
    for (const hit of ray.intersectObjects([...shelves.values()].map((s) => s.group), true)) {
      let o = hit.object;
      while (o && !o.userData.pick) o = o.parent;
      if (o) return emit({ type: 'pick', ...o.userData.pick });
    }
  };
  canvas.addEventListener('pointerdown', onDown);
  canvas.addEventListener('pointerup', onUp);
  canvas.style.touchAction = 'none';

  const padHeld = new Set();
  let stickHeld = null, stickAt = 0;
  function pollPads() {
    const pads = navigator.getGamepads ? navigator.getGamepads() : [];
    const now = new Set();
    for (const p of pads) {
      if (!p) continue;
      p.buttons.forEach((b, i) => { if (b.pressed && padIntent(i)) now.add(i); });
      const s = stickIntent(p.axes[0] || 0, p.axes[1] || 0);
      if (s && (s.type !== stickHeld || t - stickAt > 0.32)) { emit(s); stickAt = t; }
      stickHeld = s ? s.type : null;
    }
    now.forEach((i) => { if (!padHeld.has(i)) emit(padIntent(i)); });
    padHeld.clear(); now.forEach((i) => padHeld.add(i));
  }

  // ---- frame
  let viewW = 1, viewH = 1, insets = { top: 0, bottom: 0 };
  function resize() {
    const w = canvas.clientWidth || canvas.width, h = canvas.clientHeight || canvas.height;
    viewW = Math.max(1, w); viewH = Math.max(1, h);
    renderer.setSize(w, h, false);
    camera.aspect = w / Math.max(1, h);
    camera.updateProjectionMatrix();
  }
  resize();

  let last = performance.now(), raf = 0, alive = true;
  function frame(now) {
    if (!alive) return;
    raf = requestAnimationFrame(frame);
    const dt = Math.min(0.1, (now - last) / 1000); last = now; t += dt;
    pollPads();
    const a = amp(), holding = !opened || t - opened < WIDE_HOLD;
    const pose = holding ? wide : cam.pose;
    const view = holding || pose === wide || !cam.item ? null : bayView(pose, cam.item, cam.bayTop, camera.aspect, a);
    const aim = view ? cam.aim.set(...view.look) : cam.aim.set(...wide.look);
    const shift = view ? view.eyeShift : [0, 0, 0];
    const eyeT = new T.Vector3(pose.eye[0] + shift[0], pose.eye[1] + shift[1], pose.eye[2] + shift[2]);
    // A phone keeps the HUD's top and bottom bands off the shelf; a landscape screen (share 0) frames as before.
    const share = view ? view.share : 0, box = view ? view.frame : pose.frame;
    const fit = bandFit(camera.aspect, box[0], box[1], pose.dist, viewH, insets.top * share, insets.bottom * share);
    const fovT = fit.fov;
    if (a === 0) { cam.eye.copy(eyeT); cam.look.copy(aim); cam.fov = fovT; cam.dip = 0; cam.off = fit.offsetY; }
    else {
      const k = damp(dt, 3.2), left = cam.look.distanceTo(aim);
      if (cam.jumping && left < 0.12) cam.jumping = false;
      const dipT = cam.jumping ? dipFor(left, a) : 0;
      cam.dip += (dipT - cam.dip) * damp(dt, 6);
      cam.eye.lerp(eyeT, k); cam.look.lerp(aim, damp(dt, 2.6)); cam.fov += (fovT - cam.fov) * k;
      cam.off += (fit.offsetY - cam.off) * k;
    }
    const back = new T.Vector3().subVectors(cam.eye, cam.look).normalize().multiplyScalar(cam.dip);
    camera.position.copy(cam.eye).add(back);
    camera.fov = cam.fov;
    if (Math.abs(cam.off) > 0.25) camera.setViewOffset(viewW, viewH, 0, cam.off, viewW, viewH);
    else if (camera.view && camera.view.enabled) camera.clearViewOffset();
    camera.updateProjectionMatrix();
    camera.lookAt(cam.look);
    if (focused) {
      focused.holder.position.y = bob(t, a);
      focused.holder.rotation.y = Math.sin(t * 0.9) * 0.12 * a;
      ring.material.opacity = 0.85 + 0.15 * Math.sin(t * 3) * a;
    }
    hooks.onFrame?.(dt, t);
    renderer.render(scene, camera);
  }
  raf = requestAnimationFrame(frame);

  function dispose() {
    alive = false; cancelAnimationFrame(raf);
    window.removeEventListener('keydown', onKey);
    canvas.removeEventListener('pointerdown', onDown);
    canvas.removeEventListener('pointerup', onUp);
    listeners.clear();
    scene.traverse((o) => { o.geometry?.dispose(); [].concat(o.material || []).forEach((m) => { for (const v of Object.values(m)) if (v?.isTexture) v.dispose(); m.dispose(); }); });
    scene.environment?.dispose();
    tags.dispose();
    renderer.dispose();
  }

  return {
    showTab, focus, frameBay, stamp, resize, dispose, hooks, screenPos, setWallet: tags.setWallet,
    /** The HUD bands (px) a portrait view keeps the shelf out of. */
    setInsets(i) { insets = { top: Math.max(0, +i?.top || 0), bottom: Math.max(0, +i?.bottom || 0) }; },
    items: manifest.items.map((i) => ({ id: i.id, family: i.family })),
    onIntent(cb) { listeners.add(cb); return () => listeners.delete(cb); },
    setMotion(m) { motion = m; },
    parts: { renderer, scene, camera, counter, shelves: () => shelves, dust: (bay) => ['l', 'c', 'r'].map((s) => shelves.get(bay) && node(shelves.get(bay).group, 'dust_' + s)) },
  };
}

// A hand-drawn loop (not a perfect circle): a closed wobbly curve through a thin tube, unit radius, in the XY plane.
function makeRing() {
  const pts = [];
  for (let i = 0; i < 48; i++) {
    const a = (i / 48) * Math.PI * 2, r = 1 + 0.035 * Math.sin(a * 3 + 0.6) + 0.02 * Math.cos(a * 5);
    pts.push(new T.Vector3(Math.cos(a) * r, Math.sin(a) * r, 0));
  }
  const geo = new T.TubeGeometry(new T.CatmullRomCurve3(pts, true), 96, 0.05, 6, true);
  const mat = new T.MeshBasicMaterial({ color: PINK, transparent: true, opacity: 1, toneMapped: false, depthWrite: false });
  const m = new T.Mesh(geo, mat);
  m.renderOrder = 2;
  return m;
}
