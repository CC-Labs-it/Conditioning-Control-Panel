/* Display-only web pictures stay behind the game canvas. Zero-alpha world faces
 * punch depth-tested windows through it, so scenery still occludes the pictures.
 * These images never become textures, canvas pixels or server-side copies. */
import { Group, InstancedMesh, PlaneGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, NoBlending } from 'three/webgpu';
import { vec3, float } from 'three/tsl';

const ZERO = new Matrix4().makeScale(0, 0, 0);

/** CSS homogeneous transform for a world rectangle. No per-pixel readback. */
export function panelTransform(slot, camera, width, height, out = new Matrix4()) {
  const { C, X, Y, Z, w, h } = slot;
  out.set(X[0] * w, -Y[0] * h, Z[0], C[0] - X[0] * w / 2 + Y[0] * h / 2,
    X[1] * w, -Y[1] * h, Z[1], C[1] - X[1] * w / 2 + Y[1] * h / 2,
    X[2] * w, -Y[2] * h, Z[2], C[2] - X[2] * w / 2 + Y[2] * h / 2, 0, 0, 0, 1);
  out.premultiply(camera.matrixWorldInverse).premultiply(camera.projectionMatrix);
  const e = out.elements;
  for (const k of [0, 4, 8, 12]) {
    e[k] = (e[k] + e[k + 3]) * width / 2;
    e[k + 1] = (-e[k + 1] + e[k + 3]) * height / 2;
    e[k + 2] = 0;
  }
  e[10] = 1;
  return out;
}

export function createWebMediaScreens({ scene, slots, canvas, isVisible = () => true, document: doc = globalThis.document } = {}) {
  const group = new Group(); group.name = 'web-media-windows';
  const geometry = new PlaneGeometry(1, 1);
  // Opaque ordering and depth writes matter: transparent blending would retain
  // the scene alpha and could show the wrong panel through a nearer cabinet.
  const material = new MeshBasicNodeMaterial({ blending: NoBlending, depthWrite: true });
  material.colorNode = vec3(.11, .07, .12); material.opacityNode = float(0);
  material.polygonOffset = true; material.polygonOffsetFactor = -2; material.polygonOffsetUnits = -4;
  const windows = new InstancedMesh(geometry, material, Math.max(1, slots.length));
  windows.frustumCulled = false; group.add(windows); scene?.add(group);
  for (let i = 0; i < windows.count; i++) windows.setMatrixAt(i, ZERO);
  let root = null, feed = null, age = 0, lastTurn = -1;
  const rows = slots.map(slot => ({ slot, element: null, source: null }));
  const matrix = new Matrix4(), eye = new Vector3(), centre = new Vector3(), axis = new Vector3(), clip = new Vector3();
  const bx = new Vector3(), by = new Vector3(), bz = new Vector3();
  const previousStyle = canvas ? { position: canvas.style.position, zIndex: canvas.style.zIndex } : null;
  function ensureRoot() {
    if (root || !doc || !canvas) return;
    root = doc.createElement('div'); root.setAttribute('aria-hidden', 'true'); root.className = 'gr-web-media';
    root.style.cssText = 'position:fixed;inset:0;overflow:hidden;pointer-events:none;z-index:0;background:#0b0a12';
    canvas.style.position = 'relative'; canvas.style.zIndex = '1';
    canvas.parentNode.insertBefore(root, canvas);
    for (const row of rows) {
      const el = row.element = doc.createElement('div');
      el.style.cssText = 'position:absolute;left:0;top:0;width:256px;height:' + (256 * row.slot.h / row.slot.w) + 'px;transform-origin:0 0;overflow:hidden;display:none';
      const img = doc.createElement('img');
      img.style.cssText = 'width:100%;height:100%;object-fit:cover;display:block';
      const label = doc.createElement('span'); label.textContent = 'via Scrolller';
      label.style.cssText = 'position:absolute;left:2%;bottom:1%;font:600 12px/1.2 sans-serif;color:white;text-shadow:0 0 2px black';
      el.append(img, label); root.append(el); row.image = img;
    }
  }
  function hide(row, i) {
    windows.setMatrixAt(i, ZERO);
    if (row.element) row.element.style.display = 'none';
    if (row.source) { row.image.removeAttribute('src'); row.source = null; }
  }
  function update(dt, view = {}) {
    if (!view.paused) age += Math.max(0, dt);
    const camera = view.camera;
    if (!feed || !camera || !canvas || !feed.ready) {
      rows.forEach(hide); windows.instanceMatrix.needsUpdate = true; return;
    }
    ensureRoot();
    if (!root) return;
    const width = canvas.clientWidth, height = canvas.clientHeight, turn = Math.floor(age / 7);
    camera.getWorldPosition(eye);
    for (let i = 0; i < rows.length; i++) {
      const row = rows[i], sl = row.slot;
      centre.set(...sl.C).sub(eye);
      // Near-plane crossing and the back of a cabinet keep their Loom face.
      const facing = centre.dot(axis.set(...sl.Z)) < 0;
      clip.set(...sl.C).applyMatrix4(camera.matrixWorldInverse);
      const nearestZ = clip.z + Math.abs(axis.set(...sl.X).transformDirection(camera.matrixWorldInverse).z) * sl.w / 2
        + Math.abs(axis.set(...sl.Y).transformDirection(camera.matrixWorldInverse).z) * sl.h / 2;
      if (!isVisible(sl) || !facing || nearestZ > -(camera.near || .1) || centre.lengthSq() > 700 * 700) { hide(row, i); continue; }
      const src = feed.sourceFor(i + turn);
      if (!src?.src || !src.complete || !src.naturalWidth) { hide(row, i); continue; }
      if (row.source !== src) { row.source = src; row.image.src = src.src; }
      const el = row.element;
      panelTransform(sl, camera, width, height, matrix);
      const e = matrix.elements;
      for (let j = 0; j < 4; j++) { e[j] /= 256; e[j + 4] /= 256 * sl.h / sl.w; }
      el.style.transform = 'matrix3d(' + matrix.elements.join(',') + ')';
      el.style.zIndex = String(Math.max(1, 100000 - Math.round(centre.length())));
      // Wait for this DOM copy too. The game canvas must never open an empty hole.
      if (!row.image.complete || !row.image.naturalWidth) { windows.setMatrixAt(i, ZERO); el.style.display = 'none'; continue; }
      el.style.display = 'block';
      matrix.makeBasis(bx.set(...sl.X).multiplyScalar(sl.w), by.set(...sl.Y).multiplyScalar(sl.h), bz.set(...sl.Z));
      matrix.setPosition(...sl.C); windows.setMatrixAt(i, matrix);
    }
    lastTurn = turn; windows.instanceMatrix.needsUpdate = true;
  }
  return { object: group, setFeed(value) { feed = value; }, update,
    stats: () => ({ faces: rows.filter(r => r.element?.style.display === 'block').length, turn: lastTurn }),
    dispose() {
      rows.forEach(hide); root?.remove(); root = null; feed = null;
      if (canvas && previousStyle) Object.assign(canvas.style, previousStyle);
      scene?.remove(group); geometry.dispose(); material.dispose();
    } };
}
