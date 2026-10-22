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
import {
  CELLS, GRID, ATLAS_PX, MAX_LOD, FXART_LOOK, FAMILIES, UPRIGHT, ALIGNED, cellIndex, cellInset, legacyShape, sparkFamily, pickCell,
  familyOf, hitFamily, FX2_LOOK, hitColours,
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
  // polish 2: a colour ramp from birth to death (flame: white core to the boost colour) and growth over life
  const ramp = new Float32Array(max0 * 3), hasRamp = new Uint8Array(max0), grow = new Float32Array(max0);
  const B = LOOK.fx2 || FX2_LOOK;
  const extra = () => (Q.name === 'phone' ? B.phoneExtraMul : 1);
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  m.positionNode = instancedDynamicBufferAttribute(aPos, 'vec3');
  m.scaleNode = instancedDynamicBufferAttribute(aSize, 'float');
  const rot = instancedDynamicBufferAttribute(aRot, 'vec2');
  const c = instancedDynamicBufferAttribute(aCol, 'vec4');
  m.colorNode = c.rgb;
  if (at) {
    // straight alpha, white RGB: the additive blend multiplies the tint by coverage once
    const idx = floor(rot.x.add(0.5));
    const vv = cameraViewMatrix.mul(vec4(instancedDynamicBufferAttribute(aVel, 'vec3'), 0)).xy;
    m.rotationNode = select(idx.equal(cellIndex('spark', at.cells)), atan(vv.y, vv.x).sub(Math.PI / 2), rot.y);
    m.opacityNode = c.a.mul(atlasCoverage(at, idx));
  } else {
    m.rotationNode = rot.y;
    m.opacityNode = c.a.mul(shapeNode(rot.x));
  }
  const sprite = new Sprite(m);
  sprite.count = max0; sprite.frustumCulled = false; sprite.renderOrder = 2;

  let head = 0;
  const cols = new Map(), info = new Map(), tmp = new Vector3(), dir = new Vector3();
  const cap = () => Math.min(max0, Q.particles);
  const rnd = (a, b) => a + Math.random() * (b - a);
  const rgb = (hex) => { let v = cols.get(hex); if (!v) { const k = new Color(hex); v = [k.r, k.g, k.b]; cols.set(hex, v); } return v; };
  /** Per cell name: the attribute code, size share, upright. */
  const cellOf = (name) => {
    let v = info.get(name);
    if (!v) {
      v = at ? { code: cellIndex(name, at.cells), size: A.cellSize[name] ?? 1, upright: UPRIGHT.has(name) || ALIGNED.has(name) }
        : { code: legacyShape(name), size: 1, upright: false };
      info.set(name, v);
    }
    return v;
  };

  /** World-space emit; cell = an atlas cell name (render/atlas.js CELLS). */
  /** hex2: the colour it ends as (a ramp from hex), gr: how much it grows over its life (0 = shrinks as before). */
  function emit(x, y, z, vx, vy, vz, hex, sz, lifeS, cell = 'disc', g = 1, sp = 0, hex2 = null, gr = 0) {
    const i = head; head = (head + 1) % cap();
    const p = aPos.array, cl = aCol.array, k = rgb(hex), ci = cellOf(cell);
    p[i * 3] = x; p[i * 3 + 1] = y; p[i * 3 + 2] = z;
    vel[i * 3] = vx; vel[i * 3 + 1] = vy; vel[i * 3 + 2] = vz;
    cl[i * 4] = k[0]; cl[i * 4 + 1] = k[1]; cl[i * 4 + 2] = k[2]; cl[i * 4 + 3] = 1;
    aRot.array[i * 2] = ci.code; aRot.array[i * 2 + 1] = ci.upright ? 0 : Math.random() * 6.283;
    size[i] = sz * ci.size; life[i] = maxLife[i] = lifeS; grav[i] = g; spin[i] = ci.upright ? 0 : sp;
    grow[i] = gr; hasRamp[i] = hex2 ? 1 : 0;
    if (hex2) { const e = rgb(hex2); ramp[i * 3] = e[0] - k[0]; ramp[i * 3 + 1] = e[1] - k[1]; ramp[i * 3 + 2] = e[2] - k[2]; }
  }
  /** Kart-local emit: position through obj.matrixWorld, velocity through its rotation. */
  function local(obj, lx, ly, lz, vx, vy, vz, hex, sz, lifeS, cell, g, sp, hex2 = null, gr = 0) {
    tmp.set(lx, ly, lz).applyMatrix4(obj.matrixWorld);
    dir.set(vx, vy, vz).applyQuaternion(obj.quaternion);
    emit(tmp.x, tmp.y, tmp.z, dir.x, dir.y, dir.z, hex, sz, lifeS, cell, g, sp, hex2, gr);
  }

  /** Drift sparks: the tier colour, a white-hot share and an accent of the next tier's colour (pink, white,
   * gold), and floating embers that outlive the streaks. */
  function sparks(obj, n, hex, sz, lifeS, spread, petal, tier) {
    const r = LOOK.saucerR, [lo, hi] = LOOK.sparkSpeedMps, fam = sparkFamily(tier, petal);
    const colours = feel.get('juice.sparkColours'), accent = colours[tier % colours.length] || '#ffffff', ember = B.emberShare * extra();
    for (let k = 0; k < n; k++) {
      const side = k & 1 ? 1 : -1, pick = Math.random(), c = Math.random();
      const cell = at ? pickCell(fam, pick) : pick < petal ? 'petal' : k % 3 === 0 ? 'disc' : 'spark';
      const isPetal = cell === 'petal', h = c < B.sparkWhite ? '#ffffff' : c < B.sparkWhite + B.sparkAccent ? accent : hex;
      local(obj, side * r * 0.7, 0.05, r * 0.7, side * rnd(0, spread), rnd(lo, hi) * 0.6, rnd(lo, hi), h,
        sz * rnd(0.7, 1.3), lifeS * (isPetal ? 2 : 1), cell, isPetal ? 0.25 : 1, rnd(-1, 1) * LOOK.sparkSpin);
      if (Math.random() < ember) {
        const v = B.emberSpeedMul;
        local(obj, side * r * 0.7, 0.1, r * 0.7, side * rnd(0, spread) * v, rnd(lo, hi) * v, rnd(lo, hi) * v, Math.random() < 0.5 ? '#ffffff' : hex,
          sz * B.emberSizeMul, lifeS * B.emberLifeMul, at ? pickCell(FAMILIES.ember, Math.random()) : 'spark', -0.15, rnd(-1, 1) * LOOK.sparkSpin * 0.3, hex);
      }
    }
  }

  /**
   * Per kart and frame. mem = the kart's own carry memory ({} at first), opts.mul scales rates (rivals,
   * far karts), opts.slip = fxv.slip (0..1 wake charge). Reads drift stage and boost kind off the state,
   * so rivals get the same ladder as the player.
   */
  function kart(obj, st, mem, dt, { mul = 1, slip = 0 } = {}) {
    const colours = feel.get('juice.sparkColours'), dieS = feel.get('juice.sparkDieS');
    const drifting = st[S.driftDir] !== 0, stage = st[S.driftStage] | 0;
    if (drifting && stage > 0) {
      const pr = sparkProfile(stage, LOOK, mul);
      mem.carry = (mem.carry || 0) + pr.rate * dt;
      const n = Math.floor(mem.carry); mem.carry -= n;
      sparks(obj, n, colours[stage - 1], pr.size, dieS, LOOK.sparkSpreadMps, pr.petal, stage);
      mem.tier = stage;
    } else mem.carry = 0;
    const bk = st[S.boostKind];
    if (!drifting && mem.tier > 0) {
      if (bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3) {
        const pr = sparkProfile(mem.tier, LOOK);
        sparks(obj, Math.round(LOOK.burstCount * mul), colours[mem.tier - 1], pr.size * 1.2, dieS * 1.4, LOOK.burstSpreadMps, LOOK.burstPetals, mem.tier);
      }
      mem.tier = 0;
    }
    if (bk !== BOOST.NONE) {
      // the boost flame: soft tongues born white hot at the saucer's back, swelling into the boost colour
      // as they stream away, with glitter sparks shot through them
      mem.carryX = (mem.carryX || 0) + LOOK.exhaustRate * B.flameRateMul * extra() * mul * dt;
      const n = Math.floor(mem.carryX); mem.carryX -= n;
      const hex = bk >= BOOST.DRIFT1 && bk <= BOOST.DRIFT3 ? colours[bk - 1] : bk === BOOST.LAUNCH ? LOOK.launchTrail : LOOK.exhaustColour;
      for (let k = 0; k < n; k++) {
        if (Math.random() < B.flameSparkShare) {
          local(obj, rnd(-0.25, 0.25), 0.3, LOOK.saucerR, rnd(-1.2, 1.2), rnd(0, 1.2), rnd(6, 11), Math.random() < 0.5 ? '#ffffff' : hex,
            LOOK.exhaustSizeM * rnd(0.3, 0.5), LOOK.exhaustLifeS * 1.4, at ? 'glitter' : 'spark', 0.3, rnd(-1, 1) * LOOK.sparkSpin);
          continue;
        }
        const cell = at ? pickCell(FAMILIES.flame, Math.random()) : 'disc';
        local(obj, rnd(-0.15, 0.15), rnd(0.2, 0.35), LOOK.saucerR, rnd(-0.4, 0.4), rnd(0.2, 0.7), rnd(4, 8), B.flameCore,
          LOOK.exhaustSizeM * rnd(0.5, 0.8), LOOK.exhaustLifeS * B.flameLifeMul, cell, -0.2, rnd(-1, 1), k & 1 ? hex : LOOK.exhaustColour, B.flameGrow);
      }
    } else mem.carryX = 0;
    if (slip > 0.05) {
      // slipstream: soft wisps and long thin streaks pour past the kart from the front
      mem.carryW = (mem.carryW || 0) + LOOK.wispRate * slip * mul * dt;
      const n = Math.floor(mem.carryW); mem.carryW -= n;
      for (let k = 0; k < n; k++) {
        const side = k & 1 ? 1 : -1, streak = at && Math.random() < B.wispStreakShare;
        local(obj, side * rnd(0.5, 1.3), rnd(0.2, 1.1), -rnd(0.4, 1.6), side * rnd(0, 0.6), rnd(-0.2, 0.3), LOOK.wispSpeedMps * rnd(0.8, 1.3) * (streak ? 1.3 : 1),
          LOOK.wispColour, LOOK.wispSizeM * rnd(0.6, 1.2) * (streak ? B.wispStreakSizeMul : 1), LOOK.wispLifeS * (streak ? 1.3 : 1),
          streak ? 'spark' : at ? pickCell(FAMILIES.wisp, Math.random()) : 'disc', 0, at && !streak ? rnd(-1, 1) : 0, streak ? '#bfe8ff' : null, streak ? 0 : 0.6);
      }
    } else mem.carryW = 0;
  }

  /** Landing dust: a flat ring of landing rings and soft discs around the saucer, tier 1..3. */
  function dust(obj, tier) {
    const n = Math.round(LOOK.dustCount * tier / 3);
    for (let k = 0; k < n; k++) {
      const a = (k / n) * Math.PI * 2, v = LOOK.dustSpeedMps;
      const cell = at ? pickCell(FAMILIES.dust, Math.random()) : k % 3 ? 'disc' : 'petal';
      local(obj, Math.cos(a) * LOOK.saucerR, 0.02, Math.sin(a) * LOOK.saucerR, Math.cos(a) * v, rnd(0.2, 1), Math.sin(a) * v,
        LOOK.dustColour, LOOK.dustSizeM * rnd(0.45, 0.8), LOOK.dustLifeS, cell, 0.3, 0);
    }
  }

  /**
   * A burst at a world point p ([x, y, z]): n sprites, radial, colours cycled. With the atlas the cells
   * come from opts.family (render/atlas.js FAMILIES), else from the LOOK colour list it was given
   * (bump, pop, box); without it the petal and sparkle shares pick the B1 shapes.
   */
  function burst(p, n, opts) {
    if (!p) return;
    const { colours, speed = 4, sizeM = 0.14, lifeS = 0.5, petal = 0, sparkle = 0.5, up = 1, g = 0.6 } = opts;
    const fam = at ? FAMILIES[familyOf(opts, LOOK)] : null;
    for (let k = 0; k < n; k++) {
      const a = Math.random() * 6.283, e = rnd(-0.3, 1), v = speed * rnd(0.5, 1), pick = Math.random();
      const cell = fam ? pickCell(fam, pick) : pick < petal ? 'petal' : pick < petal + sparkle ? 'spark' : 'disc';
      const gg = cell === 'petal' ? 0.2 : cell === 'drip' ? 1.6 : g;
      emit(p[0], p[1] + 0.3, p[2], Math.cos(a) * v * Math.cos(e), (Math.sin(e) * v + up) * 0.8, Math.sin(a) * v * Math.cos(e),
        colours[k % colours.length], sizeM * rnd(0.6, 1.3), lifeS * rnd(0.7, 1.2), cell, gg, rnd(-1, 1) * LOOK.sparkSpin);
    }
  }

  /**
   * An item hit at world p: Freeze sprays frost crystals, Golden stars and glitter, Melt drips that
   * fall, Glass and Echo their pop. kind = the effect or verdict id; few = the Gentle share (0.6).
   * Returns false when that kind sprays nothing.
   */
  function hit(p, kind, { few = 1 } = {}) {
    const family = hitFamily(kind);
    if (!p || !family) return false;
    const colours = family === 'freeze' ? A.frostColours : family === 'golden' ? A.goldColours : family === 'melt' ? A.meltColours
      : hitColours(family) || LOOK.popColours;
    const melt = family === 'melt', soft = family === 'pink' || family === 'spiral' || family === 'brainDrain' || family === 'blackout';
    burst(p, Math.round(A.hitCount * few), { family, colours, speed: A.hitSpeedMps * (melt ? 0.6 : soft ? 0.8 : 1), sizeM: A.hitSizeM * (soft ? 1.2 : 1),
      lifeS: melt || soft ? 0.8 : 0.6, sparkle: 0.7, up: melt ? 2 : 1, g: melt ? 1.6 : soft ? 0.15 : 0.4 });
    return true;
  }

  function update(dt) {
    const n = cap(), drag = Math.exp(-LOOK.particleDrag * dt), g0 = LOOK.particleGravityMps2 * dt;
    const p = aPos.array, cl = aCol.array, sz = aSize.array, rt = aRot.array;
    for (let i = 0; i < n; i++) {
      if (life[i] <= 0) { sz[i] = 0; continue; }
      life[i] -= dt;
      const j = i * 3;
      vel[j] *= drag; vel[j + 1] = vel[j + 1] * drag - g0 * grav[i]; vel[j + 2] *= drag;
      p[j] += vel[j] * dt; p[j + 1] += vel[j + 1] * dt; p[j + 2] += vel[j + 2] * dt;
      rt[i * 2 + 1] += spin[i] * dt;
      const t = Math.max(0, life[i] / maxLife[i]);
      cl[i * 4 + 3] = t < 0.85 ? t / 0.85 : 1;          // no pop-in: full at birth, eased out
      if (hasRamp[i]) {
        // the ramp is stored as (end - start); step the colour by this frame's share of the life
        const s = maxLife[i] > 0 ? Math.min(dt, life[i] + dt) / maxLife[i] : 0;
        cl[i * 4] += ramp[i * 3] * s; cl[i * 4 + 1] += ramp[i * 3 + 1] * s; cl[i * 4 + 2] += ramp[i * 3 + 2] * s;
      }
      sz[i] = size[i] * (grow[i] > 0 ? 0.4 + grow[i] * (1 - t) : 0.5 + 0.5 * t);
    }
    sprite.count = n;
    aPos.needsUpdate = true; aCol.needsUpdate = true; aSize.needsUpdate = true; aRot.needsUpdate = true;
    if (at) aVel.needsUpdate = true;
  }
  return { object: sprite, atlas: !!at, kart, dust, burst, hit, update, dispose() { m.dispose(); } };
}
