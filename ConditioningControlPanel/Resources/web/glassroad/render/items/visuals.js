/* render/items/visuals.js - the effect bubbles in the world (CONTRACT-B1 section 3, SCREENFX lane).
 * Every bubble (held over a kart, thrown, lobbed, laid as a trap, a road pickup, a box turning,
 * a shield, a pop ring) is one instance of one sprite cloud wearing the living atlas icon in a glass
 * shell; their drifting motes are one additive cloud. Boxes are low floating collectible cards
 * that shrink into a bubble when driven through; the Melt trap is a wobbling spiral puddle; the
 * Pink wave a ring; Echo three rim-lit copies. Pools and per-tier caps from render/items/look.js.
 * Gentle: half speed, no Echo flicker. Reads ItemsView only; ages (t) are seconds since spawn.
 * Pickups share four moving-picture faces and the owner-selected Drop Duel back. Held, thrown,
 * trap and box-turn bubbles are the effect-bubble GLB shell (instanced)
 * round the living icon; the Melt puddle samples fx-melt-spiral.
 * Polish 2: icons breathe inside glass that catches a drifting light; throws trail a sparkling ribbon,
 * lobs a dotted arc with a soft shadow and pulsing ring waiting under the target; traps pulse and wake a
 * warning ring as a kart nears; impacts ring out over the road with a soft glow capped to a share of the
 * view (asks opts.photosafe once, skipped when refused or not wired); box cards glint now and then and
 * burst into tumbling confetti cards when taken. The player's held bubble stays in the HUD (slotfx.js).
 * Polish 4, items as events: a throw leaves charged (the bubble pops big and settles, a glow swells and a ring
 * opens where it left) and drags a longer, haloed ribbon you can follow; an impact (hit, trap, glass, golden,
 * decoy) throws a wider ring, twice the motes, a second shockwave, confetti in the kind's colour and a fountain
 * of glitter. The self item auras are on the karts (render/karts.js).
 * Music road pass 2 (render/items/status.js): every racer under an effect wears that effect's look for all to
 * see, throws fly as comets with a trail and sparks, launches puff and landings burst with a shock ring.
 */
import { Group, Mesh, PlaneGeometry, SphereGeometry, Vector3, Quaternion } from 'three/webgpu';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { S } from '../../sim/state.js';
import { ITEM_IDS } from '../../sim/itemTable.js';
import { Q } from '../quality.js';
import { ITEM_LOOK as L, ITEM_TIERS, MOTE_TINT } from './look.js';
import {
  tierOf, comp, moteAt, popPhase, popMote, rollIconAt, createPool, shellFit, nearCapR, impactSeen,
  breathe, trailLook, trapWarn, lobMark, impactPhase, confettiAt, boxGlint, launchPop, launchFlare, impactFountain,
  iconFps, iconLive,
} from './fxmath.js';
import { createLivingIcons, cellOf } from './living.js';
import { bubbleCloud, moteCloud, meltPuddle, ghost, ringGlow, shellCloud, shieldCloud, blobShadow, confettiCloud } from './materials.js';
import { createPickupCards, PICKUP_CARD } from './pickupCards.js';
import { createItemStatus } from './status.js';
import { advanceRibbon, flightSpark } from './flight.js';
import { selectShieldShells, shieldLook } from './shieldMath.js';

const UP = new Vector3(0, 1, 0), V = new Vector3(), W = new Vector3(), N = new Vector3(), QT = new Quaternion(), QS = new Quaternion();
const OFF = [0, 0, 0];
const TINTS = Object.fromEntries(Object.entries(MOTE_TINT).map(([k, s]) => [k, s.split(',').map((x) => Number(x) / 255)]));
const MODE = { icon: 0, goldIcon: 1, shell: 2, goldShell: 3, bare: 4 };

/** The effect_shell geometry from the effect-bubble GLB, centred on its middle at radius 1; null without it. */
function shellGeometry(model, fit) {
  if (!model) return null;
  let geo = null;
  model.scene.updateMatrixWorld(true);
  model.scene.traverse((o) => {
    if (geo || !o.isMesh || !o.material || o.material.name !== 'effect_shell') return;
    geo = o.geometry.clone().applyMatrix4(o.matrixWorld);
  });
  if (!geo) return null;
  const k = 1 / (fit.radius || 0.55);
  return geo.translate(0, -fit.centreY, 0).scale(k, k, k);
}

/** N = the road normal n (up when missing). */
function normal(n) {
  N.set(comp(n, 0), comp(n, 1), comp(n, 2));
  if (N.lengthSq() < 0.5) N.copy(UP); else N.normalize();
  return N;
}

/** Lay obj on the surface with normal n, turned by spin about it. */
function lay(obj, p, n, spin = 0, lift = 0) {
  normal(n);
  obj.position.set(comp(p, 0) + N.x * lift, comp(p, 1) + N.y * lift, comp(p, 2) + N.z * lift);
  obj.quaternion.setFromUnitVectors(UP, N).multiply(QS.setFromAxisAngle(UP, spin));
}

const IMPACTS = new Set(['hit', 'trap', 'glass', 'golden', 'decoy']);
const hexRgb = (h) => { const n = parseInt(h.slice(1), 16); return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255]; };
const MOTE_HEX = Object.fromEntries(Object.entries(MOTE_TINT).map(([k, s]) => [k, '#' + s.split(',').map((x) => Number(x).toString(16).padStart(2, '0')).join('')]));
let frame = 0;

/** opts.photosafe (fx/photosafe.js): an impact's soft glow asks it once and is skipped when refused or missing. */
export function createItemVisuals(scene, art, feel, { photosafe = null, backend = '', track = null, waterVolume = null } = {}) {
  const top = ITEM_TIERS.high, start = tierOf(Q.name);
  const root = new Group(); root.name = 'item-visuals'; scene.add(root);
  // polish 6: webgl2 redraws the icon atlas at most 15 fps (backend from buildWorld's renderer; the debug global is the fallback)
  const backendOf = () => backend || (globalThis.__glassroad && globalThis.__glassroad.backend) || '';
  const icons = createLivingIcons({ px: start.iconPx, fps: () => iconFps(tierOf(Q.name).iconFps, backendOf()) });
  // motes: bubble rings, pop sprays, throw ribbons, lob dots, box glints and impact glows share one draw
  const bubbles = bubbleCloud(top.bubbles + 24, icons.texture);
  const motes = moteCloud(top.bubbles * top.motes + 24 * Math.ceil(L.popMotes * L.impactMotesMul) + 8 * (2 * L.trailPts + L.lobDots + 17) + 12 * L.fountain.high + top.puddles * 10 + 24);
  root.add(bubbles.sprite, motes.sprite);
  const confetti = confettiCloud(L.confetti.high * 6 + L.hitConfetti.high * 4);
  root.add(confetti.mesh);
  const status = createItemStatus(root, feel, { photosafe, track, waterVolume });
  const CONF = L.confettiColours.map(hexRgb);
  const mask = art && art.tex ? art.tex('sky-spiral-open-mask') : null;
  const meltTex = art && art.tex ? art.tex(L.meltMask) : null;
  const gentle = () => !!feel.get('gentle.on');
  let timeS = 0;

  // ---- the effect-bubble shell (instanced) and shared collectible card artwork ----
  const fit = shellFit(art && art.row ? art.row(L.shellModel) : null);
  const shellGeo = shellGeometry(art && art.model ? art.model(L.shellModel) : null, fit);
  const shells = shellGeo ? shellCloud(shellGeo, top.bubbles) : null;
  if (shells) root.add(shells.mesh);
  const shields = shieldCloud(), shieldRows = [], shieldState = {}, underlayState = {}; root.add(shields.mesh);
  const pickupCards = createPickupCards();

  // ---- boxes: one card per track box, built on first sight ----
  const boxes = [], prevAlive = [], turnT = [];
  function buildBox(i) {
    const o = pickupCards.build(i);
    root.add(o);
    return o;
  }

  // ---- pooled meshes: melt puddles, pink waves, echo copies ----
  const plane = new PlaneGeometry(2, 2).rotateX(-Math.PI / 2);
  const puddles = createPool(top.puddles, () => {
    const pm = meltPuddle(mask, meltTex), mesh = new Mesh(plane, pm.material);
    mesh.frustumCulled = false; mesh.renderOrder = 1; root.add(mesh);
    return { mesh, u: pm.uniforms };
  }, (e) => { e.mesh.visible = false; });
  const waves = createPool(4, () => {
    const g = ringGlow(L.waveColour), mesh = new Mesh(plane, g.material);
    mesh.frustumCulled = false; root.add(mesh);
    return { mesh, u: g.uniforms };
  }, (e) => { e.mesh.visible = false; });
  // polish 2: flat rings (shockwaves, trap warnings, lob landing marks) and soft landing shadows
  const marks = createPool(48, () => {
    const g = ringGlow(L.markColour), mesh = new Mesh(plane, g.material);
    mesh.frustumCulled = false; mesh.renderOrder = 1; root.add(mesh);
    return { mesh, u: g.uniforms };
  }, (e) => { e.mesh.visible = false; });
  const shadows = createPool(6, () => {
    const g = blobShadow(), mesh = new Mesh(plane, g.material);
    mesh.frustumCulled = false; root.add(mesh);
    return { mesh, u: g.uniforms };
  }, (e) => { e.mesh.visible = false; });
  /** A flat ring at p (normal n or up), radius rM, colour hex, opacity a. */
  function mark(p, n, rM, hex, a, lift = 0.06) {
    const e = a > 0.004 ? marks.take() : null;
    if (!e) return;
    lay(e.mesh, p, n, 0, lift); e.mesh.scale.setScalar(rM);
    e.u.colour.value.set(hex); e.u.opacity.value = a; e.mesh.visible = true;
  }
  const trails = new Map(), seen = new Set(), glows = new Map(), launches = new Map();
  const trailOf = (id, n) => { let h = trails.get(id); if (!h) trails.set(id, h = { pts: new Float32Array(n * 3), n: 0 }); return h; };
  const confT = [];
  const kartModel = art && art.model ? art.model('emi-teacup-kart') : null;
  const ghostGeo = new SphereGeometry(0.7, 16, 10).scale(1, 0.8, 1).translate(0, 0.6, 0);
  // polish 6: every copy wears the one ghost material (opacity per object) and the whole pool exists from boot,
  // so the warm links every program an Echo can need (a material per copy linked 6 programs mid-race)
  const ghostLook = ghost(L.echoColour);
  const copies = createPool(top.copies * 4, () => {
    let obj;
    const meshes = [];
    if (kartModel) { obj = cloneSkinned(kartModel.scene); obj.traverse((m) => { if (m.isMesh) { m.material = ghostLook.material; m.frustumCulled = false; meshes.push(m); } }); }
    else { obj = new Mesh(ghostGeo, ghostLook.material); meshes.push(obj); }
    root.add(obj);
    return { obj, set(a) { for (const m of meshes) m.userData.echoA = a; } };
  }, (e) => { e.obj.visible = false; });
  // one of each pooled look exists from boot so the warm links it (pass 4: the first echo copy froze Firefox ~55 s in)
  // Full pools are built behind the loading cover. Firefox must not link a second
  // puddle or ring material for the first time when several traps are laid together.
  for (const p of [puddles, waves, marks, shadows]) p.prime(p.cap);
  copies.prime(copies.cap);
  // Warm all four face materials behind the loading cover, including their canvas map path.
  for (let i = 0; i < PICKUP_CARD.faces; i++) { boxes[i] = buildBox(i); boxes[i].visible = false; }

  // ---- helpers ----
  function addMotes(x, y, z, R, id, n, seed, alpha = 1) {
    const c = TINTS[id] || TINTS.box;
    for (let i = 0; i < n; i++) {
      // timeS already runs at the Gentle rate (update); slowing it again here would quarter it
      const m = moteAt(i, n, timeS, R, OFF, seed);
      motes.push(x + OFF[0], y + OFF[1], z + OFF[2], m.size, c[0], c[1], c[2], m.alpha * alpha);
    }
  }
  let drawn = 0, cam = null, camFx = 0, camFy = 0, camFz = -1;
  /** True when a bubble at (x, y, z), radius R, is in view and big enough for its icon's life to show. */
  function iconSeen(x, y, z, R) {
    if (!cam || !cam.position) return true;
    const p = cam.position, dx = x - p.x, dy = y - p.y, dz = z - p.z, d = Math.hypot(dx, dy, dz);
    return iconLive(R, d, d > 1e-6 ? (dx * camFx + dy * camFy + dz * camFz) / d : 1, cam.fov || 0);
  }
  /** R shrunk so a bubble at (x, y, z) stays under nearMaxFrac of the view's height (no camera: unchanged). */
  function capR(x, y, z, R, nearFrac = L.nearMaxFrac) {
    if (!cam || !cam.position) return R;
    const p = cam.position;
    return nearCapR(R, Math.hypot(x - p.x, y - p.y, z - p.z), cam.fov, nearFrac);
  }
  /** Item sprites obey the near cap; the real shield mesh has no camera-facing sprite. */
  function bubble(x, y, z, id, R, alpha = 1, mode = MODE.icon, seed = 0, moteN = -1, nearFrac = L.nearMaxFrac) {
    R = capR(x, y, z, R, nearFrac);
    if (drawn >= tierOf(Q.name).bubbles || R <= 0.001) return;
    const cell = mode >= MODE.shell ? -1 : cellOf(id);
    if (cell >= 0) icons.use(id, iconSeen(x, y, z, R));
    const br = mode >= MODE.shell ? 1 : breathe(timeS, seed, gentle());
    if (!bubbles.push(x, y, z, (R * 2 * br) / (mode >= MODE.shell ? L.shellFill : L.iconFill), cell, alpha, mode, seed)) return;
    drawn++;
    const n = moteN >= 0 ? moteN : tierOf(Q.name).motes;
    if (n > 0) addMotes(x, y, z, R, id, n, seed, alpha);
  }
  /** A 3D effect bubble (held, thrown, trap, box turn): the GLB shell round a bare living icon; the sprite bubble without it. */
  function bubble3d(x, y, z, id, R, alpha = 1, seed = 0, moteN = -1, nearFrac = L.nearMaxFrac) {
    if (!shells) { bubble(x, y, z, id, R, alpha, MODE.icon, seed, moteN, nearFrac); return; }
    R = capR(x, y, z, R, nearFrac);
    if (drawn >= tierOf(Q.name).bubbles || R <= 0.001) return;
    if (!shells.push(x, y, z, R, alpha, seed)) return;
    icons.use(id, iconSeen(x, y, z, R));
    // the living icon breathes inside its still glass
    bubbles.push(x, y, z, 2 * R * fit.carrier * breathe(timeS, seed, gentle()), cellOf(id), alpha, MODE.bare, seed);
    drawn++;
    const n = moteN >= 0 ? moteN : tierOf(Q.name).motes;
    if (n > 0) addMotes(x, y, z, R, id, n, seed, alpha);
  }
  const kartPos = (k) => V.set(k.st[S.posX], k.st[S.posY], k.st[S.posZ]);
  const kartQuat = (k) => QT.set(k.st[S.quatX], k.st[S.quatY], k.st[S.quatZ], k.st[S.quatW]);

  function update(view, dt, karts = [], camera = null) {
    dt = Math.max(0, Math.min(dt || 0, 0.1));
    cam = camera;
    if (cam && cam.matrixWorld) { const e = cam.matrixWorld.elements; camFx = -e[8]; camFy = -e[9]; camFz = -e[10]; }
    const soft = gentle(), rate = soft ? 0.5 : 1;
    timeS += dt * rate;
    bubbles.uniforms.time.value = timeS;
    status.update(view, dt, karts);
    if (shells) { shells.uniforms.time.value = timeS; shells.begin(); }
    bubbles.begin(); motes.begin(); puddles.begin(); waves.begin(); copies.begin(); marks.begin(); shadows.begin(); confetti.begin(); drawn = 0;
    shields.begin();
    const endAll = () => { bubbles.end(); motes.end(); puddles.end(); waves.end(); copies.end(); marks.end(); shadows.end(); confetti.end(); shields.end(); if (shells) shells.end(); };
    if (!view) {
      trails.clear(); launches.clear(); glows.clear();
      for (const box of boxes) box.visible = false;
      pickupCards.update(dt, soft); endAll(); return;
    }
    const tier = tierOf(Q.name), moteK = tier.motes / top.motes;
    const showS = feel.get('items.popShowS') || 0.6;
    const mid = PICKUP_CARD.height / 2;

    // boxes: spinning cards; driven through, the card spins away and a bubble rises out of it
    (view.boxes || []).forEach((b, i) => {
      const o = boxes[i] || (boxes[i] = buildBox(i));
      if (prevAlive[i] && !b.alive) { turnT[i] = 0; confT[i] = 0; }
      prevAlive[i] = b.alive;
      const turning = turnT[i] !== undefined && turnT[i] < L.boxTurnS + showS;
      const bob = Math.sin(timeS * L.boxBobHz * 6.283 + i) * PICKUP_CARD.bob;
      lay(o, b.p, b.n, timeS * PICKUP_CARD.spin + i, PICKUP_CARD.hover + bob);
      if (turning) {
        turnT[i] += dt;
        const q = Math.min(1, turnT[i] / L.boxTurnS), rise = Math.max(0, turnT[i] - L.boxTurnS) / showS;
        o.visible = q < 1; o.scale.setScalar(1 - q);
        o.quaternion.multiply(QS.setFromAxisAngle(UP, q * q * 9));
        const up = rise + mid * (1 - q);
        bubble3d(o.position.x + N.x * up, o.position.y + N.y * up, o.position.z + N.z * up, 'box', L.bubbleR * Math.min(1, q * 1.4), 1 - rise, i);
      } else {
        turnT[i] = undefined;
        o.visible = !!b.alive; o.scale.setScalar(Math.max(0.01, b.k === undefined ? 1 : b.k));
        // a glint sweeps the card's face corner to corner now and then
        const gl = o.visible ? boxGlint(timeS, i, soft) : null;
        if (gl && gl.alpha > 0.01) {
          const h = PICKUP_CARD.height * 0.42 * o.scale.x;
          W.set(gl.u * h * 0.7, mid + gl.u * h, 0.06).applyQuaternion(o.quaternion).add(o.position);
          motes.push(W.x, W.y, W.z, L.glintSizeM * (0.6 + 0.4 * gl.alpha), 1, 0.97, 1, gl.alpha);
          motes.push(W.x, W.y, W.z, L.glintSizeM * 2.2, 1, 0.75, 0.9, gl.alpha * 0.25);
        }
      }
      if (o.visible && iconSeen(o.position.x, o.position.y + mid, o.position.z, mid)) pickupCards.use(i);
      // taken: confetti cards burst out of the card, tumble and fall
      if (confT[i] !== undefined) {
        confT[i] += dt;
        const n = L.confetti[Q.name] || L.confetti.high;
        if (confT[i] >= L.confettiS) confT[i] = undefined;
        else for (let k = 0; k < n; k++) {
          const c = confettiAt(k, confT[i], OFF, i * 13), col = CONF[k % CONF.length];
          confetti.push(o.position.x + OFF[0], o.position.y + mid + OFF[1], o.position.z + OFF[2], c.rx, c.ry, L.confettiSizeM, col[0], col[1], col[2], c.alpha);
        }
      }
    });

    // road pickups: boost bubbles hovering over the inside line
    for (const b of view.bubbles || []) {
      if (!b.alive) continue;
      normal(b.n);
      const lift = L.pickupHoverM + Math.sin(timeS * 1.7 + b.i) * 0.12;
      bubble(comp(b.p, 0) + N.x * lift, comp(b.p, 1) + N.y * lift, comp(b.p, 2) + N.z * lift, 'box', L.pickupR * (b.k === undefined ? 1 : b.k), 1, MODE.icon, b.i + 40);
    }

    // held: a bubble bobbing over each kart; rolling cycles the icons, never faster than rollSwapS
    for (const h of view.held || []) {
      const k = karts[h.racer];
      if (!k) continue;
      const me = h.racer === 0, [sx, sy, sz] = me ? L.playerHeldAt : [0, L.heldUpM, 0];
      if (me && !L.playerHeldInWorld) continue;   // the HUD item slot shows the player's own
      const bob = Math.sin(timeS * L.heldBobHz * 6.283 + h.racer) * L.heldBobM * (me ? 0.5 : 1);
      kartPos(k); W.set(sx, sy + bob, sz).applyQuaternion(kartQuat(k)).add(V);
      const id = h.rolling ? rollIconAt(h.rollT, ITEM_IDS, soft ? L.rollSwapGentleS : L.rollSwapS) : h.id;
      bubble3d(W.x, W.y, W.z, id || 'box', L.heldR * (me ? L.playerHeldScale : 1), 1, h.racer * 7, me ? 4 : -1);
    }

    // throws and lobs: the sim's p already carries the arc. A throw draws a sparkling ribbon of motes
    // behind it; a lob a dotted arc, and a soft shadow with a pulsing ring waits where it will land
    seen.clear();
    const lobS = feel.get('items.lobFlightS') || 1.6;
    for (const pr of view.projectiles || []) {
      const x = comp(pr.p, 0), y = comp(pr.p, 1), z = comp(pr.p, 2), lob = pr.mode === 'lob', id = pr.id | 0, age = pr.t || 0;
      const radius = pr.radiusM || (lob ? L.lobR : L.throwR);
      bubble3d(x, y, z, pr.kind, radius * launchPop(age, soft), 1, id * 3, -1, L.hazardMaxFrac);
      seen.add(id);
      // where it left: a glow swells and goes (capped like an impact glow), a ring opens on the road
      let ln = launches.get(id);
      if (!ln || age < ln.age) launches.set(id, ln = { x, y, z, age });
      ln.age = age;
      const fl = launchFlare(age, soft), lc = TINTS[pr.kind] || TINTS.box;
      if (fl.glowA > 0.003) {
        const p = cam && cam.position, d = p ? Math.hypot(ln.x - p.x, ln.y - p.y, ln.z - p.z) : 99;
        const r = nearCapR(fl.glowR, d, cam ? cam.fov : 0, L.impactGlowFrac);
        motes.push(ln.x, ln.y + r * 0.3, ln.z, r * 2, lc[0] * 0.5 + 0.5, lc[1] * 0.5 + 0.5, lc[2] * 0.5 + 0.5, fl.glowA);
      }
      if (fl.ringA > 0.004) { OFF[0] = ln.x; OFF[1] = ln.y - (feel.get('items.throwHeightM') || 1); OFF[2] = ln.z; mark(OFF, null, fl.ring, MOTE_HEX[pr.kind] || L.markColour, fl.ringA, 0.08); }
      const n = lob ? L.lobDots : L.trailPts, h = trailOf(id, n), c = TINTS[pr.kind] || TINTS.box;
      if (age < (h.age || 0)) h.n = 0;
      h.age = age;
      advanceRibbon(h, x, y, z, lob ? L.lobDotStepM : L.trailStepM);
      // Paired spiralling sparks make a seeking projectile's turn visible in space.
      // Straight throws retain a solid white nose and a broader coloured tail.
      const ns = pr.homing ? (soft ? 4 : Math.round(8 * moteK)) : 0;
      for (let i = 0; i < ns; i++) {
        const a = flightSpark(i, ns, timeS, id, pr.v, radius, OFF);
        motes.push(x + OFF[0], y + OFF[1], z + OFF[2], 0.3, c[0], c[1], c[2], a * (soft ? 0.55 : 1));
      }
      const shown = Math.min(h.n, Math.max(2, Math.round(h.n * (lob ? 1 : moteK))));
      for (let i = h.n - shown; i < h.n - 1; i++) {
        const j = i * 3, tl = trailLook(i - (h.n - shown), shown, timeS, id, soft);
        if (lob) motes.push(h.pts[j], h.pts[j + 1], h.pts[j + 2], L.lobDotSizeM, c[0], c[1], c[2], 0.35 + 0.5 * tl.size);
        else {
          motes.push(h.pts[j], h.pts[j + 1], h.pts[j + 2], L.trailSizeM * tl.size, c[0], c[1], c[2], L.trailAlpha * tl.alpha);
          if (i & 1) motes.push(h.pts[j], h.pts[j + 1] + 0.05, h.pts[j + 2], L.trailSizeM * 0.45 * tl.size, 1, 1, 1, L.trailAlpha * tl.alpha);
          else motes.push(h.pts[j], h.pts[j + 1], h.pts[j + 2], L.trailSizeM * L.trailHaloMul * tl.size, c[0], c[1], c[2], L.trailHaloAlpha * tl.alpha);
        }
      }
      if (lob && pr.landingP) {
        const m = lobMark(pr.progress ?? (pr.t || 0) / lobS, timeS, soft), e = shadows.take();
        if (e) { lay(e.mesh, pr.landingP, pr.landingN, 0, 0.05); e.mesh.scale.setScalar(m.r); e.u.opacity.value = m.alpha; e.mesh.visible = true; }
        mark(pr.landingP, pr.landingN, m.ring, L.markColour, m.ringAlpha, 0.07);
      }
    }
    for (const id of trails.keys()) if (!seen.has(id)) trails.delete(id);
    for (const id of launches.keys()) if (!seen.has(id)) launches.delete(id);

    // traps: Melt is a puddle; any other kind sits on the road as a low bubble that pulses; either wakes
    // a warning ring as a kart comes near
    for (const tr of view.traps || []) {
      let d2 = Infinity;
      for (const k of karts) {
        if (!k || !k.st) continue;
        const dx = k.st[S.posX] - comp(tr.p, 0), dy = k.st[S.posY] - comp(tr.p, 1), dz = k.st[S.posZ] - comp(tr.p, 2);
        d2 = Math.min(d2, dx * dx + dy * dy + dz * dz);
      }
      const warn = tr.armed ? trapWarn(Math.sqrt(d2)) : 0;
      const ringR = tr.radiusM || (tr.kind === 'melt' ? (feel.get('items.meltRadiusM') || 3.6) : L.trapWarnRingM);
      // The steady footprint is the real contact radius; the warning brightens it
      // rather than changing its size, so the driver can judge the escape line.
      mark(tr.p, tr.n, ringR, MOTE_HEX[tr.kind] || L.trapWarnColour, (tr.armed ? 0.38 : 0.2) + warn * 0.45 * (soft ? 0.5 : 1));
      const alpha = tr.armed ? 1 : L.trapUnarmedAlpha;
      if (tr.kind === 'melt') {
        const e = puddles.take();
        if (!e) continue;
        // materials.js scales UV radius by span to leave room for decorative
        // drips. Compensate the mesh so the solid body reaches the contact ring.
        const span = 1 + L.meltWobble * 0.5 + L.meltDripAmp;
        const R = ringR * L.meltPuddleMul;
        lay(e.mesh, tr.p, tr.n, 0, L.meltLiftM); e.mesh.scale.setScalar(R * span);
        e.mesh.visible = true; e.u.time.value = timeS; e.u.alpha.value = alpha * Math.min(1, (tr.t || 0) * 3 + 0.2); e.u.seed.value = (tr.id | 0) * 1.7;
        const n = Math.round((soft ? 4 : 8) * moteK);
        for (let i = 0; i < n; i++) {
          const a = timeS * 0.45 + i * 6.283 / n + (tr.id | 0), r = R * (0.62 + 0.18 * Math.sin(a * 2));
          W.set(Math.cos(a) * r, 0.2 + (i % 3) * 0.2, Math.sin(a) * r).applyQuaternion(e.mesh.quaternion).add(e.mesh.position);
          motes.push(W.x, W.y, W.z, 0.25 + 0.1 * Math.sin(a), 1, 0.28, 0.65, alpha * (soft ? 0.3 : 0.55));
        }
        continue;
      }
      normal(tr.n);
      const lift = L.trapHoverM + Math.sin(timeS * 1.2 + (tr.id | 0)) * 0.08 * (1 + warn);
      const pulse = 1 + L.trapPulse * (soft ? 0.4 : 1 + warn) * Math.sin(timeS * 6.283 * L.trapPulseHz * (1 + warn) + (tr.id | 0));
      bubble3d(comp(tr.p, 0) + N.x * lift, comp(tr.p, 1) + N.y * lift, comp(tr.p, 2) + N.z * lift, tr.kind, L.trapR * pulse, alpha, (tr.id | 0) * 5, -1, L.hazardMaxFrac);
    }

    // Pink wave: a ring opening along the road
    for (const wv of view.waves || []) {
      const e = waves.take();
      if (!e) break;
      const q = Math.min(1, (wv.t || 0) / 0.9);
      lay(e.mesh, wv.p, null, 0, 0.6);
      e.mesh.scale.setScalar(Math.max(0.5, q * Math.min(wv.rangeM || 40, 40) * 0.25 + L.waveWidthM));
      e.mesh.visible = true; e.u.opacity.value = L.waveAlpha * (1 - q);
    }

    // One real shell stays around the kart, even when Glass and Golden timers overlap.
    for (const sh of selectShieldShells(view.shields, shieldRows)) {
      const k = karts[sh.racer]; if (!k) continue;
      kartPos(k); kartQuat(k); W.set(0, 1, 0).applyQuaternion(QT);
      const glass = sh.kind === 'golden' ? view.shields.find(row => row.racer === sh.racer && row.kind === 'glass') : null;
      const underlay = glass ? shieldLook(glass, feel.get('items.glassMaxS'), sh.racer === 0, soft, underlayState) : null;
      const state = shieldLook(sh, feel.get(sh.kind === 'golden' ? 'items.goldenS' : 'items.glassMaxS'), sh.racer === 0, soft, shieldState, underlay);
      const cx = V.x + W.x * L.shieldUpM, cy = V.y + W.y * L.shieldUpM, cz = V.z + W.z * L.shieldUpM;
      shields.push(cx, cy, cz, QT, state.radius, state.alpha, state.gold, state.warning);
      if (sh.kind === 'golden') addMotes(cx, cy, cz, state.radius * .8, 'golden', Math.round(L.goldMotes * moteK), sh.racer * 11, state.alpha);
    }
    // Echo copies retain their independent timing and presentation.
    for (const sh of view.shields || []) {
      if (sh.kind === 'glass' || sh.kind === 'golden') continue;
      const k = karts[sh.racer];
      if (!k) continue;
      kartQuat(k);
      const fade = Math.min(1, (sh.k === undefined ? 1 : sh.k) * 4);
      const n = Math.min(sh.copies === undefined ? 3 : sh.copies, tierOf(Q.name).copies);
      for (let j = 0; j < n; j++) {
        const e = copies.take();
        if (!e) break;
        const side = j === 0 ? -1 : j === 1 ? 1 : 0, back = j === 2 ? 2 : 1;
        const sway = Math.sin(timeS * 0.9 + j * 2.1) * L.echoSwayM;
        V.set(k.st[S.posX], k.st[S.posY], k.st[S.posZ]);
        W.set(side * L.echoSideM + sway, 0, back * L.echoBackM).applyQuaternion(QT);
        e.obj.position.copy(V).add(W); e.obj.quaternion.copy(QT); e.obj.visible = true;
        const flicker = soft ? 0 : Math.sin(timeS * L.echoFlickerHz * 6.283 + j * 2) * L.echoFlickerAmp;
        e.set(L.echoAlpha * fade * (1 + flicker));
      }
    }

    // pops: a ring that opens and fades, and a spray of motes in the kind's colour
    for (const pop of view.pops || []) {
      const q = popPhase(pop.t || 0, showS);
      if (q >= 1) continue;
      const x = comp(pop.p, 0), y = comp(pop.p, 1) + 0.6, z = comp(pop.p, 2);
      const big = IMPACTS.has(pop.at);
      const [r0, r1] = big ? L.impactPopRingM : L.popRingM, kind = pop.at === 'golden' ? 'golden' : pop.at === 'glass' ? 'glass' : pop.kind;
      bubble(x, y, z, kind, r0 + (r1 - r0) * Math.sqrt(q), (1 - q) * (pop.at === 'decoy' ? 0.5 : 1), pop.at === 'golden' ? MODE.goldShell : MODE.shell, 0, 0);
      const c = TINTS[kind] || TINTS.box, n = Math.round(L.popMotes * (big ? L.impactMotesMul : 1) * tierOf(Q.name).motes / top.motes);
      for (let i = 0; i < n; i++) {
        popMote(i, n, pop.t || 0, OFF, x * 0.37 + z);
        motes.push(x + OFF[0], y + OFF[1], z + OFF[2], L.popMoteSizeM ?? L.moteSizeM * 1.4, c[0], c[1], c[2], 1 - q);
      }
      // an impact: a shockwave ring runs out over the road and a soft glow swells and goes, never more
      // than impactGlowFrac of the view tall; the glow asks photosafe once, only when near and on screen
      // (unseen ones never spend the player's budget), and stays dark when refused
      if (!big) continue;
      // confetti in the kind's colour (and white) and a fountain of glitter, both fading out with the pop
      const seed = Math.round(x * 3.1 + z * 1.7), fade = 1 - q * q, tq = Q.name;
      const nc = L.hitConfetti[tq] || L.hitConfetti.high, nf = Math.round((L.fountain[tq] || L.fountain.high) * (soft ? 0.6 : 1));
      for (let k = 0; k < nc; k++) {
        const cf = confettiAt(k, (pop.t || 0) * 1.4, OFF, seed), white = k % 3 === 2;
        confetti.push(x + OFF[0], y + OFF[1], z + OFF[2], cf.rx, cf.ry, L.confettiSizeM * 0.8, white ? 1 : c[0], white ? 1 : c[1], white ? 1 : c[2], cf.alpha * fade);
      }
      for (let k = 0; k < nf; k++) {
        const fa = impactFountain(k, nf, pop.t || 0, OFF, seed);
        if (fa > 0.01) motes.push(x + OFF[0], y + OFF[1], z + OFF[2], L.fountainSizeM * (k & 1 ? 0.6 : 1), k & 1 ? 1 : c[0], k & 1 ? 1 : c[1], k & 1 ? 1 : c[2], fa * fade);
      }
      const key = kind + pop.at + Math.round(x * 2) + ',' + Math.round(z * 2);
      let g = glows.get(key);
      if (!g) glows.set(key, g = { ok: false, asked: false, f: 0 });
      g.f = frame;
      if (!g.asked && cam) {
        const e = cam.matrixWorld.elements, cp = cam.position;
        if (impactSeen(x - cp.x, y - cp.y, z - cp.z, -e[8], -e[9], -e[10], { maxM: L.impactAskM ?? 60, vFovDeg: cam.fov || 0, aspect: cam.aspect || 16 / 9 })) {
          g.asked = true; g.ok = !!(photosafe && photosafe.request('flash'));
        }
      }
      const ip = impactPhase(pop.t || 0, { glow: g.ok, gentle: soft });
      mark(pop.p, null, ip.ring, MOTE_HEX[kind] || L.markColour, ip.ringA, 0.08);
      // a second shockwave a beat later, narrower, in white
      const ip2 = impactPhase((pop.t || 0) - L.impactRing2S, { glow: false, gentle: soft });
      if ((pop.t || 0) > L.impactRing2S) mark(pop.p, null, ip2.ring * L.impactRing2Mul, '#ffffff', ip2.ringA * 0.8, 0.09);
      if (ip.glowA > 0.003) {
        const p = cam && cam.position, d = p ? Math.hypot(x - p.x, y - p.y, z - p.z) : 99;
        const r = nearCapR(L.impactGlowM, d, cam ? cam.fov : 0, L.impactGlowFrac);
        // lifted by half its radius so the road never slices the bottom off the glow
        motes.push(x, y + r * 0.5, z, r * 2, c[0] * 0.5 + 0.5, c[1] * 0.5 + 0.5, c[2] * 0.5 + 0.5, ip.glowA);
      }
    }
    for (const [k, g] of glows) if (g.f !== frame) glows.delete(k);
    frame++;

    pickupCards.update(dt, soft);
    icons.tick(dt, timeS, soft);
    endAll();
  }

  return {
    update,
    setMediaSource(fn) { pickupCards.setMediaSource(fn); },
    dispose() {
      scene.remove(root);
      status.dispose(); icons.dispose(); bubbles.dispose(); motes.dispose(); plane.dispose(); ghostGeo.dispose(); shields.dispose();
      if (shells) shells.dispose();
      pickupCards.dispose();
      puddles.each((e) => e.mesh.material.dispose()); waves.each((e) => e.mesh.material.dispose());
      marks.each((e) => e.mesh.material.dispose()); shadows.each((e) => e.mesh.material.dispose()); confetti.dispose();
      ghostLook.material.dispose();
    },
  };
}
