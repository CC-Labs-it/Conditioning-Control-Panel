/* render/wordBubbles.js - the song's words in glass bubbles over the road (sim/words.js views).
 *
 * One instanced sprite draw for every bubble, ring and flash. Each word is written once into a slot of a
 * shared canvas atlas (8 x 8 cells, least recently used slot reused), dark bold text with a light edge so
 * it reads over the glass at 30 m. The glass itself is drawn in the shader: a pale pink body, a bright rim
 * and one highlight. The look over time is render/wordBubblesMath.js (IN, live swell, pop OUT, miss OUT).
 * A pop's flash core asks photosafe.request('pop') once and is skipped when refused or under Gentle.
 * Only the local player's bubbles exist here: nobody else's are ever drawn.
 */
import { Sprite, SpriteNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, CanvasTexture, SRGBColorSpace } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, vec2, vec3, float, texture, mix, smoothstep, length, max, floor, mod, step, atan, sin } from 'three/tsl';
import { bubbleLook, fitFontPx, BUBBLE_LOOK as BL } from './wordBubblesMath.js';

const GRID = 8, CELL = 256;
const MODE = { bubble: 0, ring: 1, flash: 2 };

function wordAtlas() {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = GRID * CELL;
  const ctx = canvas.getContext('2d');
  const tex = new CanvasTexture(canvas);
  tex.colorSpace = SRGBColorSpace;
  const slots = new Map(), used = new Float64Array(GRID * GRID).fill(-1), owner = new Array(GRID * GRID).fill(null);
  let tick = 0;
  function draw(slot, word) {
    const x = (slot % GRID) * CELL, y = Math.floor(slot / GRID) * CELL;
    ctx.clearRect(x, y, CELL, CELL);
    const px = fitFontPx(word, CELL);
    ctx.font = `800 ${px}px system-ui, "Segoe UI", sans-serif`;
    ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.lineJoin = 'round'; ctx.lineWidth = Math.max(4, px * 0.16);
    ctx.strokeStyle = 'rgba(255,246,251,0.95)';
    ctx.strokeText(word, x + CELL / 2, y + CELL / 2, CELL * 0.86);
    ctx.fillStyle = '#3b0f33';
    ctx.fillText(word, x + CELL / 2, y + CELL / 2, CELL * 0.86);
    tex.needsUpdate = true;
  }
  return {
    texture: tex,
    frame() { tick++; },
    /** The slot holding word, drawing it into the least recently used one when new. */
    slotOf(word) {
      let s = slots.get(word);
      if (s === undefined) {
        s = 0;
        for (let i = 1; i < used.length; i++) if (used[i] < used[s]) s = i;
        if (owner[s] !== null) slots.delete(owner[s]);
        owner[s] = word; slots.set(word, s);
        draw(s, word);
      }
      used[s] = tick;
      return s;
    },
    dispose() { tex.dispose(); },
  };
}

/** The bubble material over one instanced sprite: v = (slot, alpha, mode, seed). */
function bubbleCloud(cap, atlas) {
  const aPos = new InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(cap), 1).setUsage(DynamicDrawUsage);
  const aV = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const pos = instancedDynamicBufferAttribute(aPos, 'vec3'), size = instancedDynamicBufferAttribute(aSize, 'float');
  const v = instancedDynamicBufferAttribute(aV, 'vec4');
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  m.positionNode = pos;
  m.scaleNode = size;
  const p = uv(), d = length(p.sub(0.5)).mul(2), slot = v.x, alpha = v.y, mode = v.z, seed = v.w;
  const isRing = step(0.5, mode).mul(step(mode, 1.5)), isFlash = step(1.5, mode), isBubble = float(1).sub(isRing).sub(isFlash);
  // glass: a pale pink body, a bright iridescent rim, one highlight top left
  const ang = atan(p.y.sub(0.5), p.x.sub(0.5));
  const film = sin(vec3(0, 2.1, 4.2).add(ang.add(seed.mul(6.28)).add(d.mul(4)))).mul(0.5).add(0.5);
  const rimTint = mix(vec3(1, 0.62, 0.86), film, 0.35);
  const inside = float(1).sub(smoothstep(0.97, 1.0, d));
  const rim = smoothstep(0.72, 0.95, d).mul(inside);
  const spot = float(1).sub(smoothstep(0, 0.16, length(p.sub(vec2(0.34, 0.68)))));
  const auv = vec2(p.x.add(mod(slot, GRID)), p.y.add(float(GRID - 1).sub(floor(slot.div(GRID))))).div(GRID);
  const word = texture(atlas, auv);
  const bodyCol = mix(vec3(1, 0.86, 0.94), rimTint, rim);
  const glassCol = bodyCol.add(vec3(1, 1, 1).mul(spot.mul(0.8)));
  const bubbleCol = mix(glassCol, word.rgb, word.a);
  const bubbleA = max(word.a, max(inside.mul(0.32), max(rim.mul(0.9), spot.mul(0.85)))).mul(inside);
  // ring: a thin bright band; flash: a soft white core
  const ringA = smoothstep(0.78, 0.9, d).mul(float(1).sub(smoothstep(0.9, 1.0, d)));
  const flashA = float(1).sub(smoothstep(0, 1, d)).pow(2);
  m.colorNode = bubbleCol.mul(isBubble).add(rimTint.mul(isRing)).add(vec3(1, 0.97, 0.99).mul(isFlash));
  m.opacityNode = bubbleA.mul(isBubble).add(ringA.mul(isRing)).add(flashA.mul(isFlash)).mul(alpha);
  const sprite = new Sprite(m);
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 4;
  let n = 0;
  return {
    sprite,
    begin() { n = 0; },
    push(x, y, z, s, a, b, c, dd) {
      if (n >= cap) return false;
      const P = aPos.array, Q = aV.array;
      P[n * 3] = x; P[n * 3 + 1] = y; P[n * 3 + 2] = z; aSize.array[n] = s;
      Q[n * 4] = a; Q[n * 4 + 1] = b; Q[n * 4 + 2] = c; Q[n * 4 + 3] = dd; n++;
      return true;
    },
    end() { sprite.count = n; sprite.visible = n > 0; aPos.needsUpdate = aSize.needsUpdate = aV.needsUpdate = true; },
    dispose() { m.dispose(); },
  };
}

/**
 * @param scene the world scene
 * @param opts { feel (gentle.on), photosafe (request('pop')), cap (bubbles + rings + flashes on screen) }
 * @returns {{ update(live: object[], camera?: object): void, clear(): void, dispose(): void, object: object }}
 */
export function createWordBubbles(scene, { feel = null, photosafe = null, cap = 128 } = {}) {
  const atlas = wordAtlas(), cloud = bubbleCloud(cap, atlas.texture);
  scene.add(cloud.sprite);
  const flashed = new Map();   // view id -> the pop's flash was granted (asked once per pop)
  const look = {};
  const gentle = () => !!(feel && feel.get('gentle.on'));

  function update(live) {
    atlas.frame();
    cloud.begin();
    const g = gentle();
    for (const v of live) {
      const seed = ((v.id * 0.618034) % 1);
      if (v.state === 'popped' && !flashed.has(v.id)) flashed.set(v.id, !g && (!photosafe || photosafe.request('pop')));
      if (v.state === 'live') flashed.delete(v.id);   // the song looped: the word may pop again
      bubbleLook(v, { gentle: g, flashOk: !!flashed.get(v.id), seed }, look);
      const slot = atlas.slotOf(v.w), size = v.gate ? BL.gateSizeM : BL.sizeM;
      for (let k = 0; k < v.n; k++) {
        const p = v.p[k], y = p[1] + look.lift;
        if (look.alpha > 0.002) cloud.push(p[0], y, p[2], size * look.scale, slot, look.alpha, MODE.bubble, seed + k * 0.31);
        if (look.ring > 0.002) cloud.push(p[0], y, p[2], size * look.ringScale, slot, look.ring, MODE.ring, seed);
        if (look.flash > 0.002 && k === v.hit) cloud.push(p[0], y, p[2], size * BL.flashSize, slot, look.flash, MODE.flash, seed);
      }
    }
    if (flashed.size > 256) flashed.clear();
    cloud.end();
  }

  return {
    object: cloud.sprite,
    update,
    clear() { flashed.clear(); cloud.begin(); cloud.end(); },
    dispose() { scene.remove(cloud.sprite); cloud.dispose(); atlas.dispose(); },
  };
}
