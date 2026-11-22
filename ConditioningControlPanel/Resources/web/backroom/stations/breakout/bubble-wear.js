/* stations/breakout/bubble-wear.js - the worn bubble skin on Breakout's picture bubbles (the colliders).
 *
 * The skins are bubble-skins.js, a byte-for-byte copy of prizecounter/sheet/skins.js, and assets/skin-shell.png is
 * that tree's shell picture: a station never fetches from another web tree, so the copies are pinned by
 * BubbleSkinsWebParityTests (C#) and must be copied again together.
 *
 * The page learns the skin from the OPTIONAL init.bubbleSkin / settings.bubbleSkin (CONTRACT 2.2): absent or not a
 * known id = soap, and soap draws nothing here, so a soap bubble is exactly today's bubble.
 *
 * Cost, up to three bubbles live at once: champagne, heart, marble and disco draw into ONE shared 256 px canvas per
 * skin at most 30 times a second, then each bubble is one drawImage. Spiral and frost draw per bubble: spiral wears
 * its own Loom picture (64 or 112 px, made once at birth, then only turned), frost grows its rime with the bubble's
 * age; both are a few dozen cheap shapes. The pop is the skin's own particles in one world capped at PART_CAP.
 * Motion: reduced motion holds one still frame and skips the skin's pop particles (the field's own pop burst stays,
 * as today); Calm runs the interior at half speed and every skin's gentle path (no white flash, Disco no beams). */
import { SKINS, rng, NOTES, rollSpec, stepParts, drawParts, loadShells } from './bubble-skins.js';

export const SKIN_IDS = Object.freeze(['soap', 'champagne', 'heart', 'spiral', 'marble', 'disco', 'frost']);
/** A known skin id (any case, trimmed), else soap. */
export const resolveSkin = (id) => { const k = typeof id === 'string' ? id.trim().toLowerCase() : ''; return SKIN_IDS.includes(k) ? k : 'soap'; };
/** How strongly the skin sits over the picture (the picture is the payload; today's soap shell sits at .55). */
export const WEAR_ALPHA = Object.freeze({ champagne: 0.9, heart: 0.72, spiral: 0.7, marble: 0.62, disco: 0.6, frost: 0.85 });
export const SHARED = Object.freeze(['champagne', 'heart', 'marble', 'disco']);
export const TEX = 256, TEX_R = 90, PART_CAP = 360, TEX_FPS = 30, STILL_T = 2.4;

/** The interior clock: reduced motion holds one fixed frame, Calm runs at half speed. */
export const interiorTime = (t, { reduced = false, calm = false } = {}) => (reduced ? STILL_T : calm ? t * 0.5 : t);

/** One bubble as the skins expect it, from a seed (no global counter, so the same seed draws the same bubble). */
export function bubbleRecord(kind, seed, r, { gentle = false, doc = null } = {}) {
  const skin = SKINS[kind];
  if (!skin || kind === 'soap') return null;
  seed >>>= 0;
  const b = { kind, skin, x: 0, y: 0, r, seed, rnd: rng(seed), age: 0, grow: 1, state: 'alive', pt: 0, gentle, note: NOTES[seed % 5] };
  if (skin.init && doc) skin.init(b, doc);
  if (kind === 'spiral' && !b.spec) b.spec = rollSpec(rng(seed * 7919 + 13));   // the same spec init rolls, without a canvas
  return b;
}

/** The skin's own pop as plain particle data pushed into w ({ p: [], cap }). Deterministic for a seed. */
export function popParticles(w, kind, x, y, r, seed, { gentle = false, record = null } = {}) {
  const b = record || bubbleRecord(kind, seed, r, { gentle });
  if (!b) return 0;
  b.x = x; b.y = y; b.r = r; b.gentle = gentle; b.rnd = rng((b.seed ^ 0x9e3779b9) >>> 0);
  const n0 = w.p.length;
  b.skin.burst(w, b);
  return w.p.length - n0;
}

export function createSkinWear({ doc = globalThis.document || null, shellSrc = null } = {}) {
  const canMake = !!(doc && typeof doc.createElement === 'function');
  if (canMake) { try { loadShells(shellSrc || new URL('./assets/skin-shell.png', import.meta.url).href, doc); } catch (e) { /* no shell = the skins' drawn outline */ } }
  const world = { c: null, p: [], cap: PART_CAP };
  const shared = new Map();                 // skin -> { cv, ctx, b, at }
  const own = new Map();                    // collider -> record (spiral, frost)
  let clock = 0, nextSeed = 0x51ab;

  function sharedTex(kind, t, gentle) {
    let e = shared.get(kind);
    if (!e) {
      if (!canMake) return null;
      const cv = doc.createElement('canvas'); cv.width = cv.height = TEX;
      const ctx = cv.getContext('2d'); if (!ctx) return null;
      e = { cv, ctx, b: bubbleRecord(kind, 0x5eed + SKIN_IDS.indexOf(kind), TEX_R, { gentle, doc }), at: -1 };
      shared.set(kind, e);
    }
    if (e.at >= 0 && Math.abs(t - e.at) < 1 / TEX_FPS && e.b.gentle === gentle) return e;
    e.at = t; e.b.gentle = gentle;
    const c = e.ctx;
    c.setTransform(1, 0, 0, 1, 0, 0); c.clearRect(0, 0, TEX, TEX); c.globalAlpha = 1;
    c.save(); c.translate(TEX / 2, TEX / 2); e.b.skin.draw(c, e.b, t + (e.b.seed % 50), 0); c.restore();
    return e;
  }
  function recordFor(col, kind, r, gentle) {
    let b = own.get(col);
    if (!b || b.kind !== kind) { b = bubbleRecord(kind, (nextSeed++ * 2654435761) >>> 0, r, { gentle, doc: canMake ? doc : null }); own.set(col, b); }
    return b;
  }

  return {
    /** Advance the interior clock once per frame (dt in seconds, the field's time scale already applied). */
    tick(dt) { clock += Math.max(0, dt || 0); for (const c of own.keys()) if (c.fading) own.delete(c); },
    /** Draws the worn skin over a collider in place of the soap shell. False for soap (draw today's bubble). */
    draw(g, col, r, a, look) {
      const kind = resolveSkin(look && look.skin);
      if (kind === 'soap' || !(r > 0)) return false;
      const gentle = !!(look.calm || look.reduced), t = interiorTime(clock, look);
      if (SHARED.includes(kind)) {
        const e = sharedTex(kind, t, gentle);
        if (!e) return false;
        const s = TEX * r / TEX_R;
        g.save(); g.globalAlpha = Math.max(0, Math.min(1, a)) * WEAR_ALPHA[kind]; g.drawImage(e.cv, col.x - s / 2, col.y - s / 2, s, s); g.restore();
        return true;
      }
      const b = recordFor(col, kind, r, gentle);
      if (!b) return false;
      b.r = r; b.gentle = gentle; b.age = look.reduced ? 4.5 : (col.age || 0);
      g.save(); g.globalAlpha = Math.max(0, Math.min(1, a)) * WEAR_ALPHA[kind]; g.translate(col.x, col.y);
      b.skin.draw(g, b, t + (b.seed % 50), 0); g.restore();
      return true;
    },
    /** The skin's own pop at a bubblePop event ({x, y, r}). Nothing for soap or under reduced motion. */
    pop(d, look, colliders = []) {
      const kind = resolveSkin(look && look.skin);
      if (kind === 'soap' || (look && look.reduced) || !d || !(d.r > 0)) return 0;
      let record = null;
      for (const c of colliders) if (c.x === d.x && c.y === d.y && own.has(c)) { record = own.get(c); own.delete(c); break; }
      return popParticles(world, kind, d.x, d.y, d.r, (nextSeed++ * 2654435761) >>> 0, { gentle: !!(look && look.calm), record });
    },
    /** Steps and draws the pop particles in the field's transform. */
    frame(g, dt) { if (!world.p.length) return; world.c = g; stepParts(world, Math.max(0, dt || 0)); drawParts(world); world.c = null; },
    particles: () => world.p.length,
    clear() { world.p.length = 0; own.clear(); },
    dispose() { world.p.length = 0; own.clear(); shared.clear(); },
  };
}
