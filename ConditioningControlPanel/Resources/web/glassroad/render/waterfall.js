/* render/waterfall.js - the chute's waterfall sheet, its spray and the splash ring (water wave 1).
 *
 * The sheet: one translucent strip over every chute run (stream nodes steeper than WATER_LOOK.chuteGradeDeg,
 * render/waterMath.js sheetRuns), floated sheetLiftM over the road and sheetOverM past its edges, running
 * sheetRunM past the lip and the foot. One geometry, one material, one draw. Its streak field scrolls down
 * the slope at sheetSpeedMps (the clock is the caller's: Gentle halves it). Colour from the section
 * palette plus white streaks; its alpha fades in at the lip and out at the foot and the sides.
 * Spray: existing particle cells (render/particles.js burst) at every lip and foot every sprayEveryS while
 * the player is within sprayNearM; a `chute` event sprays the lip at once. A `splash` lands a white ring
 * (render/wakes.js ring, wider with the fall) and a burst of drops at the splash point.
 * No per-frame loop: update advances one clock and one spray timer; the runs are a handful of points.
 */
import { Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, float, vec3, floor, fract, hash, pow, smoothstep, attribute, clamp } from 'three/tsl';
import { LOOK } from './look.js';
import { WATER_LOOK, sheetRuns, splashRingM } from './waterMath.js';

const SPRAY_COLOURS = Object.freeze(['#ffffff', '#e6f6ff', '#ffffff']);

/** Road samples every stepM for sheetRuns: { s, y, surface }. */
export function sampleSurface(track, stepM = 1) {
  const n = Math.max(2, Math.ceil(track.lengthM / stepM)), out = [];
  for (let i = 0; i <= n; i++) {
    const s = Math.min(track.lengthM, i * stepM), f = track.frameAt(s);
    out.push({ s, y: f.p[1], surface: f.surface });
  }
  return out;
}

export function createWaterfall(track, pal, { particles = null, wakes = null, L = WATER_LOOK } = {}) {
  const runs = track && track.frameAt ? sheetRuns(sampleSurface(track), L) : [];
  const u = { t: uniform(0) };
  const P = [], Nn = [], U = [], END = [], I = [];
  const points = [];   // { p: [x, y, z], lip: bool } for the spray
  for (const r of runs) {
    const a = r.s0 - L.sheetRunM, b = r.s1 + L.sheetRunM, rows = Math.max(2, Math.ceil(b - a) + 1), base = P.length / 3;
    for (let i = 0; i < rows; i++) {
      const s = a + (b - a) * i / (rows - 1), f = track.frameAt(s), half = f.w / 2 + L.sheetOverM, end = Math.min(s - a, b - s);
      for (const side of [-1, 1]) {
        const x = side * half;
        P.push(f.p[0] + f.b[0] * x + f.n[0] * L.sheetLiftM, f.p[1] + f.b[1] * x + f.n[1] * L.sheetLiftM, f.p[2] + f.b[2] * x + f.n[2] * L.sheetLiftM);
        Nn.push(f.n[0], f.n[1], f.n[2]); U.push(x, s - r.s0); END.push(end);
      }
      if (i) { const v = base + (i - 1) * 2; I.push(v, v + 1, v + 2, v + 1, v + 3, v + 2); }
    }
    const lip = track.frameAt(r.s0), foot = track.frameAt(r.s1);
    points.push({ p: [lip.p[0], lip.p[1] + 0.2, lip.p[2]], lip: true }, { p: [foot.p[0], foot.p[1] + 0.2, foot.p[2]], lip: false });
  }
  let mesh = null, mat = null;
  if (I.length) {
    const g = new BufferGeometry();
    g.setAttribute('position', new BufferAttribute(new Float32Array(P), 3));
    g.setAttribute('normal', new BufferAttribute(new Float32Array(Nn), 3));
    g.setAttribute('uv', new BufferAttribute(new Float32Array(U), 2));
    g.setAttribute('aEnd', new BufferAttribute(new Float32Array(END), 1));
    g.setIndex(I);
    g.computeBoundingSphere();
    mat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
    const p = uv(), lane = floor(p.x.div(L.streakW)), ph = hash(lane.add(3.1)), gain = hash(lane.add(17.7)).mul(0.5).add(0.5);
    const st = fract(p.y.div(L.streakM).sub(u.t.mul(L.sheetSpeedMps / L.streakM)).add(ph));
    const streak = pow(st, 4).mul(gain);
    const inFade = smoothstep(0, L.sheetRunM, attribute('aEnd', 'float'));
    const white = vec3(1, 0.98, 0.99);
    mat.colorNode = pal.col(0).mul(0.4).add(pal.col(1).mul(0.15)).add(white.mul(streak.mul(0.9)));
    mat.opacityNode = clamp(float(L.sheetAlpha).mul(streak.add(0.45)).mul(inFade), 0, 1);
    mesh = new Mesh(g, mat);
    mesh.renderOrder = 2; mesh.frustumCulled = true;
  }

  let timer = 0;
  const few = (gentle) => (gentle ? 0.6 : 1);
  function spray(pt, gentle, mul = 1) {
    if (!particles) return;
    particles.burst(pt.p, Math.round(L.sprayCount * few(gentle) * mul), { colours: SPRAY_COLOURS, speed: L.spraySpeedMps * (pt.lip ? 1 : 0.7),
      sizeM: 0.16, lifeS: 0.55, sparkle: 0.25, up: pt.lip ? 2.2 : 1.4, g: 0.9 });
  }
  /** Advance the streak clock (scroll = the Gentle share) and the spray timer; playerPos gates the spray. */
  function update(dt, { playerPos = null, gentle = false, paused = false, scroll = 1 } = {}) {
    if (paused) return;
    u.t.value += dt * scroll;
    if (!points.length || !playerPos) return;
    timer += dt;
    if (timer < L.sprayEveryS) return;
    timer = 0;
    for (const pt of points) {
      const d = Math.hypot(pt.p[0] - playerPos.x, pt.p[1] - playerPos.y, pt.p[2] - playerPos.z);
      if (d <= L.sprayNearM) spray(pt, gentle);
    }
  }
  /** CONTRACT `splash { s, x, speed, down }`: the ring and drops at the landing point. */
  function splash({ s = 0, x = 0, down = 0 } = {}, gentle = false) {
    if (!track || !track.frameAt) return;
    const f = track.frameAt(s), pt = [f.p[0] + f.b[0] * x + f.n[0] * 0.1, f.p[1] + f.b[1] * x + f.n[1] * 0.1, f.p[2] + f.b[2] * x + f.n[2] * 0.1];
    if (wakes) wakes.ring(pt, null, splashRingM(down, L), { gentle, colour: '#ffffff', gain: LOOK.ringGain * 0.9 });
    if (particles) particles.burst(pt, Math.round(L.splashCount * few(gentle)), { colours: SPRAY_COLOURS, speed: 3.2, sizeM: 0.18, lifeS: 0.6, sparkle: 0.2, up: 2.6, g: 0.9 });
  }
  /** CONTRACT `chute { s }`: the lip nearest s sprays at once. */
  function chute({ s = 0 } = {}, gentle = false) {
    let best = null, bd = Infinity;
    for (const r of runs) { const d = Math.abs(r.s0 - s); if (d < bd) { bd = d; best = r; } }
    const pt = best ? points[runs.indexOf(best) * 2] : null;
    if (pt) spray(pt, gentle, 2);
  }
  return { object: mesh, runs, points, uniforms: u, update, splash, chute,
    dispose() { if (mesh) { mesh.geometry.dispose(); mat.dispose(); } } };
}
