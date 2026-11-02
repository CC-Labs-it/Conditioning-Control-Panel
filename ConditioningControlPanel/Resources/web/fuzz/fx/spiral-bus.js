/* ============================================================================
 * fx/spiral-bus.js - one set of colours and one pattern clock for every spiral
 * on the page. Each spiral is a "site": a canvas of its own (its css, opacity
 * and turning stay with whoever made it) that the bus repaints when it has to.
 *
 * COST. Nothing is drawn while nothing changes: with neither unlock owned a site
 * is one still frame turned by the compositor, exactly as before. While it does
 * change (a palette fade, Spiral Colours, Spiral Shapes) it runs on the Loom's
 * field shader in one shared WebGL context, copied to the site's canvas at a
 * small size and a capped rate, and it steps down by itself if a paint runs long.
 * No WebGL: the Loom's 2D drawSpiral, smaller and slower, with a plain crossfade.
 * ==========================================================================*/

import { createFieldRenderer, projectToV1 } from '../../arcademy/engine/loom/loomField.js';
import { drawSpiral } from '../../arcademy/engine/loom/loomSpiral.js';
import { createMorphClock, driftPair, groundOf, isHex, mixHex, morphFrame, smooth } from './spiral-patterns.js';

export const FADE_S = 3;
const WARM_AFTER_S = 2;
const DEFAULT_PAIR = Object.freeze(['#ff4fa8', '#6a3cff']);
const LADDER = [1024, 768, 512, 384, 256, 192];
const PAINT_BUDGET_MS = 7;      // a paint over this for a while steps the site down
const FALLBACK_RATE = 0.34;     // the 2D Loom draws on the CPU: about a third as often, at most this size
const FALLBACK_MAX = 320;

/* ---- the shared field ----
 * Two contexts at most, one for the page and one for small things: a browser copies the WHOLE drawing buffer out of a WebGL
 * canvas, whatever part is asked for, so a button sharing the page's 768 px buffer would pay for 768 px every paint. */
const SMALL_MAX = 320;
const fields = { small: null, big: null };
let glDead = false;
export const fieldInfo = { madeMs: 0, made: 0 };

/** The Loom's shader smooths an edge by how far a pixel reaches round the circle. A tightly wound band's edge mostly runs the other way,
 *  so it comes out serrated, and a small canvas blown up to the page shows it. The shader stays the Loom's: this only tells it pixels are wider. */
function widenEdges(gl, by) {
  const look = gl.getUniformLocation.bind(gl), set1 = gl.uniform1f.bind(gl);
  let px = null;
  gl.getUniformLocation = (p, name) => { const l = look(p, name); if (name === 'u_px') px = l; return l; };
  gl.uniform1f = (l, v) => set1(l, px && l === px ? v * by : v);
}

function getField(size) {
  if (glDead) return null;
  const key = size <= SMALL_MAX ? 'small' : 'big';
  let f = fields[key];
  if (!f) {
    const t0 = performance.now();
    try {
      const c = document.createElement('canvas');
      c.width = c.height = key === 'small' ? SMALL_MAX : size;
      f = createFieldRenderer(c);
      if (!f) { glDead = true; return null; }
      f.minmax = f.gl.getExtension('EXT_blend_minmax');
      widenEdges(f.gl, 2.2);
      c.addEventListener('webglcontextlost', (e) => { e.preventDefault(); glDead = true; });
      fields[key] = f;
    } catch (e) { glDead = true; return null; }
    fieldInfo.madeMs += performance.now() - t0;
    fieldInfo.made += 1;
  }
  /* The page's buffer is exactly its size: a resize only happens when it goes live or still, and the copy pays for every pixel in here. */
  if (key === 'big' ? f.canvas.width !== size : f.canvas.width < size) f.canvas.width = f.canvas.height = size;
  return f;
}

const fieldReady = (size) => !glDead && !!fields[size <= SMALL_MAX ? 'small' : 'big'];

/** Draw a frame into a 2D canvas context, size w x h. On the field when there is one, else the Loom's 2D renderer. */
export function paintLoom(g, frame, phase, w, h, { lightGround = false, cover = true, noField = false, seeThrough = false } = {}) {
  const S = Math.max(w, h);
  const f = noField ? null : getField(S);
  g.setTransform(1, 0, 0, 1, 0, 0);
  if (f) {
    const gl = f.gl, minmax = f.minmax;
    gl.disable(gl.BLEND);
    f.render(frame.a, phase, S, S);
    if (frame.b) {
      gl.enable(gl.BLEND);
      if (minmax) {
        gl.blendEquation(lightGround ? minmax.MIN_EXT : minmax.MAX_EXT);
        gl.blendFunc(gl.ONE, gl.ONE);
      } else {
        gl.blendEquation(gl.FUNC_ADD);
        gl.blendColor(0, 0, 0, frame.cross);
        gl.blendFunc(gl.CONSTANT_ALPHA, gl.ONE_MINUS_CONSTANT_ALPHA);
      }
      f.render(frame.b, phase, S, S);
      gl.disable(gl.BLEND);
    }
    if (cover) g.drawImage(f.canvas, (S - w) / 2, f.canvas.height - S + (S - h) / 2, w, h, 0, 0, w, h);
    else g.drawImage(f.canvas, 0, f.canvas.height - S, S, S, 0, 0, w, h);
    return true;
  }
  const draw = (q, alpha) => {
    g.globalAlpha = alpha;
    /* The Loom's rings follow the size it is told: told only the real size, they show as big steps on the page. */
    g.setTransform(0.25, 0, 0, 0.25, (w - S) / 2, (h - S) / 2);
    drawSpiral(g, S * 4, projectToV1(q), ((phase % 1) + 1) % 1);
    g.setTransform(1, 0, 0, 1, 0, 0);
    g.globalAlpha = 1;
  };
  if (seeThrough) g.clearRect(0, 0, w, h);
  draw(frame.a, 1);
  if (frame.b) draw(frame.b, frame.cross);
  return false;
}

/** Black ground to see-through: brightness becomes alpha. Runs only at a low rate, on a small canvas. */
function knockOut(g, w, h) {
  const img = g.getImageData(0, 0, w, h), d = img.data;
  for (let i = 0; i < d.length; i += 4) {
    const a = Math.max(d[i], d[i + 1], d[i + 2]);
    if (a > 0 && a < 255) { const k = 255 / a; d[i] *= k; d[i + 1] *= k; d[i + 2] *= k; }
    d[i + 3] = a;
  }
  g.putImageData(img, 0, 0);
}

/* ---- the bus ---- */
export function createBus() {
  let start = DEFAULT_PAIR, target = DEFAULT_PAIR, fade = 1, read = false;
  let driftOn = false, driftAmt = 0, driftSec = 0, kick = 0;
  let clock = createMorphClock();
  let gentleNow = false, reducedNow = false;
  const uses = new WeakSet();
  const sites = [];
  let lastTick = 0;
  const log = { morphs: 0 };

  const basePair = () => (fade >= 1 ? target : [mixHex(start[0], target[0], smooth(0, 1, fade)), mixHex(start[1], target[1], smooth(0, 1, fade))]);
  function colours() {
    const base = basePair();
    if (driftAmt <= 0) return base;
    const d = driftPair(base, driftSec);
    return driftAmt >= 1 ? d : [mixHex(base[0], d[0], driftAmt), mixHex(base[1], d[1], driftAmt)];
  }

  function setPair(spiral, snap) {
    if (!Array.isArray(spiral) || !isHex(spiral[0])) return;
    const next = [spiral[0].toLowerCase(), (isHex(spiral[1]) ? spiral[1] : spiral[0]).toLowerCase()];
    if (next[0] === target[0] && next[1] === target[1]) return;
    start = snap ? next : colours();
    target = next;
    fade = snap ? 1 : 0;
  }

  /** The palette: the first read lands at once, every change after that fades. Hears any 'palette' event and reads what is true now. */
  function use(ctx) {
    if (uses.has(ctx)) return;
    uses.add(ctx);
    const readNow = () => { setPair(ctx.palette?.()?.spiral, !read); read = true; };
    ctx.on?.('palette', readNow);
    readNow();
  }

  /** Called once a frame by the first site to get there. */
  function advance(dt, gentle, reduced) {
    gentleNow = gentle; reducedNow = reduced;
    if (fade < 1) fade = Math.min(1, fade + dt / FADE_S);
    const wantDrift = driftOn && !reduced;
    if (wantDrift) {
      driftAmt = Math.min(1, driftAmt + dt);
      kick = gentle ? 0 : Math.max(0, kick - dt);
      driftSec += dt * ((gentle ? 0.35 : 1) + kick * 7);
    } else if (driftAmt > 0 && !driftOn) {
      driftAmt = Math.max(0, driftAmt - dt / 2);
      if (driftAmt === 0) driftSec = 0;
    }
    const was = clock.morphing;
    clock.step(dt, { gentle, reduced });
    if (was && !clock.morphing) log.morphs += 1;
  }

  const fading = () => fade < 1 || (!driftOn && driftAmt > 0);
  const moving = () => clock.active || driftAmt > 0 || driftOn || fade < 1;

  /**
   * A spiral on the bus. opts:
   *  ground: a hex for a fixed ground, null for the dark ground that follows the palette, 'clear' for see-through.
   *  rates: paints a second { morph, hold, drift, fade }. stillPx(): the backing size of its still frame.
   *  paintStill(phase): draws the still frame. liveMax: the largest size while it moves. visible(): false skips the paint. warm: readies the field early.
   */
  function site(ctx, canvas, g, opts) {
    use(ctx);
    const seeThrough = opts.ground === 'clear';
    const lightGround = isHex(opts.ground) && parseInt(opts.ground.slice(1, 3), 16) > 127;
    const lad = LADDER.filter((s) => s <= opts.liveMax);
    let rung = 0, overBudget = 0, rateMul = 1;
    let live = false, spent = 0, phase = 0, dirty = true, warmIn = WARM_AFTER_S;
    const me = {
      speed: 1,
      stats: { label: opts.label || '', paints: 0, ms: 0, last: 0, gl: false },
      get live() { return live; },
      get phase() { return phase; },
      get size() { return canvas.width; },
      /** The colours of a still frame right now. */
      still() { const p = colours(); return { colors: p, bg: seeThrough ? '#000000' : isHex(opts.ground) ? opts.ground : groundOf(p[0]) }; },
      /** A still frame on the field, cleaner than the 2D Loom's rings. False when there is no field, so the caller draws it the old way. */
      stillOnField(at) {
        if (seeThrough || !fieldReady(canvas.width) || !getField(canvas.width)) return false;
        const pair = colours(), ground = isHex(opts.ground) ? opts.ground : groundOf(pair[0]);
        return paintLoom(g, morphFrame(0, null, 0, pair, ground), at, canvas.width, canvas.width, { lightGround, cover: false });
      },
      frame(dt) {
        const gentle = !!ctx.gentle, reduced = !!ctx.reduced;
        /* Whichever site gets here first this frame moves the bus. Frames stop on a pause, and a long gap counts as one short step. */
        const t = performance.now();
        if (t - lastTick >= 8) { advance(Math.min(0.1, (t - lastTick) / 1000), gentle, reduced); lastTick = t; }
        if (clock.active && !gentle) phase += dt * 0.06 * me.speed;
        /* The shader takes a moment to compile, once, and never on the frame of a buy: warm it up a little after the spiral arrives, then swap the still for the cleaner one. */
        if (opts.warm && warmIn > 0 && (warmIn -= dt) <= 0 && !live) {
          if (getField(canvas.width)) opts.paintStill(((phase % 1) + 1) % 1);
        }
        const go = moving();
        if (!go) {
          if (live) { live = false; dirty = true; opts.paintStill(((phase % 1) + 1) % 1); }
          return;
        }
        if (!live) { live = true; dirty = true; rung = 0; rateMul = 1; }
        const morphing = clock.morphing && !reduced;
        const r = opts.rates;
        let rate = Math.max(morphing ? r.morph : 0, fading() ? r.fade : 0, driftOn && !reduced ? r.drift : 0, clock.on && !gentle && !reduced ? r.hold : 0);
        if (gentle) rate = Math.min(rate, 10);
        rate *= rateMul;
        spent += dt;
        if (!dirty && (rate <= 0 || spent < 1 / rate)) return;
        if (opts.visible && !opts.visible()) return;
        spent = 0; dirty = false;
        paint();
      },
      remove() {
        const i = sites.indexOf(me);
        if (i >= 0) sites.splice(i, 1);
      },
    };
    function paint() {
      const size0 = lad[Math.min(rung, lad.length - 1)];
      const hasField = !!getField(size0);
      const size = hasField ? size0 : Math.min(size0, FALLBACK_MAX);
      if (canvas.width !== size) canvas.width = canvas.height = size;
      const pair = colours();
      const ground = seeThrough ? '#000000' : isHex(opts.ground) ? opts.ground : groundOf(pair[0]);
      const frame = morphFrame(clock.from, clock.to, clock.t, pair, ground, { gentle: gentleNow || reducedNow });
      const t0 = performance.now();
      const gl = paintLoom(g, frame, phase, size, size, { lightGround, cover: false, seeThrough });
      if (seeThrough && gl) knockOut(g, size, size);
      const ms = performance.now() - t0;
      const st = me.stats;
      st.paints += 1; st.ms += ms; st.last = ms; st.gl = gl; st.size = size;
      if (!gl) rateMul = Math.min(rateMul, FALLBACK_RATE);
      overBudget = ms > PAINT_BUDGET_MS ? overBudget + 1 : Math.max(0, overBudget - 1);
      if (overBudget >= 12) {
        overBudget = 0;
        if (rung < lad.length - 1) rung += 1;
        else rateMul = Math.max(0.25, rateMul * 0.7);
      }
    }
    sites.push(me);
    return me;
  }

  return {
    use, site, colours,
    get clock() { return clock; },
    get fading() { return fading(); },
    get target() { return target; },
    get driftOn() { return driftOn; },
    get log() { return log; },
    sites,
    advance,
    setDrift(on, { arrive = false } = {}) {
      driftOn = !!on;
      if (on && arrive) kick = 1.5;
    },
    /** Spiral Shapes on or off. On a first buy the first melt starts at once; after a reload it holds a moment on the saved shape. */
    setShapes(on, { rng, start = 0, arrive = false, onSettle } = {}) {
      if (on) {
        if (!clock.active) clock = createMorphClock({ rng, start, wait: arrive ? 0 : 3, onSettle });
        clock.setOn(true);
      } else clock.setOn(false);
    },
  };
}

/** The one the game uses. A New run is a page reload, so this lives as long as the run does. */
export const spiralBus = createBus();
