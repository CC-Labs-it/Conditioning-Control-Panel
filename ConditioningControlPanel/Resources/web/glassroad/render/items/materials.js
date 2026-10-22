/* render/items/materials.js - node materials for the effect bubbles (render/items/visuals.js).
 * bubbleCloud: every bubble on screen in one instanced sprite draw (the living atlas icon inside a
 * glass shell: iridescent rim, a highlight, optional gold tint or shell-only for shields and pops).
 * moteCloud: the drifting motes, additive. meltPuddle: the Melt trap, the spiral mask on the road
 * with a wobbling, dripping edge. ghost: Echo copies. ring: the Pink wave.
 */
import {
  Sprite, SpriteNodeMaterial, MeshBasicNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, AdditiveBlending,
  DoubleSide, Color,
} from 'three/webgpu';
import {
  instancedDynamicBufferAttribute, uniform, uv, vec2, vec3, float, texture, mix, smoothstep, sin, cos, atan, log, length,
  max, pow, floor, mod, normalize, cameraPosition, normalView, positionViewDirection, abs, step,
} from 'three/tsl';
import { ITEM_LOOK as L } from './look.js';
import { ATLAS_GRID } from './living.js';

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

/** info = (cell or -1, alpha, mode 0 icon / 1 gold icon / 2 shell / 3 gold shell, seed). */
export function bubbleCloud(cap, atlas) {
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  const c = cloud(cap, m, 'info'), { pos, size, v } = c.nodes, u = { time: uniform(0) };
  // pulled toward the camera by its radius so the front of the glass sits in front of what it holds
  m.positionNode = pos.add(normalize(cameraPosition.sub(pos)).mul(size.mul(0.5)));
  m.scaleNode = size;
  const g = ATLAS_GRID, cell = v.x, mode = v.z, shell = step(1.5, mode), gold = mod(mode, 2);
  const p = uv(), d = length(p.sub(0.5)).mul(2);
  const auv = vec2(p.x.add(mod(cell, g)), p.y.add(float(g - 1).sub(floor(cell.div(g))))).div(g);
  const icon = texture(atlas, auv);
  const iconA = icon.a.mul(shell.oneMinus()).mul(step(0, cell));
  const ang = atan(p.y.sub(0.5), p.x.sub(0.5));
  const hue = cos(vec3(0, 2.1, 4.2).add(d.mul(5).add(ang).add(u.time.mul(0.6)).add(v.w))).mul(0.5).add(0.5);
  const tint = mix(mix(hue, vec3(1, 1, 1), L.shellWhite), col3(L.goldTint), gold);
  const dn = d.div(mix(float(L.iconFill), float(L.shellFill), shell));   // 1 = the bubble's edge
  const rim = smoothstep(0.6, 1.0, dn).mul(smoothstep(1.0, 1.04, dn).oneMinus()).mul(shell.mul(0.5).add(0.5));
  const spec = smoothstep(0, 0.12, length(p.sub(0.5).div(mix(float(L.iconFill), float(L.shellFill), shell)).sub(vec2(-0.3, 0.32)))).oneMinus();
  const body = smoothstep(0.98, 1.0, dn).oneMinus().mul(L.shellAlpha).mul(shell);
  const glass = tint.mul(rim.mul(L.shellRim)).add(spec);
  m.colorNode = mix(glass.add(tint.mul(body)), icon.rgb, iconA);
  m.opacityNode = max(iconA, max(rim.mul(0.85), max(spec.mul(0.7), body))).mul(v.y);
  return { ...c, uniforms: u, dispose: () => m.dispose() };
}

/** col = (r, g, b, alpha) 0..1 per mote; additive soft dots. */
export function moteCloud(cap) {
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const c = cloud(cap, m, 'col'), { pos, size, v } = c.nodes;
  m.positionNode = pos; m.scaleNode = size;
  m.colorNode = v.rgb;
  m.opacityNode = v.a.mul(smoothstep(0.1, 0.5, uv().sub(0.5).length()).oneMinus());
  return { ...c, dispose: () => m.dispose() };
}

/** The Melt trap puddle on a 2 x 2 plane (local xy, lay it on the road). mask = sky-spiral-open-mask or null. */
export function meltPuddle(mask) {
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
  const spiral0 = mask ? texture(mask, qr).r : sin(a.mul(5).add(log(r.add(0.02)).mul(6)).sub(u.time)).mul(0.5).add(0.5);
  const spiral = smoothstep(L.meltBands[0], L.meltBands[1], spiral0);
  const gloss = smoothstep(edge.sub(0.3), edge.sub(0.08), r).mul(inside).mul(0.45)
    .add(smoothstep(0, 0.25, length(lp.sub(vec2(sin(u.time.mul(0.4)).mul(0.3), 0.25)))).oneMinus().mul(0.35));
  m.colorNode = mix(col3(L.meltDark), col3(L.meltPink), spiral).add(col3(L.meltGloss).mul(gloss));
  m.opacityNode = inside.mul(u.alpha).mul(0.92);
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

/** Pink wave: a flat additive ring drawn on a unit plane, opacity uniform. */
export function ringGlow(hex) {
  const u = { opacity: uniform(0) };
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  const d = length(uv().sub(0.5)).mul(2);
  m.colorNode = col3(hex).mul(smoothstep(0.8, 0.93, d).mul(smoothstep(0.93, 1.0, d).oneMinus())).mul(u.opacity);
  return { material: m, uniforms: u };
}
