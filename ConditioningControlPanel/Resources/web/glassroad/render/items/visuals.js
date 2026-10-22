/* render/items/visuals.js - the effect bubbles in the world (CONTRACT-B1 section 3, SCREENFX lane).
 * Every bubble (held over a kart, thrown, lobbed, laid as a trap, a road pickup, a box turning,
 * a shield, a pop ring) is one instance of one sprite cloud wearing the living atlas icon in a glass
 * shell; their drifting motes are one additive cloud. Boxes are spinning DtRH cards (art card-*)
 * that shrink into a bubble when driven through; the Melt trap is a wobbling spiral puddle; the
 * Pink wave a ring; Echo three rim-lit copies. Pools and per-tier caps from render/items/look.js.
 * Gentle: half speed, no Echo flicker. Reads ItemsView only; ages (t) are seconds since spawn.
 */
import { Group, Mesh, PlaneGeometry, SphereGeometry, Vector3, Quaternion, Box3, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { clone as cloneSkinned } from 'three/addons/utils/SkeletonUtils.js';
import { S } from '../../sim/state.js';
import { ITEM_IDS } from '../../sim/itemTable.js';
import { Q } from '../quality.js';
import { ITEM_LOOK as L, ITEM_TIERS, MOTE_TINT } from './look.js';
import { tierOf, comp, moteAt, popPhase, popMote, rollIconAt, createPool } from './fxmath.js';
import { createLivingIcons, cellOf } from './living.js';
import { bubbleCloud, moteCloud, meltPuddle, ghost, ringGlow } from './materials.js';

const UP = new Vector3(0, 1, 0), V = new Vector3(), W = new Vector3(), N = new Vector3(), QT = new Quaternion(), QS = new Quaternion();
const OFF = [0, 0, 0];
const TINTS = Object.fromEntries(Object.entries(MOTE_TINT).map(([k, s]) => [k, s.split(',').map((x) => Number(x) / 255)]));
const MODE = { icon: 0, goldIcon: 1, shell: 2, goldShell: 3 };

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

export function createItemVisuals(scene, art, feel) {
  const top = ITEM_TIERS.high, start = tierOf(Q.name);
  const root = new Group(); root.name = 'item-visuals'; scene.add(root);
  const icons = createLivingIcons({ px: start.iconPx, fps: start.iconFps });
  const bubbles = bubbleCloud(top.bubbles + 24, icons.texture), motes = moteCloud(top.bubbles * top.motes + 24 * L.popMotes);
  root.add(bubbles.sprite, motes.sprite);
  const mask = art && art.tex ? art.tex('sky-spiral-open-mask') : null;
  const gentle = () => !!feel.get('gentle.on');
  let timeS = 0;

  // ---- boxes: one card per track box, built on first sight ----
  const boxes = [], prevAlive = [], turnT = [];
  function buildBox(i) {
    const id = L.boxCards[i % L.boxCards.length], g = art && art.model ? art.model(id) : null;
    const o = new Group();
    if (g) {
      const card = cloneSkinned(g.scene), r = new Box3().setFromObject(card).getBoundingSphere({ radius: 1, center: V }).radius || 1;
      card.scale.setScalar(L.boxSizeM / 2 / r); o.add(card);
    } else {
      const material = new MeshBasicNodeMaterial({ color: L.boxColour, side: DoubleSide });
      o.add(new Mesh(new PlaneGeometry(L.boxSizeM * 0.75, L.boxSizeM), material));
      spare.push(material);
    }
    o.traverse((m) => { if (m.isMesh) m.frustumCulled = false; });
    root.add(o);
    return o;
  }
  const spare = [];

  // ---- pooled meshes: melt puddles, pink waves, echo copies ----
  const plane = new PlaneGeometry(2, 2).rotateX(-Math.PI / 2);
  const puddles = createPool(top.puddles, () => {
    const pm = meltPuddle(mask), mesh = new Mesh(plane, pm.material);
    mesh.frustumCulled = false; mesh.renderOrder = 1; root.add(mesh);
    return { mesh, u: pm.uniforms };
  }, (e) => { e.mesh.visible = false; });
  const waves = createPool(4, () => {
    const g = ringGlow(L.waveColour), mesh = new Mesh(plane, g.material);
    mesh.frustumCulled = false; root.add(mesh);
    return { mesh, u: g.uniforms };
  }, (e) => { e.mesh.visible = false; });
  const kartModel = art && art.model ? art.model('emi-teacup-kart') : null;
  const ghostGeo = new SphereGeometry(0.7, 16, 10).scale(1, 0.8, 1).translate(0, 0.6, 0);
  const copies = createPool(top.copies * 4, () => {
    const g = ghost(L.echoColour);
    let obj;
    if (kartModel) { obj = cloneSkinned(kartModel.scene); obj.traverse((m) => { if (m.isMesh) { m.material = g.material; m.frustumCulled = false; } }); }
    else obj = new Mesh(ghostGeo, g.material);
    root.add(obj);
    return { obj, u: g.uniforms };
  }, (e) => { e.obj.visible = false; });

  // ---- helpers ----
  function addMotes(x, y, z, R, id, n, seed, alpha = 1) {
    const c = TINTS[id] || TINTS.box;
    for (let i = 0; i < n; i++) {
      // timeS already runs at the Gentle rate (update); slowing it again here would quarter it
      const m = moteAt(i, n, timeS, R, OFF, seed);
      motes.push(x + OFF[0], y + OFF[1], z + OFF[2], m.size, c[0], c[1], c[2], m.alpha * alpha);
    }
  }
  let drawn = 0;
  function bubble(x, y, z, id, R, alpha = 1, mode = MODE.icon, seed = 0, moteN = -1) {
    if (drawn >= tierOf(Q.name).bubbles) return;
    const cell = mode >= MODE.shell ? -1 : cellOf(id);
    if (cell >= 0) icons.use(id);
    if (!bubbles.push(x, y, z, (R * 2) / (mode >= MODE.shell ? L.shellFill : L.iconFill), cell, alpha, mode, seed)) return;
    drawn++;
    const n = moteN >= 0 ? moteN : tierOf(Q.name).motes;
    if (n > 0) addMotes(x, y, z, R, id, n, seed, alpha);
  }
  const kartPos = (k) => V.set(k.st[S.posX], k.st[S.posY], k.st[S.posZ]);
  const kartQuat = (k) => QT.set(k.st[S.quatX], k.st[S.quatY], k.st[S.quatZ], k.st[S.quatW]);

  function update(view, dt, karts = []) {
    dt = Math.max(0, Math.min(dt || 0, 0.1));
    const soft = gentle(), rate = soft ? 0.5 : 1;
    timeS += dt * rate;
    bubbles.uniforms.time.value = timeS;
    bubbles.begin(); motes.begin(); puddles.begin(); waves.begin(); copies.begin(); drawn = 0;
    if (!view) { bubbles.end(); motes.end(); puddles.end(); waves.end(); copies.end(); return; }
    const showS = feel.get('items.popShowS') || 0.6;

    // boxes: spinning cards; driven through, the card spins away and a bubble rises out of it
    (view.boxes || []).forEach((b, i) => {
      const o = boxes[i] || (boxes[i] = buildBox(i));
      if (prevAlive[i] && !b.alive) turnT[i] = 0;
      prevAlive[i] = b.alive;
      const turning = turnT[i] !== undefined && turnT[i] < L.boxTurnS + showS;
      const bob = Math.sin(timeS * L.boxBobHz * 6.283 + i) * L.boxBobM;
      lay(o, b.p, b.n, timeS * L.boxSpinRadps * 1.0 + i, L.boxHoverM + bob);
      if (turning) {
        turnT[i] += dt;
        const q = Math.min(1, turnT[i] / L.boxTurnS), rise = Math.max(0, turnT[i] - L.boxTurnS) / showS;
        o.visible = q < 1; o.scale.setScalar(1 - q);
        o.quaternion.multiply(QS.setFromAxisAngle(UP, q * q * 9));
        bubble(o.position.x + N.x * rise, o.position.y + N.y * rise, o.position.z + N.z * rise, 'box', L.bubbleR * Math.min(1, q * 1.4), 1 - rise, MODE.icon, i);
      } else {
        turnT[i] = undefined;
        o.visible = !!b.alive; o.scale.setScalar(Math.max(0.01, b.k === undefined ? 1 : b.k));
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
      const bob = Math.sin(timeS * L.heldBobHz * 6.283 + h.racer) * L.heldBobM * (me ? 0.5 : 1);
      kartPos(k); W.set(sx, sy + bob, sz).applyQuaternion(kartQuat(k)).add(V);
      const id = h.rolling ? rollIconAt(h.rollT, ITEM_IDS, soft ? L.rollSwapGentleS : L.rollSwapS) : h.id;
      bubble(W.x, W.y, W.z, id || 'box', L.heldR * (me ? L.playerHeldScale : 1), 1, MODE.icon, h.racer * 7, me ? 4 : -1);
    }

    // throws and lobs: the sim's p already carries the arc
    for (const pr of view.projectiles || []) {
      bubble(comp(pr.p, 0), comp(pr.p, 1), comp(pr.p, 2), pr.kind, pr.mode === 'lob' ? L.lobR : L.throwR, 1, MODE.icon, (pr.id | 0) * 3);
    }

    // traps: Melt is a puddle; any other kind sits on the road as a low bubble
    for (const tr of view.traps || []) {
      const alpha = tr.armed ? 1 : L.trapUnarmedAlpha;
      if (tr.kind === 'melt') {
        const e = puddles.take();
        if (!e) continue;
        const R = (feel.get('items.meltRadiusM') || 2.4) * L.meltPuddleMul;
        lay(e.mesh, tr.p, tr.n, 0, L.meltLiftM); e.mesh.scale.setScalar(R);
        e.mesh.visible = true; e.u.time.value = timeS; e.u.alpha.value = alpha * Math.min(1, (tr.t || 0) * 3 + 0.2); e.u.seed.value = (tr.id | 0) * 1.7;
        continue;
      }
      normal(tr.n);
      const lift = L.trapHoverM + Math.sin(timeS * 1.2 + (tr.id | 0)) * 0.08;
      bubble(comp(tr.p, 0) + N.x * lift, comp(tr.p, 1) + N.y * lift, comp(tr.p, 2) + N.z * lift, tr.kind, L.trapR, alpha, MODE.icon, (tr.id | 0) * 5);
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

    // shields: glass shell, golden shell plus a slow gold shimmer, Echo copies beside the kart
    for (const sh of view.shields || []) {
      const k = karts[sh.racer];
      if (!k) continue;
      kartPos(k); kartQuat(k); W.set(0, 1, 0).applyQuaternion(QT);
      const cx = V.x + W.x * L.shieldUpM, cy = V.y + W.y * L.shieldUpM, cz = V.z + W.z * L.shieldUpM;
      const fade = Math.min(1, (sh.k === undefined ? 1 : sh.k) * 4);
      if (sh.kind === 'glass' || sh.kind === 'golden') {
        bubble(cx, cy, cz, sh.kind, L.shieldR, fade, sh.kind === 'golden' ? MODE.goldShell : MODE.shell, sh.racer, 0);
        if (sh.kind === 'golden') addMotes(cx, cy, cz, L.shieldR * 0.8, 'golden', Math.round(L.goldMotes * tierOf(Q.name).motes / top.motes), sh.racer * 11, fade);
        continue;
      }
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
        e.u.opacity.value = L.echoAlpha * fade * (1 + flicker);
      }
    }

    // pops: a ring that opens and fades, and a spray of motes in the kind's colour
    for (const pop of view.pops || []) {
      const q = popPhase(pop.t || 0, showS);
      if (q >= 1) continue;
      const x = comp(pop.p, 0), y = comp(pop.p, 1) + 0.6, z = comp(pop.p, 2);
      const [r0, r1] = L.popRingM, kind = pop.at === 'golden' ? 'golden' : pop.at === 'glass' ? 'glass' : pop.kind;
      bubble(x, y, z, kind, r0 + (r1 - r0) * Math.sqrt(q), (1 - q) * (pop.at === 'decoy' ? 0.5 : 1), pop.at === 'golden' ? MODE.goldShell : MODE.shell, 0, 0);
      const c = TINTS[kind] || TINTS.box, n = Math.round(L.popMotes * tierOf(Q.name).motes / top.motes);
      for (let i = 0; i < n; i++) {
        popMote(i, n, pop.t || 0, OFF, x * 0.37 + z);
        motes.push(x + OFF[0], y + OFF[1], z + OFF[2], L.moteSizeM * 1.4, c[0], c[1], c[2], 1 - q);
      }
    }

    icons.tick(dt, timeS, soft);
    bubbles.end(); motes.end(); puddles.end(); waves.end(); copies.end();
  }

  return {
    update,
    dispose() {
      scene.remove(root);
      icons.dispose(); bubbles.dispose(); motes.dispose(); plane.dispose(); ghostGeo.dispose();
      puddles.each((e) => e.mesh.material.dispose()); waves.each((e) => e.mesh.material.dispose());
      copies.each((e) => { e.obj.traverse((m) => { if (m.isMesh && m.material) m.material.dispose(); }); });
      for (const m of spare) m.dispose();
    },
  };
}
