/* render/hitCam.js - the HIT CAM: a small live picture-in-picture of the rival your item just hit.
 *
 * Trigger: item-hit with by = the local racer, someone else the victim, verdict 'hit' (solo and online alike: the
 * net lane emits the same shape). A throw or lob at a homing target (item-fire, target >= 0) opens it early as
 * INCOMING on that target; no hit within hitCam.leadTimeoutS closes it quietly. Only worth it when the hit is
 * not plainly on screen nearby (hitCamMath.js shouldShow: further than hitCam.minDistM, behind you, or outside
 * the chase view). Several victims in one tick: the farthest worth showing. A new hit while open re-punches
 * (same victim) or cuts to the new one; nothing stacks. Off while paused, under a card, outside racing, once
 * you finish, with hitCam.on false or on a tier whose Q.hitCam is 0.
 * Camera: a low 3/4 seat behind and to one side of the victim, damped as an OFFSET from the kart (no lag at
 * speed), slowly orbiting while it spins out; the replay lens creeps in while incoming and crash-zooms at contact,
 * with a small decaying shake. The victim counts as near for kart LOD (focus -> world.update view.focusRacer).
 * Draw: renderer.setInset (render/renderer.js, render/post.js); chrome: hud/hitCamChrome.js. Gentle: no flash,
 * no shake, calmer zoom, no overshoot.
 * update(dt, ctx) after world.update and before renderer.render; dispose() on course teardown.
 */
import { PerspectiveCamera, Vector3, Quaternion } from 'three/webgpu';
import { S } from '../sim/state.js';
import { racerLabels, itemName } from '../hud/format.js';
import { createHitCamChrome } from '../hud/hitCamChrome.js';
import { Q } from './quality.js';
import { hitCamNumbers, hitCamFrame, isPlayerHit, pickVictim, hitWord, hitCamRect, orbitAz, rigOffsets, fovFor, shakeAt, hitCamAllowed } from './hitCamMath.js';

const TILT_DEG = -1.5;

export function createHitCam({ bus, feel, renderer, world, doc = globalThis.document, me = 0 }) {
  const camera = new PerspectiveCamera(44, 16 / 9, 0.1, 6000);
  camera.name = 'hit-cam';
  const chrome = createHitCamChrome(doc, doc && doc.body);
  const hits = [], leads = [];
  let t = 0, s = null, seq = 0, safe = { top: 0, left: 0 }, safeAt = -1;
  const eyeOff = [0, 0, 0], lookOff = [0, 0, 0], upSm = [0, 1, 0];
  const P = new Vector3(), Qk = new Quaternion(), F = new Vector3(), U = new Vector3(), V = new Vector3(), R = new Vector3(), T = new Vector3();
  const stats = { opened: 0, shown: 0, skipped: 0, victim: -1, phase: 'idle' };
  const offs = [
    bus.on('item-hit', (e) => { if (isPlayerHit(e, me)) hits.push(e); }),
    bus.on('item-fire', (e) => {
      if (e && e.racer === me && (e.mode === 'throw' || e.mode === 'lob') && e.target >= 0 && e.target !== me) leads.push(e);
    }),
  ];

  /** Where the victim sits for the player: distance, behind, inside the chase view. */
  function judge(victim, ctx) {
    const r = ctx.racers[victim], pv = ctx.player;
    if (!r || !r.view || r.finished || (r.meta && r.meta.left)) return null;
    const v = r.view;
    const dx = v[S.posX] - pv[S.posX], dy = v[S.posY] - pv[S.posY], dz = v[S.posZ] - pv[S.posZ];
    Qk.set(pv[S.quatX], pv[S.quatY], pv[S.quatZ], pv[S.quatW]);
    F.set(0, 0, -1).applyQuaternion(Qk);
    const behind = dx * F.x + dy * F.y + dz * F.z < 0;
    let onScreen = false;
    const mc = ctx.mainCamera;
    if (mc) {
      T.set(v[S.posX], v[S.posY] + 0.5, v[S.posZ]);
      V.copy(T).applyMatrix4(mc.matrixWorldInverse);
      if (V.z < 0) { T.project(mc); onScreen = Math.abs(T.x) <= 0.92 && Math.abs(T.y) <= 0.92; }
    }
    return { victim, distM: Math.hypot(dx, dy, dz), behind, onScreen };
  }
  function labelOf(victim, ctx) {
    const labels = racerLabels(ctx.racers.map((r, i) => (r && r.meta) || { i, name: '', isBot: i > 0 }));
    return labels[victim] || '';
  }
  function open(victim, kind, ctx, contact) {
    const K = ctx.K, cut = !!s;
    s = { victim, kind, openT: cut ? s.openT : t, contactT: null, closeT: t + K.leadTimeoutS, side: victim % 2 ? 1 : -1,
      name: labelOf(victim, ctx), word: '', snap: true };
    if (contact) punch(K, cut);
    stats.opened++;
  }
  function punch(K, cut) {
    s.contactT = t + (cut || t - s.openT > K.enterS ? 0 : K.contactDelayS);
    s.closeT = s.contactT + K.holdS;
    s.word = hitWord(s.kind, seq++);
  }
  function close() {
    s = null; stats.victim = -1; stats.phase = 'idle';
    renderer.setInset(null); chrome.hide();
  }

  function readSafe() {
    if (!doc || !doc.body || !globalThis.getComputedStyle) return;
    const probe = doc.createElement('div');
    probe.style.cssText = 'position:fixed;visibility:hidden;padding-top:env(safe-area-inset-top);padding-left:env(safe-area-inset-left)';
    doc.body.appendChild(probe);
    const cs = getComputedStyle(probe);
    safe = { top: parseFloat(cs.paddingTop) || 0, left: parseFloat(cs.paddingLeft) || 0 };
    probe.remove();
  }

  /**
   * ctx = { racing, paused, card, finished, gentle, racers (racers.list), player (P.view), mainCamera, viewW, viewH }
   */
  function update(dt, ctx) {
    const K = hitCamNumbers(feel);
    ctx.K = K;
    t += Math.max(0, dt || 0);
    const allowed = hitCamAllowed({ on: K.on, tierScale: Q.hitCam, racing: ctx.racing, paused: ctx.paused, card: ctx.card, finished: ctx.finished });
    if (!allowed) { hits.length = 0; leads.length = 0; if (s) close(); return; }
    // hits this frame: the same victim re-punches, another cuts, a fresh one opens if worth it
    if (hits.length) {
      const cands = [];
      for (const e of hits) { const j = judge(e.victim, ctx); if (j) { j.kind = e.kind; cands.push(j); } }
      hits.length = 0;
      const same = s && cands.find((c) => c.victim === s.victim);
      if (same) { s.kind = same.kind; punch(K, s.contactT !== null); }
      else {
        const pick = pickVictim(cands, K);
        if (pick) { open(pick.victim, pick.kind, ctx, true); s.snap = true; }
        else if (cands.length) stats.skipped++;
      }
    }
    if (leads.length) {
      const e = leads[leads.length - 1];
      leads.length = 0;
      if (!s && K.leadTimeoutS > 0) { const j = judge(e.target, ctx); if (j && pickVictim([j], K)) open(e.target, e.id, ctx, false); }
    }
    if (!s) return;
    const root = world.karts && world.karts[s.victim];
    const vr = ctx.racers[s.victim];
    if (!root || !vr || (vr.meta && vr.meta.left)) { close(); return; }
    const gentle = !!ctx.gentle;
    const f = hitCamFrame(s, t, K, gentle);
    if (f.done) { close(); return; }
    stats.victim = s.victim; stats.phase = f.incoming ? 'incoming' : 'hit';
    if (f.incoming === false && s.contactT !== null && t >= s.contactT && !s.counted) { s.counted = true; stats.shown++; }

    // ---- the rig: offsets from the posed kart, damped; a cut or an open snaps ----
    root.updateMatrixWorld();
    P.setFromMatrixPosition(root.matrixWorld);
    root.getWorldQuaternion(Qk);
    F.set(0, 0, -1).applyQuaternion(Qk); U.set(0, 1, 0).applyQuaternion(Qk);
    const since = s.contactT === null ? 0 : t - s.contactT;
    const want = rigOffsets([F.x, F.y, F.z], [U.x, U.y, U.z], orbitAz(since, s.side, K), K);
    const a = s.snap ? 1 : 1 - Math.exp(-Math.max(0, dt) / Math.max(1e-3, K.followTcS));
    const au = s.snap ? 1 : 1 - Math.exp(-Math.max(0, dt) / 0.35);
    for (let i = 0; i < 3; i++) {
      eyeOff[i] += (want.eye[i] - eyeOff[i]) * a;
      lookOff[i] += (want.look[i] - lookOff[i]) * a;
    }
    // camera up: world up, leaning toward the kart's up only on a steep road (loops, banks)
    const ku = U.y < 0.75 ? [U.x, U.y, U.z] : [0, 1, 0];
    for (let i = 0; i < 3; i++) upSm[i] += (ku[i] - upSm[i]) * au;
    s.snap = false;
    camera.position.set(P.x + eyeOff[0], P.y + eyeOff[1], P.z + eyeOff[2]);
    camera.up.set(upSm[0], upSm[1], upSm[2]).normalize();
    T.set(P.x + lookOff[0], P.y + lookOff[1], P.z + lookOff[2]);
    if (f.shake > 0) {
      const [sx, sy] = shakeAt(since, f.shake, K.shakeHz, s.victim);
      V.subVectors(T, camera.position).normalize();
      R.crossVectors(V, camera.up).normalize();
      T.addScaledVector(R, sx).addScaledVector(camera.up, sy);
    }
    camera.lookAt(T);

    // ---- the rect: seat by viewport, slid and scaled by the timeline ----
    const vw = Math.max(1, ctx.viewW), vh = Math.max(1, ctx.viewH);
    if (safeAt !== vw * 10000 + vh) { safeAt = vw * 10000 + vh; readSafe(); }
    const seat = hitCamRect(vw, vh, safe);
    const dx = -(seat.x + seat.w + 28) * f.slide;
    camera.fov = fovFor(K.fovDeg, f.zoom);
    camera.aspect = seat.w / seat.h;
    if (ctx.mainCamera) { camera.far = ctx.mainCamera.far; camera.near = Math.max(0.05, ctx.mainCamera.near); camera.layers.mask = ctx.mainCamera.layers.mask; }
    camera.updateProjectionMatrix();
    camera.updateMatrixWorld();
    const rotDeg = gentle ? 0 : TILT_DEG;
    renderer.setInset({ scene: world.scene, camera, viewW: vw, viewH: vh,
      rect: { cx: seat.x + seat.w / 2 + dx, cy: seat.y + seat.h / 2, w: seat.w * f.scale, h: seat.h * f.scale, rotRad: rotDeg * Math.PI / 180 } });
    chrome.render({ x: seat.x, y: seat.y, w: seat.w, h: seat.h, dx, rotDeg, scale: f.scale, flash: f.flash, glow: f.glow,
      word: f.word, wordText: s.word, chip: f.chip, chipText: f.incoming ? 'INCOMING' : 'HIT CAM', name: s.name,
      item: itemName(s.kind), gentle });
  }

  return {
    camera,
    stats,
    /** The racer the inset follows (-1 none): fed to world.update as view.focusRacer for kart LOD. */
    get focus() { return s ? s.victim : -1; },
    update,
    reset() { hits.length = 0; leads.length = 0; if (s) close(); },
    /** Compile the inset's programs behind the loading card. */
    warm(mainCamera) {
      if (mainCamera) { camera.far = mainCamera.far; camera.near = Math.max(0.05, mainCamera.near); camera.position.copy(mainCamera.position); camera.quaternion.copy(mainCamera.quaternion); camera.updateMatrixWorld(); }
      return renderer.warmInset ? renderer.warmInset(world.scene, camera, mainCamera) : null;
    },
    dispose() { for (const off of offs) off(); hits.length = 0; leads.length = 0; renderer.setInset(null); chrome.dispose(); },
  };
}
