/* render/items/materials.js - node materials for the effect bubbles (render/items/visuals.js).
 * bubbleCloud: every bubble on screen in one instanced sprite draw (the living atlas icon inside a
 * glass shell: iridescent rim, a highlight, optional gold tint or shell-only for shields and pops).
 * moteCloud: the drifting motes, additive. meltPuddle: the Melt trap, the spiral mask on the road
 * with a wobbling, dripping edge. ghost: Echo copies. ring: the Pink wave.
 * Wave 2: shellCloud is the effect-bubble GLB shell instanced (one draw) round a bare icon sprite
 * (bubbleCloud mode 4); cardEdge is the item card's glowing item_edge.
 */
import {
  Sprite, SpriteNodeMaterial, MeshBasicNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, AdditiveBlending,
  DoubleSide, Color, InstancedMesh, Matrix4, Quaternion, Vector3, Euler, PlaneGeometry,
} from 'three/webgpu';
import {
  instancedDynamicBufferAttribute, uniform, uv, vec2, vec3, float, texture, mix, smoothstep, sin, cos, atan, log, length,
  max, pow, floor, mod, normalize, cameraPosition, normalView, positionViewDirection, abs, step, dot,
} from 'three/tsl';
import { ITEM_LOOK as L } from './look.js';
import { ATLAS_GRID } from './living.js';
import { lensDepth, lensScale, lensAlpha } from '../lens.js';

const col3 = (hex) => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };

/** An instanced sprite cloud with a CPU writer: push(x, y, z, size, a, b, c, d) per instance. */
function cloud(cap, material, vec4Name) {
  const aPos = new InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(cap), 1).setUsage(DynamicDrawUsage);
  const aV = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const pos = instancedDynamicBufferAttribute(aPos, 'vec3'), size = instancedDynamicBufferAttribute(aSize, 'float');
  const v = instancedDynamicBufferAttribute(aV, 'vec4');
  const sprite = new Sprite(material);
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 3;
  let n = 0;
  return {
    sprite, nodes: { pos, size, v, [vec4Name]: v }, cap,
    get n() { return n; },
    begin() { n = 0; },
    push(x, y, z, s, a, b, c, d) {
      if (n >= cap) return false;
      const p = aPos.array, q = aV.array;
      p[n * 3] = x; p[n * 3 + 1] = y; p[n * 3 + 2] = z; aSize.array[n] = s;
      q[n * 4] = a; q[n * 4 + 1] = b; q[n * 4 + 2] = c; q[n * 4 + 3] = d; n++;
      return true;
    },
    end() { sprite.count = n; sprite.visible = n > 0; aPos.needsUpdate = true; aSize.needsUpdate = true; aV.needsUpdate = true; },
  };
}

/** info = (cell or -1, alpha, mode 0 icon / 1 gold icon / 2 shell / 3 gold shell / 4 bare icon in a GLB shell, seed). */
export function bubbleCloud(cap, atlas) {
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  const c = cloud(cap, m, 'info'), { pos, size, v } = c.nodes, u = { time: uniform(0) };
  // pulled toward the camera by its radius so the front of the glass sits in front of what it holds
  m.positionNode = pos.add(normalize(cameraPosition.sub(pos)).mul(size.mul(0.5)));
  m.scaleNode = size;
  const g = ATLAS_GRID, cell = v.x, mode = v.z, bare = step(3.5, mode), dressed = bare.oneMinus();
  const shell = step(1.5, mode).mul(dressed), gold = mod(mode, 2).mul(dressed);
  const p = uv(), d = length(p.sub(0.5)).mul(2);
  const auv = vec2(p.x.add(mod(cell, g)), p.y.add(float(g - 1).sub(floor(cell.div(g))))).div(g);
  const icon = texture(atlas, auv);
  const iconA = icon.a.mul(shell.oneMinus()).mul(step(0, cell));
  const ang = atan(p.y.sub(0.5), p.x.sub(0.5));
  const hue = cos(vec3(0, 2.1, 4.2).add(d.mul(5).add(ang).add(u.time.mul(0.6)).add(v.w))).mul(0.5).add(0.5);
  const tint = mix(mix(hue, vec3(1, 1, 1), L.shellWhite), col3(L.goldTint), gold);
  const dn = d.div(mix(float(L.iconFill), float(L.shellFill), shell));   // 1 = the bubble's edge
  const rim = smoothstep(0.6, 1.0, dn).mul(smoothstep(1.0, 1.04, dn).oneMinus()).mul(shell.mul(0.5).add(0.5));
  // polish 2: the highlight drifts round the top left, a caustic glint answers it low on the far side
  const sa = u.time.mul(L.shineHz * Math.PI * 2).add(v.w);
  const sp = vec2(sin(sa).mul(0.08).sub(0.3), cos(sa).mul(0.06).add(0.32)), pc = p.sub(0.5).div(mix(float(L.iconFill), float(L.shellFill), shell));
  const spec = smoothstep(0, 0.12, length(pc.sub(sp))).oneMinus();
  const caus = smoothstep(0, 0.2, length(pc.add(sp.mul(0.8)))).oneMinus().pow(2).mul(L.causticGain * 0.6).mul(sin(sa.mul(2.3)).mul(0.3).add(0.7));
  const body = smoothstep(0.98, 1.0, dn).oneMinus().mul(L.shellAlpha).mul(shell);
  const glass = tint.mul(rim.mul(L.shellRim).add(caus)).add(spec);
  m.colorNode = mix(glass.add(tint.mul(body)), icon.rgb, iconA);
  // a bare icon draws only the icon: the GLB shell around it is the glass
  m.opacityNode = max(iconA, max(rim.mul(0.85), max(spec.mul(0.7), max(caus, body))).mul(dressed)).mul(v.y);
  return { ...c, uniforms: u, dispose: () => m.dispose() };
}

/**
 * The effect-bubble GLB shell (effect_shell), instanced: one draw for every 3D bubble. geometry is the
 * shell centred on its middle at radius 1 (visuals.js builds it with shellFit). push(x, y, z, R, alpha,
 * seed). A Fresnel glass in place of the GLB clearcoat / iridescence: a clear centre, an iridescent rim
 * drifting slowly with time and seed, one highlight. Back faces culled, drawn after the icons inside.
 */
export function shellCloud(geometry, cap) {
  const aInfo = new InstancedBufferAttribute(new Float32Array(cap * 2), 2).setUsage(DynamicDrawUsage);
  const info = instancedDynamicBufferAttribute(aInfo, 'vec2'), u = { time: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
  const fres = pow(float(1).sub(abs(dot(normalView, positionViewDirection))), L.shellGlassPow);
  const hue = cos(vec3(0, 2.1, 4.2).add(fres.mul(4)).add(u.time.mul(0.6)).add(info.y)).mul(0.5).add(0.5);
  const tint = mix(hue, vec3(1, 1, 1), L.shellWhite);
  // polish 2: the lamp's highlight drifts round the top left (shineHz, per seed) and a caustic glint
  // gathers low on the far side of the glass, the light focused through the sphere
  const sa = u.time.mul(L.shineHz * Math.PI * 2).add(info.y);
  const lamp = normalize(vec3(sin(sa).mul(L.shineDriftRad * 0.4).sub(0.45), cos(sa).mul(L.shineDriftRad * 0.3).add(0.55), 0.7));
  const spec = pow(max(dot(normalView, lamp), 0), 48).mul(L.shellGlassSpec);
  const caus = pow(max(dot(normalView, vec3(lamp.x.negate(), lamp.y.negate().mul(0.8), 0.6).normalize()), 0), L.causticSharp)
    .mul(L.causticGain).mul(sin(sa.mul(2.3)).mul(0.3).add(0.7));
  m.colorNode = tint.mul(fres.mul(L.shellGlassRim).add(caus)).add(spec);
  m.opacityNode = max(fres.mul(0.85), max(spec, max(caus.mul(0.8), float(L.shellGlassBody)))).mul(info.x);
  const mesh = new InstancedMesh(geometry, m, cap);
  mesh.count = 0; mesh.frustumCulled = false; mesh.renderOrder = 4;
  const M = new Matrix4(), P = new Vector3(), Qi = new Quaternion(), Sc = new Vector3();
  let n = 0;
  return {
    mesh, uniforms: u, cap,
    get n() { return n; },
    begin() { n = 0; },
    push(x, y, z, R, alpha, seed) {
      if (n >= cap) return false;
      mesh.setMatrixAt(n, M.compose(P.set(x, y, z), Qi, Sc.setScalar(R)));
      aInfo.array[n * 2] = alpha; aInfo.array[n * 2 + 1] = seed; n++;
      return true;
    },
    end() { mesh.count = n; mesh.visible = n > 0; mesh.instanceMatrix.needsUpdate = true; aInfo.needsUpdate = true; },
    dispose() { m.dispose(); geometry.dispose(); },
  };
}

/** The item card's item_edge: an unlit glow whose strength the caller sets each frame (fxmath.cardEdgeGlow). */
export function cardEdge(hex) {
  const u = { glow: uniform(1) };
  const m = new MeshBasicNodeMaterial();
  m.colorNode = col3(hex).mul(u.glow);
  return { material: m, uniforms: u };
}

/** col = (r, g, b, alpha) 0..1 per mote; additive soft dots. */
export function moteCloud(cap) {
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const c = cloud(cap, m, 'col'), { pos, size, v } = c.nodes;
  // the lens rule (render/lens.js): near the camera a mote, glow or trail halo is capped and fades
  const d = lensDepth(pos);
  m.positionNode = pos; m.scaleNode = lensScale(size, d);
  m.colorNode = v.rgb;
  m.opacityNode = v.a.mul(smoothstep(0.1, 0.5, uv().sub(0.5).length()).oneMinus()).mul(lensAlpha(d));
  return { ...c, dispose: () => m.dispose() };
}

/**
 * The Melt trap puddle on a 2 x 2 plane (local xy, lay it on the road). melt = the wave 2 fx-melt-spiral
 * mask (broad warped arms with drips; preferred), mask = sky-spiral-open-mask; either may be null.
 */
export function meltPuddle(mask, melt = null) {
  const u = { time: uniform(0), alpha: uniform(1), seed: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
  const lp = uv().mul(2).sub(1), span = 1 + L.meltWobble * 0.5 + L.meltDripAmp;
  const r = length(lp).mul(span), a = atan(lp.y, lp.x), t = u.time.mul(L.meltHz).add(u.seed);
  // same edge as fxmath.meltEdge: a wobble plus arms that reach out and pull back like drips
  const wob = sin(a.mul(3).add(t.mul(2.1))).add(sin(a.mul(5).sub(t.mul(1.3)))).mul(0.25 * L.meltWobble);
  const arm = pow(max(sin(a.mul(L.meltArms).add(t)), 0), 3).mul(sin(t.mul(0.7).add(a.mul(2))).mul(0.4).add(0.6)).mul(L.meltDripAmp);
  const edge = wob.add(arm).add(1);
  const inside = smoothstep(edge.sub(0.07), edge, r).oneMinus();
  const turn = u.time.mul(L.meltTurnRadps).add(sin(r.mul(6).sub(u.time.mul(1.2))).mul(0.25));
  const q = lp.mul(0.5).div(edge), qr = vec2(q.x.mul(cos(turn)).sub(q.y.mul(sin(turn))), q.x.mul(sin(turn)).add(q.y.mul(cos(turn)))).add(0.5);
  const tex = melt || mask;
  const spiral0 = tex ? texture(tex, qr).r : sin(a.mul(5).add(log(r.add(0.02)).mul(6)).sub(u.time)).mul(0.5).add(0.5);
  const spiral = smoothstep(L.meltBands[0], L.meltBands[1], spiral0);
  const gloss = smoothstep(edge.sub(0.3), edge.sub(0.08), r).mul(inside).mul(0.45)
    .add(smoothstep(0, 0.25, length(lp.sub(vec2(sin(u.time.mul(0.4)).mul(0.3), 0.25)))).oneMinus().mul(0.35));
  m.colorNode = mix(col3(L.meltDark), col3(L.meltPink), spiral).add(col3(L.meltGloss).mul(gloss));
  // the melt mask is strength: its arms and drips read solid, the gaps between keep meltFloor
  const body = melt ? spiral0.mul(1 - L.meltFloor).add(L.meltFloor) : float(1);
  m.opacityNode = inside.mul(body).mul(u.alpha).mul(0.92);
  return { material: m, uniforms: u };
}

/** Echo copy: an additive rim-lit ghost. */
export function ghost(hex) {
  const u = { opacity: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const fres = pow(float(1).sub(abs(normalView.dot(positionViewDirection))), 1.5);
  m.colorNode = col3(hex).mul(fres.mul(0.8).add(0.2)).mul(u.opacity);
  return { material: m, uniforms: u };
}

/** Pink wave, shockwaves, warning and landing rings: a flat additive ring drawn on a unit plane, opacity and colour uniforms. */
export function ringGlow(hex) {
  const u = { opacity: uniform(0), colour: uniform(new Color(hex)) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const d = length(uv().sub(0.5)).mul(2);
  m.colorNode = vec3(u.colour).mul(smoothstep(0.8, 0.93, d).mul(smoothstep(0.93, 1.0, d).oneMinus())).mul(u.opacity);
  return { material: m, uniforms: u };
}

/** A soft dark ellipse on the road (the lob's landing shadow) on a unit plane, opacity uniform; normal blending. */
export function blobShadow(hex = '#1a0820') {
  const u = { opacity: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -2 });
  const d = length(uv().sub(0.5)).mul(2);
  m.colorNode = col3(hex);
  m.opacityNode = smoothstep(0.15, 1, d).oneMinus().pow(1.6).mul(u.opacity);
  return { material: m, uniforms: u };
}

/**
 * Confetti cards: one instanced draw of small flat cards. push(x, y, z, rx, ry, size, r, g, b, alpha);
 * the face catches the light as it turns (brighter face on, darker edge on), both sides drawn.
 */
export function confettiCloud(cap) {
  const aCol = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const c = instancedDynamicBufferAttribute(aCol, 'vec4');
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  m.colorNode = c.rgb.mul(abs(normalView.z).mul(0.55).add(0.6));
  m.opacityNode = c.a;
  const mesh = new InstancedMesh(new PlaneGeometry(1, 0.64), m, cap);
  mesh.count = 0; mesh.frustumCulled = false; mesh.renderOrder = 3;
  const M = new Matrix4(), P = new Vector3(), Qi = new Quaternion(), Sc = new Vector3(), E = new Euler();
  let n = 0;
  return {
    mesh, cap,
    begin() { n = 0; },
    push(x, y, z, rx, ry, size, r, g, b, alpha) {
      if (n >= cap || alpha <= 0.002) return false;
      mesh.setMatrixAt(n, M.compose(P.set(x, y, z), Qi.setFromEuler(E.set(rx, ry, rx * 0.5)), Sc.setScalar(size)));
      const q = aCol.array; q[n * 4] = r; q[n * 4 + 1] = g; q[n * 4 + 2] = b; q[n * 4 + 3] = alpha; n++;
      return true;
    },
    end() { mesh.count = n; mesh.visible = n > 0; mesh.instanceMatrix.needsUpdate = true; aCol.needsUpdate = true; },
    dispose() { m.dispose(); mesh.geometry.dispose(); },
  };
}
