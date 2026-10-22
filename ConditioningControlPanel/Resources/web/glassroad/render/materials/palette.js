/* render/materials/palette.js - section palette uniforms shared by road, rails and sky.
 * Two palettes (from, to) and a mix; col(i) is the crossfaded colour i as a TSL node. The from side
 * may itself be a weighted blend ([[id, w], ...]): a gate mid-fade starts from what was on screen.
 *
 * Polish 3 (SPECTRUM lane): the three base colours come from render/spectrum.js SPECTRUM_BASE (the
 * saturated set; look.js PALETTES stays the fallback for an unknown id), and the palette also carries
 *   - ramp(x): the full-spectrum ramp (spectrum.js rampBytes, one row per section) blended by the same
 *     from / to weights, x wraps, plus the slow hue clock;
 *   - rampRoad(x): the same ramp levelled both ways into the road band (spectrum.js roadRampAt), for
 *     patterns that stream past the eye (a hue step there is never a brightness step);
 *   - flow: metres the road patterns have streamed toward the player (wraps at flowWrap, which the road
 *     sets to the lap length so every fitted period stays continuous). Its speed is flowMps(kart speed):
 *     drive({ speedMps, gentle }) feeds it; until the integrator calls drive, the speed is estimated from
 *     the scene camera's travel each render (orthographic shadow cameras are skipped);
 *   - motion: 1, or 0.5 under Gentle (drive's gentle flag, else the motionSource uniform's value, which the
 *     road points at its contrast uniform); it scales the flow and the hue clock.
 */
import { Color, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, RepeatWrapping, ClampToEdgeWrapping, SRGBColorSpace, Vector3 } from 'three/webgpu';
import { uniform, mix, texture, vec2, fract, float } from 'three/tsl';
import { PALETTES, FALLBACK_PALETTE } from '../look.js';
import { SPECTRUM_BASE, SPECTRUM_LOOK, RAMP_ROWS, rampBytes, rampAt, roadRampAt, sectionKey, flowMps } from '../spectrum.js';

const rampTexs = new Map();
/** The shared ramp texture (rows dawn, deep, afterglow); road: the road-levelled twin. */
export function spectrumRamp(road = false) {
  if (rampTexs.has(road)) return rampTexs.get(road);
  const n = SPECTRUM_LOOK.rampN;
  const tex = new DataTexture(rampBytes(n, road ? roadRampAt : rampAt), n, RAMP_ROWS.length, RGBAFormat, UnsignedByteType);
  tex.wrapS = RepeatWrapping; tex.wrapT = ClampToEdgeWrapping;
  tex.magFilter = tex.minFilter = LinearFilter; tex.colorSpace = SRGBColorSpace;
  tex.generateMipmaps = false; tex.needsUpdate = true;
  rampTexs.set(road, tex);
  return tex;
}

export function createPalette() {
  const a = [0, 1, 2].map(() => uniform(new Color()));
  const b = [0, 1, 2].map(() => uniform(new Color()));
  const t = uniform(1);
  const rows = uniform(new Vector3(1, 0, 0));          // ramp row weights (dawn, deep, afterglow), from and to mixed
  const hue = uniform(0), flow = uniform(0), motion = uniform(1);
  const tmp = new Color();
  let fromKey = null, toName = null, fromRows = [1, 0, 0], toRows = [1, 0, 0];
  const base = (name) => SPECTRUM_BASE[name] || PALETTES[name] || SPECTRUM_BASE[sectionKey(name)] || PALETTES[FALLBACK_PALETTE];
  const load = (us, list) => {
    for (let i = 0; i < 3; i++) {
      us[i].value.setRGB(0, 0, 0);
      for (const [name, w] of list) us[i].value.add(tmp.set(base(name)[i]).multiplyScalar(w));
    }
  };
  const rowsOf = (list) => {
    const r = [0, 0, 0];
    for (const [name, w] of list) r[RAMP_ROWS.indexOf(sectionKey(name))] += w;
    return r;
  };

  // the clocks: flow (metres) and hue (turns), advanced once per render of the main camera
  const S = SPECTRUM_LOOK;
  let driven = null, lastMs = -1, lastPos = null, estMps = 0;
  const P = new Vector3();
  const rampNode = (tex, x) => {
    const xx = fract(x.add(hue)), R = RAMP_ROWS.length;
    const at = (row) => texture(tex, vec2(xx, float((row + 0.5) / R)));
    return at(0).rgb.mul(rows.x).add(at(1).rgb.mul(rows.y)).add(at(2).rgb.mul(rows.z));
  };
  const pal = {
    t, rows, hue, flow, motion,
    flowWrap: 1e6,
    motionSource: null,
    col: (i) => mix(a[i], b[i], t),
    /** The spectrum ramp at x (a TSL float; wraps), hue clock added, rows blended by the section mix. */
    ramp(x) { return rampNode(spectrumRamp(false), x); },
    /** The road-levelled ramp at x (same clock and rows): for patterns streaming past the eye. */
    rampRoad(x) { return rampNode(spectrumRamp(true), x); },
    /** from: a palette id or [[id, weight], ...] (weights sum to 1); mixT 0 = from, 1 = to. */
    set(from, to, mixT) {
      const list = Array.isArray(from) ? from : [[from, 1]];
      const key = list.map(([n, w]) => n + ':' + w.toFixed(3)).join('|');
      if (key !== fromKey) { load(a, list); fromKey = key; fromRows = rowsOf(list); }
      if (to !== toName) { load(b, [[to, 1]]); toName = to; toRows = rowsOf([[to, 1]]); }
      t.value = mixT;
      rows.value.set(...fromRows.map((v, i) => v + (toRows[i] - v) * mixT));
    },
    /** Optional, once a frame: the player's speed (m/s), the Gentle flag and the pause. Overrides the camera estimate;
     *  while paused the road lanes and the hue drift hold still. */
    drive({ speedMps = 0, gentle = false, paused = false } = {}) { driven = { speedMps, gentle, paused }; },
  };
  const tick = (frame) => {
    const cam = frame && frame.camera;
    if (!cam || !cam.isPerspectiveCamera) return;
    const now = performance.now();
    if (lastMs >= 0 && now - lastMs < 2) return;      // a second render in the same frame
    const dt = lastMs < 0 ? 0 : Math.min(0.1, (now - lastMs) / 1000);
    lastMs = now;
    if (driven && driven.paused) { driven = null; return; }   // the pause card freezes the floor too
    cam.getWorldPosition(P);
    if (dt > 0 && lastPos) {
      const v = Math.min(S.flowMaxMps, P.distanceTo(lastPos) / dt);
      estMps += (v - estMps) * Math.min(1, dt / S.speedTcS);
    }
    lastPos = (lastPos || new Vector3()).copy(P);
    const m = driven ? (driven.gentle ? 0.5 : 1) : pal.motionSource ? Math.max(0.25, Math.min(1, pal.motionSource.value)) : 1;
    motion.value = m;
    const speed = driven ? driven.speedMps : estMps;
    driven = null;
    flow.value = (flow.value + dt * flowMps(speed, m)) % pal.flowWrap;
    hue.value = (hue.value + dt * m / S.hueCycleS) % 1;
  };
  flow.onRenderUpdate(tick);
  hue.onRenderUpdate(tick);
  return pal;
}
