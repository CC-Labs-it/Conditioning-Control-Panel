/* ============================================================================
 * board/name-tags.js - the two players, standing in as floating name tags.
 *
 * One tag a side, out at that side's end of the board (board/name-tags-plan.js
 * anchorFor): a thick rounded slab with the name and the rating on both faces,
 * always turning to face the camera a beat late. The tags live off the bus:
 *
 *   idle         a slow bob, out of step with each other
 *   the turn     the side to move rises a little and lights up; it settles back
 *                and goes dark when it has moved
 *   a move       the mover hops and leans
 *   a capture    the side that lost a man flinches (squash, jerk back, a red
 *                flash); the taker swells a little
 *   check        the checking side leans in; the checked side TREMBLES and
 *                SWEATS, harder with every check in a row; both stop once it
 *                has moved out (the drops still in the air finish falling)
 *   the end      the winner jumps, turns once round and stays a size up in
 *                gold; the loser sinks, droops and greys; a draw is a shrug
 *   a new deal   everything springs home and the tags pop back in
 *
 * Every effect has an in and an out: kicks ride damped springs back to rest,
 * the lift, glow, tint and shake ease both ways. Reduced motion: no bob, no
 * tremble, no hops or jerks, no drops; the turn glow, a steady check tint and
 * the end colours carry it instead.
 *
 * Shown for players and spectators alike; Options "Name tags" (on by default)
 * puts them away. Hidden while the front door is up.
 * ==========================================================================*/

import * as THREE from 'three';
import { TAG, tagState, reduce, namesFor, anchorFor, spring, tremble, trembleAmp, sweatRate, bob, inCheck, checkTint } from './name-tags-plan.js';
import { presentation, onPresentation, setPresentation } from '../game/preferences.js';

const SIDES = ['w', 'b'];
const LOOK = {
  w: { body: 0xF5E6C8, ink: '#2A2150', sub: '#B0457F', disc: '#FFFFFF', discEdge: '#2A2150' },
  b: { body: 0x2E2757, ink: '#F5E6C8', sub: '#FF8AC6', disc: '#1A1A3E', discEdge: '#F5E6C8' },
};
const GLOW = new THREE.Color(0xFF69B4);
const HOT = new THREE.Color(0xFF5A3C);     // check
const GOLD = new THREE.Color(0xFFC94A);    // the winner
const GREY = new THREE.Color(0x77738A);    // the loser
const CANVAS_W = 768;
const CANVAS_H = 256;

function reducedNow() {
  try { return !!presentation().reducedMotion || !!window.PBP?.settings?.reducedMotion; } catch { return false; }
}

function roundedRect(w, h, r) {
  const s = new THREE.Shape();
  const x = -w / 2, y = -h / 2;
  s.moveTo(x + r, y);
  s.lineTo(x + w - r, y);
  s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r);
  s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h);
  s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r);
  s.quadraticCurveTo(x, y, x + r, y);
  return s;
}

/** The face of a tag: a disc in the side's colour, the name, and the rating under it. */
function paintLabel(ctx, side, label) {
  const look = LOOK[side];
  ctx.clearRect(0, 0, CANVAS_W, CANVAS_H);
  // the side's man, as a disc
  ctx.beginPath();
  ctx.arc(108, CANVAS_H / 2, 66, 0, Math.PI * 2);
  ctx.fillStyle = look.disc;
  ctx.fill();
  ctx.lineWidth = 10;
  ctx.strokeStyle = look.discEdge;
  ctx.stroke();
  const left = 206, room = CANVAS_W - left - 30;
  const hasRating = label.rating !== null && label.rating !== undefined;
  let size = 96;
  ctx.font = `700 ${size}px 'Segoe UI', system-ui, sans-serif`;
  while (size > 44 && ctx.measureText(label.name).width > room) {
    size -= 4;
    ctx.font = `700 ${size}px 'Segoe UI', system-ui, sans-serif`;
  }
  ctx.fillStyle = look.ink;
  ctx.textBaseline = 'middle';
  ctx.fillText(label.name, left, hasRating ? CANVAS_H / 2 - 30 : CANVAS_H / 2 + 4);
  if (hasRating) {
    ctx.font = `600 62px 'Segoe UI', system-ui, sans-serif`;
    ctx.fillStyle = look.sub;
    ctx.fillText(String(label.rating), left + 2, CANVAS_H / 2 + 56);
  }
}

function haloTexture() {
  const c = document.createElement('canvas');
  c.width = 128; c.height = 128;
  const g = c.getContext('2d');
  const grad = g.createRadialGradient(64, 64, 4, 64, 64, 63);
  grad.addColorStop(0, 'rgba(255,255,255,0.9)');
  grad.addColorStop(0.45, 'rgba(255,255,255,0.35)');
  grad.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = grad;
  g.fillRect(0, 0, 128, 128);
  const tex = new THREE.CanvasTexture(c);
  tex.colorSpace = THREE.SRGBColorSpace;
  return tex;
}

/** A sweat drop: a sphere pulled up into a point. */
function dropGeometry() {
  const pts = [];
  for (let i = 0; i <= 10; i++) {
    const t = i / 10;                       // 0 = the point, 1 = the bottom
    const a = t * Math.PI;
    const r = Math.sin(a) * (0.35 + 0.65 * t) * 0.05;
    pts.push(new THREE.Vector2(r, 0.07 * Math.cos(a) + 0.02));
  }
  return new THREE.LatheGeometry(pts, 10);
}

/**
 * `view` the scene (scene.js); `bus` the game bus; `game` the driver switch; `menuUp` says
 * whether the front door covers the board; `own` names the player (an online seat's own tag).
 */
export function createNameTags({ view, bus, game = null, menuUp = () => false, own = () => '' }) {
  const root = new THREE.Group();
  root.name = 'pbp-name-tags';
  view.scene.add(root);
  const dropsGroup = new THREE.Group();
  root.add(dropsGroup);

  const bodyGeo = new THREE.ExtrudeGeometry(roundedRect(TAG.width, TAG.height, TAG.corner), {
    depth: TAG.depth, bevelEnabled: true, bevelThickness: TAG.bevel, bevelSize: TAG.bevel, bevelSegments: 3, curveSegments: 8,
  });
  bodyGeo.translate(0, 0, -TAG.depth / 2);
  const faceZ = TAG.depth / 2 + TAG.bevel + 0.004;
  const labelGeo = new THREE.PlaneGeometry(TAG.width - 0.14, (TAG.width - 0.14) * CANVAS_H / CANVAS_W);
  const ringGeo = new THREE.TorusGeometry(0.09, 0.022, 8, 20);
  const halo = haloTexture();

  let state = tagState();
  let deal = null;
  let players = null;
  let clock = 0;
  let enabled = presentation().nameTags !== false;
  let aspect = 16 / 9;

  const tags = {};
  for (const side of SIDES) {
    const anchor = new THREE.Group();
    const face = new THREE.Group();
    const rig = new THREE.Group();
    anchor.add(face); face.add(rig); root.add(anchor);
    const baseColor = new THREE.Color(LOOK[side].body);
    const bodyMat = new THREE.MeshPhysicalMaterial({
      color: baseColor.clone(), roughness: 0.36, clearcoat: 0.7, clearcoatRoughness: 0.2,
      emissive: GLOW.clone(), emissiveIntensity: 0,
    });
    const body = new THREE.Mesh(bodyGeo, bodyMat);
    rig.add(body);
    const canvas = document.createElement('canvas');
    canvas.width = CANVAS_W; canvas.height = CANVAS_H;
    const tex = new THREE.CanvasTexture(canvas);
    tex.colorSpace = THREE.SRGBColorSpace;
    tex.anisotropy = 4;
    const labelMat = new THREE.MeshBasicMaterial({ map: tex, transparent: true, toneMapped: false, depthWrite: false });
    const front = new THREE.Mesh(labelGeo, labelMat);
    front.position.z = faceZ;
    const back = new THREE.Mesh(labelGeo, labelMat);
    back.position.z = -faceZ;
    back.rotation.y = Math.PI;
    rig.add(front, back);
    const ring = new THREE.Mesh(ringGeo, new THREE.MeshStandardMaterial({ color: 0xD9C27A, metalness: 0.8, roughness: 0.3 }));
    ring.position.set(-TAG.width / 2 + 0.2, TAG.height / 2 - 0.02, 0);
    rig.add(ring);
    const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: halo, color: GLOW.clone(), transparent: true, opacity: 0, blending: THREE.AdditiveBlending, depthWrite: false }));
    glow.scale.set(TAG.width * 1.9, TAG.height * 3.2, 1);
    glow.position.z = -0.12;
    rig.add(glow);
    tags[side] = {
      side, anchor, face, rig, body, bodyMat, baseColor, glow, canvas, tex, label: null,
      y: 0, vy: 0, roll: 0, vroll: 0, tilt: 0, vtilt: 0, squash: 0, vsquash: 0,
      lift: 0, vlift: 0, droop: 0, vdroop: 0, big: 0, vbig: 0,
      glowLvl: 0, tint: 0, flash: 0, gold: 0, grey: 0, shake: 0, shakeRun: 0,
      spin: 0, spinT: -1, appear: 0, sweatDebt: 0, faced: false,
    };
  }

  /* ---- the drops (sweat; gold sparks at a win) -------------------------- */
  const dropGeo = dropGeometry();
  const sparkGeo = new THREE.OctahedronGeometry(0.045, 0);
  const pool = [];
  for (let i = 0; i < 36; i++) {
    const mat = new THREE.MeshPhysicalMaterial({ color: 0x9FD8FF, roughness: 0.08, clearcoat: 1, transparent: true, opacity: 0, emissive: 0x2A6A9A, emissiveIntensity: 0.35 });
    const mesh = new THREE.Mesh(dropGeo, mat);
    mesh.visible = false;
    dropsGroup.add(mesh);
    pool.push({ mesh, mat, life: 0, max: 1, v: new THREE.Vector3(), spark: false });
  }
  const tmp = new THREE.Vector3();
  function spawn(tag, { spark = false, big = false } = {}) {
    const d = pool.find((p) => p.life <= 0);
    if (!d) return;
    // off the top corners and the sides, as the tag stands now
    const edge = Math.random();
    const lx = (edge < 0.5 ? -1 : 1) * (TAG.width / 2 - 0.15 - Math.random() * (spark ? 0.9 : 0.35));
    const ly = spark ? (Math.random() - 0.2) * TAG.height * 0.6 : TAG.height / 2 - 0.05 - Math.random() * 0.25;
    tmp.set(lx, ly, (Math.random() - 0.5) * 0.08);
    tag.rig.localToWorld(tmp);
    d.mesh.position.copy(tmp);
    d.spark = spark;
    d.mesh.geometry = spark ? sparkGeo : dropGeo;
    d.mat.color.set(spark ? 0xFFD66B : 0x9FD8FF);
    d.mat.emissive.set(spark ? 0xFFB020 : 0x2A6A9A);
    d.mat.emissiveIntensity = spark ? 1.2 : 0.35;
    const out = Math.sign(lx) || 1;
    // out sideways along the tag's own face, a little up, then gravity
    const right = tmp.set(1, 0, 0).applyQuaternion(tag.face.getWorldQuaternion(new THREE.Quaternion())).multiplyScalar(out);
    if (spark) d.v.copy(right).multiplyScalar(0.6 + Math.random() * 1.6).add(new THREE.Vector3((Math.random() - 0.5) * 1.2, 2.6 + Math.random() * 2.4, (Math.random() - 0.5) * 1.2));
    else d.v.copy(right).multiplyScalar(0.35 + Math.random() * 0.55).add(new THREE.Vector3(0, 0.5 + Math.random() * 0.6, 0));
    d.max = spark ? 1.3 : TAG.dropLife * (big ? 1.6 : 1);
    d.life = d.max;
    const s = big ? 2.1 : (0.8 + Math.random() * 0.5);
    d.mesh.scale.setScalar(s);
    d.mesh.rotation.set(0, 0, 0);
    d.mesh.visible = true;
  }
  function updateDrops(dt) {
    for (const d of pool) {
      if (d.life <= 0) continue;
      d.life -= dt;
      if (d.life <= 0) { d.mesh.visible = false; d.mat.opacity = 0; continue; }
      d.v.y -= TAG.gravity * dt * (d.spark ? 0.7 : 1);
      d.mesh.position.addScaledVector(d.v, dt);
      const k = d.life / d.max;
      d.mat.opacity = Math.min(1, k * 2.5) * (d.spark ? 1 : 0.85);
      if (d.spark) { d.mesh.rotation.x += dt * 9; d.mesh.rotation.y += dt * 7; }
      else d.mesh.rotation.z = Math.atan2(d.v.x, -d.v.y) * 0.4;   // the point trails the fall
    }
  }

  /* ---- the labels ------------------------------------------------------- */
  function paintNames() {
    const names = namesFor(deal || {}, { players, own: own() });
    for (const side of SIDES) {
      const t = tags[side];
      const label = names[side];
      if (t.label && t.label.name === label.name && t.label.rating === label.rating) continue;
      t.label = label;
      paintLabel(t.canvas.getContext('2d'), side, label);
      t.tex.needsUpdate = true;
    }
  }

  /* ---- the beats -------------------------------------------------------- */
  function play(beat) {
    const t = tags[beat.side];
    if (!t) return;
    const calm = reducedNow();
    const dir = beat.side === 'w' ? 1 : -1;
    switch (beat.kind) {
      case 'reset':
        t.spinT = -1; t.spin = 0; t.shakeRun = 0;
        t.appear = 0;            // pops back in
        t.faced = false;
        break;
      case 'hop':
        if (calm) break;
        t.vy += TAG.hop;
        t.vroll += TAG.lean * (Math.random() < 0.5 ? 1 : -1);
        break;
      case 'flinch':
        t.flash = 1;
        if (calm) break;
        t.vsquash += TAG.flinch;
        t.vtilt -= TAG.flinchTilt;
        t.vroll += TAG.lean * 0.6 * dir;
        break;
      case 'proud':
        if (!calm) t.vsquash -= TAG.proud;
        break;
      case 'gotcha':
        if (!calm) t.vtilt += TAG.gotcha;
        break;
      case 'win':
        if (!calm) {
          t.vy += TAG.winHop;
          t.spinT = 0;
          for (let i = 0; i < 18; i++) spawn(t, { spark: true });
        }
        break;
      case 'lose':
        if (!calm) spawn(t, { big: true });
        break;
      case 'draw':
        if (!calm) { t.vy += TAG.hop * 0.6; t.vroll += TAG.lean * 0.5 * dir; }
        break;
      default: break;
    }
  }

  function fold(type, p) {
    let payload = p;
    if (type === 'turn' && p && Number.isFinite(p.ply) && p.ply !== state.ply + 1) {
      let check = false;
      try { check = !!(game && game.rules && game.rules.inCheck && game.rules.inCheck()); } catch { check = false; }
      payload = { ...p, inCheck: check };
    }
    const out = reduce(state, type, payload);
    state = out.state;
    for (const b of out.beats) play(b);
  }

  const offs = [];
  const on = (type, fn) => { if (bus) offs.push(bus.on(type, fn)); };
  on('local', (p) => { deal = p || {}; players = null; fold('local', p); paintNames(); });
  on('newgame', (p) => fold('newgame', p));
  on('capture', (p) => fold('capture', p));
  on('check', (p) => fold('check', p));
  on('turn', (p) => fold('turn', p));
  on('gameover', (p) => fold('gameover', p));
  on('players', (p) => {
    if (!p || (!p.white && !p.black)) return;
    players = { w: p.white || null, b: p.black || null };
    paintNames();
  });
  on('watch-info', (p) => {
    if (!p || !p.players || (!p.players.w && !p.players.b)) return;
    players = { w: p.players.w || null, b: p.players.b || null };
    paintNames();
  });

  /* ---- the Options box --------------------------------------------------- */
  const box = typeof document !== 'undefined' ? document.getElementById('game-name-tags') : null;
  const boxChange = () => setPresentation({ nameTags: !!box.checked });
  if (box) box.addEventListener('change', boxChange);
  const prefOff = onPresentation((p) => {
    enabled = p.nameTags !== false;
    if (box) box.checked = enabled;
  });

  /* ---- the frame -------------------------------------------------------- */
  const camPos = new THREE.Vector3();
  const anchorPos = new THREE.Vector3();
  const euler = new THREE.Euler(0, 0, 0, 'YXZ');
  const want = new THREE.Quaternion();
  const camQuat = new THREE.Quaternion();
  const turn = new THREE.Quaternion();
  const toBoard = new THREE.Vector3();
  const col = new THREE.Color();

  function update(dt) {
    dt = Math.min(0.1, Math.max(0, dt || 0));
    clock += dt;
    const calm = reducedNow();
    const shown = enabled && state.dealt && !menuUp();
    aspect = view.camera.aspect || aspect;
    view.camera.getWorldPosition(camPos);
    for (const side of SIDES) {
      const t = tags[side];
      // in and out
      const step = shown ? dt / TAG.fadeIn : -dt / TAG.fadeOut;
      t.appear = Math.max(0, Math.min(1, t.appear + step));
      t.anchor.visible = t.appear > 0.001;
      if (!t.anchor.visible) continue;
      const a = anchorFor(side, aspect);
      const over = state.over;
      const end = over ? (over.winner ? (over.winner === side ? 'win' : 'lose') : 'draw') : null;
      const myTurn = !over && state.dealt && state.toMove === side;
      const run = inCheck(state, side) ? state.run[side] : 0;

      // the springs: kicks go home, the slow ones follow their targets
      [t.y, t.vy] = spring(t.y, t.vy, 0, TAG.springK * 0.55, TAG.springC * 0.5, dt);
      [t.roll, t.vroll] = spring(t.roll, t.vroll, 0, TAG.springK, TAG.springC, dt);
      [t.tilt, t.vtilt] = spring(t.tilt, t.vtilt, 0, TAG.springK, TAG.springC, dt);
      [t.squash, t.vsquash] = spring(t.squash, t.vsquash, 0, TAG.springK * 1.4, TAG.springC * 1.1, dt);
      const liftTo = (myTurn ? TAG.turnLift : 0) - (end === 'lose' ? TAG.loseSink : 0);
      [t.lift, t.vlift] = spring(t.lift, t.vlift, calm ? 0 : liftTo, TAG.slowK, TAG.slowC, dt);
      [t.droop, t.vdroop] = spring(t.droop, t.vdroop, end === 'lose' && !calm ? TAG.loseDroop : 0, TAG.slowK, TAG.slowC, dt);
      [t.big, t.vbig] = spring(t.big, t.vbig, end === 'win' ? TAG.winScale : 0, TAG.slowK, TAG.slowC, dt);
      const ease = (cur, to, rate) => cur + (to - cur) * (1 - Math.exp(-rate * dt));
      t.glowLvl = ease(t.glowLvl, myTurn ? TAG.turnGlow : 0, 6);
      t.tint = ease(t.tint, checkTint(run), 5);
      t.gold = ease(t.gold, end === 'win' ? 1 : 0, 4);
      t.grey = ease(t.grey, end === 'lose' ? 1 : 0, 3);
      t.flash = Math.max(0, t.flash - dt * 2.2);
      if (run > 0) t.shakeRun = run;
      t.shake = ease(t.shake, run > 0 && !calm ? 1 : 0, run > 0 ? 9 : 4);
      if (t.shake < 0.01 && run === 0) t.shakeRun = 0;

      // the winner's one turn round
      if (t.spinT >= 0) {
        t.spinT += dt / 0.95;
        const k = Math.min(1, t.spinT);
        t.spin = (1 - Math.pow(1 - k, 3)) * Math.PI * 2 * TAG.winSpin;
        if (k >= 1) { t.spinT = -1; t.spin = 0; }
      }

      // where it stands
      const shake = t.shake > 0.001 ? tremble(clock, t.shakeRun) : { x: 0, y: 0, rz: 0 };
      const sh = t.shake;
      t.anchor.position.set(a.x, a.y + (calm ? 0 : bob(clock, side)) + t.lift, a.z);

      // facing the camera, a beat late
      // square to the screen (no roll at the edge of the picture), turned a little toward the
      // middle of the board so the slab's edge shows
      t.anchor.getWorldPosition(anchorPos);
      view.camera.getWorldQuaternion(camQuat);
      toBoard.copy(anchorPos).project(view.camera);
      euler.set(0, -Math.sign(toBoard.x || 1) * TAG.inward * Math.min(1, Math.abs(toBoard.x) * 1.6), 0);
      want.copy(camQuat).multiply(turn.setFromEuler(euler));
      if (!t.faced || calm) { t.face.quaternion.copy(want); t.faced = true; }
      else t.face.quaternion.slerp(want, 1 - Math.exp(-TAG.face * dt));

      // the rig: hops, leans, flinches, the shake, the droop, the spin
      const pop = t.appear < 1 && shown ? backOut(t.appear) : t.appear;
      // a tag near the camera is drawn smaller, so the two read the same size from any seat
      const sized = Math.max(0.55, Math.min(1.3, camPos.distanceTo(anchorPos) / TAG.sizeAt));
      t.dist = t.dist ? t.dist + (sized - t.dist) * (1 - Math.exp(-4 * dt)) : sized;
      const scale = Math.max(0.0001, pop) * (1 + t.big) * t.dist;
      t.rig.position.set(shake.x * sh, t.y + shake.y * sh, 0);
      t.rig.rotation.set(t.tilt + t.droop, t.spin, t.roll + shake.rz * sh * 1.5);
      t.rig.scale.set(scale * (1 + t.squash * 0.2), scale * (1 - t.squash * 0.35), scale);

      // colour: the turn glow, the check tint, the flinch flash, gold and grey at the end
      col.copy(GLOW).lerp(HOT, Math.min(1, t.tint + t.flash)).lerp(GOLD, t.gold);
      t.bodyMat.emissive.copy(col);
      t.bodyMat.emissiveIntensity = Math.max(t.glowLvl * 0.32, t.tint * 0.42, t.flash * 0.7, t.gold * 0.5);
      t.bodyMat.color.copy(t.baseColor).lerp(GREY, t.grey * 0.7);
      t.glow.material.color.copy(col);
      t.glow.material.opacity = Math.min(0.9, Math.max(t.glowLvl * 0.75, t.tint * 0.7, t.flash * 0.8, t.gold * 0.9)) * t.appear;

      // sweat, while in check
      if (run > 0 && !calm && t.appear > 0.9) {
        t.sweatDebt += sweatRate(run) * dt;
        while (t.sweatDebt >= 1) { t.sweatDebt -= 1; spawn(t); }
      } else t.sweatDebt = 0;
    }
    updateDrops(dt);
  }

  function debug() {
    const out = { enabled, dealt: state.dealt, toMove: state.toMove, over: state.over, run: { ...state.run } };
    for (const side of SIDES) {
      const t = tags[side];
      out[side] = { name: t.label && t.label.name, rating: t.label ? t.label.rating : null, appear: +t.appear.toFixed(2), shake: +t.shake.toFixed(2), tint: +t.tint.toFixed(2), glow: +t.glowLvl.toFixed(2), amp: trembleAmp(t.shakeRun), drops: pool.filter((d) => d.life > 0).length };
    }
    return out;
  }

  function dispose() {
    for (const off of offs) { try { off(); } catch { /* gone */ } }
    if (box) box.removeEventListener('change', boxChange);
    try { prefOff(); } catch { /* gone */ }
    view.scene.remove(root);
    bodyGeo.dispose(); labelGeo.dispose(); ringGeo.dispose(); dropGeo.dispose(); sparkGeo.dispose(); halo.dispose();
    for (const side of SIDES) { tags[side].tex.dispose(); tags[side].bodyMat.dispose(); tags[side].glow.material.dispose(); }
    for (const d of pool) d.mat.dispose();
  }

  paintNames();
  return { update, debug, dispose, group: root, fold };
}

/** easeOutBack: the pop in overshoots a touch and settles. */
function backOut(x) {
  const c1 = 1.70158, c3 = c1 + 1;
  return 1 + c3 * Math.pow(x - 1, 3) + c1 * Math.pow(x - 1, 2);
}
