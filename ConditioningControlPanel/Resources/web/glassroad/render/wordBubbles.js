/* render/wordBubbles.js - the song's impact words in glass bubbles over the road (sim/words.js views).
 *
 * Course lettering (owner 2026-10-25): wordTheme.js shares fonts, finishes and colours with wordRush.js.
 * Mainframe is terminal green, Keep engraved rose/gold, Boudoir satin/pearl, GOONWORKS comic fuchsia,
 * Threshold prism chrome. Glass follows the live level accent without re-lettering the atlas.
 * One instanced sprite draw for every bubble, ring and flash. Historical pass 4 (owner: "the text inside the bubbles is
 * actually shiny, pink and cool to look at"): each word is LETTERED once into a slot of a shared canvas atlas
 * (8 x 8 cells) in its kind's diegetic face (render/fonts.js, wordBubblesMath BUBBLE_TEXT: editorial italics,
 * gate capitals, the trigger's own round face): a pink gradient fill, a thick plum stroke over a soft pink
 * halo, a white gloss band over the top of the letters and seeded glitter inside them. The shader adds a slow
 * shimmer sweep across the fill and a twinkle on the glitter (Gentle: a slower, softer sweep and no twinkle).
 * The song's words are lettered when its song first shows, one word a frame in sung order (about 2.5 ms each, so
 * no frame carries the whole ~50 ms), and the atlas uploads ONCE when they are all in (or at once, should a bubble
 * on screen need a word not uploaded yet); a word the song did not name still gets a slot on the fly (least
 * recently used reused).
 * The glass itself is drawn in the shader: a pale pink body, a bright rim and one highlight. The look over
 * time is render/wordBubblesMath.js (IN, live swell, the karaoke light, pop OUT, miss OUT). A pop's flash core
 * asks photosafe.request('pop') once and is skipped when refused or under Gentle.
 * Only the local player's bubbles exist here: nobody else's are ever drawn.
 */
import { Color, Sprite, SpriteNodeMaterial, InstancedBufferAttribute, DynamicDrawUsage, CanvasTexture, SRGBColorSpace } from 'three/webgpu';
import { instancedDynamicBufferAttribute, uv, vec2, vec3, vec4, float, texture, mix, smoothstep, length, max, min, floor, mod, step, atan, sin, clamp,
  uniform, fract, exp, dot } from 'three/tsl';
import { bubbleLook, fitFontPx, letterLayout, asciiWord, BUBBLE_LOOK as BL, BUBBLE_TEXT as BT } from './wordBubblesMath.js';
import { FACES } from './fonts.js';
import { scriptBubbles } from '../sim/words.js';
import { wordTheme, wordFace, wordAccent } from './wordTheme.js';
import { wordIdentity } from '../sim/wordIdentity.js';
import { roadWordIdentityUv } from './roadWordIdentity.js';

const GRID = 8, CELL = 256;
const MODE = { bubble: 0, ring: 1, flash: 2 };
const SUNG_MAX = 0.45;   // a bubble's mode carries its karaoke light, 0..SUNG_MAX (under the ring's 0.5 step)
const TEXT_OUT = 0.45;   // ...and its slot's fraction the word's fade out of the glass, 0..TEXT_OUT

/** A small seeded rng (mulberry32) so a word's glitter is the same every time it is lettered. */
function rng(seed) {
  let a = seed >>> 0 || 0x9e3779b9;
  return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
}
const hashStr = (s) => { let h = 2166136261; for (let i = 0; i < s.length; i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619); return h >>> 0; };

/** A four-point glint at (x, y), arms r long. */
function glint(c, x, y, r) {
  c.beginPath();
  c.moveTo(x, y - r); c.quadraticCurveTo(x, y, x + r, y); c.quadraticCurveTo(x, y, x, y + r);
  c.quadraticCurveTo(x, y, x - r, y); c.quadraticCurveTo(x, y, x, y - r);
  c.fill();
}

function wordAtlas(theme) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = GRID * CELL;
  const ctx = canvas.getContext('2d');
  // the fill is built on a scratch cell (gloss and glitter clip to the letters there), then laid over the stroke
  const scratch = document.createElement('canvas');
  scratch.width = scratch.height = CELL;
  const sc = scratch.getContext('2d');
  const tex = new CanvasTexture(canvas);
  tex.colorSpace = SRGBColorSpace;
  const slots = new Map(), used = new Float64Array(GRID * GRID).fill(-1), owner = new Array(GRID * GRID).fill(null);
  let tick = 0;
  const unsent = new Set();   // slots lettered since the last upload

  function letter(slot, word, row) {
    const x0 = (slot % GRID) * CELL, y0 = Math.floor(slot / GRID) * CELL, cx = CELL / 2, cy = CELL / 2;
    const f = wordFace(theme, row), face = FACES[f.face];
    let text = asciiWord(word);
    if (f.upper) text = text.toUpperCase();
    const ref = 100;
    sc.font = face.css(ref, f.weight);
    const measure = (str) => { const mm = sc.measureText(str); return mm && mm.width > 0 ? mm.width : 0; };
    const lay = measure(text) > 0 ? letterLayout(text, measure, ref, CELL) : { px: fitFontPx(text, CELL), lines: [text] };
    const px = lay.px, n = lay.lines.length, font = face.css(px, f.weight), maxW = CELL * 0.9;
    // the italic faces lean right: nudge them back so the word sits in the middle of the glass
    const ox = face.style === 'italic' ? -px * 0.04 : 0;
    const lineY = (i) => cy + (i - (n - 1) / 2) * px * BT.lineH;
    ctx.clearRect(x0, y0, CELL, CELL);
    // 1. halo and plum stroke, straight on the atlas
    ctx.save();
    ctx.font = font; ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.lineJoin = 'round';
    ctx.lineWidth = Math.max(5, px * BT.stroke); ctx.strokeStyle = theme.ink;
    for (const blur of [px * BT.haloBlur, 0]) {
      ctx.shadowColor = theme.halo; ctx.shadowBlur = blur;
      for (let i = 0; i < n; i++) ctx.strokeText(lay.lines[i], x0 + cx + ox, y0 + lineY(i), maxW);
    }
    ctx.restore();
    // 2. the fill on the scratch cell: gradient, then the gloss band and glitter clipped to the letters
    sc.clearRect(0, 0, CELL, CELL);
    sc.font = font; sc.textAlign = 'center'; sc.textBaseline = 'middle';
    const r = rng(hashStr(text + row));
    for (let i = 0; i < n; i++) {
      const y = lineY(i), top = y - px * 0.42, foot = y + px * 0.42;
      sc.globalCompositeOperation = 'source-over';
      const g = sc.createLinearGradient(0, top, 0, foot);
      for (const [at, col] of theme.fill) g.addColorStop(at, col);
      sc.fillStyle = g;
      sc.fillText(lay.lines[i], cx + ox, y, maxW);
    }
    sc.globalCompositeOperation = 'source-atop';
    // A static finish cut into the letters: terminal scanlines, comic dots, gilt engraving or satin.
    sc.fillStyle = theme.ink;
    sc.globalAlpha = theme.motif === 'terminal' ? 0.28 : 0.16;
    if (theme.motif === 'terminal') {
      for (let y = 0; y < CELL; y += 6) sc.fillRect(0, y, CELL, 1.5);
    } else if (theme.motif === 'comic') {
      for (let y = 0; y < CELL; y += 9) for (let x = (y % 18 ? 4 : 0); x < CELL; x += 9) {
        sc.beginPath(); sc.arc(x, y, 1.25, 0, Math.PI * 2); sc.fill();
      }
    } else if (theme.motif === 'engraved') {
      sc.strokeStyle = '#fff4d1'; sc.lineWidth = 1;
      for (let x = -CELL; x < CELL * 2; x += 10) { sc.beginPath(); sc.moveTo(x, 0); sc.lineTo(x - CELL / 2, CELL); sc.stroke(); }
    }
    sc.globalAlpha = 1;
    for (let i = 0; i < n; i++) {
      const y = lineY(i), top = y - px * 0.42, foot = y + px * 0.42, w = Math.min(maxW, measure(lay.lines[i]) * px / ref || CELL * 0.8);
      const gl = sc.createLinearGradient(0, top, 0, top + (foot - top) * BT.glossTo);
      gl.addColorStop(0, `rgba(255,255,255,${BT.glossAlpha})`);
      gl.addColorStop(0.7, `rgba(255,255,255,${BT.glossAlpha * 0.45})`);
      gl.addColorStop(1, 'rgba(255,255,255,0.12)');
      sc.fillStyle = gl;
      sc.fillRect(0, top - px * 0.3, CELL, (foot - top) * BT.glossTo + px * 0.3);
      sc.fillStyle = '#ffffff';
      for (let k = 0; k < theme.glitter / n; k++) {
        const gx = cx + ox + (r() - 0.5) * w, gy = y + (r() - 0.5) * px * 0.7, gr = 0.8 + r() * 1.6;
        if (r() < 0.25) glint(sc, gx, gy, gr * 2.6);
        else { sc.beginPath(); sc.arc(gx, gy, gr, 0, Math.PI * 2); sc.fill(); }
      }
    }
    sc.globalCompositeOperation = 'source-over';
    ctx.drawImage(scratch, x0, y0);
    // 3. a few specular glints riding the letters' edges (the shader twinkles anything this white)
    ctx.save();
    ctx.fillStyle = '#ffffff';
    for (let k = 0; k < theme.stars; k++) {
      const i = k % n, w = Math.min(maxW, measure(lay.lines[i]) * px / ref || CELL * 0.8);
      const gx = x0 + cx + ox + (r() - 0.5) * w * 1.05, gy = y0 + lineY(i) + (k % 2 ? 0.36 : -0.4) * px;
      glint(ctx, gx, gy, px * (0.07 + r() * 0.05));
    }
    ctx.restore();
    unsent.add(slot);
  }

  return {
    texture: tex,
    frame() { tick++; },
    /** Lettered but not uploaded yet. */
    unsent(slot) { return unsent.has(slot); },
    get pending() { return unsent.size > 0; },
    /** Uploads the atlas once for everything lettered since the last upload. */
    flush() { if (unsent.size) { unsent.clear(); tex.needsUpdate = true; } },
    /** The slot holding this word in this row's face, lettering it into the least recently used one when new. */
    slotOf(word, row = 0) {
      const key = row + '|' + word;
      let s = slots.get(key);
      if (s === undefined) {
        s = 0;
        for (let i = 1; i < used.length; i++) if (used[i] < used[s]) s = i;
        if (owner[s] !== null) slots.delete(owner[s]);
        owner[s] = key; slots.set(key, s);
        letter(s, word, row);
      }
      used[s] = tick;
      return s;
    },
    dispose() { tex.dispose(); },
  };
}

/** The bubble material over one instanced sprite: v = (slot + word fade, alpha, mode, seed). The slot's fraction
 * carries how far the word has left the glass (0 there, TEXT_OUT gone), so a pop empties the bubble. */
function bubbleCloud(cap, atlas, theme) {
  const aPos = new InstancedBufferAttribute(new Float32Array(cap * 3), 3).setUsage(DynamicDrawUsage);
  const aSize = new InstancedBufferAttribute(new Float32Array(cap), 1).setUsage(DynamicDrawUsage);
  const aV = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const aIdentity = new InstancedBufferAttribute(new Float32Array(cap * 4), 4).setUsage(DynamicDrawUsage);
  const pos = instancedDynamicBufferAttribute(aPos, 'vec3'), size = instancedDynamicBufferAttribute(aSize, 'float');
  const v = instancedDynamicBufferAttribute(aV, 'vec4');
  const identity = instancedDynamicBufferAttribute(aIdentity, 'vec4');
  // the shimmer clock and its strengths (uniforms: a Gentle switch never builds a new program)
  const u = { time: uniform(0), sweepHz: uniform(BT.sweepHz), sweepGain: uniform(BT.sweepGain), twinkle: uniform(1),
    tint: uniform(new Color(theme.accent)), sung: uniform(new Color(theme.echo)), identityMotion: uniform(1) };
  const m = new SpriteNodeMaterial({ transparent: true, depthWrite: false });
  m.positionNode = pos;
  m.scaleNode = size;
  const p = uv(), d = length(p.sub(0.5)).mul(2), slot = floor(v.x), alpha = v.y, mode = v.z, seed = v.w;
  const textA = clamp(float(1).sub(fract(v.x).div(TEXT_OUT)), 0, 1);
  const isRing = step(0.5, mode).mul(step(mode, 1.5)), isFlash = step(1.5, mode), isBubble = float(1).sub(isRing).sub(isFlash);
  // glass: a pale pink body, a bright iridescent rim, one highlight top left
  const ang = atan(p.y.sub(0.5), p.x.sub(0.5));
  const film = sin(vec3(0, 2.1, 4.2).add(ang.add(seed.mul(6.28)).add(d.mul(4)))).mul(0.5).add(0.5);
  const rimTint = mix(mix(u.tint, vec3(1), 0.3), film, 0.1);
  const inside = float(1).sub(smoothstep(0.97, 1.0, d));
  const rim = smoothstep(0.72, 0.95, d).mul(inside);
  const spot = float(1).sub(smoothstep(0, 0.16, length(p.sub(vec2(0.34, 0.68)))));
  // Only source lettering deforms. The glass, rim, ring and flash keep their original coordinates.
  const semantic = roadWordIdentityUv(p, identity, u), glyphUv = clamp(semantic.uv, .002, .998);
  const auv = vec2(glyphUv.x.add(mod(slot, GRID)), glyphUv.y.add(float(GRID - 1).sub(floor(slot.div(GRID))))).div(GRID);
  const tex = texture(atlas, auv);
  // The existing colour atlas has bright interiors and dark outlines. EMPTY drains only the interiors.
  const interior = smoothstep(.3, .55, dot(tex.rgb, vec3(.299, .587, .114)));
  const sourceCoverage = semantic.mask.mul(mix(1, semantic.coreMask, interior));
  const word = vec4(mix(tex.rgb, semantic.tint, semantic.tintMix.mul(interior)), tex.a.mul(textA).mul(sourceCoverage));
  // the lettering's life: a slow diagonal shimmer band runs across the pink fill (never the plum stroke), and
  // the white glitter twinkles, each speck on its own phase
  const diag = p.x.add(p.y.mul(0.45));
  const dd = diag.sub(fract(u.time.mul(u.sweepHz).add(seed)).mul(2.4).sub(0.6)).div(0.1);
  const lum = dot(word.rgb, vec3(0.299, 0.587, 0.114));
  const fillMask = smoothstep(0.3, 0.55, lum).mul(word.a);
  const sheen = exp(dd.mul(dd).negate()).mul(fillMask).mul(u.sweepGain);
  const speck = smoothstep(0.86, 0.97, min(word.r, min(word.g, word.b))).mul(word.a);
  const cellId = floor(auv.mul(GRID * CELL / 6));
  const ph = fract(sin(dot(cellId, vec2(12.9898, 78.233))).mul(43758.5453));
  const tw = sin(u.time.mul(BT.twinkleHz).add(ph.mul(6.28))).mul(0.5).add(0.5).pow(3).mul(u.twinkle);
  const wordCol = word.rgb.add(vec3(1, 0.93, 0.98).mul(sheen)).add(vec3(1, 1, 1).mul(speck.mul(tw).mul(0.9)));
  // the karaoke light (wordBubblesMath sungLight) rides in the bubble's mode, 0..SUNG_MAX: on its word the
  // glass fills warm gold, the rim glows and the body thickens behind the word, so the line answers the voice
  const sung = clamp(mode.mul(isBubble).div(SUNG_MAX), 0, 1);
  const bodyCol = mix(mix(mix(u.tint, vec3(1), 0.7), mix(u.sung, vec3(1), 0.35), sung), mix(rimTint, u.sung, sung.mul(0.6)), rim);
  const glassCol = bodyCol.add(vec3(1, 1, 1).mul(spot.mul(0.8))).add(u.sung.mul(rim.mul(sung).mul(0.6)));
  const bubbleCol = mix(glassCol, wordCol, word.a);
  const bubbleA = max(word.a, max(inside.mul(sung.mul(0.38).add(0.3)), max(rim.mul(0.9), spot.mul(0.85)))).mul(inside);
  // ring: a thin bright band; flash: a soft white core
  const ringA = smoothstep(0.78, 0.9, d).mul(float(1).sub(smoothstep(0.9, 1.0, d)));
  const flashA = float(1).sub(smoothstep(0, 1, d)).pow(2);
  m.colorNode = bubbleCol.mul(isBubble).add(rimTint.mul(isRing)).add(vec3(1, 0.97, 0.99).mul(isFlash));
  m.opacityNode = bubbleA.mul(isBubble).add(ringA.mul(isRing)).add(flashA.mul(isFlash)).mul(alpha);
  const sprite = new Sprite(m);
  sprite.name = 'word-bubbles';
  sprite.count = 0; sprite.frustumCulled = false; sprite.renderOrder = 4;
  let n = 0;
  return {
    sprite, u, attributes: { position: aPos, size: aSize, values: aV, identity: aIdentity },
    begin() { n = 0; },
    push(x, y, z, s, a, b, c, e, kind = 0, elapsed = 0, collected = -1, chars = 1) {
      if (n >= cap) return false;
      const P = aPos.array, Q = aV.array;
      P[n * 3] = x; P[n * 3 + 1] = y; P[n * 3 + 2] = z; aSize.array[n] = s;
      Q[n * 4] = a; Q[n * 4 + 1] = b; Q[n * 4 + 2] = c; Q[n * 4 + 3] = e;
      aIdentity.array[n * 4] = kind; aIdentity.array[n * 4 + 1] = elapsed;
      aIdentity.array[n * 4 + 2] = collected; aIdentity.array[n * 4 + 3] = chars; n++;
      return true;
    },
    end() { sprite.count = n; sprite.visible = n > 0; aPos.needsUpdate = aSize.needsUpdate = aV.needsUpdate = aIdentity.needsUpdate = true; },
    dispose() { m.dispose(); },
  };
}

const sizeOf = (v) => (v.row === 5 ? BL.triggerSizeM : v.row === 3 ? BL.gateSizeM : BL.sizeM);

/**
 * @param scene the world scene
 * @param opts { feel (gentle.on), photosafe (request('pop')), cap (bubbles + rings + flashes on screen) }
 * @returns {{ update(live: object[], song?: object, dt?: number, context?: object): void, prepare(song: object): void, clear(): void,
 *   dispose(): void, object: object }}
 */
export function createWordBubbles(scene, { feel = null, photosafe = null, cap = 128, track = null, pal = null } = {}) {
  const theme = wordTheme(track, pal), atlas = wordAtlas(theme), cloud = bubbleCloud(cap, atlas.texture, theme);
  scene.add(cloud.sprite);
  const flashed = new Map();   // view id -> the pop's flash was granted (asked once per pop)
  const look = {};
  const gentle = () => !!(feel && feel.get('gentle.on'));
  let lettered = null, clock = 0, queue = [], qi = 0;

  /** Queues every bubble word of the song for lettering (one a frame from the next update, one upload at the end). */
  function prepare(song) {
    if (!song || song === lettered) return;
    lettered = song; qi = 0;
    try { queue = scriptBubbles(song); } catch (e) { queue = []; console.warn('[glassroad/words] lettering failed: ' + (e && e.message)); }
  }

  function update(live, song = null, dt = 1 / 60, { paused = false, motion = 'full', gentle: gentleView = false } = {}) {
    if (paused) return;
    if (song) prepare(song);
    if (qi < queue.length) { const b = queue[qi++]; atlas.slotOf(b.w, b.row); if (qi >= queue.length) atlas.flush(); }
    atlas.frame();
    cloud.begin();
    const g = gentle() || gentleView || motion === 'off';
    clock = (clock + Math.min(0.1, Math.max(0, dt || 0))) % 3600;
    cloud.u.time.value = clock;
    cloud.u.sweepHz.value = g ? BT.gentleSweepHz : BT.sweepHz;
    cloud.u.sweepGain.value = g ? BT.gentleSweepGain : BT.sweepGain;
    cloud.u.twinkle.value = g ? 0 : 1;
    cloud.u.identityMotion.value = g ? 0 : motion === 'less' ? .5 : 1;
    cloud.u.tint.value.set(wordAccent(theme, pal));
    for (const v of live) {
      const seed = ((v.id * 0.618034) % 1);
      if (v.state === 'popped' && !flashed.has(v.id)) flashed.set(v.id, !g && (!photosafe || photosafe.request('pop')));
      if (v.state === 'live') flashed.delete(v.id);   // the song looped: the word may pop again
      bubbleLook(v, { gentle: g, flashOk: !!flashed.get(v.id), seed }, look);
      const kind = wordIdentity(v.w), popS = g ? BL.gentlePopS : BL.popS;
      const collected = v.state === 'popped' ? Math.max(0, Math.min(1, v.outT / popS)) : -1;
      if (kind && collected >= 0) look.text = 1 - collected * collected * (3 - 2 * collected);
      const slot = atlas.slotOf(v.w, v.row || 0), size = sizeOf(v);
      if (atlas.unsent(slot)) atlas.flush();   // on screen before the queue finished: upload now
      for (let k = 0; k < v.n; k++) {
        const p = v.p[k], y = p[1] + look.lift;
        if (look.alpha > 0.002) cloud.push(p[0], y, p[2], size * look.scale, slot + (1 - look.text) * TEXT_OUT, look.alpha, MODE.bubble + look.sung * SUNG_MAX, seed + k * 0.31,
          kind, -v.remainS, collected, asciiWord(v.w).length);
        if (look.ring > 0.002) cloud.push(p[0], y, p[2], size * look.ringScale, slot, look.ring, MODE.ring, seed);
        if (look.flash > 0.002 && k === v.hit) cloud.push(p[0], y, p[2], size * BL.flashSize, slot, look.flash, MODE.flash, seed);
      }
    }
    if (flashed.size > 256) flashed.clear();
    cloud.end();
  }

  return {
    object: cloud.sprite,
    _dev: { attributes: cloud.attributes, uniforms: cloud.u },
    update, prepare,
    /** The lettering atlas canvas (dev probes look at it). */
    get atlasCanvas() { return atlas.texture.image; },
    clear() { flashed.clear(); cloud.begin(); cloud.end(); },
    dispose() { scene.remove(cloud.sprite); cloud.dispose(); atlas.dispose(); },
  };
}
