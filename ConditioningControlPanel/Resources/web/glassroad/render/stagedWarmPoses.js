/* render/stagedWarmPoses.js - the phone tier's staged shader preparation, the split (plan: render/stagedWarmPlan.js,
 * runner: render/stagedWarm.js).
 *
 * stagePlan() runs inside the boot warm (render/renderer.js warmAll, every object shown, the saved runtime state at
 * hand): it gives each renderable a need time from sampled camera poses and splits the scene into what the cover
 * prepares now and deferred groups.
 */
import { Frustum, Matrix4, Vector3 } from 'three/webgpu';
import { STAGE, planStages } from './stagedWarmPlan.js';
import { sampleCourseIntro } from './courseIntroMath.js';

const M = new Matrix4(), F = new Frustum(), V = new Vector3();

/** Intro poses (t = view seconds), then a chase camera along the lap at STAGE.chaseMps, plus a rear look at the grid. */
export function stagePoses(track, shots, { introS = 10.6, goS = 3, gentle = false } = {}) {
  const poses = [];
  for (let t = 0; t <= introS + 1e-6; t += STAGE.introStepS) {
    const p = sampleCourseIntro(track, shots, t, gentle);
    if (p) poses.push({ t, eye: p.eye, look: p.look, fov: p.fov });
  }
  const spawn = track.spawnPose(0), t0 = introS + goS, len = track.lengthM || 0;
  const at = (f, lat, up) => f.p.map((v, i) => v + f.b[i] * lat + f.n[i] * up);
  const back = track.frameAt(spawn.s - 30), grid = track.frameAt(spawn.s);
  poses.push({ t: t0, eye: at(grid, 0, 3), look: at(back, 0, 1), fov: 70 });   // the rear view on the grid
  for (let d = 0; d <= len; d += STAGE.chaseStepM) {
    const t = t0 + d / STAGE.chaseMps;
    for (const lat of [-0.3, 0.3]) {
      const f = track.frameAt(spawn.s + d - 7), ahead = track.frameAt(spawn.s + d + 18), w = (f.w || 12) * lat;
      poses.push({ t, eye: at(f, w, 3), look: at(ahead, w, 1), fov: 80 });
    }
  }
  return poses;
}

function chainVisible(o, root, vis) {
  for (let p = o; p && p !== root; p = p.parent) if (vis.get(p) === false) return false;
  return true;
}

/**
 * The split, inside warmAll. items: the renderables; saved: [[object, visible, frustumCulled, count]] as found;
 * hiddenMats: materials that were invisible. Pieces with no frustum test (never culled, hidden, empty pools, invisible
 * materials) are pinned to the cover: when they appear is not knowable.
 */
export function stagePlan({ items, saved, hiddenMats, scene, camera, poses, pinRoot = null, perGroup = STAGE.perGroup }) {
  const vis = new Map(), culled = new Map(), counts = new Map();
  for (const [o, v, f, n] of saved) { vis.set(o, v); culled.set(o, f); counts.set(o, n); }
  const cam = camera.clone();
  const entries = items.map((item) => {
    const mats = [].concat(item.material || []).filter(Boolean);
    let extra = false;
    for (let q = item; q; q = q.parent) if (q === pinRoot) { extra = true; break; }
    const pinned = extra || item.receiveShadow || culled.get(item) === false || !chainVisible(item, scene, vis) || counts.get(item) === 0 ||
      mats.some((m) => hiddenMats.has(m));
    return { item, mats: mats.map((m) => m.id ?? m), pinned, needT: pinned ? 0 : Infinity };
  });
  const open = entries.filter((e) => !e.pinned);
  for (const pose of poses) {
    if (!open.length) break;
    cam.position.fromArray(pose.eye); cam.up.set(0, 1, 0); cam.lookAt(V.fromArray(pose.look));
    cam.fov = pose.fov; cam.updateProjectionMatrix(); cam.updateMatrixWorld(true);
    F.setFromProjectionMatrix(M.multiplyMatrices(cam.projectionMatrix, cam.matrixWorldInverse), cam.coordinateSystem);
    for (let i = open.length - 1; i >= 0; i--) {
      const e = open[i];
      let hit = false;
      try { hit = e.item.isSprite ? F.intersectsSprite(e.item) : F.intersectsObject(e.item); } catch { hit = true; }
      if (hit) { e.needT = pose.t; open.splice(i, 1); }
    }
  }
  // never in a sampled view (a branch, a piece behind every pose): the time of the nearest pose eye
  for (const e of open) {
    const c = e.item.getWorldPosition(V.set(0, 0, 0));
    let best = Infinity, t = poses.length ? poses[poses.length - 1].t : 0;
    for (const p of poses) { const d = (c.x - p.eye[0]) ** 2 + (c.y - p.eye[1]) ** 2 + (c.z - p.eye[2]) ** 2; if (d < best) { best = d; t = p.t; } }
    e.needT = t;
  }
  const plan = planStages(entries, { perGroup });
  plan.stats = { items: items.length, now: plan.now.length, deferred: plan.groups.reduce((n, g) => n + g.items.length, 0), groups: plan.groups.length,
    lastNeedT: plan.groups.length ? Math.round(plan.groups[plan.groups.length - 1].needT * 10) / 10 : 0 };
  return plan;
}
