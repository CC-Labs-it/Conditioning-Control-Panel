import { Mesh, SphereGeometry, MeshBasicNodeMaterial, BackSide, Color, FogExp2 } from 'three/webgpu';
import { uniform, positionGeometry, normalize, abs, float, mix } from 'three/tsl';

// SHIFT: world light keyframes on the song clock. Fog carries the near world (most of it is unlit), a dome
// just inside the sky tints the far world and the horizon, the background and the two lights follow.
// Every change eases in over at least 0.8 s (Gentle: 1.2 s) and out over 1.5 s; nothing flickers.
export const SHIFT_PRESETS = Object.freeze({
  dim: { fog: '#14061a', density: .05, sky: '#22062c', skyA: .8, bg: '#3a1840', light: .35 },
  carousel: { fog: '#ffcf7a', density: .0035, sky: '#ffb347', skyA: .5, bg: '#ffd99a', light: 1.1 },
  'pink-spots': { fog: '#26001a', density: .04, sky: '#3a0026', skyA: .85, bg: '#40002a', light: .3 },
  pink: { fog: '#ff6ec7', density: .0035, sky: '#ff4fb0', skyA: .72, bg: '#ff9ad5', light: 1 },
  green: { fog: '#00ff73', density: .018, sky: '#00ff66', skyA: .6, bg: '#00b33c', light: .8 },
  daylight: { fog: '#e9f6ff', density: .011, sky: '#9fd8ff', skyA: .85, bg: '#bfe6ff', light: 1.4 },
});
const sat = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const smooth = x => { const u = sat(x); return u * u * (3 - 2 * u); };

/** How much of a SHIFT row is on at songS (0..amount): eased in over its ramp, out after its end. */
export function shiftAmount(row, songS, { bpm = 0, gentle = false } = {}) {
  const p = row.params || {}, bar = bpm > 0 ? 240 / bpm : 2;
  const inS = Math.max(gentle ? 1.2 : .8, p.rampS ?? (p.transitionBars ? p.transitionBars * bar : .8));
  const out = p.holdToEnd ? 0 : smooth((songS - row.endS) / Math.max(gentle ? 1.2 : .3, p.outS ?? 1.5));
  return (p.amount ?? 1) * smooth((songS - row.startS) / inS) * (1 - out);
}

export function createCueShift(scene, { hemi = null, sun = null, radius = 2800 } = {}) {
  // A course that already drives its own fog keeps doing so; the shift blends on top of that value.
  const managed = !!scene.fog;
  if (!scene.fog) scene.fog = new FogExp2('#000000', 0);   // present from the start so warmed shaders carry fog
  const base = { fog: scene.fog.color.clone(), bg: scene.background?.isColor ? scene.background.clone() : null,
    hemi: hemi?.intensity ?? 0, sun: sun?.intensity ?? 0 };
  const u = { color: uniform(new Color('#ff4fb0')), alpha: uniform(0) };
  const dir = normalize(positionGeometry);
  const material = new MeshBasicNodeMaterial({ side: BackSide, transparent: true, depthWrite: false, fog: false });
  material.colorNode = u.color;
  // Strongest at the horizon, a little thinner overhead and underfoot.
  material.opacityNode = u.alpha.mul(mix(float(.55), float(1), float(1).sub(abs(dir.y))));
  const dome = new Mesh(new SphereGeometry(radius, 32, 16), material);
  dome.renderOrder = -.5; dome.frustumCulled = false; dome.visible = false; dome.name = 'cue-shift-dome';
  scene.add(dome);
  const fog = new Color(), bg = new Color(), sky = new Color(), tmp = new Color();
  let level = 0, owned = 0, disposed = false;
  function apply(rows, songS, { bpm, gentle, at }) {
    fog.copy(base.fog); if (base.bg) bg.copy(base.bg);
    let density = managed ? scene.fog.density : 0, light = 1, skyA = 0, total = 0;
    const shifts = rows.filter(r => r.kind === 'SHIFT' && SHIFT_PRESETS[r.params?.preset]).sort((a, b) => a.startS - b.startS);
    for (const row of shifts) {
      const k = shiftAmount(row, songS, { bpm, gentle }), P = SHIFT_PRESETS[row.params.preset];
      if (!(k > 0)) continue;
      fog.lerp(tmp.set(P.fog), k); if (base.bg) bg.lerp(tmp.set(P.bg), k);
      density += (P.density - density) * k; light += (P.light - light) * k;
      sky.lerp(tmp.set(P.sky), total ? k : 1); skyA += (P.skyA - skyA) * k; total = Math.max(total, k);
    }
    level = total;
    if (total > 0 || owned) {
      scene.fog.color.copy(fog); scene.fog.density = density;
      if (base.bg && scene.background?.isColor) scene.background.copy(bg);
      if (hemi) hemi.intensity = base.hemi * light;
      if (sun) sun.intensity = base.sun * light;
      owned = total > 0 ? 1 : 0;
      if (!owned && base.bg && scene.background?.isColor) scene.background.copy(base.bg);
      if (!owned) scene.fog.color.copy(base.fog);
    }
    u.color.value.copy(sky); u.alpha.value = skyA;
    dome.visible = skyA > .002;
    if (at) dome.position.copy(at);
  }
  return { object: dome,
    update(rows = [], songS = NaN, { paused = false, bpm = 0, gentle = false, at = null } = {}) {
      if (disposed || paused) return;
      apply(rows, Number.isFinite(songS) ? songS : -Infinity, { bpm, gentle, at });
    },
    clear() { if (!disposed) apply([], -Infinity, {}); },
    get stats() { return { level, dome: dome.visible }; },
    dispose() {
      if (disposed) return; this.clear(); disposed = true;
      dome.removeFromParent(); dome.geometry.dispose(); material.dispose();
    } };
}
