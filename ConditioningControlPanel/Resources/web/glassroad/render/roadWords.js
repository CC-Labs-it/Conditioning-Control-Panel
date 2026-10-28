/* render/roadWords.js - the song's ROAD words painted on the track as road markings (pass 4, sim/roadWords.js).
 *
 * One draw: a pooled mesh of marking strips that follow the road surface (each strip is ROWS rows down the road
 * from track.frameAt, so it rides every crest and bank), 3.5 cm over the glass with a polygon offset, under the
 * karts (depth tested, never written). The letters come from a canvas atlas built ONCE per song, after the fonts
 * loaded (render/fonts.js), a few markings per frame so the build never stalls a frame, then uploaded once. The
 * atlas carries three masks per marking, measured and drawn with the same face and weight: R the fill, G a soft
 * glow, B the contour (thick ink with a clear core and a soft edge, no plate). The letters are drawn upright in the
 * atlas and stretched along the road (lenM of road per atlas row, STRETCH times longer than wide), the road
 * marking way, so they read at a grazing angle from the chase camera. Looks per music mode, the IN wipe, the
 * karaoke light, the drop lift, the glitch slide and the shatter shards are render/roadWordsMath.js plus the
 * shader below; the per-marking state rides vertex attributes, the music frame rides uniforms, so nothing here
 * ever builds a new program mid-race (the mesh sits in the scene from boot and the warm links it).
 * Gentle: no glitch, no kick pulse, slower IN and light (roadWordsMath); the drop boost asks photosafe once.
 */
import { Mesh, BufferGeometry, BufferAttribute, DynamicDrawUsage, MeshBasicNodeMaterial, DoubleSide, CanvasTexture, NoColorSpace,
  CustomBlending, OneFactor, OneMinusSrcAlphaFactor, Vector4 } from 'three/webgpu';
import { attribute, uniform, texture, vec2, vec3, float, floor, fract, hash, mix, smoothstep, clamp, exp, step, max, min, length, positionLocal, normalLocal, uniformArray, int } from 'three/tsl';
import { wrap } from '../core/math.js';
import { S } from '../sim/state.js';
import { createRoadRun, ROAD_LOOK } from '../sim/roadWords.js';
import { FACES } from './fonts.js';
import { Q } from './quality.js';
import { STYLES, STYLE, phraseStyle, packAtlas, markLook, frameLook } from './roadWordsMath.js';
import { createDecalSampler } from './surfaceDecals.js';
import { roadLetteringProfile, glyphFit, letteringLane, letteringFootprint } from './roadLettering.js';
import { planRoadWordAccents, frameRoadWordMusic } from './roadWordMusic.js';

export const ROAD_WORD_ACCENTS = Object.freeze({
  threshold: Object.freeze({ ink: [1.12, .69, .12], glow: [1.2, .78, .22] }),
  keep: Object.freeze({ ink: [1.05, .11, .34], glow: [.08, .95, .48] }),
  mainframe: Object.freeze({ ink: [1, .58, .06], glow: [1.2, .7, .12] }),
  boudoir: Object.freeze({ ink: [.14, .95, .64], glow: [.18, 1.1, .72] }),
  goonworks: Object.freeze({ ink: [.06, .85, 1.1], glow: [.06, .95, 1.25] }),
});

/** Selected sung words own their final hue, after the legacy gloss, hot core and shatter palette. */
export function roadWordAccentColour(base, accent, strength, sung) {
  const amount = smoothstep(.16, .6, clamp(strength.mul(sung), 0, 1));
  return mix(base, clamp(accent, 0, 1), amount);
}

/** One transform for the complete fill AND contour. Atlas padding contains the bounded pulse. */
export function roadWordMusicUv(loc, geo, u) {
  const pulse = clamp(u.musicKick.mul(.028).add(u.musicSnare.mul(.012)), 0, .04);
  const jitter = clamp(u.vibration, -.08, .08);
  return vec2(loc.x.sub(.5).div(pulse.add(1)).add(.5).sub(jitter.div(max(geo.x, .5))),
    loc.y.sub(.5).div(float(1).sub(pulse.mul(.45))).add(.5).sub(jitter.mul(.3).div(max(geo.y, .5))));
}

export const ROAD_PAINT = Object.freeze({
  atlasW: 2048, atlasH: 4096,      // one sheet per song; the phone tier holds half (atlasPhoneH); rows shrink to fit
  atlasPhoneH: 2048,
  rowPx: [128, 112, 96, 84, 72],   // atlas row heights tried in order
  padPx: 0.24,                     // continuous outline and glow stay inside their own cell
  stretch: 1.95,                   // wider letter faces stay legible at racing speed
  slots: 40,                       // markings on the road at once
  rows: 8,                         // strip rows down one marking (it follows crests and banks)
  liftM: 0.16,                     // above arrows, pictures and the displaced liquid
  cols: 8,                        // follow curved canal support across the whole word
  buildMs: 3,                      // atlas drawing budget per frame
  glowPx: 0.14,                    // the glow blur (share of the row)
  ringN: 16,                       // offset fills that draw the contour
  shardM: 1.15,                    // shatter shard size (metres of road)
  shardSlideM: 0.32,               // how far a shard slides at full shatter
  stencilAt: [0.47, 0.535],        // the stencil gap across the letters (share of the row height)
  glowCover: 0.12,                 // how much of the glow covers the road (the rest is added light)
});

const VERTS = (ROAD_PAINT.rows + 1) * (ROAD_PAINT.cols + 1), LOOK_VEC4 = 5;

/** The atlas: every marking of the song drawn once, a few per frame. */
function createAtlas(P = ROAD_PAINT, styles = STYLES) {
  const H = Q.name === 'phone' ? P.atlasPhoneH : P.atlasH;
  const canvas = typeof document !== 'undefined' ? document.createElement('canvas') : null;
  if (canvas) { canvas.width = P.atlasW; canvas.height = H; }
  const ctx = canvas ? canvas.getContext('2d') : null;
  if (ctx) { ctx.fillStyle = '#000'; ctx.fillRect(0, 0, P.atlasW, H); }
  const tex = new CanvasTexture(canvas || { width: 1, height: 1 });
  tex.colorSpace = NoColorSpace;          // masks, not colours
  tex.anisotropy = 8;                     // the grazing angle needs it
  let job = null;
  function font(st, px, c2 = ctx) {
    c2.font = FACES[st.face].css(px, st.weight);
    if ('letterSpacing' in c2) c2.letterSpacing = Math.round(st.spacing * px) + 'px';
  }
  // the glow is blurred in a TALL scratch (the marking's road proportions) and squashed into its row, so it is
  // as soft along the road as across it; blurred in the row itself it smeared 2.6 times longer toward the camera
  let tall = null, tctx = null;
  function tallOf(w, h) {
    if (!tall) { tall = document.createElement('canvas'); tctx = tall.getContext('2d'); }
    if (tall.width < w || tall.height < h) { tall.width = Math.max(tall.width, w); tall.height = Math.max(tall.height, h); }
    return tctx;
  }

  /**
   * Plans the song's sheet: items [{ key, text, style }] -> { cells: Map key -> { u0, v0, u1, v1, wPx }, rowPx } and
   * starts drawing. maxInkM is the widest marking (metres), lenM the marking length (metres per row).
   */
  function plan(items, maxInkM, lenM) {
    if (!ctx) return null;
    for (const rowPx of P.rowPx) {
      const latMPerPx = lenM / rowPx / P.stretch, maxInk = Math.floor(maxInkM / latMPerPx), pad = Math.round(rowPx * P.padPx);
      const widths = items.map((it) => {
        const st = styles[it.style];
        let px = Math.round(rowPx * st.em);
        font(st, px);
        px *= glyphFit(ctx.measureText(it.text), px, rowPx, st.stroke, P.stretch);
        font(st, px);
        const measured = ctx.measureText(it.text);
        const w = Math.max(measured.width, (measured.actualBoundingBoxLeft || 0) + (measured.actualBoundingBoxRight || 0));
        it.base = ((measured.actualBoundingBoxAscent || px * .8) - (measured.actualBoundingBoxDescent || 0)) / 2;
        it.squeeze = w > maxInk ? maxInk / w : 1;
        it.inkPx = Math.min(w, maxInk);
        it.px = px;
        return Math.ceil(it.inkPx + pad * 2);
      });
      const pk = packAtlas(widths, P.atlasW, rowPx, H);
      if (!pk) continue;
      const cells = new Map();
      items.forEach((it, i) => {
        const x = pk.x[i], y = pk.y[i], w = widths[i];
        // flipY: canvas top is v 1; the text's top (far end of the marking) gets the larger v
        cells.set(it.key, { u0: x / P.atlasW, u1: (x + w) / P.atlasW, v0: 1 - (y + rowPx) / H, v1: 1 - y / H,
          wM: w * latMPerPx, x, y, w });
      });
      ctx.globalCompositeOperation = 'source-over';
      ctx.fillStyle = '#000'; ctx.fillRect(0, 0, P.atlasW, H);
      job = { items, cells, rowPx, pad, next: 0 };
      return { cells, rowPx };
    }
    return null;
  }

  function drawOne(it) {
    const { cells, rowPx } = job, c = cells.get(it.key), st = styles[it.style], k = P.stretch;
    const base = it.base, cx = c.x + c.w / 2, cy = c.y + rowPx / 2 + base;
    // G: the glow, a shadow only (the text is drawn far off and clipped away), in the tall scratch
    const th = Math.ceil(rowPx * k), t2 = tallOf(c.w, th), OFF = 8192;
    t2.setTransform(1, 0, 0, 1, 0, 0);
    t2.globalCompositeOperation = 'source-over';
    t2.clearRect(0, 0, c.w, th);
    t2.save();
    t2.beginPath(); t2.rect(0, 0, c.w, th); t2.clip();
    font(st, it.px, t2);
    t2.textAlign = 'center'; t2.textBaseline = 'alphabetic';
    t2.shadowColor = 'rgb(0,255,0)'; t2.shadowBlur = Math.max(4, rowPx * P.glowPx); t2.shadowOffsetX = OFF;
    t2.setTransform(it.squeeze, 0, 0, k, c.w / 2 - OFF, (rowPx / 2 + base) * k);
    t2.fillStyle = 'rgb(0,255,0)'; t2.fillText(it.text, 0, 0);
    t2.restore();
    ctx.save();
    ctx.beginPath(); ctx.rect(c.x, c.y, c.w, rowPx); ctx.clip();
    ctx.globalCompositeOperation = 'lighter';
    ctx.drawImage(tall, 0, 0, c.w, th, c.x, c.y, c.w, rowPx);
    ctx.textAlign = 'center'; ctx.textBaseline = 'alphabetic';
    font(st, it.px);
    // B: the contour, a ring of offset fills that is round ON THE ROAD (k times thinner along the row's height)
    const r = Math.max(1.5, it.px * st.stroke);
    ctx.fillStyle = 'rgb(0,0,255)';
    for (let i = 0; i < P.ringN; i++) {
      const a = (i / P.ringN) * Math.PI * 2;
      ctx.setTransform(it.squeeze, 0, 0, 1, cx + Math.cos(a) * r, cy + Math.sin(a) * r / k);
      ctx.fillText(it.text, 0, 0);
    }
    ctx.setTransform(it.squeeze, 0, 0, 1, cx, cy);
    ctx.fillText(it.text, 0, 0);
    // R: the fill
    ctx.fillStyle = 'rgb(255,0,0)'; ctx.fillText(it.text, 0, 0);
    ctx.restore();
  }

  return {
    texture: tex, plan, canvas,
    get ready() { return !!job && job.next >= job.items.length; },
    /** Draws markings until the frame budget is spent; uploads the sheet once when the last one is in. */
    step(budgetMs) {
      if (!job || job.next >= job.items.length) return false;
      const t0 = performance.now();
      while (job.next < job.items.length && performance.now() - t0 < budgetMs) drawOne(job.items[job.next++]);
      if (job.next >= job.items.length) { tex.needsUpdate = true; return true; }
      return false;
    },
    dispose() { tex.dispose(); },
  };
}

/** The pooled marking mesh and its shader. */
function createPaint(atlasTex, P = ROAD_PAINT, pal = null, styles = STYLES, theme = 'threshold') {
  const n = P.slots, V = VERTS;
  const buf = (k) => new BufferAttribute(new Float32Array(n * V * k), k).setUsage(DynamicDrawUsage);
  // six vertex buffers (WebGPU allows eight per pipeline): the per-look constants live in a uniform array
  // indexed by the look id riding aLoc.z, never in attributes
  const a = { position: buf(3), normal: buf(3), aLoc: buf(4), aRect: buf(4), aGeo: buf(4), aState: buf(4) };
  const g = new BufferGeometry();
  for (const k in a) g.setAttribute(k, a[k]);
  const idx = new Uint16Array(n * P.rows * P.cols * 6), stride = P.cols + 1;
  for (let s = 0, o = 0; s < n; s++) for (let r = 0; r < P.rows; r++) for (let c = 0; c < P.cols; c++) {
    const b = s * V + r * stride + c;
    idx[o++] = b; idx[o++] = b + 1; idx[o++] = b + stride + 1; idx[o++] = b; idx[o++] = b + stride + 1; idx[o++] = b + stride;
  }
  g.setIndex(new BufferAttribute(idx, 1));
  for (let s = 0; s < n; s++) for (let v = 0; v < V; v++) { a.aLoc.array[(s * V + v) * 4] = (v % stride) / P.cols; a.aLoc.array[(s * V + v) * 4 + 1] = Math.floor(v / stride) / P.rows; }

  const accent = ROAD_WORD_ACCENTS[theme] || ROAD_WORD_ACCENTS.threshold;
  const u = { kartS: uniform(0), lapM: uniform(1000), wave: uniform(0), waveD: uniform(0), pulse: uniform(0), glitch: uniform(0),
    glow: uniform(0.5), shatter: uniform(0), beat: uniform(0), time: uniform(0), calm: uniform(0), sweep: uniform(0),
    musicKick: uniform(0), musicSnare: uniform(0), musicHats: uniform(0), vibration: uniform(0),
    accentInk: uniform(new Vector4(...accent.ink, 1)), accentGlow: uniform(new Vector4(...accent.glow, 1)) };
  const loc = attribute('aLoc', 'vec4'), rect = attribute('aRect', 'vec4'), geo = attribute('aGeo', 'vec4');
  const st = attribute('aState', 'vec4');
  // per look, LOOK_VEC4 vec4s: ink (rgb, alpha), edge, glow, fx (stencil, wear, kick, glitch), fx2 (energy, gloss)
  const looks = uniformArray(styles.flatMap((x) => [new Vector4(...x.ink, x.inkA), new Vector4(...x.edge, x.edgeA),
    new Vector4(...x.glow, x.glowA), new Vector4(x.stencil, x.wear, x.kick, x.glitch), new Vector4(x.energy, x.gloss, 0, 0)]), 'vec4');
  const li = int(loc.z.add(0.5)).mul(LOOK_VEC4);
  const ink = looks.element(li), edgeC = looks.element(li.add(1)), glowC = looks.element(li.add(2));
  const fx = looks.element(li.add(3)), fx2 = looks.element(li.add(4));
  // geo: x width m, y length m, z road s, w seed
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide,
    polygonOffset: true, polygonOffsetFactor: -2, polygonOffsetUnits: -6,
    blending: CustomBlending, blendSrc: OneFactor, blendDst: OneMinusSrcAlphaFactor, blendSrcAlpha: OneFactor, blendDstAlpha: OneMinusSrcAlphaFactor });
  // vertex: the karaoke lift plus the drop wave, which lifts the letters as it runs down the road to the kart
  const dAhead = fract(geo.z.sub(u.kartS).div(u.lapM).add(0.5)).sub(0.5).mul(u.lapM);
  const waveLift = exp(dAhead.sub(u.waveD).div(9).pow(2).negate()).mul(u.wave).mul(0.42);
  m.positionNode = positionLocal.add(normalLocal.mul(st.w.add(waveLift)));

  // the glitch: slices across the marking slide sideways on the beat (the helix), a move and never a strobe
  const slice = floor(loc.y.mul(4));
  const gx = hash(slice.add(u.beat.mul(7.13)).add(geo.w.mul(31))).sub(0.5).mul(u.glitch).mul(fx.w).div(max(geo.x, 0.5));
  // the shatter: shards fixed to the marking, each sliding off a little and turning its hue
  const lm = vec2(loc.x.mul(geo.x), loc.y.mul(geo.y)).div(P.shardM), ci = floor(lm), cf = fract(lm);
  let F1 = float(9), F2 = float(9), sid = float(0), sid2 = float(0);
  for (let ox = -1; ox <= 1; ox++) for (let oy = -1; oy <= 1; oy++) {
    const cid = ci.add(vec2(ox, oy));
    const h1 = hash(cid.x.add(cid.y.mul(57.3)).add(geo.w.mul(13))), h2 = hash(cid.x.mul(11.7).add(cid.y.mul(23.1)).add(3.7));
    const d = length(vec2(ox, oy).add(vec2(h1, h2)).sub(cf));
    F2 = min(F2, max(F1, d));
    const nearer = step(d, F1);
    sid = mix(sid, h1, nearer); sid2 = mix(sid2, h2, nearer); F1 = min(F1, d);
  }
  const sh = u.shatter;
  const slide = vec2(sid.sub(0.5).div(max(geo.x, 0.5)), sid2.sub(0.5).div(max(geo.y, 0.5))).mul(sh).mul(P.shardSlideM * 2);
  const glyphUv = roadWordMusicUv(loc, geo, u);
  const lx = glyphUv.x.add(gx).add(slide.x), ly = glyphUv.y.add(slide.y);
  const tuv = vec2(mix(rect.x, rect.z, clamp(lx, 0.002, 0.998)), mix(rect.y, rect.w, clamp(ly, 0.002, 0.998)));
  // Only the decorative glow splits. The complete core and its outline share one bounded transform.
  const glowTx = texture(atlasTex, tuv);
  const tx = texture(atlasTex, vec2(mix(rect.x, rect.z, clamp(glyphUv.x, .002, .998)), mix(rect.y, rect.w, clamp(glyphUv.y, .002, .998))));
  const inBox = step(0, lx).mul(step(lx, 1)).mul(step(0, ly)).mul(step(ly, 1));
  // the stencil gap and the worn thermoplastic grain
  const gap = smoothstep(P.stencilAt[0] - 0.012, P.stencilAt[0], loc.y).mul(float(1).sub(smoothstep(P.stencilAt[1], P.stencilAt[1] + 0.012, loc.y))).mul(fx.x);
  const grain = hash(floor(loc.x.mul(geo.x).mul(9)).add(floor(loc.y.mul(geo.y).mul(5)).mul(91.7)).add(geo.w));
  const worn = step(0.8, grain).mul(fx.y).mul(0.75);
  const crack = float(1).sub(smoothstep(0.03, 0.09, F2.sub(F1))).mul(sh);
  const fill = tx.r.mul(float(1).sub(gap.mul(.25))).mul(float(1).sub(worn.mul(.2)));
  const edge = tx.b.mul(float(1).sub(tx.r));
  const glowM = glowTx.g.mul(inBox);
  // IN: the paint wipes in from a glow line, near end to far
  const inK = st.y;
  const reveal = float(1).sub(smoothstep(inK.sub(0.05), inK, loc.y));
  const frontLine = exp(loc.y.sub(inK).div(0.045).pow(2).negate()).mul(float(1).sub(smoothstep(0.82, 1, inK))).mul(smoothstep(0, 0.06, inK));
  // the karaoke light: the paint burns brighter and warmer on its word, the glow swells
  const sung = st.z;
  const bright = float(1).add(u.pulse.mul(fx.z)).add(sung.mul(0.65));
  // the gloss: a slow diagonal light running over the letters (the song clock; Gentle runs it slower)
  const sweepAt = fract(u.time.mul(u.sweep).add(geo.w)).mul(2.4).sub(0.7);
  const gloss = exp(loc.x.mul(0.5).add(loc.y).sub(sweepAt).div(0.09).pow(2).negate()).mul(fx2.y).mul(0.9);
  const themedInk = ink.xyz;
  const themedGlow = glowC.xyz;
  const themedEdge = edgeC.xyz;
  const hot = mix(themedInk, vec3(1.4), .48);
  // the shard hue: every shard takes a hue of its own between pink, cyan and gold (the road shatter's colours)
  const tint = mix(mix(vec3(1.25, 0.42, 0.95), vec3(0.42, 0.95, 1.25), sid2), vec3(1.25, 0.95, 0.4), step(0.72, sid));
  const turn = sh.mul(0.75);
  const inkCol0 = mix(themedInk, themedInk.mul(pal ? mix(pal.col(0), pal.col(1), sid2) : tint), turn);
  const legacyInk = mix(inkCol0.mul(bright), hot, sung.mul(0.45)).add(vec3(1.2, 1.1, 1.15).mul(gloss));
  const inkCol = roadWordAccentColour(legacyInk, u.accentInk.xyz, loc.w, sung);
  const legacyGlow = mix(themedGlow, pal ? pal.col(1) : tint, turn);
  const glowCol = roadWordAccentColour(legacyGlow, u.accentGlow.xyz, loc.w, sung)
    .mul(float(1).add(sung.mul(0.5)).add(u.musicHats.mul(.15)));
  // premultiplied: the fill and the contour cover the road, the glow and the wipe line mostly ADD light (a dark
  // halo over the bright glass read as a shadow), so a neon marking glows and the bloom pass picks up its core
  const fA = fill.mul(ink.w).mul(reveal);
  const eA = edge.mul(max(edgeC.w, .92)).mul(reveal).mul(float(1).sub(fA));
  const gL = glowM.mul(glowC.w).mul(mix(float(0.7), u.glow, fx2.x)).mul(mix(float(1), float(1.8), sung)).mul(reveal).mul(float(1).sub(fA)).mul(float(1).sub(edge.mul(0.6)));
  const lA = frontLine.mul(0.9);
  const crackL = crack.mul(tx.b).mul(0.9);
  const lit = inkCol.mul(fA).add(themedEdge.mul(eA)).add(glowCol.mul(gL)).add(hot.mul(lA)).add(glowCol.mul(1.3).mul(crackL));
  const cover = clamp(fA.add(eA).add(gL.mul(P.glowCover)).add(lA.mul(0.3)), 0, 1);
  m.colorNode = lit.mul(st.x);
  m.opacityNode = cover.mul(st.x);
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.renderOrder = 5; mesh.castShadow = false; mesh.receiveShadow = false;
  mesh.visible = false;
  mesh.name = 'road-words';
  return { mesh, a, u, dispose() { g.dispose(); m.dispose(); } };
}

/**
 * @param scene the world scene
 * @param opts { track, feel (speed.topMps), photosafe (the drop boost asks it once per drop) }
 * @returns {{ object, update(song, songS, kart, mf, ctx): void, clear(): void, dispose(): void, stats(): object }}
 */
export function createRoadWords(scene, { track, feel = null, photosafe = null, pal = null, waterVolume = null } = {}) {
  const P = ROAD_PAINT, R = ROAD_LOOK, V = VERTS, profile = roadLetteringProfile(track.id), styles = profile.styles;
  const atlas = createAtlas(P, styles), paint = createPaint(atlas.texture, P, pal, styles, track.id), sample = createDecalSampler(track, waterVolume);
  scene.add(paint.mesh);
  const { a, u } = paint;
  u.lapM.value = track.lengthM;
  let song = null, run = null, cells = null, styleOf = null, keyOf = null, accents = null, ready = false, rowPx = 0;
  const slotOf = new Map(), slotId = new Int32Array(P.slots).fill(-1), slotS = new Float64Array(P.slots), seen = new Uint8Array(P.slots);
  const slotLane = new Float64Array(P.slots).fill(NaN), slotScale = new Float64Array(P.slots);
  // the footprint a slot's strip was last written with, and whether any of its vertices sits on the liquid:
  // a strip whose footprint did not move is rewritten only while displaced water can lift it (perf, Firefox)
  const slotW = new Float64Array(P.slots).fill(NaN), slotFoot = new Float64Array(P.slots).fill(NaN), slotWet = new Uint8Array(P.slots), wetOut = {};
  const fr = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
  const player = { s: 0, lat: 0, speedMps: 0 }, look = {}, fl = {}, wordMusic = {};
  let dropIdx = -1, dropOk = false, staticDirty = false, lastSongS = NaN;
  const pose = {};

  /** A new song: its run, a look per phrase and the atlas plan (drawn over the next frames). */
  function setSong(s) {
    song = s; run = null; cells = null; ready = false; clear();
    if (!s.words || !s.words.length) return;
    run = createRoadRun(s, track);
    const sc = run.script;
    accents = planRoadWordAccents(s, sc);
    styleOf = new Uint8Array(sc.length); keyOf = new Array(sc.length);
    let ph = -1, sty = STYLE.STENCIL;
    const items = new Map();
    for (const e of sc) {
      if (e.phrase !== ph) { ph = e.phrase; sty = phraseStyle(s, e.startS, ph); }
      const st = styles[sty], text = st.upper ? e.text.toUpperCase() : e.text.toLowerCase();
      const key = st.face + '|' + st.weight + '|' + st.em + '|' + st.spacing + '|' + st.stroke + '|' + text;
      styleOf[e.i] = sty; keyOf[e.i] = key;
      if (!items.has(key)) items.set(key, { key, text, style: sty });
    }
    const pl = atlas.plan([...items.values()], R.maxWidthM, R.lenM);
    if (!pl) { run = null; console.warn('[glassroad/roadWords] the script does not fit the atlas'); return; }
    cells = pl.cells; rowPx = pl.rowPx;
  }

  function writeStatic(slot, id) {
    const sty = styleOf[id], c = cells.get(keyOf[id]);
    for (let v = 0; v < V; v++) {
      const i = (slot * V + v) * 4;
      a.aRect.array[i] = c.u0; a.aRect.array[i + 1] = c.v0; a.aRect.array[i + 2] = c.u1; a.aRect.array[i + 3] = c.v1;
      a.aLoc.array[i + 2] = sty;
      a.aLoc.array[i + 3] = accents ? accents[id] : 0;
    }
  }

  /** The strip of one marking on the road: ROWS rows from track frames, lateral x, centred on s.
   *  false = the strip already sits there (same footprint, dry or still water): nothing was written. */
  function writeStrip(slot, view, laneX, scale, force = true, waterMoves = false) {
    const c = cells.get(keyOf[view.id]), lenM = R.lenM, lap = track.lengthM;
    const footprint = letteringFootprint(track, view.s, laneX, c.wM * scale, lenM);
    const wM = footprint.width, lane = footprint.lane;
    const same = slotS[slot] === view.s && slotW[slot] === wM && slotFoot[slot] === lane;
    if (same && !force && !(waterMoves && slotWet[slot])) { slotLane[slot] = laneX; slotScale[slot] = scale; return false; }
    const field = !same && waterVolume ? waterVolume.field : null;
    if (!same) slotWet[slot] = 0;
    const P3 = a.position.array, N3 = a.normal.array, G4 = a.aGeo.array, seed = (view.id * 0.618034) % 1;
    for (let r = 0; r <= P.rows; r++) {
      const s = wrap(view.s - lenM / 2 + (lenM * r) / P.rows, lap);
      for (let col = 0; col <= P.cols; col++) {
        const x = lane + (col / P.cols - .5) * wM, v = slot * V + r * (P.cols + 1) + col;
        if (field && !slotWet[slot] && field.sample(s, x, wetOut).wet) slotWet[slot] = 1;
        sample(s, x, fr);
        for (let j = 0; j < 3; j++) { P3[v * 3 + j] = fr.p[j] + fr.n[j] * P.liftM; N3[v * 3 + j] = fr.n[j]; }
        G4[v * 4] = wM; G4[v * 4 + 1] = lenM; G4[v * 4 + 2] = s; G4[v * 4 + 3] = seed;
      }
    }
    slotS[slot] = view.s;
    slotLane[slot] = laneX; slotScale[slot] = scale;
    slotW[slot] = wM; slotFoot[slot] = lane;
    return true;
  }

  function freeSlot(slot) {
    slotOf.delete(slotId[slot]); slotId[slot] = -1;
    slotLane[slot] = NaN; slotW[slot] = NaN; slotFoot[slot] = NaN; slotWet[slot] = 0;
    for (let v = 0; v < V; v++) a.aState.array[(slot * V + v) * 4] = 0;
  }

  function update(s, tS, kart, mf, { gentle = false, paused = false, reactive = null, motion = 'full' } = {}) {
    gentle = gentle || motion === 'off';
    // a new song plans its atlas; no song (the menu, between races) only hides the paint: the atlas is kept
    if (s && s !== song) setSong(s);
    if (!s) { if (paint.mesh.visible) clear(); return; }
    if (run && !ready) {
      atlas.step(P.buildMs);
      ready = atlas.ready;
    }
    if (paused) return;
    if (!run || !ready || !kart || !Number.isFinite(tS)) { if (paint.mesh.visible) clear(); return; }
    const dt = paused || !Number.isFinite(lastSongS) ? 0 : Math.max(0, Math.min(.1, tS - lastSongS));
    lastSongS = tS;
    player.s = kart[S.s] || 0; player.lat = kart[S.lateral] || 0;
    player.speedMps = Math.hypot(kart[S.velX] || 0, kart[S.velY] || 0, kart[S.velZ] || 0);
    const live = run.update(tS, player);
    seen.fill(0);
    let geoDirty = false;
    for (const v of live) {
      // The void carries physical sung-word platforms, so ordinary floor paint stops at its lips.
      if ((track.aerials || []).some(a => v.s + v.lenM / 2 > a.gapS0 && v.s - v.lenM / 2 < a.gapS1)) continue;
      let slot = slotOf.get(v.id);
      profile.pose(run.script[v.id].text, tS, mf?.beatPhase || 0, gentle, pose);
      const targetLane = player.lat * .9 + pose.offsetM;
      if (slot === undefined) {
        slot = slotId.indexOf(-1);
        if (slot < 0) continue;              // the pool is full: this one waits
        slotId[slot] = v.id; slotOf.set(v.id, slot);
        writeStatic(slot, v.id); writeStrip(slot, v, targetLane, pose.scale); staticDirty = geoDirty = true;
      } else {
        const lane = gentle ? slotLane[slot] : letteringLane(slotLane[slot], targetLane, dt);
        if (slotS[slot] !== v.s || lane !== slotLane[slot] || pose.scale !== slotScale[slot] || (waterVolume && !paused)) {
          if (writeStrip(slot, v, lane, pose.scale, false, !!waterVolume && !paused)) geoDirty = true;
        }
      }
      seen[slot] = 1;
      markLook(v, { gentle }, look);
      for (let k = 0; k < V; k++) {
        const i = (slot * V + k) * 4;
        a.aState.array[i] = look.alpha; a.aState.array[i + 1] = look.inK; a.aState.array[i + 2] = look.sung; a.aState.array[i + 3] = look.liftM + pose.liftM;
      }
    }
    for (let sl = 0; sl < P.slots; sl++) if (!seen[sl] && slotId[sl] >= 0) freeSlot(sl);
    // the drop boost asks photosafe once per drop (the event index keys the grant, never a timer)
    if (mf && mf.waveIdx !== dropIdx) { dropIdx = mf.waveIdx; dropOk = dropIdx >= 0 && !gentle && (!photosafe || photosafe.request('road-words-drop')); }
    frameLook(mf, { gentle, dropOk: dropOk && !gentle }, fl);
    frameRoadWordMusic(tS, mf, reactive, { gentle, motion }, wordMusic);
    u.musicKick.value = wordMusic.kick; u.musicSnare.value = wordMusic.snare;
    u.musicHats.value = wordMusic.hats; u.vibration.value = wordMusic.vibration;
    u.kartS.value = player.s; u.wave.value = fl.wave; u.waveD.value = fl.waveD; u.pulse.value = fl.pulse;
    u.glitch.value = paused ? u.glitch.value : fl.glitch; u.glow.value = fl.glow; u.shatter.value = fl.shatter; u.beat.value = fl.beat;
    u.time.value = tS; u.calm.value = fl.calm; u.sweep.value = gentle ? 0.12 : 0.3;
    a.aState.needsUpdate = true;
    if (geoDirty) { a.position.needsUpdate = a.normal.needsUpdate = a.aGeo.needsUpdate = true; }
    if (staticDirty) { a.aRect.needsUpdate = a.aLoc.needsUpdate = true; staticDirty = false; }
    paint.mesh.visible = live.length > 0;
  }

  /** Every marking off the road (a restart; the same song's atlas is kept). */
  function clear() {
    for (let sl = 0; sl < P.slots; sl++) if (slotId[sl] >= 0) freeSlot(sl);
    a.aState.needsUpdate = true;
    if (run) run.reset();
    paint.mesh.visible = false;
    dropIdx = -1; dropOk = false; lastSongS = NaN;
  }

  return {
    object: paint.mesh, update, clear, _dev: { canvas: atlas.canvas, music: wordMusic },
    stats() { return { ready, markings: run ? run.total : 0, live: slotOf.size, shown: run ? run.shown : 0, rowPx,
      accented: accents ? accents.reduce((sum, strength) => sum + (strength > 0 ? 1 : 0), 0) : 0,
      kick: wordMusic.kick || 0, snare: wordMusic.snare || 0, vibration: wordMusic.vibration || 0 }; },
    dispose() { scene.remove(paint.mesh); paint.dispose(); atlas.dispose(); },
  };
}
