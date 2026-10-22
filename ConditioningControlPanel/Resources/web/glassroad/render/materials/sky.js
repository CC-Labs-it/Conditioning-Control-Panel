/* render/materials/sky.js - the spiral dome: a large inward-facing sphere that follows the kart.
 * The spiral's eye sits below the horizon ahead (scene.js tilts it from the nadir toward a slow
 * heading), so the arms turn behind and under the glass road and falls still drop toward it. Arms
 * are bright palette bands over a darker ground; turn is integrated on the CPU; contrast is what
 * Gentle halves; rescue brightens the dome toward white and the eye glows (beat 7).
 */
import { MeshBasicNodeMaterial, BackSide, Vector3 } from 'three/webgpu';
import { uniform, positionLocal, normalize, atan, acos, log, sin, mix, smoothstep, clamp, vec3, dot } from 'three/tsl';
import { LOOK } from '../look.js';

export function createSkyMaterial(pal) {
  const u = { turn: uniform(0), twist: uniform(0), contrast: uniform(LOOK.skyContrast), rescue: uniform(0),
    eye: uniform(new Vector3(0, -1, 0)), eyeR: uniform(new Vector3(1, 0, 0)), eyeU: uniform(new Vector3(0, 0, -1)) };
  const d = normalize(positionLocal);
  const r = acos(clamp(dot(d, u.eye), -1, 1)).add(0.02);                 // angle from the eye
  const az = atan(dot(d, u.eyeU), dot(d, u.eyeR));
  const wave = sin(az.mul(LOOK.skyArms).add(log(r).mul(u.twist)).sub(u.turn)).mul(0.5).add(0.5);
  // soft-edged arms, faded out right at the eye so the log rings never alias
  const arm = smoothstep(0.1, 0.9, wave).mul(smoothstep(0.04, 0.35, r));
  const up = d.y.mul(0.5).add(0.5);
  const ground = mix(pal.col(2), pal.col(0), up).mul(LOOK.skyDim * LOOK.skyGapDim);
  const bands = mix(pal.col(0), pal.col(1), up).mul(LOOK.skyDim);
  const col = mix(ground, bands, arm.mul(u.contrast));
  const eye = smoothstep(0, 1.2, r).oneMinus();
  const m = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, fog: false });
  m.colorNode = mix(col, vec3(1, 1, 1), u.rescue.mul(eye.mul(0.5).add(LOOK.rescueWhite)));
  return { material: m, uniforms: u };
}
