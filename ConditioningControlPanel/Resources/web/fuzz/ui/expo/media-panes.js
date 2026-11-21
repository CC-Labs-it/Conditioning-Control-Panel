import * as THREE from '../../../vendor/three/three.module.min.js';
import { createMedia } from '../documentary/media.js';

const UNIT = 180;
const CSS = `
.expo-media {position:absolute;inset:0;overflow:hidden;pointer-events:none;perspective-origin:50% 50%}
.expo-media-camera {position:absolute;left:50%;top:50%;width:0;height:0;transform-style:preserve-3d}
.expo-media-face {position:absolute;left:0;top:0;transform-origin:0 0;backface-visibility:hidden;overflow:hidden;pointer-events:none}
.expo-media [hidden] {display:none!important}
.expo-media .td-run-media {position:absolute;inset:0;overflow:hidden;background:#17241d}
.expo-media .td-run-picture {position:absolute;inset:0;width:100%;height:100%;object-fit:contain;background:#17241d}
.expo-media .td-run-credit {position:absolute;bottom:0;left:0;right:0;background:#f5faeced;color:#183d29;padding:4px 8px;text-align:right;font:16px/1.2 Arial,sans-serif}
`;
// Match the museum: CSS has downward-positive Y and pixel-based translations.
const cssMatrix = matrix => matrix.elements.map((value, index) => {
  const row = index % 4, column = Math.floor(index / 4);
  return value * (row === 1 ? -1 : 1) * (column === 1 ? -1 : 1) * (column === 3 && row < 3 ? UNIT : 1);
}).join(',');

/** Display-only panes share the museum's source consent, cache leases, motion cap and credit. */
export function createExpoMedia(ctx, root, camera, dome, { lite = false } = {}) {
  const doc = root.ownerDocument, media = createMedia(ctx, { lite });
  const viewport = doc.createElement('div'), view = doc.createElement('div');
  viewport.className = 'expo-media'; view.className = 'expo-media-camera'; viewport.append(view);
  root.insertBefore(viewport, root.children[1] || null); ctx.css(CSS);
  const point = new THREE.Vector3(), normal = new THREE.Vector3(), toEye = new THREE.Vector3();
  const matrix = new THREE.Matrix4(), offset = new THREE.Matrix4(), eye = new THREE.Vector3();
  const reverse = new THREE.Matrix4().makeRotationY(Math.PI);
  let disposed = false, held = false, mediaState = '';
  const pauseMedia = on => {
    const state = `${!!on}:${!!ctx.gentle}:${!!ctx.reduced}`;
    if (state !== mediaState) { mediaState = state; media.pause(on); }
  };
  const slots = dome.picturePanes.map((pane, i) => {
    const node = doc.createElement('div'); node.className = 'expo-media-face'; node.hidden = true;
    if (pane.round) {
      node.style.borderRadius = '50%';
      node.style.maskImage = 'radial-gradient(ellipse at 46% 47%, #000 17%, #000c 51%, transparent 72%)';
    }
    node.dataset.expoPicture = String(i); node.style.width = `${pane.width * UNIT}px`;
    node.style.height = `${pane.height * UNIT}px`; view.append(node);
    return { pane, node, unmount: null };
  });
  const visible = object => {
    for (let item = object; item; item = item.parent) if (!item.visible) return false;
    return true;
  };
  function projectedBounds(mesh) {
    const geometry = mesh.geometry; if (!geometry.boundingBox) geometry.computeBoundingBox();
    const box = geometry.boundingBox;
    const result = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity, near: Infinity };
    for (let i = 0; i < 8; i++) {
      point.set(i & 1 ? box.max.x : box.min.x, i & 2 ? box.max.y : box.min.y, i & 4 ? box.max.z : box.min.z);
      point.applyMatrix4(mesh.matrixWorld).applyMatrix4(camera.matrixWorldInverse);
      if (point.z > -camera.near) return null;
      result.near = Math.min(result.near, -point.z); point.applyMatrix4(camera.projectionMatrix);
      result.left = Math.min(result.left, point.x); result.right = Math.max(result.right, point.x);
      result.top = Math.min(result.top, point.y); result.bottom = Math.max(result.bottom, point.y);
    }
    return result;
  }
  function hide(slot, release = false) {
    slot.node.hidden = true;
    if (release) { slot.unmount?.(); slot.unmount = null; }
  }
  function draw() {
    if (disposed) return;
    const active = dome.inside && dome.depth > 0.75;
    viewport.hidden = !active;
    if (!active) { slots.forEach(slot => hide(slot, true)); pauseMedia(true); return; }
    camera.updateMatrixWorld(true); eye.setFromMatrixPosition(camera.matrixWorld);
    const focal = root.clientHeight * 0.5 * camera.projectionMatrix.elements[5];
    viewport.style.perspective = `${focal}px`;
    view.style.transform = `translateZ(${focal}px) matrix3d(${cssMatrix(camera.matrixWorldInverse)})`;
    const blockers = dome.mediaOccluders.filter(({ mesh }) => visible(mesh) && mesh.material.opacity > 0.05)
      .map(blocker => ({ ...blocker, bounds: projectedBounds(blocker.mesh) })).filter(blocker => blocker.bounds);
    for (const slot of slots) {
      const { pane, node } = slot;
      offset.makeTranslation(0, 0, pane.offset); matrix.copy(pane.object.matrixWorld).multiply(offset);
      point.setFromMatrixPosition(matrix); normal.set(0, 0, 1).transformDirection(matrix);
      toEye.copy(eye).sub(point);
      if(pane.twoSided && normal.dot(toEye)<0){
        matrix.copy(pane.object.matrixWorld).multiply(reverse).multiply(offset);
        normal.negate();point.setFromMatrixPosition(matrix);toEye.copy(eye).sub(point);
      }
      if (pane.opacity < 0.01 || !visible(pane.object) || normal.dot(toEye) <= 0) { hide(slot); continue; }
      const bounds = { left: Infinity, top: Infinity, right: -Infinity, bottom: -Infinity, far: 0 };
      let inFront = true;
      for (let i = 0; i < 4; i++) {
        point.set((i & 1 ? 1 : -1) * pane.width / 2, (i & 2 ? 1 : -1) * pane.height / 2, 0);
        point.applyMatrix4(matrix).applyMatrix4(camera.matrixWorldInverse);
        if (point.z > -camera.near) { inFront = false; break; }
        bounds.far = Math.max(bounds.far, -point.z); point.applyMatrix4(camera.projectionMatrix);
        bounds.left = Math.min(bounds.left, point.x); bounds.right = Math.max(bounds.right, point.x);
        bounds.top = Math.min(bounds.top, point.y); bounds.bottom = Math.max(bounds.bottom, point.y);
      }
      // Conservative whole-pane hiding keeps remote pixels behind every foreground fixture.
      const occluded = blockers.some(({ owner, bounds: b }) => owner !== pane.object && b.near < bounds.far - 0.02 &&
        b.left < bounds.right && b.right > bounds.left && b.top < bounds.bottom && b.bottom > bounds.top);
      if (!inFront || occluded || bounds.right < -1 || bounds.left > 1 || bounds.bottom < -1 || bounds.top > 1) { hide(slot); continue; }
      node.hidden = false; node.style.opacity = String(pane.opacity);
      node.style.transform = `matrix3d(${cssMatrix(matrix)}) translate(-50%,-50%)`;
      if (!slot.unmount) slot.unmount = media.mount(node, { id: `expo-picture-${node.dataset.expoPicture}`, kind: 'run-picture' });
    }
    pauseMedia(held || ctx.paused || doc.hidden || !slots.some(slot => !slot.node.hidden));
  }
  return {
    draw,
    pause(on) { held = !!on; pauseMedia(held || ctx.paused || doc.hidden || !dome.inside || !slots.some(slot => !slot.node.hidden)); },
    dispose() { if (disposed) return; disposed = true; slots.forEach(slot => hide(slot, true)); media.dispose(); viewport.remove(); },
  };
}
