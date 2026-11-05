/* render/boostRim.js - the boost speed rim as TSL nodes inside the post composite (render/post.js); numbers, the
 * intensity law, the palette-skewed hue set and the Gentle / motion rules live in render/boostRimMath.js.
 *
 * Soft radial rainbow streaks come in from the four edges and corners (densest in the corners), flowing inward toward
 * the vanishing point, over a faint coloured corner wash. The centre box (BOOST_RIM.clearBox of each axis) is never
 * touched. One uniform-gated branch in the existing composite quad: no new pass, no render target, no allocation per
 * frame. The phone tier draws one streak layer instead of two. Screen-space only: the camera never sees it.
 */
import { Color, SRGBColorSpace } from 'three/webgpu';
import { Fn, If, uniform, screenUV, vec2, vec3, float, abs, max, min, mix, clamp, smoothstep, length, atan, floor, fract, sin } from 'three/tsl';
import { Q } from './quality.js';
import { BOOST_RIM as L, createRimState, rimHues } from './boostRimMath.js';

const TAU = Math.PI * 2, OFF = Object.freeze({ enabled: false });
const hash1 = (x, salt) => fract(sin(x.mul(12.9898).add(salt)).mul(43758.5453));

export function createBoostRim() {
  const n = L.hueStops, target = rimHues('#ff4fa3', '#9be7ff'), shown = Float32Array.from(target);
  const cols = Array.from({ length: n }, (_, i) => uniform(new Color().setRGB(shown[i * 3], shown[i * 3 + 1], shown[i * 3 + 2], SRGBColorSpace)));
  const u = { level: uniform(0), flow: uniform(0), aspect: uniform(16 / 9), layers: uniform(2) };
  const state = createRimState();
  let primary = '', accent = '', pollS = 0, warm = false;

  // the hue ramp: stop 0 .. stop n-1 round the screen, wrapping back to stop 0
  const ramp = (t) => {
    const k = fract(t).mul(n);
    let c = vec3(cols[0]);
    for (let i = 1; i <= n; i++) c = mix(c, vec3(cols[i % n]), clamp(k.sub(i - 1), 0, 1));
    return c;
  };
  // one layer of streaks: angular cells, each a soft line with smooth dashes flowing inward
  const layer = (ang01, r, i, level) => {
    const ac = ang01.mul(L.cells[i]), cell = floor(ac), f = fract(ac);
    const h1 = hash1(cell, 3.1 + i * 17.7), h2 = hash1(cell, 41.3 + i * 5.3), h3 = hash1(cell, 77.9 + i * 9.1);
    const prof = smoothstep(0, mix(0.16, 0.42, h2), abs(f.sub(0.5))).oneMinus();   // edges ascending (WGSL, GLSL)
    const freq = float(L.dashFreq[i]).mul(h1.mul(L.dashFreqVar[1] - L.dashFreqVar[0]).add(L.dashFreqVar[0]));   // each streak its own share of the layer rate
    const q = fract(r.add(u.flow).mul(freq).add(h1.mul(7)));
    const dash = q.mul(q.oneMinus()).mul(4).pow(1.5);                         // a smooth bell: no hard dash edge
    // more streaks as the level grows; the soft threshold fades a streak in, never pops it
    const thr = float(0.92).sub(level.mul(0.82));
    const show = smoothstep(thr, thr.add(0.08), h3);
    return { a: prof.mul(dash).mul(show), c: ramp(ang01.add(h3.mul(0.12))) };
  };

  /** The rim over the composite's colour node `out`. */
  function apply(out) {
    return Fn(() => {
      const o = vec3(out).toVar();
      If(u.level.greaterThan(0.001), () => {
        const p = screenUV.sub(0.5).mul(2), ax = abs(p.x), ay = abs(p.y);
        const box = max(ax, ay), corner = min(length(p).div(Math.SQRT2), 1);
        const d = mix(box, corner, L.cornerBias);
        const mask = smoothstep(L.clearBox, 1, d).mul(smoothstep(L.clearBox, L.clearBox + 0.08, box));
        If(mask.greaterThan(0), () => {
          const ang01 = atan(p.y, p.x.mul(u.aspect)).div(TAU).add(0.5), r = length(p);
          const k = mask.mul(u.level);
          o.assign(mix(o, ramp(ang01), k.mul(L.washAlpha)));
          const s0 = layer(ang01, r, 0, u.level);
          o.assign(mix(o, s0.c, clamp(s0.a.mul(k).mul(L.maxAlpha), 0, 1)));
          If(u.layers.greaterThan(1.5), () => {
            const s1 = layer(ang01, r, 1, u.level);
            o.assign(mix(o, s1.c, clamp(s1.a.mul(k).mul(L.maxAlpha * 0.8), 0, 1)));
          });
        });
      });
      return o;
    })();
  }

  function setPalette(pair) {
    const p = pair && pair[0], a = pair && pair[1];
    if (typeof p !== 'string' || (p === primary && a === accent)) return;
    primary = p; accent = a; rimHues(p, a, target);
    if (!warm) { shown.set(target); warm = true; }
  }

  /**
   * Per frame from post.update. fx = FxState (speedFeel, speedLines); rim = { enabled, off, boostKind, motion, gentle,
   * photosafe, riseMs, fadeMs, palette() } (boot.js fills one reused object). dt 0 (pause) freezes it.
   */
  function update(fx, dt, aspect, rim) {
    u.aspect.value = aspect > 0 ? aspect : 16 / 9;
    u.layers.value = L.streaks[Q.name] || 1;
    if (!rim || !fx) { state.update(OFF, dt); u.level.value = 0; return; }
    rim.speedFeel = fx.speedFeel; rim.speedLines = fx.speedLines;
    const f = state.update(rim, dt);
    u.level.value = f.level;
    u.flow.value = f.flowS * L.flowUps;
    if (!(f.level > 0.001) || !(dt > 0)) return;
    pollS -= dt;
    if ((pollS <= 0 || !warm) && typeof rim.palette === 'function') {
      pollS = 0.25;
      try { setPalette(rim.palette()); } catch { /* keep the last palette */ }
    }
    const e = 1 - Math.exp(-dt / 0.3);   // course palette changes cross-fade, never step
    for (let i = 0; i < n; i++) {
      for (let j = 0; j < 3; j++) shown[i * 3 + j] += (target[i * 3 + j] - shown[i * 3 + j]) * e;
      cols[i].value.setRGB(shown[i * 3], shown[i * 3 + 1], shown[i * 3 + 2], SRGBColorSpace);
    }
  }

  return { apply, update, uniforms: u, get stats() { return { ...state.frame, primary, accent }; }, reset() { state.reset(); u.level.value = 0; } };
}
