/* render/items/status.js - hits everyone can read, and throws you can follow (music road pass 2, owner item 7).
 *
 * Three layers in one instanced sprite draw (render/items/statusMath.js has the numbers and the pure parts):
 * - status looks: every racer under an effect wears its kind's look (cards round a Flashes victim, a pink
 *   cloud, a dark cloud, frost and crystals, a spiral overhead, a rising green funnel, pink drips, prism
 *   shards), so every OTHER player sees what is happening to it; IN pops, OUT shrinks away. The player's own
 *   look is smaller and fainter (the screen effect is already theirs).
 * - throws: each flying bubble gets a glowing comet head, a tapering trail of light and sparks shed along it;
 *   it launches with a spark puff and lands with a burst.
 * - hits: the first frame a racer's effect starts, a burst in the kind's colours and a shock ring at the kart.
 * Gentle: half speed, no overshoot, fewer sparks; every ring asks photosafe once (refused: a faint ring).
 * Reads the racers' fxv.effect (sim/items.js writeMods) and ItemsView.projectiles; never writes either.
 */
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, Vector3, Quaternion } from 'three/webgpu';
import {
  instancedDynamicBufferAttribute, uv, vec2, vec3, float, mix, smoothstep, sin, cos, atan, log, length, max, min, abs, step, pow,
} from 'three/tsl';
import { S } from '../../sim/state.js';
import { MOTE_TINT } from './look.js';
import { STATUS_LOOK as L, SHAPE, statusStep, statusState, statusSprites, createBurstPool, createTrail } from './statusMath.js';
import { lensDepth, lensScale, lensAlpha } from '../lens.js';
import { createMalusSpirals } from './malusSpirals.js';

const TINT = Object.fromEntries(Object.entries(MOTE_TINT).map(([k, s]) => [k, s.split(',').map((x) => Number(x) / 255)]));
const WHITE = [1, 1, 1];
const colOf = (kind) => (L.kinds[kind] ? L.kinds[kind].col : TINT[kind] || WHITE);
const altOf = (kind) => (L.kinds[kind] ? L.kinds[kind].alt : WHITE);

/** 1 where shape == k (a TSL float), else 0. */
const is = (shape, k) => float(1).sub(step(0.5, abs(shape.sub(k))));

/** The status sprite material: per instance pos, size, info (shape, alpha, spin, glow), col (rgb, white core). */
function statusMaterial(cap) {
  const aPos = new InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(cap), 1).setUsage(DynamicDrawUsage);
  const aInfo = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const aCol = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const pos = instancedDynamicBufferAttribute(aPos, 'vec3'), size = instancedDynamicBufferAttribute(aSize, 'float');
  const info = instancedDynamicBufferAttribute(aInfo, 'vec4'), col = instancedDynamicBufferAttribute(aCol, 'vec4');
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  const d = lensDepth(pos);
  m.positionNode = pos; m.scaleNode = lensScale(size, d); m.rotationNode = info.z;
  const shape = info.x, p = uv().sub(0.5).mul(2), r = length(p), ang = atan(p.y, p.x);
  const disc = (a, b) => float(1).sub(smoothstep(a, b, r));
  // each shape as a coverage mask (0..1) and a white core share
  const puff = disc(0.25, 1).mul(sin(ang.mul(5).add(shape)).mul(0.12).add(0.88));
  const bx = abs(p.x).sub(0.56), by = abs(p.y).sub(0.86), box = length(max(vec2(bx, by), 0)).add(min(max(bx, by), 0));
  const card = float(1).sub(smoothstep(-0.02, 0.04, box.add(0.06)));
  const cardRim = card.mul(smoothstep(-0.2, -0.1, box));
  const pip = disc(0.18, 0.26);                                   // the card's centre pip
  const arm = sin(ang.mul(2).sub(log(r.max(0.02)).mul(4.5))).mul(0.5).add(0.5);
  const spiral = disc(0.85, 1).mul(smoothstep(0.45, 0.6, arm));
  const star = max(float(1).sub(smoothstep(0, 0.1, abs(p.x).mul(abs(p.y).add(0.12)).mul(6))), disc(0, 0.35)).mul(disc(0.8, 1));
  const dia = abs(p.x).add(abs(p.y).mul(0.55));
  const crystal = float(1).sub(smoothstep(0.62, 0.7, dia)).mul(smoothstep(0.08, 0.14, abs(p.x)).mul(0.35).add(0.65));
  const tri = max(abs(p.x).mul(1.5).add(p.y.mul(0.85)), p.y.negate()).sub(0.55);
  const shard = float(1).sub(smoothstep(0, 0.06, tri));
  const dropD = length(vec2(p.x, p.y.add(0.25))).sub(0.55).min(abs(p.x).mul(1.6).add(p.y.mul(0.9)).sub(0.4).max(p.y.negate().sub(0.25)));
  const drop = float(1).sub(smoothstep(0, 0.06, dropD));
  const ring = float(1).sub(smoothstep(0.06, 0.16, abs(r.sub(0.8))));
  const hx = p.x.mul(1.15), hy = p.y.mul(1.15).add(0.2);
  const hq = hx.mul(hx).add(hy.mul(hy)).sub(0.55);
  const heart = float(1).sub(smoothstep(-0.02, 0.02, hq.mul(hq).mul(hq).sub(hx.mul(hx).mul(pow(hy.max(0), 3)).mul(0.6))));
  const mask = puff.mul(is(shape, SHAPE.puff)).add(card.mul(is(shape, SHAPE.card))).add(spiral.mul(is(shape, SHAPE.spiral)))
    .add(star.mul(is(shape, SHAPE.star))).add(crystal.mul(is(shape, SHAPE.crystal))).add(shard.mul(is(shape, SHAPE.shard)))
    .add(drop.mul(is(shape, SHAPE.drop))).add(ring.mul(is(shape, SHAPE.ring))).add(heart.mul(is(shape, SHAPE.heart)));
  // colour: a card is white with a coloured rim and pip; everything else is its colour with a hotter core
  const isCard = is(shape, SHAPE.card);
  const tint = vec3(col.x, col.y, col.z);
  const core = disc(0, 0.7).mul(col.w);
  const plain = mix(tint, vec3(1, 1, 1), core.mul(0.6));
  const cardCol = mix(vec3(1, 0.98, 0.99), tint, max(cardRim, pip));
  m.colorNode = mix(plain, cardCol, isCard).mul(info.w.add(1));
  m.opacityNode = mask.min(1).mul(info.y).mul(lensAlpha(d));
  const sprite = new Sprite(m);
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 4;
  let n = 0;
  return {
    sprite, material: m,
    begin() { n = 0; },
    push(x, y, z, s, shp, a, spin, c, glow = 0, coreW = 0.5) {
      if (n >= cap || a <= 0.004 || s <= 0.001) return;
      aPos.array[n * 3] = x; aPos.array[n * 3 + 1] = y; aPos.array[n * 3 + 2] = z; aSize.array[n] = s;
      const i = aInfo.array; i[n * 4] = shp; i[n * 4 + 1] = Math.min(1, a); i[n * 4 + 2] = spin; i[n * 4 + 3] = glow;
      const q = aCol.array; q[n * 4] = c[0]; q[n * 4 + 1] = c[1]; q[n * 4 + 2] = c[2]; q[n * 4 + 3] = coreW;
      n++;
    },
    end() {
      sprite.count = n; sprite.visible = n > 0;
      aPos.needsUpdate = aSize.needsUpdate = aInfo.needsUpdate = aCol.needsUpdate = true;
    },
    get n() { return n; },
  };
}

const V = new Vector3(), Q = new Quaternion(), OFF = new Vector3();
const GOPT = [{ gentle: false }, { gentle: true }];

/** @returns {{ update(view: object, dt: number, karts: object[], opts: object): void, clear(): void, dispose(): void }} */
export function createItemStatus(root, feel, { photosafe = null, cap = 1100, track = null, waterVolume = null } = {}) {
  const cloud = statusMaterial(cap);
  const malusSpirals = createMalusSpirals(root, feel, { track, waterVolume });
  root.add(cloud.sprite);
  const pool = createBurstPool(640);
  const states = [], seeds = [];
  const trails = new Map();         // projectile id -> { trail, kind, seen }
  const spareTrails = [];
  const rings = [];                 // { p: [x,y,z], t, col, gain }
  let seed = 9, timeS = 0, frameId = 0;
  const rand = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
  const gentle = () => !!feel.get('gentle.on');
  const LAST = [0, 0, 0], HERE = [0, 0, 0], PC = [0, 0, 0];
  // one status sprite into the cloud, in the current racer's kart space (ctx set per racer: no closures per frame)
  const ctx = { sc: 1, al: 1, col: WHITE, alt: WHITE, dark: false };
  const putStatus = (x, y, z, size, shp, a, spin, useAlt) => {
    OFF.set(x * ctx.sc, y * ctx.sc, z * ctx.sc).applyQuaternion(Q).add(V);
    cloud.push(OFF.x, OFF.y, OFF.z, size * ctx.sc, shp, a * ctx.al, spin, useAlt ? ctx.alt : ctx.col, ctx.dark ? 0 : 0.25, ctx.dark ? 0 : 0.5);
  };
  const putTrail = (x, y, z, f) => cloud.push(x, y, z, 1.15 * (1 - f * 0.8), SHAPE.puff, 0.75 * (1 - f), 0, f < 0.3 ? WHITE : ctx.col, 0.3, 0.7);
  const putSpark = (x, y, z, size, shp, a, cr, cg, cb) => { PC[0] = cr; PC[1] = cg; PC[2] = cb; cloud.push(x, y, z, size, shp, a, timeS * 2, PC, 0.4, 0.6); };

  function ring(p, kind, big = 1) {
    if (rings.length >= 16) rings.shift();
    const ok = !gentle() && (!photosafe || photosafe.request('item-status'));
    rings.push({ p: [p[0], p[1], p[2]], t: 0, col: colOf(kind), gain: (ok ? 1 : 0.35) * big });
  }

  function hitBurst(p, kind, soft, self) {
    const n = Math.round(L.burstN * (soft ? 0.5 : 1) * (self ? 0.6 : 1));
    const spec = L.kinds[kind];
    const shp = kind === 'flash' ? SHAPE.card : kind === 'pink' ? SHAPE.heart : kind === 'shatter' ? SHAPE.shard : SHAPE.star;
    pool.burst(p, n, L.burstSpeed * (soft ? 0.6 : 1), L.burstLifeS, L.burstSize * (shp === SHAPE.card ? 1.4 : 1), shp,
      spec ? spec.col : colOf(kind), spec ? spec.alt : WHITE, rand);
    ring(p, kind, self ? 0.6 : 1);
  }

  function update(view, dt, karts = [], musicView = {}) {
    dt = Math.max(0, Math.min(dt || 0, 0.1));
    const soft = gentle(), rate = soft ? 0.5 : 1;
    timeS += dt * rate; frameId++;
    cloud.begin();
    malusSpirals.update(karts, dt, musicView);
    const riseS = (feel.get('gentle.riseMs') || 300) / 1000;

    // status looks on every racer
    for (let i = 0; i < karts.length; i++) {
      const r = karts[i], st = r && r.st;
      if (!st) continue;
      const s = states[i] || (states[i] = statusState());
      if (seeds[i] === undefined) seeds[i] = i * 2.399;
      const k = statusStep(s, r.fxv ? r.fxv.effect : null, dt, { gentle: soft, riseS });
      if (!s.kind) continue;
      V.set(st[S.posX], st[S.posY], st[S.posZ]);
      Q.set(st[S.quatX], st[S.quatY], st[S.quatZ], st[S.quatW]);
      const self = i === 0, sc = self ? L.selfScale : 1, al = self ? L.selfAlpha : 1;
      if (s.fresh && dt > 0) { HERE[0] = V.x; HERE[1] = V.y + 1; HERE[2] = V.z; hitBurst(HERE, s.kind, soft, self); }
      if (al <= 0) continue;
      ctx.sc = sc; ctx.al = al; ctx.col = colOf(s.kind); ctx.alt = altOf(s.kind); ctx.dark = s.kind === 'blackout';
      statusSprites(s.kind, s.age, k, seeds[i], putStatus, GOPT[soft ? 1 : 0]);
    }

    // throws: comet heads, trails and sparks; a landing bursts where the trail ended
    const pr = (view && view.projectiles) || [];
    for (const p of pr) {
      let e = trails.get(p.id);
      if (!e) {
        e = { trail: spareTrails.pop() || createTrail(28, 0.4), kind: p.kind, seen: 0 };
        e.trail.reset(); trails.set(p.id, e);
        if (dt > 0) pool.burst(p.p, soft ? 8 : 16, 3.5, 0.45, 0.3, SHAPE.star, colOf(p.kind), WHITE, rand, 1);
      }
      e.seen = frameId; e.kind = p.kind;
      e.trail.push(p.p[0], p.p[1], p.p[2]);
      const col = colOf(p.kind), pulse = 1 + Math.sin(timeS * 9) * (soft ? 0 : 0.12);
      // the head: a big soft glow, a turning star core
      cloud.push(p.p[0], p.p[1], p.p[2], 1.9 * pulse, SHAPE.puff, 0.85, 0, col, 0.6, 0.9);
      cloud.push(p.p[0], p.p[1], p.p[2], 1.1, SHAPE.star, 1, timeS * 3, WHITE, 0.4, 1);
      // the trail: wide and bright at the head, thin and faint at the tail
      ctx.col = col;
      e.trail.points(putTrail);
      if (dt > 0 && rand() < (soft ? 0.35 : 0.8)) {
        pool.spawn(p.p[0], p.p[1], p.p[2], (rand() - 0.5) * 2, rand() * 1.5, (rand() - 0.5) * 2, 0.55, 0.28, SHAPE.star, rand() < 0.5 ? col : WHITE, -3, 2);
      }
    }
    for (const [id, e] of trails) {
      if (e.seen === frameId) continue;
      // gone this frame: it landed (a hit, a block, a fizzle); burst where it last was
      if (e.trail.n && dt > 0) {
        e.trail.last(LAST);
        pool.burst(LAST, soft ? 10 : 22, 5, 0.6, 0.38, SHAPE.star, colOf(e.kind), WHITE, rand);
        ring(LAST, e.kind, 0.8);
      }
      trails.delete(id); spareTrails.push(e.trail);
    }

    // shock rings and the particle store
    for (let j = rings.length - 1; j >= 0; j--) {
      const g = rings[j];
      g.t += dt;
      const f = g.t / L.ringLifeS;
      if (f >= 1) { rings.splice(j, 1); continue; }
      cloud.push(g.p[0], g.p[1], g.p[2], L.ringSize * (0.25 + 0.75 * (1 - (1 - f) * (1 - f))), SHAPE.ring, (1 - f) * g.gain, 0, g.col, 0.5, 0.3);
    }
    pool.step(dt);
    pool.each(putSpark);
    cloud.end();
  }

  return {
    update,
    clear() { malusSpirals.clear(); pool.clear(); rings.length = 0; trails.clear(); for (const s of states) if (s) { s.kind = null; s.outAge = -1; } },
    dispose() { malusSpirals.dispose(); root.remove(cloud.sprite); cloud.material.dispose(); },
  };
}
