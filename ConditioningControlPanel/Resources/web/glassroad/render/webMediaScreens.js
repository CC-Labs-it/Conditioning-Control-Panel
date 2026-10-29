/* Display-only web pictures stay behind the game canvas. Zero-alpha world faces
 * punch depth-tested windows through it, so scenery still occludes the pictures.
 * These images and clips never become textures, canvas pixels or server-side copies.
 *
 * CLIPS (GIF-style Scrolller loops, host/scrolllerFeed.js). A face shows its own muted
 * <video>; only the nearest visible faces play, at most maxPlaying() at once (4, phone 2),
 * the rest show the clip's poster still. Every clip pauses while the race pauses or the
 * tab hides; an off-screen face pauses and lets go of its decoder after OFF_KEEP_S (and at
 * most maxPlaying() paused clips are held at all, the longest gone freed first).
 * Gentle Visuals and OS reduced motion slow clips to GENTLE_RATE, never flicker them. */
import { Group, InstancedMesh, PlaneGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, NoBlending } from 'three/webgpu';
import { vec3, float } from 'three/tsl';
import { isVideo, displayVideo } from '../host/scrolllerFeed.js';

const ZERO = new Matrix4().makeScale(0, 0, 0);
export const OFF_KEEP_S = 5;
export const GENTLE_RATE = .5;
/** Concurrent playing clips by quality tier. */
export const maxPlayingFor = (tier) => tier === 'phone' ? 2 : 4;

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

export function createWebMediaScreens({ scene, slots, canvas, isVisible = () => true, maxPlaying = () => 4, document: doc = globalThis.document } = {}) {
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
  const rows = slots.map(slot => ({ slot, element: null, image: null, video: null, still: '', clip: '', offFor: 0, dist: 0 }));
  const matrix = new Matrix4(), eye = new Vector3(), centre = new Vector3(), axis = new Vector3(), clip = new Vector3();
  const bx = new Vector3(), by = new Vector3(), bz = new Vector3();
  const previousStyle = canvas ? { position: canvas.style.position, zIndex: canvas.style.zIndex } : null;
  let motion = null;
  try { motion = doc?.defaultView?.matchMedia?.('(prefers-reduced-motion: reduce)') || null; } catch { motion = null; }
  const pauseAll = () => { for (const row of rows) if (row.clip) { try { row.video.pause(); } catch { /* gone */ } } };
  const onVisibility = () => { if (doc?.hidden) pauseAll(); };
  doc?.addEventListener?.('visibilitychange', onVisibility);
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
      const video = displayVideo(doc);
      video.style.cssText = 'width:100%;height:100%;object-fit:cover;display:none';
      const label = doc.createElement('span'); label.textContent = 'via Scrolller';
      label.style.cssText = 'position:absolute;left:2%;bottom:1%;font:600 12px/1.2 sans-serif;color:white;text-shadow:0 0 2px black';
      el.append(img, video, label); root.append(el); row.image = img; row.video = video; row.label = label;
    }
  }
  /** Let go of a face's clip: no src, load() frees the decoder. */
  function freeClip(row) {
    if (!row.clip) return;
    try { row.video.pause(); row.video.removeAttribute('src'); row.video.removeAttribute('poster'); row.video.load?.(); } catch { /* gone */ }
    row.clip = '';
  }
  function setStill(row, url) {
    if (row.still === url) return;
    row.still = url;
    if (url) row.image.src = url; else row.image?.removeAttribute('src');
  }
  /** Close a face. `keep` (off-screen for a moment) pauses its clip instead of freeing it. */
  function hide(row, i, keep = false) {
    windows.setMatrixAt(i, ZERO);
    if (row.element) row.element.style.display = 'none';
    if (keep && row.clip) { try { row.video.pause(); } catch { /* gone */ } }
    else freeClip(row);
    setStill(row, '');
  }
  function update(dt, view = {}) {
    if (!view.paused) age += Math.max(0, dt);
    const camera = view.camera;
    if (!feed || !camera || !canvas || !feed.ready) {
      rows.forEach((r, i) => hide(r, i)); windows.instanceMatrix.needsUpdate = true; return;
    }
    ensureRoot();
    if (!root) return;
    // Scrolller pictures keep "via Scrolller"; the player's own files (host/localLibrary.js) carry no credit.
    const credit = typeof feed.credit === 'string' ? feed.credit : 'via Scrolller';
    for (const row of rows) if (row.label && row.label.textContent !== credit) { row.label.textContent = credit; row.label.style.display = credit ? '' : 'none'; }
    const width = canvas.clientWidth, height = canvas.clientHeight, turn = Math.floor(age / 7);
    const hold = !!view.paused || !!doc?.hidden, rate = (view.gentle || motion?.matches) ? GENTLE_RATE : 1;
    camera.getWorldPosition(eye);
    const shown = [];
    for (let i = 0; i < rows.length; i++) {
      const row = rows[i], sl = row.slot;
      centre.set(...sl.C).sub(eye);
      // Near-plane crossing and the back of a cabinet keep their Loom face.
      const facing = centre.dot(axis.set(...sl.Z)) < 0;
      clip.set(...sl.C).applyMatrix4(camera.matrixWorldInverse);
      const nearestZ = clip.z + Math.abs(axis.set(...sl.X).transformDirection(camera.matrixWorldInverse).z) * sl.w / 2
        + Math.abs(axis.set(...sl.Y).transformDirection(camera.matrixWorldInverse).z) * sl.h / 2;
      if (!isVisible(sl) || !facing || nearestZ > -(camera.near || .1) || centre.lengthSq() > 700 * 700) {
        row.offFor += Math.max(0, dt); hide(row, i, row.offFor < OFF_KEEP_S); continue;
      }
      row.offFor = 0; row.dist = centre.length(); shown.push(i);
    }
    // The nearest faces get the decoders.
    shown.sort((a, b) => rows[a].dist - rows[b].dist);
    let playing = 0;
    const cap = Math.max(0, maxPlaying() | 0);
    for (const i of shown) {
      const row = rows[i], sl = row.slot;
      const src = feed.sourceFor(i + turn);
      if (!src?.src) { hide(row, i); continue; }
      let face = null;
      if (isVideo(src) && playing < cap) {
        playing++;
        const v = row.video;
        if (row.clip !== src.src) {
          freeClip(row);
          v.preload = 'auto';
          if (src.poster) v.poster = src.poster;
          v.src = src.src; row.clip = src.src;
        }
        if (v.playbackRate !== rate) { v.defaultPlaybackRate = rate; v.playbackRate = rate; }
        if (hold) { if (!v.paused) v.pause(); } else if (v.paused) { try { v.play()?.catch?.(() => {}); } catch { /* refused */ } }
        // Until its first frame the clip's poster stands in, so a face never blinks open empty.
        setStill(row, v.readyState >= 2 ? '' : (src.poster || ''));
        face = v.readyState >= 2 && !v.error ? 'clip' : null;
      } else {
        freeClip(row);
        setStill(row, isVideo(src) ? (src.poster || '') : (src.complete && src.naturalWidth ? src.src : ''));
      }
      if (!face && row.still && row.image.complete && row.image.naturalWidth) face = 'still';
      const el = row.element;
      // Wait for this DOM copy too. The game canvas must never open an empty hole.
      if (!face) { windows.setMatrixAt(i, ZERO); el.style.display = 'none'; continue; }
      row.video.style.display = face === 'clip' ? 'block' : 'none';
      row.image.style.display = face === 'still' ? 'block' : 'none';
      panelTransform(sl, camera, width, height, matrix);
      const e = matrix.elements;
      for (let j = 0; j < 4; j++) { e[j] /= 256; e[j + 4] /= 256 * sl.h / sl.w; }
      el.style.transform = 'matrix3d(' + matrix.elements.join(',') + ')';
      el.style.zIndex = String(Math.max(1, 100000 - Math.round(row.dist)));
      el.style.display = 'block';
      matrix.makeBasis(bx.set(...sl.X).multiplyScalar(sl.w), by.set(...sl.Y).multiplyScalar(sl.h), bz.set(...sl.Z));
      matrix.setPosition(...sl.C); windows.setMatrixAt(i, matrix);
    }
    // Paused off-screen clips stay a moment, never more than cap of them: the longest gone go first.
    let spare = rows.filter(r => r.clip).length - cap * 2;
    if (spare > 0) for (const r of rows.filter(x => x.clip && x.offFor > 0).sort((a, b) => b.offFor - a.offFor)) { if (spare-- <= 0) break; freeClip(r); }
    lastTurn = turn; windows.instanceMatrix.needsUpdate = true;
  }
  return { object: group, setFeed(value) { feed = value; }, update,
    stats: () => ({ faces: rows.filter(r => r.element?.style.display === 'block').length, turn: lastTurn,
      clips: rows.filter(r => r.clip).length, playing: rows.filter(r => r.clip && r.video && !r.video.paused).length }),
    dispose() {
      rows.forEach((r, i) => hide(r, i)); root?.remove(); root = null; feed = null;
      doc?.removeEventListener?.('visibilitychange', onVisibility);
      if (canvas && previousStyle) Object.assign(canvas.style, previousStyle);
      scene?.remove(group); geometry.dispose(); material.dispose();
    } };
}
