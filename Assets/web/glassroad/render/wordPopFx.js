/* render/wordPopFx.js - the juice of a word bubble taken (sim/words.js pop events on the bus as `word-pop`).
 *
 * A pop throws the scene's pop burst (render/particles.js, the shared particle pool), lays a glowing ring on
 * the road under the kart (render/wakes.js) and fires light STREAKS out of the bubble: thin bright dashes,
 * each a sprite rotated every frame to its own direction on screen, growing out fast and shrinking away (IN
 * and OUT). How much of each comes from render/wordBubblesMath.js popJuice: the pop streak grows it, a row
 * or a finished line doubles it up and adds a flare (a white-gold core) that asks photosafe first. Gentle:
 * about half of everything and no flare. One instanced draw for every streak; nothing allocates per frame.
 * Polish 6 (JUICE): the player's item use (`item-fire`, racer 0) throws a RELEASE at the kart on the same
 * pools: a small burst in the item's tint, a ring under the saucer and a few streaks (ITEM_RELEASE numbers;
 * Gentle about half, no new material).
 */
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, AdditiveBlending, Vector3 } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, vec3, float, mix, smoothstep, abs } from 'three/tsl';
import { LOOK } from './look.js';
import { popJuice, POP_JUICE as J } from './wordBubblesMath.js';

const CAP = 96;
const PINK = [1, 0.55, 0.84], WHITE = [1, 0.96, 0.99], GOLD = [1, 0.84, 0.36], CYAN = [0.7, 0.95, 1];
const GOLD_POP = ['#ffd36b', '#fff1c2', '#ffffff', '#ffc2df'];

/** Polish 6: the release at the kart when the player uses an item: burst count, speed, ring size, streaks, tints. */
export const ITEM_RELEASE = Object.freeze({
  count: 18, speedMps: 3.2, sizeM: 0.15, ringM: 2.2, streaks: 6, liftM: 0.5,
  tint: Object.freeze({ flash: '#fff4c2', spiral: '#7fe8ff', brainDrain: '#9dff9a', freeze: '#9fe6ff', blackout: '#b9a6ff', pink: '#ff5fa8',
    melt: '#ff7fbf', shatter: '#e0b0ff', surge: '#ffd36b', glass: '#e8f6ff', echo: '#9dff9a', golden: '#ffd36b' }),
});
/** The release burst colours for an item id: its tint, white and the tint again (unknown ids: the pop colours). */
export function releaseColours(id) {
  const t = ITEM_RELEASE.tint[id];
  return t ? [t, '#ffffff', t] : LOOK.popColours;
}

/** @returns {{ object: Sprite, update(dt: number, camera: object): void, clear(): void, dispose(): void }} */
export function createWordPopFx(scene, { particles = null, wakes = null, kart = null, bus = null, feel = null, photosafe = null } = {}) {
  const aPos = new InstancedBufferAttribute(new Float32Array(CAP * 3), 3).setUsage(DynamicDrawUsage);
  const aV = new InstancedBufferAttribute(new Float32Array(CAP * 4), 4).setUsage(DynamicDrawUsage);   // size, alpha, rotation, hot
  const aC = new InstancedBufferAttribute(new Float32Array(CAP * 3), 3).setUsage(DynamicDrawUsage);
  const pos = instancedDynamicBufferAttribute(aPos, 'vec3'), v = instancedDynamicBufferAttribute(aV, 'vec4'), col = instancedDynamicBufferAttribute(aC, 'vec3');
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = pos;
  m.scaleNode = v.x;
  m.rotationNode = v.z;
  // a dash along the sprite's x: bright head at the right, tapering to nothing at the tail, thin in y
  const p = uv(), along = p.x, across = abs(p.y.sub(0.5));
  const width = float(J.streakW).mul(along.mul(0.7).add(0.3));
  const bar = float(1).sub(smoothstep(width.mul(0.35), width, across));
  const taper = smoothstep(0.02, 0.85, along).mul(float(1).sub(smoothstep(0.92, 1, along)));
  m.colorNode = mix(col, vec3(1, 1, 1), bar.mul(v.w).mul(0.3));
  m.opacityNode = bar.mul(taper).mul(v.y);
  const sprite = new Sprite(m);
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 5; sprite.visible = false;
  scene.add(sprite);

  // the streak pool: struct of arrays
  const px = new Float32Array(CAP), py = new Float32Array(CAP), pz = new Float32Array(CAP);
  const vx = new Float32Array(CAP), vy = new Float32Array(CAP), vz = new Float32Array(CAP);
  const age = new Float32Array(CAP).fill(9), life = new Float32Array(CAP), len = new Float32Array(CAP);
  const cr = new Float32Array(CAP), cg = new Float32Array(CAP), cb = new Float32Array(CAP), hot = new Float32Array(CAP);
  let next = 0, liveN = 0;
  const pending = [];
  const R = new Vector3(), U = new Vector3(), F = new Vector3(), CAM = new Vector3();
  // the lens rule for these additive dashes: a pop sits right under the camera, so the nearest ones fade
  const near = (x, y, z) => {
    const d = Math.hypot(x - CAM.x, y - CAM.y, z - CAM.z), k = (d - J.nearM[0]) / (J.nearM[1] - J.nearM[0]);
    return k <= 0 ? 0.15 : k >= 1 ? 1 : 0.15 + 0.85 * k * k * (3 - 2 * k);
  };
  const gentle = () => !!(feel && feel.get('gentle.on'));
  const off = bus ? bus.on('word-pop', (e) => { if (e && e.p && pending.length < 8) pending.push(e); }) : null;
  const fires = [];
  const offFire = bus ? bus.on('item-fire', (e) => { if (e && e.racer === 0 && fires.length < 4) fires.push(e.id || null); }) : null;
  const REL_P = [0, 0, 0], REL_E = { p: REL_P };

  /** The player's item leaves the kart: a tinted burst, a ring under the saucer and a few streaks. */
  function release(id, g) {
    if (!kart || !kart.visible) return;
    REL_P[0] = kart.position.x; REL_P[1] = kart.position.y + ITEM_RELEASE.liftM; REL_P[2] = kart.position.z;
    const few = g ? 0.55 : 1;
    if (particles) particles.burst(REL_P, Math.round(ITEM_RELEASE.count * few), { colours: releaseColours(id), speed: ITEM_RELEASE.speedMps,
      sizeM: ITEM_RELEASE.sizeM, sparkle: 0.6, petal: 0.1, g: 0.2, near: true });
    if (wakes) wakes.ring([kart.position.x, kart.position.y, kart.position.z], kart.quaternion, ITEM_RELEASE.ringM,
      { gentle: g, gain: LOOK.ringGain * 0.7, colour: ITEM_RELEASE.tint[id] || null });
    spawn(REL_E, { streaks: Math.round(ITEM_RELEASE.streaks * few), gold: id === 'golden' || id === 'surge', flare: false, level: 0.2 }, g);
  }

  function spawn(e, j, g) {
    const n = j.streaks;
    for (let k = 0; k < n; k++) {
      const i = next; next = (next + 1) % CAP;
      // out in every direction, a little more up and toward the camera so they read on screen
      const a = (k / n) * Math.PI * 2 + Math.random() * 0.5, el = (Math.random() - 0.3) * 0.9;
      const sp = J.streakSpeedMps[0] + Math.random() * (J.streakSpeedMps[1] - J.streakSpeedMps[0]);
      const ca = Math.cos(a), sa = Math.sin(a), ce = Math.cos(el);
      vx[i] = (R.x * ca + U.x * sa) * ce * sp + F.x * Math.sin(el) * sp * 0.5;
      vy[i] = (R.y * ca + U.y * sa) * ce * sp + 1.5;
      vz[i] = (R.z * ca + U.z * sa) * ce * sp + F.z * Math.sin(el) * sp * 0.5;
      px[i] = e.p[0]; py[i] = e.p[1]; pz[i] = e.p[2];
      age[i] = 0; life[i] = J.streakLifeS * (0.8 + Math.random() * 0.4) * (g ? 1.6 : 1);
      len[i] = J.streakLenM[0] + Math.random() * (J.streakLenM[1] - J.streakLenM[0]);
      const c = j.gold && k % 2 === 0 ? GOLD : k % 3 === 0 ? CYAN : k % 3 === 1 ? PINK : WHITE;
      cr[i] = c[0]; cg[i] = c[1]; cb[i] = c[2]; hot[i] = j.flare ? 1 : 0.4 + j.level * 0.6;
    }
  }

  function pop(e, g) {
    const j = popJuice(e, { gentle: g });
    if (j.flare && photosafe && !photosafe.request('word-flare')) j.flare = false;
    if (particles && j.particles > 0) {
      particles.burst(e.p, j.particles, { colours: j.gold ? GOLD_POP : LOOK.popColours, speed: LOOK.popSpeedMps * (1.2 + j.level * 0.6),
        sizeM: LOOK.popSizeM * (e.row ? 1.4 : 1.15), petal: 0.25, sparkle: j.gold ? 0.6 : 0.4, g: 0.15 });
    }
    if (wakes && kart && kart.visible) wakes.ring([kart.position.x, kart.position.y, kart.position.z], kart.quaternion, j.ringM,
      { gentle: g, gain: LOOK.ringGain * (j.flare ? 1.2 : 0.8), colour: j.gold ? '#ffd36b' : null });
    spawn(e, j, g);
  }

  function update(dt, camera) {
    if (camera) {
      camera.updateMatrixWorld();
      const me = camera.matrixWorld.elements;
      R.set(me[0], me[1], me[2]); U.set(me[4], me[5], me[6]); F.set(me[8], me[9], me[10]); CAM.set(me[12], me[13], me[14]);
    }
    if (pending.length) { const g = gentle(); for (const e of pending) pop(e, g); pending.length = 0; }
    if (fires.length) { const g = gentle(); for (const id of fires) release(id, g); fires.length = 0; }
    const P = aPos.array, V = aV.array, C = aC.array;
    let n = 0;
    for (let i = 0; i < CAP; i++) {
      if (age[i] >= life[i]) continue;
      age[i] += dt;
      const t = age[i] / life[i];
      if (t >= 1) continue;
      const drag = Math.exp(-dt * 3.2);
      vx[i] *= drag; vy[i] = vy[i] * drag - 4 * dt; vz[i] *= drag;
      px[i] += vx[i] * dt; py[i] += vy[i] * dt; pz[i] += vz[i] * dt;
      // IN: shoots out to full length in the first fifth; OUT: shrinks and fades over the rest
      const grow = Math.min(1, t / 0.2), fade = 1 - Math.max(0, (t - 0.2) / 0.8);
      const sx = vx[i] * R.x + vy[i] * R.y + vz[i] * R.z, sy = vx[i] * U.x + vy[i] * U.y + vz[i] * U.z;
      P[n * 3] = px[i]; P[n * 3 + 1] = py[i]; P[n * 3 + 2] = pz[i];
      V[n * 4] = len[i] * (0.4 + 0.6 * grow) * (0.6 + 0.4 * fade); V[n * 4 + 1] = fade * fade * J.peakAlpha * near(px[i], py[i], pz[i]); V[n * 4 + 2] = Math.atan2(sy, sx); V[n * 4 + 3] = hot[i];
      C[n * 3] = cr[i]; C[n * 3 + 1] = cg[i]; C[n * 3 + 2] = cb[i];
      n++;
    }
    liveN = n;
    sprite.count = n; sprite.visible = n > 0;
    if (n) aPos.needsUpdate = aV.needsUpdate = aC.needsUpdate = true;
  }

  return {
    object: sprite,
    update,
    get live() { return liveN; },
    clear() { age.fill(9); pending.length = 0; fires.length = 0; sprite.count = 0; sprite.visible = false; },
    dispose() { if (off) off(); if (offFire) offFire(); scene.remove(sprite); m.dispose(); },
  };
}
