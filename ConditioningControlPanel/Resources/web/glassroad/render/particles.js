/* render/particles.js - one pooled additive sprite cloud for every kart (B1 juice pass, wave 2 atlas).
 *
 * With Astra's particle atlas (`particles-atlas`, render/atlas.js) every sprite draws one atlas cell
 * tinted by its colour: drift sparks are streaks turned along their screen velocity (glints join at
 * tier 3, petals by the tier's petal share), boost exhaust soft discs and glitter, slipstream smoke,
 * landing rings, bump shards and glints, pickup pops bubble rims and glitter, item hits frost crystals
 * (Freeze), stars and glitter (Golden) and drips (Melt). Without the atlas the B1 shader shapes draw
 * (0 disc, 1 sparkle with four arms, 2 petal). Ring allocation over the tier budget (Q.particles);
 * one draw call. CPU integrates, the GPU reads its attributes.
 * Polish 2 (numbers in atlas.js FX2_LOOK, LOOK.fx2 wins): drift sparks mix the tier colour with white-hot
 * and next-tier accents and shed floating embers; the boost flame is soft tongues born white at the saucer
 * that swell into the boost colour (a colour ramp and growth per particle) with glitter shot through;
 * slipstream adds long streaks turned along their motion; every effect hit sprays its own cells and tint.
 * Extras run at FX2_LOOK.phoneExtraMul on the phone tier; the ring buffer keeps the frame cost flat.
 * Polish 3 "spectacle" (numbers in atlas.js JUICE_LOOK, LOOK.juice wins): every effect 2 to 4 times bigger
 * or brighter so it reads at 1440p at full speed. Drift sparks grow and a tier-up bursts with a halo ring;
 * a boost start bursts with a ring and the flame doubles; wisps you can see; bumps throw stars, shards, a
 * hot core and a shockwave ring; landings, pops, boxes and item hits grow and take the spectrum
 * (render/spectrum.js through atlas.js juiceHues). Gentle keeps half of every growth and 0.6 of the
 * counts; mid and phone tiers scale the added counts (tierShare).
 * Polish 4 "control and payoff" (numbers in atlas.js P4_LOOK, LOOK.p4 wins): drift tiers wear blue, orange,
 * pink (atlas.js tierColour) and their sparks grow in size, rate and a share of stars per tier. event()
 * takes the FEEL lane's bus events by name: drift-start (a puff at the rear wheels), drift-tier (the tier-up
 * burst), drift-boost (the mini-turbo burst: a cone thrown back, stars round the saucer, a tier halo), trick
 * (confetti thrown up at takeoff) and trick-land (a ring and confetti). Once a kart has had drift-tier or
 * drift-boost events, the state-edge fallbacks for the same moments stand down (no double bursts).
 * Polish 5 (numbers in atlas.js BIRTH_LOOK): every particle is born over about 50 ms, from a third of its size
 * and no alpha, eased out, so nothing pops in as a hard tick; drift spark streaks are a touch fatter at tier 1.
 * The ring keeps a high-water mark: only the slots below it are drawn and uploaded (addUpdateRange).
 */
import { Sprite, SpriteNodeMaterial, AdditiveBlending, InstancedBufferAttribute, DynamicDrawUsage, Color, Vector3 } from 'three/webgpu';
import {
  instancedDynamicBufferAttribute, uv, smoothstep, vec2, vec4, abs, max, select, texture, fwidth, log2, clamp, mod, floor, atan,
  cameraViewMatrix,
} from 'three/tsl';
import { S, BOOST } from '../sim/state.js';
import { LOOK } from './look.js';
import { Q } from './quality.js';
import { sparkProfile } from './kartPose.js';
import { currentArt } from './art.js';
import { lensDepth, lensScale, lensAlpha } from './lens.js';
import { LENS } from './lensMath.js';
import {
  CELLS, GRID, ATLAS_PX, MAX_LOD, FXART_LOOK, FAMILIES, UPRIGHT, ALIGNED, cellIndex, cellInset, legacyShape, sparkFamily, pickCell,
  familyOf, hitFamily, FX2_LOOK, hitColours, juiceLook, juiceHues, burstBoost, gentleGrow, tierShare,
  p4Look, tierColour, trickKey, BIRTH_LOOK, birthK, birthSize, sparkWidth, ringSlot,
} from './atlas.js';

export const SHAPE = Object.freeze({ disc: 0, sparkle: 1, petal: 2 });

function shapeNode(kind) {
  const q = uv().sub(0.5).mul(2), r = q.length();
  const disc = smoothstep(0.25, 1, r).oneMinus();
  const arm = (a, b) => smoothstep(0, 0.14, abs(a)).oneMinus().mul(smoothstep(0.15, 1, abs(b)).oneMinus());
  const sparkle = max(smoothstep(0, 0.4, r).oneMinus(), max(arm(q.x, q.y), arm(q.y, q.x)));
  const petal = smoothstep(0.45, 1, vec2(q.x.mul(2.1), q.y.mul(q.y.mul(0.35).add(1))).length()).oneMinus();
  return select(kind.lessThan(0.5), disc, select(kind.lessThan(1.5), sparkle, petal));
}

/** The particle atlas and how to read it, from an art set; null when it is missing. */
export function atlasOf(art) {
  const tex = art && art.tex ? art.tex('particles-atlas') : null;
  if (!tex) return null;
  const row = (art.row && art.row('particles-atlas')) || {};
  const sizePx = row.width || ATLAS_PX, maxLod = Number.isFinite(row.maxSafeLod) ? row.maxSafeLod : MAX_LOD;
  return { tex, cells: row.cells || CELLS, sizePx, maxLod, inset: cellInset(sizePx, maxLod) };
}

/**
 * Coverage (.a) of atlas cell idx (a float node, row * 4 + col) at sprite uv q (y up): the cell's
 * top is image row 0 (v = 0 with flipY false, KTX2), half-texel inset at the clamped mip, and the mip
 * picked from the pixel footprint but never past maxLod (smaller mips mix neighbouring cells).
 */
export function atlasCoverage(at, idx, q = uv()) {
  const s = 1 - 2 * at.inset;
  const col = mod(idx, GRID), row = floor(idx.div(GRID));
  const top = row.add(at.inset).add(q.y.oneMinus().mul(s)).div(GRID);
  const a = vec2(col.add(at.inset).add(q.x.mul(s)).div(GRID), at.tex.flipY ? top.oneMinus() : top);
  const fw = fwidth(a);
  const lod = clamp(log2(max(fw.x, fw.y).mul(at.sizePx)), 0, at.maxLod);
  return texture(at.tex, a).level(lod).a;
}

export function createParticles(feel, art = currentArt()) {
  const max0 = Q.particles, at = atlasOf(art), A = LOOK.fxArt || FXART_LOOK;
  const aPos = new InstancedBufferAttribute(new Float32Array(max0 * 3), 3).setUsage(DynamicDrawUsage);
  const aCol = new InstancedBufferAttribute(new Float32Array(max0 * 4), 4).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(max0), 1).setUsage(DynamicDrawUsage);
  const aRot = new InstancedBufferAttribute(new Float32Array(max0 * 2), 2).setUsage(DynamicDrawUsage);   // shape or cell, angle
  const aVel = new InstancedBufferAttribute(new Float32Array(max0 * 3), 3).setUsage(DynamicDrawUsage);   // streaks turn along it
  const vel = aVel.array, life = new Float32Array(max0), maxLife = new Float32Array(max0);
  const size = new Float32Array(max0), spin = new Float32Array(max0), grav = new Float32Array(max0);
  // polish 4: a light-drag flag per particle (confetti hangs with the kart instead of falling behind)
  const light = new Uint8Array(max0);
  let lightNow = 0;
  // polish 2: a colour ramp from birth to death (flame: white core to the boost colour) and growth over life
  const ramp = new Float32Array(max0 * 3), hasRamp = new Uint8Array(max0), grow = new Float32Array(max0);
  const B = LOOK.fx2 || FX2_LOOK, J = juiceLook(LOOK), HUES = juiceHues(), P4 = p4Look(LOOK);
  const tierCol = (t) => tierColour(t, P4);
  const extra = () => (Q.name === 'phone' ? B.phoneExtraMul : 1);
  const gentle = () => !!(feel && feel.get('gentle.on'));
  const big = (m) => gentleGrow(m, gentle(), J);                     // a growth, halved under Gentle
  const more = () => tierShare(Q.name, J) * (gentle() ? J.gentleCount : 1);   // share of the added counts
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const posN = instancedDynamicBufferAttribute(aPos, 'vec3');
  m.positionNode = posN;
  // the lens rule (render/lens.js): a sprite near the camera is capped to a small share of the view and fades
  // out before it reaches the lens, so no burst or flame can fill the view with additive white
  const lensD = lensDepth(posN), sizeN = instancedDynamicBufferAttribute(aSize, 'float');
  m.scaleNode = lensScale(sizeN, lensD);
  const rot = instancedDynamicBufferAttribute(aRot, 'vec2');
  const c = instancedDynamicBufferAttribute(aCol, 'vec4');
  m.colorNode = c.rgb;
  if (at) {
    // straight alpha, white RGB: the additive blend multiplies the tint by coverage once
    const idx = floor(rot.x.add(0.5));
    // a shockwave ring is a thin outline, not an area: it may open ringCapMul times wider than a sprite
    const sc = select(idx.equal(cellIndex('ring', at.cells)), lensScale(sizeN, lensD, { ...LENS, nearFrac: LENS.nearFrac * LENS.ringCapMul, farFrac: LENS.farFrac * LENS.ringCapMul }), m.scaleNode);
    // a spark streak keeps its width share in rot.y (it turns along its velocity, so it has no angle of its own)
    m.scaleNode = select(idx.equal(cellIndex('spark', at.cells)), vec2(sc.mul(rot.y), sc), vec2(sc, sc));
    const vv = cameraViewMatrix.mul(vec4(instancedDynamicBufferAttribute(aVel, 'vec3'), 0)).xy;
    m.rotationNode = select(idx.equal(cellIndex('spark', at.cells)), atan(vv.y, vv.x).sub(Math.PI / 2), rot.y);
    m.opacityNode = c.a.mul(atlasCoverage(at, idx)).mul(lensAlpha(lensD));
  } else {
    m.rotationNode = rot.y;
    m.opacityNode = c.a.mul(shapeNode(rot.x)).mul(lensAlpha(lensD));
  }
  const sprite = new Sprite(m);
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 2;
  const BL = BIRTH_LOOK, attrs = [aPos, aCol, aSize, aRot, aVel];
  let widthNow = 1, hw = 0;
  const alive = (i) => life[i] > 0;

  let head = 0, hue = 0;
  const cols = new Map(), info = new Map(), tmp = new Vector3(), dir = new Vector3(), carry = new Vector3();
  const cap = () => Math.min(max0, Q.particles);
  const rnd = (a, b) => a + Math.random() * (b - a);
  const nextHue = () => HUES[(hue++) % HUES.length];
  const rgb = (hex) => { let v = cols.get(hex); if (!v) { const k = new Color(hex); v = [k.r, k.g, k.b]; cols.set(hex, v); } return v; };
  /** Per cell name: the attribute code, size share, upright. */
  const cellOf = (name) => {
    let v = info.get(name);
    if (!v) {
      v = at ? { code: cellIndex(name, at.cells), size: A.cellSize[name] ?? 1, upright: UPRIGHT.has(name) || ALIGNED.has(name), aligned: name === 'spark' }
        : { code: legacyShape(name), size: 1, upright: false, aligned: false };
      info.set(name, v);
    }
    return v;
  };

  /** World-space emit; cell = an atlas cell name (render/atlas.js CELLS). */
  /** hex2: the colour it ends as (a ramp from hex), gr: how much it grows over its life (0 = shrinks as before). */
  function emit(x, y, z, vx, vy, vz, hex, sz, lifeS, cell = 'disc', g = 1, sp = 0, hex2 = null, gr = 0) {
    const r = ringSlot(head, hw, cap(), alive), i = r.i;
    head = r.head; hw = r.hw;
    const p = aPos.array, cl = aCol.array, k = rgb(hex), ci = cellOf(cell);
    p[i * 3] = x; p[i * 3 + 1] = y; p[i * 3 + 2] = z;
    vel[i * 3] = vx; vel[i * 3 + 1] = vy; vel[i * 3 + 2] = vz;
    // born clear and small (polish 5): update() opens it over BIRTH_LOOK.birthS
    cl[i * 4] = k[0]; cl[i * 4 + 1] = k[1]; cl[i * 4 + 2] = k[2]; cl[i * 4 + 3] = 0;
    aRot.array[i * 2] = ci.code; aRot.array[i * 2 + 1] = ci.aligned ? widthNow : ci.upright ? 0 : Math.random() * 6.283;
    aSize.array[i] = sz * ci.size * (gr > 0 ? 0.4 : 1) * BL.sizeFrom;
    size[i] = sz * ci.size; life[i] = maxLife[i] = lifeS; grav[i] = g; spin[i] = ci.upright ? 0 : sp; light[i] = lightNow;
    grow[i] = gr; hasRamp[i] = hex2 ? 1 : 0;
    if (hex2) { const e = rgb(hex2); ramp[i * 3] = e[0] - k[0]; ramp[i * 3 + 1] = e[1] - k[1]; ramp[i * 3 + 2] = e[2] - k[2]; }
  }
  /** Kart-local emit: position through obj.matrixWorld, velocity through its rotation. */
  /** inherit: the share of the kart velocity (carry, set per kart) it keeps, so a flame hangs off the saucer at speed. */
  function local(obj, lx, ly, lz, vx, vy, vz, hex, sz, lifeS, cell, g, sp, hex2 = null, gr = 0, inherit = 0) {
    tmp.set(lx, ly, lz).applyMatrix4(obj.matrixWorld);
    dir.set(vx, vy, vz).applyQuaternion(obj.quaternion);
    if (inherit) dir.addScaledVector(carry, inherit);
    emit(tmp.x, tmp.y, tmp.z, dir.x, dir.y, dir.z, hex, sz, lifeS, cell, g, sp, hex2, gr);
  }

  /**
   * Polish 3: a shockwave halo at a world point, a camera-facing ring cell that opens to diameter dM over
   * lifeS (born at about a fifth of it, fading as it opens). Needs the atlas (the B1 disc would read as a blob).
   */
  function halo(x, y, z, dM, lifeS, hex, hex2 = null) {
    if (!at) return;
    const r = A.ringCellR || 0.58, s = big(dM) / r / 2.2 / (cellOf('ring').size || 1);
    emit(x, y, z, 0, 0, 0, hex, s, lifeS * (gentle() ? 2 : 1), 'ring', 0, 0, hex2, 1.8);
  }

  /** Drift sparks: the tier colour, a white-hot share and an accent of the next tier's colour (pink, white,
   * gold), and floating embers that outlive the streaks. */
  function sparks(obj, n, hex, sz, lifeS, spread, petal, tier) {
    const r = LOOK.saucerR, [lo0, hi0] = LOOK.sparkSpeedMps, fam = sparkFamily(tier, petal);
    const lo = lo0 * J.sparkSpeedMul, hi = hi0 * J.sparkSpeedMul;
    const accent = tier >= 3 ? '#ffffff' : tierCol(tier + 1), ember = B.emberShare * extra(), stars = P4.sparkTierStars[Math.min(Math.max(tier, 1), 3) - 1];
    widthNow = sparkWidth(tier, BL);
    for (let k = 0; k < n; k++) {
      const side = k & 1 ? 1 : -1, pick = Math.random(), c = Math.random();
      const cell = at && Math.random() < stars ? 'star' : at ? pickCell(fam, pick) : pick < petal ? 'petal' : k % 3 === 0 ? 'disc' : 'spark';
      const isPetal = cell === 'petal', h = c < B.sparkWhite ? '#ffffff' : c < B.sparkWhite + B.sparkAccent ? accent : hex;
      local(obj, side * r * 0.7, 0.05, r * 0.7, side * rnd(0, spread), rnd(lo, hi) * 0.6, rnd(lo, hi), h,
        sz * rnd(0.7, 1.3), lifeS * (isPetal ? 2 : 1), cell, isPetal ? 0.25 : 1, rnd(-1, 1) * LOOK.sparkSpin);
      if (Math.random() < ember) {
        const v = B.emberSpeedMul;
        local(obj, side * r * 0.7, 0.1, r * 0.7, side * rnd(0, spread) * v, rnd(lo, hi) * v, rnd(lo, hi) * v, Math.random() < 0.5 ? '#ffffff' : hex,
          sz * B.emberSizeMul, lifeS * B.emberLifeMul, at ? pickCell(FAMILIES.ember, Math.random()) : 'spark', -0.15, rnd(-1, 1) * LOOK.sparkSpin * 0.3, hex);
      }
    }
    widthNow = 1;
  }

  /** Polish 3: a drift tier-up: a radial ring of sparks in the new tier colour with white and stars, and a halo. */
  function tierUp(obj, tier, mul) {
    const hex = tierCol(tier);
    const n = Math.round(J.tierBurst[tier - 1] * mul * more()), v = J.tierBurstSpeedMps, sz = sparkProfile(tier, LOOK).size * big(J.sparkSizeMul) * P4.sparkTierSize[tier - 1];
    for (let k = 0; k < n; k++) {
      const a = (k / n) * 6.283 + rnd(-0.2, 0.2), w = v * rnd(0.6, 1.1), star = at && k % 4 === 0;
      local(obj, Math.cos(a) * LOOK.saucerR, 0.35, Math.sin(a) * LOOK.saucerR, Math.cos(a) * w, rnd(0.5, 2.5), Math.sin(a) * w,
        k % 3 === 0 ? '#ffffff' : hex, sz * (star ? 1.4 : 1) * rnd(0.8, 1.2), feel.get('juice.sparkDieS') * J.sparkLifeMul * 1.6,
        star ? 'star' : 'spark', 0.4, rnd(-1, 1) * LOOK.sparkSpin);
    }
    tmp.set(0, 0.4, 0).applyMatrix4(obj.matrixWorld);
    halo(tmp.x, tmp.y, tmp.z, J.tierRingM[tier - 1] * Math.sqrt(mul), J.tierRingS, hex, tier === 2 ? '#bfe8ff' : '#ffffff');
  }

  /** Polish 3: a boost start: flame tongues and glitter thrown back and out, stars round the saucer, a halo. */
  function boostBurst(obj, hex, mul) {
    const n = Math.round(J.boostBurst * mul * more()), v = J.boostBurstSpeedMps, sz = big(J.boostBurstSizeM);
    for (let k = 0; k < n; k++) {
      const a = rnd(-1.1, 1.1), w = v * rnd(0.5, 1.1), glit = k % 3 === 0;
      const acc = J.flameAccents[k % J.flameAccents.length];
      local(obj, rnd(-0.3, 0.3), rnd(0.2, 0.5), LOOK.saucerR, Math.sin(a) * w, rnd(0.5, 3), Math.cos(a) * w,
        glit ? '#ffffff' : B.flameCore, sz * (glit ? 0.6 : rnd(0.8, 1.3)), LOOK.exhaustLifeS * J.flameLifeMul * 1.6,
        glit ? (at ? 'star' : 'spark') : at ? pickCell(FAMILIES.flame, Math.random()) : 'disc', glit ? 0.3 : -0.2,
        rnd(-1, 1) * LOOK.sparkSpin, glit ? null : k & 1 ? hex : acc, glit ? 0 : B.flameGrow, J.flameInherit * 0.6);
    }
    tmp.set(0, 0.35, LOOK.saucerR * 0.5).applyMatrix4(obj.matrixWorld);
    halo(tmp.x, tmp.y, tmp.z, J.boostRingM * Math.sqrt(mul), J.boostRingS, '#ffffff', hex);
  }

  /**
   * Per kart and frame. mem = the kart's own carry memory ({} at first), opts.mul scales rates (rivals,
   * far karts), opts.slip = fxv.slip (0..1 wake charge). Reads drift stage and boost kind off the state,
   * so rivals get the same ladder as the player.
   */
  function kart(obj, st, mem, dt, { mul = 1, slip = 0 } = {}) {
    const dieS = feel.get('juice.sparkDieS') * J.sparkLifeMul;
    const drifting = st[S.driftDir] !== 0, stage = st[S.driftStage] | 0, sizeMul = big(J.sparkSizeMul);
    carry.set(st[S.velX], st[S.velY], st[S.velZ]);
    mem.vx = st[S.velX]; mem.vy = st[S.velY]; mem.vz = st[S.velZ];    // event() bursts inherit it
    if (drifting && stage > 0) {
      // the tier-up burst comes from the drift-tier event once the kart has sent one (event below)
      if (stage > (mem.stage | 0) && mem.stage !== undefined && !mem.evTier) tierUp(obj, stage, mul);
      const pr = sparkProfile(stage, LOOK, mul), ti = Math.min(stage, 3) - 1;
      mem.carry = (mem.carry || 0) + pr.rate * J.sparkRateMul * P4.sparkTierRate[ti] * tierShare(Q.name, J) * dt;
      const n = Math.floor(mem.carry); mem.carry -= n;
      sparks(obj, n, tierCol(stage), pr.size * sizeMul * P4.sparkTierSize[ti], dieS, LOOK.sparkSpreadMps * J.sparkSpeedMul, pr.petal, stage);
      mem.tier = stage;
    } else mem.carry = 0;
    mem.stage = drifting ? stage : 0;
    const bk = st[S.boostKind], driftBoost = bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3;
    if (!drifting && mem.tier > 0) {
      // the release burst: the drift-boost event's mini-turbo replaces it once the kart sends those
      if (driftBoost && !mem.evBoost) {
        const pr = sparkProfile(mem.tier, LOOK);
        sparks(obj, Math.round(LOOK.burstCount * mul), tierCol(mem.tier), pr.size * 1.2 * sizeMul, dieS * 1.4, LOOK.burstSpreadMps * J.sparkSpeedMul, LOOK.burstPetals, mem.tier);
      }
      mem.tier = 0;
    }
    const hex = driftBoost ? tierCol(bk - BOOST.DRIFT1 + 1) : bk === BOOST.LAUNCH ? LOOK.launchTrail : LOOK.exhaustColour;
    if (bk !== BOOST.NONE && bk !== mem.bk && mem.bk !== undefined && !(driftBoost && mem.evBoost)) boostBurst(obj, hex, mul);
    mem.bk = bk;
    if (bk !== BOOST.NONE) {
      // the boost flame: soft tongues born white hot at the saucer's back, swelling into the boost colour
      // (or a warm spectrum accent) as they stream away, with glitter sparks shot through them
      mem.carryX = (mem.carryX || 0) + LOOK.exhaustRate * B.flameRateMul * J.flameRateMul * extra() * tierShare(Q.name, J) * mul * dt;
      const n = Math.floor(mem.carryX); mem.carryX -= n;
      const fs = big(J.flameSizeMul), fv = J.flameSpeedMul, fl = J.flameLifeMul;
      for (let k = 0; k < n; k++) {
        if (Math.random() < B.flameSparkShare) {
          local(obj, rnd(-0.35, 0.35), 0.3, LOOK.saucerR, rnd(-1.6, 1.6), rnd(0, 1.6), rnd(6, 11) * fv, Math.random() < 0.5 ? '#ffffff' : hex,
            LOOK.exhaustSizeM * rnd(0.3, 0.5) * big(J.flameSparkSizeMul), LOOK.exhaustLifeS * 1.4 * fl, at ? (k % 4 ? 'glitter' : 'star') : 'spark', 0.3, rnd(-1, 1) * LOOK.sparkSpin, null, 0, J.flameInherit * 0.7);
          continue;
        }
        const cell = at ? pickCell(FAMILIES.flame, Math.random()) : 'disc';
        // a white-hot third, the rest born in the boost colour; all run out into a warm spectrum accent
        const acc = J.flameAccents[(mem.flameK = ((mem.flameK | 0) + 1) % J.flameAccents.length)];
        const start = k % 3 === 0 ? B.flameCore : hex, end = k % 3 === 0 ? hex : acc;
        local(obj, rnd(-0.4, 0.4), rnd(0.2, 0.5), LOOK.saucerR, rnd(-1.4, 1.4), rnd(0.2, 1.2), rnd(4, 8) * fv, start,
          LOOK.exhaustSizeM * rnd(0.5, 0.8) * fs, LOOK.exhaustLifeS * B.flameLifeMul * fl, cell, -0.2, rnd(-1, 1), end, B.flameGrow, J.flameInherit);
      }
    } else mem.carryX = 0;
    if (slip > 0.05) {
      // slipstream: soft wisps and long thin streaks pour past the kart from the front, cool tinted
      mem.carryW = (mem.carryW || 0) + LOOK.wispRate * J.wispRateMul * tierShare(Q.name, J) * slip * mul * dt;
      const n = Math.floor(mem.carryW); mem.carryW -= n;
      const ws = big(J.wispSizeMul), wl = J.wispLifeMul;
      for (let k = 0; k < n; k++) {
        const side = k & 1 ? 1 : -1, streak = at && Math.random() < B.wispStreakShare, wc = J.wispColours[(Math.random() * J.wispColours.length) | 0];
        local(obj, side * rnd(0.5, 1.6), rnd(0.2, 1.4), -rnd(0.4, 1.8), side * rnd(0, 0.6), rnd(-0.2, 0.3), LOOK.wispSpeedMps * rnd(0.8, 1.3) * (streak ? 1.3 : 1),
          streak ? '#ffffff' : wc, LOOK.wispSizeM * ws * rnd(0.6, 1.2) * (streak ? B.wispStreakSizeMul : 1), LOOK.wispLifeS * wl * (streak ? 1.3 : 1),
          streak ? 'spark' : at ? pickCell(FAMILIES.wisp, Math.random()) : 'disc', 0, at && !streak ? rnd(-1, 1) : 0, streak ? wc : null, streak ? 0 : 0.6);
      }
    } else mem.carryW = 0;
  }

  /** Landing dust: a flat ring of landing rings and soft discs around the saucer, tier 1..3, some in the spectrum, and a halo. */
  function dust(obj, tier) {
    const n = Math.round(LOOK.dustCount * J.dustCountMul * more() * tier / 3), v = LOOK.dustSpeedMps * J.dustSpeedMul, ds = big(J.dustSizeMul);
    for (let k = 0; k < n; k++) {
      const a = (k / n) * Math.PI * 2;
      const cell = at ? pickCell(FAMILIES.dust, Math.random()) : k % 3 ? 'disc' : 'petal';
      const hex = Math.random() < J.dustSpectrum ? nextHue() : LOOK.dustColour;
      local(obj, Math.cos(a) * LOOK.saucerR, 0.02, Math.sin(a) * LOOK.saucerR, Math.cos(a) * v * rnd(0.7, 1.1), rnd(0.3, 1.6), Math.sin(a) * v * rnd(0.7, 1.1),
        hex, LOOK.dustSizeM * ds * rnd(0.45, 0.8), LOOK.dustLifeS * 1.2, cell, 0.3, 0);
    }
    tmp.set(0, 0.3, 0).applyMatrix4(obj.matrixWorld);
    halo(tmp.x, tmp.y, tmp.z, J.landRingM[Math.min(Math.max(tier, 1), 3) - 1], 0.45, '#ffffff', nextHue());
  }

  /**
   * A burst at a world point p ([x, y, z]): n sprites, radial, colours cycled. With the atlas the cells
   * come from opts.family (render/atlas.js FAMILIES), else from the LOOK colour list it was given
   * (bump, pop, box); without it the petal and sparkle shares pick the B1 shapes.
   * Polish 3: bump, pop and box bursts grow by their JUICE_LOOK.burst row and take the spectrum; a bump
   * adds a hot white core, stars and a shockwave halo.
   */
  function burst(p, n, opts) {
    if (!p) return;
    const { colours, speed = 4, sizeM = 0.14, lifeS = 0.5, petal = 0, sparkle = 0.5, up = 1, g = 0.6, near = false } = opts;
    const bb = burstBoost(opts, LOOK, J), fam = at ? FAMILIES[familyOf(opts, LOOK)] : null;
    // near: the player's own burst, right under the camera: a share of the count and of the stars (lensMath.js)
    const nearMul = near ? LENS.playerBurstMul : 1;
    const cnt = Math.round(n * nearMul * (bb.family ? 1 + (bb.count - 1) * tierShare(Q.name, J) : 1));
    const sz = sizeM * big(bb.size), sp = speed * bb.speed, lf = lifeS * bb.life;
    for (let k = 0; k < cnt; k++) {
      const a = Math.random() * 6.283, e = rnd(-0.3, 1), v = sp * rnd(0.5, 1), pick = Math.random();
      const cell = fam ? pickCell(fam, pick) : pick < petal ? 'petal' : pick < petal + sparkle ? 'spark' : 'disc';
      const gg = cell === 'petal' ? 0.2 : cell === 'drip' ? 1.6 : g;
      // spectrum share: half of a bump (its white shards would wash out), a third of pops and boxes
      const hex = bb.spectrum && (bb.family === 'bump' ? k & 1 : k % 3 === 2) ? nextHue() : colours[k % colours.length];
      emit(p[0], p[1] + 0.3, p[2], Math.cos(a) * v * Math.cos(e), (Math.sin(e) * v + up) * 0.8, Math.sin(a) * v * Math.cos(e),
        hex, sz * rnd(0.6, 1.3), lf * rnd(0.7, 1.2), cell, gg, rnd(-1, 1) * LOOK.sparkSpin);
    }
    if (bb.family === 'bump') {
      const y = p[1] + 0.5;
      // the hot white core is for rivals; the player's own bump (under the camera) starts gold
      emit(p[0], y, p[2], 0, 0.6, 0, near ? '#ffd23a' : '#ffffff', big(J.bumpCoreM), J.bumpCoreS, at ? 'glint' : 'spark', 0, 0, '#ffd23a');
      const ns = Math.max(1, Math.round(J.bumpStars * nearMul * Math.min(1, n / 20) * (gentle() ? J.gentleCount : 1)));
      for (let k = 0; k < ns; k++) {
        const a = (k / ns) * 6.283, v = sp * 0.8;
        emit(p[0], y, p[2], Math.cos(a) * v, rnd(1.5, 3.5), Math.sin(a) * v, k & 1 ? '#ffd23a' : nextHue(), sz * 1.5, lf * 1.3, at ? 'star' : 'spark', 0.5, rnd(-1, 1) * LOOK.sparkSpin);
      }
      halo(p[0], y, p[2], J.bumpRingM * Math.min(1, 0.5 + n / 40), J.bumpRingS, '#ffffff', nextHue());
    } else if (bb.family === 'box' || bb.family === 'pop') halo(p[0], p[1] + 0.5, p[2], J.bumpRingM * 0.7, J.bumpRingS, '#ffffff', nextHue());
  }

  /**
   * An item hit at world p: Freeze sprays frost crystals, Golden stars and glitter, Melt drips that
   * fall, Glass and Echo their pop. kind = the effect or verdict id; few = the Gentle share (0.6).
   * Returns false when that kind sprays nothing.
   */
  function hit(p, kind, { few = 1, near = false } = {}) {
    const family = hitFamily(kind);
    if (!p || !family) return false;
    const colours = family === 'freeze' ? A.frostColours : family === 'golden' ? A.goldColours : family === 'melt' ? A.meltColours
      : hitColours(family) || LOOK.popColours;
    const melt = family === 'melt', soft = family === 'pink' || family === 'spiral' || family === 'brainDrain' || family === 'blackout';
    const cnt = A.hitCount * (1 + (J.hitCountMul - 1) * tierShare(Q.name, J));
    burst(p, Math.round(cnt * few), { family, colours, speed: A.hitSpeedMps * J.hitSpeedMul * (melt ? 0.6 : soft ? 0.8 : 1), sizeM: A.hitSizeM * big(J.hitSizeMul) * (soft ? 1.2 : 1),
      lifeS: melt || soft ? 0.8 : 0.6, sparkle: 0.7, up: melt ? 2 : 1, g: melt ? 1.6 : soft ? 0.15 : 0.4, near });
    // the player's own ring opens smaller and in the kind's colours only (no white swell under the camera)
    halo(p[0], p[1] + 0.6, p[2], J.hitRingM * (near ? 0.6 : 1), J.hitRingS, colours[0], near ? colours[0] : colours[1] || '#ffffff');
    return true;
  }

  /** Polish 4: a soft puff at both rear wheels when a slide starts, in the pale slide colour. */
  function driftPuff(obj, dir, mul) {
    const n = Math.round(8 * mul * more()), r = LOOK.saucerR;
    for (let k = 0; k < n; k++) {
      const side = k & 1 ? 1 : -1;
      local(obj, side * r * 0.6, 0.08, r * 0.55, side * rnd(0.5, 2) - dir * rnd(0.5, 1.5), rnd(0.3, 1.2), rnd(1, 3), k % 3 ? P4.slideColour : '#ffffff',
        LOOK.dustSizeM * big(J.dustSizeMul) * rnd(0.35, 0.6), LOOK.dustLifeS, at ? 'smoke' : 'disc', -0.1, rnd(-1, 1));
    }
  }

  /**
   * Polish 4: the mini-turbo burst when a drift releases its tier boost. A cone of streaks, glitter and flame
   * thrown back from the saucer in the tier colour with white and the next tier's accent, stars round the
   * saucer, and a halo that grows with the tier.
   */
  function miniTurbo(obj, tier, mul) {
    // tier 0 = the small boost of a short drift with no tier: half the burst in the pale slide colour
    const t0 = Math.min(Math.max(tier | 0, 0), 3), t = Math.max(t0, 1), i = t - 1, hex = tierCol(t0), acc = t >= 3 ? '#ffffff' : tierCol(t + 1);
    if (!t0) mul *= 0.5;
    const n = Math.round(P4.turboCount[i] * mul * more()), v = P4.turboSpeedMps[i], sz = big(P4.turboSizeM) * P4.sparkTierSize[i];
    for (let k = 0; k < n; k++) {
      const a = rnd(-0.7, 0.7), w = v * rnd(0.55, 1.15), kind = k % 4, c = Math.random();
      const cell = kind === 0 ? (at ? 'glitter' : 'spark') : kind === 1 ? (at ? pickCell(FAMILIES.flame, Math.random()) : 'disc') : 'spark';
      const col = c < P4.turboWhite ? '#ffffff' : c < P4.turboWhite + P4.turboAccent ? acc : hex;
      local(obj, rnd(-0.4, 0.4), rnd(0.15, 0.5), LOOK.saucerR * 0.8, Math.sin(a) * w, rnd(0.5, 3.5), Math.cos(a) * w, col,
        sz * (cell === 'spark' ? 0.8 : rnd(0.7, 1.2)), LOOK.exhaustLifeS * J.flameLifeMul * 1.5, cell, kind === 1 ? -0.2 : 0.5,
        rnd(-1, 1) * LOOK.sparkSpin, kind === 1 ? acc : null, kind === 1 ? B.flameGrow : 0, 0.85);   // trails the kart, never piles onto the lens
    }
    const ns = Math.round((4 + 4 * t) * (gentle() ? J.gentleCount : 1));
    for (let k = 0; k < ns; k++) {
      const a = (k / ns) * 6.283;
      local(obj, Math.cos(a) * LOOK.saucerR, 0.4, Math.sin(a) * LOOK.saucerR, Math.cos(a) * v * 0.45, rnd(1, 2.5), Math.sin(a) * v * 0.45,
        k % 4 === 0 ? '#ffffff' : hex, sz * 1.1, LOOK.exhaustLifeS * 2.2, at ? 'star' : 'spark', 0.4, rnd(-1, 1) * LOOK.sparkSpin, null, 0, 0.95);
    }
    tmp.set(0, 0.35, LOOK.saucerR * 0.4).applyMatrix4(obj.matrixWorld);
    halo(tmp.x, tmp.y, tmp.z, P4.turboRingM[i] * Math.sqrt(mul), P4.turboRingS, hex, hex);
  }

  /** Polish 4: confetti thrown up and round the kart (a trick at takeoff), spectrum coloured. */
  function confetti(obj, n, up, out, lifeS) {
    const cells = P4.confettiCells, sz = big(P4.confettiSizeM);
    lightNow = 1;
    for (let k = 0; k < n; k++) {
      const a = (k / n) * 6.283 + rnd(-0.25, 0.25), cell = at ? cells[k % cells.length] : k % 2 ? 'petal' : 'spark';
      local(obj, Math.cos(a) * LOOK.saucerR * 0.8, 0.4, Math.sin(a) * LOOK.saucerR * 0.8, Math.cos(a) * out * rnd(0.5, 1.1), up * rnd(0.6, 1.2), Math.sin(a) * out * rnd(0.5, 1.1),
        k % 5 === 0 ? '#ffffff' : nextHue(), sz * rnd(0.7, 1.3), lifeS * rnd(0.8, 1.2), cell, P4.confettiGrav, rnd(-1, 1) * LOOK.sparkSpin, null, 0, 1);
    }
    lightNow = 0;
  }

  /** Polish 4: a trick at takeoff: confetti thrown up; a great trick adds stars and a small halo. */
  function trick(obj, grade, mul) {
    const g = trickKey(grade), n = Math.round(P4.trickCount[g] * mul * more());
    confetti(obj, n, P4.trickSpeedMps, P4.trickSpeedMps * 0.45, P4.confettiLifeS);
    if (g === 'great') {
      tmp.set(0, 0.6, 0).applyMatrix4(obj.matrixWorld);
      halo(tmp.x, tmp.y, tmp.z, P4.trickRingM.good * Math.sqrt(mul), P4.trickRingS, '#ffffff', nextHue());
    }
  }

  /** Polish 4: a trick landing: a ring, a flat burst of confetti and (great) stars and a second halo. */
  function trickLand(obj, grade, mul) {
    const g = trickKey(grade), n = Math.round(P4.trickLandCount[g] * mul * more());
    confetti(obj, n, P4.trickSpeedMps * 0.5, P4.trickSpeedMps * 0.8, P4.confettiLifeS * 0.85);
    tmp.set(0, 0.3, 0).applyMatrix4(obj.matrixWorld);
    halo(tmp.x, tmp.y, tmp.z, P4.trickRingM[g] * Math.sqrt(mul), P4.trickRingS, '#ffffff', nextHue());
    if (g === 'great') {
      halo(tmp.x, tmp.y + 0.2, tmp.z, P4.trickRingM[g] * 0.6 * Math.sqrt(mul), P4.trickRingS * 1.4, nextHue(), '#ffffff');
      const ns = Math.round(10 * (gentle() ? J.gentleCount : 1));
      for (let k = 0; k < ns; k++) {
        const a = (k / ns) * 6.283;
        local(obj, Math.cos(a) * LOOK.saucerR, 0.5, Math.sin(a) * LOOK.saucerR, Math.cos(a) * 5, rnd(2, 4), Math.sin(a) * 5,
          k & 1 ? '#ffd23a' : nextHue(), big(P4.confettiSizeM) * 0.9, 0.9, at ? 'star' : 'spark', 0.5, rnd(-1, 1) * LOOK.sparkSpin, null, 0, 1);
      }
    }
  }

  /**
   * Polish 4: a FEEL lane bus event for one kart. name: 'drift-start' { dir }, 'drift-tier' { tier },
   * 'drift-boost' { tier }, 'trick' { grade, airS }, 'trick-land' { grade }. obj = the kart's Object3D
   * (matrixWorld current), mem = the same per-kart memory kart() gets, opts.mul = the rival share.
   * Returns true when it played something.
   */
  function event(obj, mem, name, p = {}, { mul = 1 } = {}) {
    if (!obj || !mem) return false;
    p = p || {};
    carry.set(mem.vx || 0, mem.vy || 0, mem.vz || 0);
    if (name === 'drift-start') { driftPuff(obj, p.dir < 0 ? -1 : 1, mul); return true; }
    if (name === 'drift-tier') { mem.evTier = true; if ((p.tier | 0) >= 1) tierUp(obj, Math.min(p.tier | 0, 3), mul); return true; }
    if (name === 'drift-boost') { mem.evBoost = true; miniTurbo(obj, p.tier | 0, mul); return true; }
    if (name === 'trick') { trick(obj, p.grade, mul); return true; }
    if (name === 'trick-land') { trickLand(obj, p.grade, mul); return true; }
    return false;
  }

  function update(dt) {
    const n = Math.min(hw, cap()), drag0 = Math.exp(-LOOK.particleDrag * dt), drag1 = Math.exp(-P4.confettiDrag * dt), g0 = LOOK.particleGravityMps2 * dt;
    const p = aPos.array, cl = aCol.array, sz = aSize.array, rt = aRot.array;
    let last = -1;
    for (let i = 0; i < n; i++) {
      if (life[i] <= 0) { sz[i] = 0; cl[i * 4 + 3] = 0; continue; }
      life[i] -= dt;
      if (life[i] <= 0) { sz[i] = 0; cl[i * 4 + 3] = 0; continue; }
      last = i;
      const j = i * 3;
      const drag = light[i] ? drag1 : drag0;
      vel[j] *= drag; vel[j + 1] = vel[j + 1] * drag - g0 * grav[i]; vel[j + 2] *= drag;
      p[j] += vel[j] * dt; p[j + 1] += vel[j + 1] * dt; p[j + 2] += vel[j + 2] * dt;
      rt[i * 2 + 1] += spin[i] * dt;
      const t = Math.max(0, life[i] / maxLife[i]);
      // polish 5: born over BL.birthS (alpha from 0, size from BL.sizeFrom), then eased out as before
      const bk = birthK(maxLife[i] - life[i], BL);
      cl[i * 4 + 3] = (t < 0.85 ? t / 0.85 : 1) * bk;
      if (hasRamp[i]) {
        // the ramp is stored as (end - start); step the colour by this frame's share of the life
        const s = maxLife[i] > 0 ? Math.min(dt, life[i] + dt) / maxLife[i] : 0;
        cl[i * 4] += ramp[i * 3] * s; cl[i * 4 + 1] += ramp[i * 3 + 1] * s; cl[i * 4 + 2] += ramp[i * 3 + 2] * s;
      }
      sz[i] = size[i] * (grow[i] > 0 ? 0.4 + grow[i] * (1 - t) : 0.5 + 0.5 * t) * birthSize(bk, BL);
    }
    // the high-water mark falls back to the last live slot, so a quiet cloud draws and uploads little
    const was = n;
    if (last + 1 < hw) { hw = last + 1; if (head >= hw) head = 0; }
    sprite.count = hw;
    if (was <= 0) return;
    // upload only the slots this pass touched (the ones it just cleared above the new mark included)
    for (const a of at ? attrs : attrs.slice(0, 4)) { a.clearUpdateRanges(); a.addUpdateRange(0, was * a.itemSize); a.needsUpdate = true; }
  }
  const api = { object: sprite, atlas: !!at, kart, dust, burst, hit, halo, event, update, dispose() { m.dispose(); } };
  // dev only (?bot=1): shot scripts reach the live particles through the boot debug record (never read by the
  // game); not enumerable, so the status JSON never tries to serialise three.js objects
  const dbg = globalThis.__glassroad;
  if (dbg && typeof dbg === 'object' && globalThis.location && /[?&]bot=1(?:&|$)/.test(globalThis.location.search)) {
    Object.defineProperty(dbg, 'fx4', { value: Object.assign(dbg.fx4 || {}, { particles: api }), enumerable: false, configurable: true, writable: true });
  }
  return api;
}
