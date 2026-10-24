/* ============================================================================
 * board/intro.js - the clockwork rise when a new game's board comes up.
 *
 * Every man starts under his square and is wound up through a brass gear
 * turning on it, rooks first; four big wheels turn round the plinth, half in
 * the plinth's dark, and everything sinks out once the last man is home. The
 * clock and the order live in board/intro-plan.js (pure, tested in node).
 *
 * It never owns a man. The rise is an offset put on just before the frame is
 * drawn and taken off straight after (render(draw)), so the game, the drag,
 * the jiggle and the anims never see it, and input is live from frame one.
 * Any press on the board, any key but Escape, or any move lands every man at
 * once and takes the brass out fast. Escape is the app's pause and panic key:
 * it is never heard here. A man rebuilt mid-intro (art arriving, a resync)
 * simply stands where the game put him.
 *
 * Levels follow the page's motion choice: full, reduced (a short calm lift, no
 * gears, no wheels) and off (the Options box "Board intro" unticked). Gentle
 * visuals on the desktop play the full clockwork, its clock slowed.
 * ==========================================================================*/

import * as THREE from 'three';
import { squareToWorld } from './scene.js';
import { introPlan, introLevel, shouldPlay, manPose, gearState, wheelState, clicksBetween, introOver } from './intro-plan.js';

const TEETH = 14;
const DEPTH = 0.07;

/** A flat gear lying in the xz plane: `teeth` teeth between the root and tip radii, a round hole, or spokes. */
function gearGeo(teeth, rRoot, rTip, rHole, depth, spokes = 0) {
  const s = new THREE.Shape(), step = Math.PI * 2 / teeth;
  for (let i = 0; i < teeth; i++) {
    const a = i * step;
    const pts = [[rRoot, a], [rRoot, a + step * 0.22], [rTip, a + step * 0.34], [rTip, a + step * 0.66], [rRoot, a + step * 0.78]];
    pts.forEach(([r, an], j) => { const x = Math.cos(an) * r, y = Math.sin(an) * r; if (i === 0 && j === 0) s.moveTo(x, y); else s.lineTo(x, y); });
  }
  s.closePath();
  if (!spokes) { const h = new THREE.Path(); h.absarc(0, 0, rHole, 0, Math.PI * 2, true); s.holes.push(h); }
  else {
    // windows between the spokes: a hub, `spokes` arms, a rim
    const rHub = rHole * 0.34, gap = 0.16;
    for (let k = 0; k < spokes; k++) {
      const a0 = k * Math.PI * 2 / spokes + gap, a1 = (k + 1) * Math.PI * 2 / spokes - gap, h = new THREE.Path();
      h.absarc(0, 0, rHole, a0, a1, false); h.absarc(0, 0, rHub * 1.9, a1 - gap * 0.6, a0 + gap * 0.6, true); h.closePath();
      s.holes.push(h);
    }
    const hub = new THREE.Path(); hub.absarc(0, 0, rHub * 0.5, 0, Math.PI * 2, true); s.holes.push(hub);
  }
  const g = new THREE.ExtrudeGeometry(s, { depth, bevelEnabled: true, bevelThickness: depth * 0.18, bevelSize: depth * 0.18, bevelSegments: 2, curveSegments: 12 });
  g.rotateX(-Math.PI / 2);                   // the shape's plane becomes the board's; the extrusion points up
  return g;
}

// The wheels sit left and right of the plinth only, below the board's top and
// half inside the plinth wall: they never stand between a seat camera and the
// board, and on a narrow phone frame they fall off the sides. Big and mid mesh,
// so the mid turns the other way, faster by the tooth ratio. [size, x, y, z, w]
const WHEELS = [['big', -5.95, -0.4, 2.35, 1], ['big', 5.95, -0.4, -2.35, 1], ['mid', -5.7, -0.34, -0.85, -30 / 18], ['mid', 5.7, -0.34, 0.85, -30 / 18]];

export function createIntro({ view, pieces, bus, game, sfx = () => null, prefs = () => ({}) }) {
  const clip = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0.004);   // nothing of a rising man shows under the board
  const box = new THREE.Box3();
  const off = [];
  let run = null;          // the intro playing, or null
  let pending = false;     // a new game was dealt; start on the next frame, once its men stand

  const levelNow = () => {
    const p = prefs() || {};
    return introLevel({ enabled: p.boardIntro !== false, reduced: !!(p.reducedMotion || window.PBP?.settings?.reducedMotion), gentle: speedNow() });
  };
  // Gentle visuals: the full clockwork, its clock slowed (game/preferences.js introSpeed, 0 = not gentle)
  const speedNow = () => Number((prefs() || {}).introSpeed) || 0;

  function begin() {
    const level = levelNow();
    const plies = typeof game?.plies === 'function' ? game.plies() : 0;
    if (!shouldPlay({ level, plies }) || !pieces.pieces.size) return;
    const list = [...pieces.pieces].map(([sq, o]) => ({ sq, type: o.userData.type, o }));
    const plan = introPlan(list, level);
    const root = new THREE.Group();
    root.name = 'pbp-intro';
    const r = { plan, level, t: 0, speed: (level === 'full' && speedNow()) || 1, skipAt: null, root, men: [], wheels: [], clipped: new Set(), geos: [], mats: [] };
    let brass = null, brassDark = null, small = null;
    if (level === 'full') {
      brass = new THREE.MeshStandardMaterial({ color: 0xffcb5c, metalness: 0.78, roughness: 0.3, envMapIntensity: 1.5, emissive: 0x3d2600 });
      brassDark = new THREE.MeshStandardMaterial({ color: 0xd9973a, metalness: 0.78, roughness: 0.38, envMapIntensity: 1.3, emissive: 0x2c1900 });
      small = gearGeo(TEETH, 0.445, 0.545, 0.34, DEPTH);
      r.mats.push(brass, brassDark); r.geos.push(small);
      // in the plinth's shadow: a deeper, duller brass than the gears on the squares
      const wheelMat = new THREE.MeshStandardMaterial({ color: 0x8f5f26, metalness: 0.7, roughness: 0.5, envMapIntensity: 0.8, emissive: 0x140a00 });
      const big = gearGeo(30, 1.9, 2.15, 1.6, 0.14, 6), mid = gearGeo(18, 1.0, 1.24, 0.8, 0.12, 5);
      r.geos.push(big, mid); r.mats.push(wheelMat);
      for (const [size, x, y, z, w] of WHEELS) {
        const m = new THREE.Mesh(size === 'big' ? big : mid, wheelMat);
        m.position.set(x, y - 0.9, z); m.receiveShadow = true; m.visible = false;
        root.add(m);
        r.wheels.push({ m, y, w });
      }
    }
    plan.men.forEach((pm, i) => {
      const o = list[i].o;
      box.setFromObject(o);
      const depth = level === 'full' ? Math.max(0.8, box.max.y) + 0.12 : 0.32;
      const man = { ...pm, o, depth, gear: null, g0: 0, disc: o.userData.contact || null, dy: 0, dr: 0, hide: false, live: true };
      if (small) {
        const gear = new THREE.Mesh(small, pm.type === 'r' ? brass : brassDark);
        gear.castShadow = true; gear.receiveShadow = true; gear.visible = false;
        const at = squareToWorld(pm.sq, -0.11);
        gear.position.copy(at);
        man.g0 = pm.dir > 0 ? 0 : Math.PI / TEETH;    // half a tooth out of phase, so the teeth interleave
        gear.rotation.y = man.g0;
        root.add(gear);
        man.gear = gear;
      }
      o.traverse(m => { if (m.isMesh) for (const mt of (Array.isArray(m.material) ? m.material : [m.material])) if (mt) r.clipped.add(mt); });
      r.men.push(man);
    });
    for (const mt of r.clipped) { mt.clippingPlanes = [clip]; mt.needsUpdate = true; }
    view.renderer.localClippingEnabled = true;
    view.scene.add(root);
    run = r;
    pose(0);
  }

  function pose(prevT) {
    const r = run;
    const { t, skipAt, level, plan } = r;
    for (const m of r.men) {
      // rebuilt under us (the art arriving, a resync): he stands where the game put him
      if (m.live && pieces.pieceAt(m.sq) !== m.o) { m.live = false; m.goneAt = t; }
      const p = m.live ? manPose(m, t, level, skipAt) : { lift: 1, spin: 0, hidden: false };
      m.dy = -(1 - p.lift) * m.depth;
      m.dr = p.spin;
      m.hide = p.hidden;
      if (m.gear) {
        // his gear goes out as fast as a skip's once he has gone
        const g = gearState(m, t, m.live ? skipAt : Math.min(skipAt ?? m.goneAt, m.goneAt));
        m.gear.visible = g.k > 0.001;
        m.gear.position.y = -0.11 + 0.112 * g.k;
        m.gear.rotation.y = m.g0 + g.angle;
      }
    }
    if (r.wheels.length) {
      const w = wheelState(plan, t, skipAt);
      for (const wh of r.wheels) {
        wh.m.visible = w.k > 0.001;
        wh.m.position.y = wh.y - (1 - w.k) * 0.9;
        wh.m.rotation.y = wh.w * w.angle;
      }
    }
    // one quiet tick per ratchet click of the rooks, never one per man
    if (skipAt == null && clicksBetween(plan, prevT, t) > 0) {
      try { sfx()?.play?.('tick'); } catch { /* sound is optional */ }
    }
  }

  function finish() {
    const r = run;
    if (!r) return;
    run = null;
    for (const mt of r.clipped) { mt.clippingPlanes = null; mt.needsUpdate = true; }
    view.renderer.localClippingEnabled = false;
    view.scene.remove(r.root);
    for (const g of r.geos) g.dispose();
    for (const m of r.mats) m.dispose();
  }

  /** Land every man now; the brass goes out fast. */
  function skip() {
    if (run && run.skipAt == null) { run.skipAt = run.t; pose(run.t); }
  }

  function update(dt) {
    if (pending) { pending = false; finish(); begin(); }
    if (!run) return;
    const prevT = run.t;
    // gentle runs the whole clockwork slower; a skip still goes out at its own pace
    run.t += Math.max(0, dt || 0) * (run.skipAt == null ? run.speed : 1);
    if (introOver(run.plan, run.t, run.skipAt)) { finish(); return; }
    pose(prevT);
  }

  /** Draw one frame with the men risen as far as the intro has them. */
  function render(draw) {
    const r = run;
    if (!r) { draw(); return; }
    const men = r.men;
    for (const m of men) {
      if (!m.live) continue;
      m.o.position.y += m.dy; m.o.rotation.y += m.dr;
      m.wasVisible = m.o.visible; if (m.hide) m.o.visible = false;
      if (m.disc) { m.discWas = m.disc.visible; if (m.dy < -0.02) m.disc.visible = false; }
    }
    try { draw(); }
    finally {
      for (const m of men) {
        if (!m.live) continue;
        m.o.position.y -= m.dy; m.o.rotation.y -= m.dr; m.o.visible = m.wasVisible;
        if (m.disc) m.disc.visible = m.discWas;
      }
    }
  }

  // A new game deals the board; a game picked up mid-way never plays it (plies > 0).
  // The stands never arm on the deal: the tape's first position comes later, off the network, and the
  // intro plays only when that position is the opening (ply 0). A join mid-game gets no intro at all.
  let watchWait = false;
  off.push(bus.on('local', (p) => {
    if (p && p.mode === 'watch') { watchWait = true; pending = false; finish(); return; }
    watchWait = false;
    pending = true;
  }));
  // Any move, a takeback, the menu or the end lands everything at once.
  for (const name of ['turn', 'takeback', 'menu', 'menu-request', 'gameover', 'replay-start']) off.push(bus.on(name, skip));
  // A resync with moves on it is a game in progress: no intro left to see.
  off.push(bus.on('resync', (p) => {
    if (watchWait) { watchWait = false; if (p && p.ply === 0) pending = true; return; }
    if (p && p.ply > 0) { pending = false; finish(); }
  }));
  // Input is live from frame one: the first press on the board or any key but Escape finishes it.
  // Listened to passively; nothing is swallowed.
  const canvas = view.renderer.domElement;
  const onPress = () => skip();
  const onKey = (e) => { if (e.key !== 'Escape') skip(); };
  canvas.addEventListener('pointerdown', onPress, { capture: true, passive: true });
  window.addEventListener('keydown', onKey, { capture: true, passive: true });

  return {
    update,
    render,
    skip,
    active: () => !!run,
    state: () => (run ? { level: run.level, t: +run.t.toFixed(3), speed: run.speed, total: +run.plan.total.toFixed(3), skipped: run.skipAt != null, men: run.men.length, wheels: run.wheels.length } : null),
    dispose() {
      finish();
      for (const fn of off) if (typeof fn === 'function') fn();
      canvas.removeEventListener('pointerdown', onPress, { capture: true });
      window.removeEventListener('keydown', onKey, { capture: true });
    },
  };
}
