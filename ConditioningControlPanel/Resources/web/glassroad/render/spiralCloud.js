// Road-level gate mist: fixed bounded batches, with motes rising along the actual road normal.
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, AdditiveBlending, Color } from 'three/webgpu';
import { instancedBufferAttribute, uniform, vec3, uv, smoothstep, sin, fract, mix } from 'three/tsl';
import { lensDepth, lensScale, lensAlpha } from './lens.js';
import { makeRng } from './spiralMath.js';
import { Q } from './quality.js';

export const CLOUD_COUNTS = Object.freeze({ high: 1000, mid: 640, phone: 280 });
export const CLOUD_COLOURS = Object.freeze({
  mainframe: ['#34e989', '#69e8e1'], keep: ['#ed3e9d', '#b681e9'], threshold: ['#ff75c5', '#bdb3ff'],
});
export function cloudAnchors(track, s, count, seed = 1) {
  const random = makeRng(seed), rows = [];
  for (let i = 0; i < count; i++) {
    const at = (s + (random() - .5) * 38 + track.lengthM) % track.lengthM;
    const frame = track.frameAt(at), x = (random() - .5) * Math.min(frame.w - 2, 42);
    const support = track.surfaceAt?.(at, x);
    const p = support?.p || frame.p.map((v, k) => v + frame.b[k] * x);
    rows.push({ s: at, x, p: [...p], n: [...(support?.n || frame.n)], phase: random(), size: .3 + random() * .7 });
  }
  return rows;
}
export function createSpiralCloud(track, s, seed) {
  const count = CLOUD_COUNTS[Q.name] || CLOUD_COUNTS.mid, rows = cloudAnchors(track, s, count, seed);
  const attr = (width, fn) => instancedBufferAttribute(new InstancedBufferAttribute(new Float32Array(rows.flatMap(fn)), width), 'vec' + width);
  const base = attr(3, r => r.p), normal = attr(3, r => r.n), data = attr(2, r => [r.phase, r.size]);
  const time = uniform(0), strength = uniform(1), phase = fract(data.x.add(time.mul(.13)));
  const pos = base.add(normal.mul(phase.mul(1.8).add(.18))), depth = lensDepth(pos);
  const material = new SpriteNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending });
  const colours = (CLOUD_COLOURS[track.id] || CLOUD_COLOURS.threshold).map(h => { const c = new Color(h); return vec3(c.r,c.g,c.b); });
  material.positionNode = pos;
  material.scaleNode = lensScale(data.y.mul(phase.mul(.8).add(.8)), depth);
  material.colorNode = mix(colours[0], colours[1], data.x).mul(1.3);
  const shape = smoothstep(.04, .5, uv().sub(.5).length()).oneMinus();
  material.opacityNode = shape.mul(sin(phase.mul(Math.PI))).mul(.48).mul(strength).mul(lensAlpha(depth));
  const object = new Sprite(material); object.name = 'spiral-road-cloud'; object.count = count;
  object.frustumCulled = false; object.renderOrder = 2;
  return { object, rows, time, update(dt, { gentle = false, paused = false } = {}) {
    if (paused) return;
    time.value += Math.max(0, dt) * (gentle ? .5 : 1); strength.value = gentle ? .6 : 1;
  }, dispose() { material.dispose(); object.removeFromParent(); } };
}
