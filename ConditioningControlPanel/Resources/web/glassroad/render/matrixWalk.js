/* The course scene's per-frame matrix walk (perf, 2026-10-26).
 *
 * r186 runs scene.updateMatrixWorld() at the top of EVERY renderer.render() call: the post chain's scene
 * pass, the sun's shadow pass nested inside it, each water probe face and the hit cam inset. Stock three
 * composes every auto-updating node and multiplies every world matrix on each of those calls, although a
 * course of ~4300 nodes moves about 200 of them in a frame and two thirds sit in hidden pools (item
 * visuals, cue props, marquees). Firefox pays about twice Edge's cost for that walk.
 *
 * installMatrixWalk(scene, three) swaps in a walk that leaves the picture unchanged:
 *  - a node is recomposed only when its position/quaternion/scale or its local matrix differ from what
 *    the walk last wrote (so an outside compose with other values, or a direct .matrix write, is still seen);
 *    its world matrix is recomputed only when it was recomposed, flagged (matrixWorldNeedsUpdate), moved
 *    to another parent, or its parent's world changed in this walk;
 *  - a hidden subtree (visible === false) is not walked; it is marked stale and recomputed whole on the first
 *    walk that finds it visible again. Bones, lights and LOD levels (LOD.update shows a level during the
 *    render, after the walk) are always walked;
 *  - a walk requested by a render nested inside a render of the same scene (the shadow pass, a reflector's
 *    capture) is skipped: nothing moves between the outer walk and the nested pass;
 *  - nodes with their own updateMatrixWorld (cameras, skinned meshes) keep three's own method for their subtree;
 *  - updateMatrixWorld(true) (the warm paths) stays stock: every node, hidden ones included, recomputed.
 * refreshMatrices(root) runs the same walk over one subtree for per-frame code that needs world matrices
 * before the render (it replaces chains of updateWorldMatrix(true, false) per node).
 */
import { Object3D, Matrix4 } from 'three/webgpu';

const STOCK = Object3D.prototype.updateMatrixWorld;
const KEY = '_glassroadMw';               // per node: one WalkRecord (a single property read per visited node)
/** The walk's state for one node: c = [px,py,pz,qx,qy,qz,qw,sx,sy,sz, matrix 16] it last wrote, the parent it
 * last saw (undefined = never walked) and whether a hidden walk skipped it. One class, so reads stay monomorphic. */
class WalkRecord {
  constructor() { this.c = new Float64Array(26); this.c[0] = NaN; this.parent = undefined; this.stale = false; }
}
const TMP = new Matrix4();
const stats = { walks: 0, nestedSkips: 0, visits: 0, composes: 0, worlds: 0, hiddenSkips: 0 };

/** The walk's counters since the page loaded (dev checks and tests). */
export function matrixWalkStats() { return { ...stats }; }

function recordOf(o) {
  let r = o[KEY];
  if (r === undefined) { r = new WalkRecord(); o[KEY] = r; }
  return r;
}

/** True when the local matrix must be rebuilt (auto-updating node) or was changed from outside. */
function localChanged(o, c) {
  const m = o.matrix.elements;
  if (o.matrixAutoUpdate) {
    const p = o.position, q = o.quaternion, s = o.scale;
    if (c[0] !== p.x || c[1] !== p.y || c[2] !== p.z || c[3] !== q._x || c[4] !== q._y || c[5] !== q._z || c[6] !== q._w
      || c[7] !== s.x || c[8] !== s.y || c[9] !== s.z) {
      o.matrix.compose(p, q, s); stats.composes++;
      c[0] = p.x; c[1] = p.y; c[2] = p.z; c[3] = q._x; c[4] = q._y; c[5] = q._z; c[6] = q._w; c[7] = s.x; c[8] = s.y; c[9] = s.z;
      for (let i = 0; i < 16; i++) c[10 + i] = m[i];
      return true;
    }
  }
  for (let i = 0; i < 16; i++) {
    if (c[10 + i] !== m[i]) {
      if (o.matrixAutoUpdate) { o.matrix.compose(o.position, o.quaternion, o.scale); stats.composes++; }
      for (let k = 0; k < 16; k++) c[10 + k] = m[k];
      return true;
    }
  }
  return false;
}

function alwaysWalked(o) {
  return o.isBone === true || o.isLight === true || (o.parent !== null && o.parent.isLOD === true);
}

function visit(o, moved, all) {
  if (o.updateMatrixWorld !== STOCK) { o.updateMatrixWorld(moved); return; }   // cameras, skinned meshes: their own rule
  if (!all && o.visible === false && !alwaysWalked(o)) { recordOf(o).stale = true; stats.hiddenSkips++; return; }
  walkFrom(o, moved, all, false);
}

function walkFrom(o, moved, all, checkWorld) {
  stats.visits++;
  const r = recordOf(o);
  let dirty = localChanged(o, r.c) || moved || o.matrixWorldNeedsUpdate;
  if (r.stale === true) { r.stale = false; dirty = true; }
  if (r.parent !== o.parent) { r.parent = o.parent; dirty = true; }
  if (!dirty && checkWorld && o.parent !== null) {
    // a subtree root: its parent may have moved since the last walk without this walk seeing it
    TMP.multiplyMatrices(o.parent.matrixWorld, o.matrix);
    dirty = !TMP.equals(o.matrixWorld);
  }
  if (dirty) {
    if (o.matrixWorldAutoUpdate === true) {
      if (o.parent === null) o.matrixWorld.copy(o.matrix); else o.matrixWorld.multiplyMatrices(o.parent.matrixWorld, o.matrix);
      stats.worlds++;
    }
    o.matrixWorldNeedsUpdate = false;
  }
  const children = o.children;
  for (let i = 0, n = children.length; i < n; i++) visit(children[i], dirty, all);
}

/** World matrices of `root` and its subtree, current for this moment (the root follows its parent's present
 * world). hidden: true also walks hidden nodes (instancing sources that are never drawn themselves). */
export function refreshMatrices(root, { hidden = false } = {}) {
  if (!root) return;
  if (root.updateMatrixWorld !== STOCK) { root.updateMatrixWorld(); return; }
  walkFrom(root, false, hidden, true);
}

/** Swap the scene's per-render matrix walk; returns { uninstall }. `three` is the WebGPURenderer (nested-pass skip). */
export function installMatrixWalk(scene, three = null) {
  if (!scene || scene.updateMatrixWorld !== STOCK) return { uninstall() {} };
  let outerDepth = Infinity;
  const before = scene.onBeforeRender, after = scene.onAfterRender;
  let resetQueued = false;
  const reset = () => { resetQueued = false; outerDepth = Infinity; };
  scene.onBeforeRender = function (renderer, ...rest) {
    const d = renderer && typeof renderer._callDepth === 'number' ? renderer._callDepth : Infinity;
    if (d < outerDepth) outerDepth = d;
    // a render is synchronous: whatever happens inside it (a throw included), the next task walks again
    if (!resetQueued) { resetQueued = true; queueMicrotask(reset); }
    return before.call(this, renderer, ...rest);
  };
  scene.onAfterRender = function (renderer, ...rest) {
    const d = renderer && typeof renderer._callDepth === 'number' ? renderer._callDepth : Infinity;
    // r186 lowers _callDepth before onAfterRender: the outer pass ends when its own depth is gone
    if (d < outerDepth) outerDepth = Infinity;
    return after.call(this, renderer, ...rest);
  };
  scene.updateMatrixWorld = function (force) {
    if (force) { outerDepth = Infinity; return STOCK.call(this, true); }
    // a pass nested inside this scene's own render (shadow map, reflector capture): the outer walk holds
    if (three && typeof three._callDepth === 'number' && three._callDepth > outerDepth) { stats.nestedSkips++; return; }
    stats.walks++;
    walkFrom(this, false, false, false);
  };
  return {
    uninstall() {
      delete scene.updateMatrixWorld;
      scene.onBeforeRender = before; scene.onAfterRender = after;
    },
  };
}
