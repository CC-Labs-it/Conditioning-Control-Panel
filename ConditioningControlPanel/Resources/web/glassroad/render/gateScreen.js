/* render/gateScreen.js - the 2D Loom on the glass when the player passes a spiral gate (polish 4, SPIRALS lane).
 *
 * Like the DtRH spiral takeover: a DOM canvas laid over the game canvas draws the REAL Loom field
 * (arcademy/engine/loom/loomField.js createFieldRenderer, the very params the gate wears) for a moment
 * after a pass: a smooth rise of at least 300 ms, a hold of about a second, a smooth fall. The canvas
 * blends as `screen` (the Loom's dark background adds nothing, only the arms light the view) under a
 * radial CSS mask, light over the road in the middle and strong at the corners, so the road stays
 * readable. One WebGL context for the screen's whole life on a small backing store (640 / 512 / 256 long
 * side by tier), CSS-upscaled; the field is analytically antialiased so it stays crisp. Nothing draws and
 * the canvas is display:none while idle.
 *
 * Photosafe: the opacity only ever moves through smooth ramps; the field turns at the Loom's own pace but
 * never sweeps an arm across a pixel faster than SPIRAL_LOOK.screen.maxBandHz (under 3 Hz); a play asks
 * photosafe.request('gate-screen') and a refusal plays at refusedMul. Gentle halves opacity and speed and
 * stretches the rise and fall to gentle.riseMs / fadeMs. Paused: nothing moves (update(dt, true)).
 * No WebGL: the Loom's own 2D fallback frame (drawFallbackFrame) on a 2D canvas.
 *
 *   const screen = createGateScreen(document.body, { feel, photosafe });
 *   spirals onPass: (g) => screen.play(g.params, 1)
 *   each frame: screen.update(dt, paused)
 */
import { createFieldRenderer, normalizeParams2, drawFallbackFrame } from '../../arcademy/engine/loom/loomField.js';
import { Q } from './quality.js';
import { SPIRAL_LOOK, loopS, screenTimes, screenEnvelope, screenRateMul, screenMaskCss } from './spiralMath.js';

const clamp01 = (v) => Math.min(1, Math.max(0, v));

/**
 * @param {HTMLElement} host the element over the game canvas (document.body: the canvas is position:fixed)
 * @param {{ feel?: object, photosafe?: { request(kind: string): boolean }, gentle?: boolean|(() => boolean)|{ on: boolean },
 *   tier?: string, look?: object, doc?: Document, makeField?: Function, zIndex?: number }} opts
 * @returns {{ play(params: object, strength?: number): boolean, update(dt: number, paused?: boolean): void, dispose(): void,
 *   readonly active: boolean, readonly level: number, canvas: HTMLCanvasElement|null }}
 */
export function createGateScreen(host, { feel = null, photosafe = null, gentle = null, tier = null, look = SPIRAL_LOOK, doc = globalThis.document,
  makeField = createFieldRenderer, zIndex = null } = {}) {
  const S = look.screen;
  const get = (k, d) => { try { const v = feel && feel.get(k); return v == null ? d : v; } catch (e) { return d; } };
  const isGentle = () => (typeof gentle === 'function' ? !!gentle() : gentle && typeof gentle === 'object' ? !!gentle.on : gentle === true ? true : !!get('gentle.on', false));
  const tierName = () => tier || Q.name || 'high';

  let canvas = null, field = null, ctx2d = null, broken = !doc || !host;
  let q = null, strength = 0, age = -1, phase = 0, loop = 1, lastDraw = -1e9, clockMs = 0, shown = false, level = 0;

  function mount() {
    if (canvas || broken) return !broken;
    try {
      canvas = doc.createElement('canvas');
      if (!canvas.style) canvas.style = {};
      const mask = screenMaskCss(look);
      Object.assign(canvas.style, {
        position: host === doc.body ? 'fixed' : 'absolute', left: '0', top: '0', width: '100%', height: '100%', pointerEvents: 'none',
        mixBlendMode: 'screen', zIndex: String(zIndex ?? S.zIndex), display: 'none', opacity: '0', maskImage: mask, webkitMaskImage: mask,
      });
      if (canvas.setAttribute) canvas.setAttribute('aria-hidden', 'true');
      canvas.className = 'gate-screen';
      size();
      field = makeField ? makeField(canvas) : null;
      if (!field) ctx2d = canvas.getContext ? canvas.getContext('2d') : null;
      if (!field && !ctx2d) { broken = true; return false; }
      if (canvas.addEventListener) canvas.addEventListener('webglcontextlost', (e) => { if (e && e.preventDefault) e.preventDefault(); stop(); broken = true; });
      if (host.appendChild) host.appendChild(canvas);
      return true;
    } catch (e) {
      console.warn('[glassroad/gateScreen] no screen: ' + (e && e.message));
      broken = true;
      return false;
    }
  }

  /** Backing store at the view's aspect, long side by tier. */
  function size() {
    const w = Math.max(1, host.clientWidth || (doc.defaultView && doc.defaultView.innerWidth) || 1280);
    const h = Math.max(1, host.clientHeight || (doc.defaultView && doc.defaultView.innerHeight) || 720);
    const long = S.backLong[tierName()] ?? S.backLong.high, k = long / Math.max(w, h);
    const bw = Math.max(16, Math.round(w * k)), bh = Math.max(16, Math.round(h * k));
    if (canvas.width !== bw || canvas.height !== bh) { canvas.width = bw; canvas.height = bh; }
  }

  function stop() {
    age = -1; level = 0; shown = false;
    if (canvas && canvas.style) { canvas.style.opacity = '0'; canvas.style.display = 'none'; }
  }

  function times() { return screenTimes({ gentle: isGentle(), riseMs: get('gentle.riseMs', 300), fadeMs: get('gentle.fadeMs', 600) }, look); }

  /**
   * Play the Loom of a gate. params: the gate's Loom params (spirals.gates[i].params or the onPass record);
   * strength 0..1 scales the peak. A play during a play keeps the level on screen and rides into a new hold.
   * @returns {boolean} false when there is no screen (no DOM, no canvas)
   */
  function play(params, str = 1) {
    if (!mount()) return false;
    q = normalizeParams2(params);
    // the gate's handedness (gateParams forces clockwise) survives normalising
    if (params && params.layer && params.layer.direction) q.layer.direction = params.layer.direction;
    if (params && params.layer2 && params.layer2.direction) q.layer2.direction = params.layer2.direction;
    loop = Math.max(0.05, loopS(q));
    const granted = photosafe && photosafe.request ? photosafe.request('gate-screen') : true;
    strength = clamp01(str) * (granted ? 1 : S.refusedMul);
    const T = times();
    if (age < 0) { age = 0; phase = 0; }
    else if (age >= T.rise + T.hold) {
      // falling: jump back onto the rise at the level now on the glass (inverse smoothstep), never a dip or a step
      const e = screenEnvelope(age, { gentle: isGentle(), riseMs: get('gentle.riseMs', 300), fadeMs: get('gentle.fadeMs', 600) }, look);
      age = T.rise * (0.5 - Math.sin(Math.asin(1 - 2 * clamp01(e)) / 3));
    } else if (age >= T.rise) age = T.rise;   // holding: a fresh hold
    return true;
  }

  /** Advance and draw. dt seconds; paused: nothing moves, the frame on the glass stays as it is. */
  function update(dt, paused = false) {
    if (age < 0 || !canvas || broken) return;
    if (paused || !(dt > 0)) return;
    if (doc.hidden) return;
    const g = isGentle();
    age += dt;
    const env = screenEnvelope(age, { gentle: g, riseMs: get('gentle.riseMs', 300), fadeMs: get('gentle.fadeMs', 600) }, look);
    if (env <= 0 && age > times().rise) { stop(); return; }
    phase = (phase + dt * screenRateMul(q, { gentle: g }, look) / loop) % 1;
    level = clamp01(env * strength * S.peak * (g ? S.gentleAlphaMul : 1));
    if (!shown) { canvas.style.display = 'block'; shown = true; }
    canvas.style.opacity = level.toFixed(3);
    clockMs += dt * 1000;
    const cap = S.frameMs[tierName()] || 0;
    if (cap && clockMs - lastDraw < cap) return;
    lastDraw = clockMs;
    size();
    if (field) field.render(q, phase);
    else if (ctx2d) drawFallbackFrame(ctx2d, q, phase, canvas.width, canvas.height);
  }

  function dispose() {
    stop();
    try { const lose = field && field.gl && field.gl.getExtension && field.gl.getExtension('WEBGL_lose_context'); if (lose) lose.loseContext(); } catch (e) { /* gone */ }
    if (canvas && canvas.parentNode) canvas.parentNode.removeChild(canvas);
    canvas = null; field = null; ctx2d = null; broken = true;
  }

  return {
    play, update, dispose,
    get active() { return age >= 0; },
    /** current opacity 0..1 on the glass (dev and tests) */
    get level() { return level; },
    get canvas() { return canvas; },
  };
}
