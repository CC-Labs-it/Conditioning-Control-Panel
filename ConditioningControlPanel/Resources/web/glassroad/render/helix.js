/* render/helix.js - the music road's DNA helix (handoff-1022/MUSIC-ROAD-BRIEF.md section 2).
 *
 * Two glowing strands wound round the whole lap with short base-pair stubs between them, built once from
 * the track frames; the vertex stage turns them with the song (render/musicRoadMath.js helixPhase, locked
 * to the bar grid) and the weight brings them IN (spiralling in from a wide radius while they brighten)
 * and OUT (the reverse). The radius keeps every strand outside the road edge where it crosses the road
 * plane, so nothing ever lies on the surface the kart drives on; near the camera they fade, so a strand
 * never slices across the view. One additive draw call; hidden while the weight is zero.
 */
import { Mesh, BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide } from 'three/webgpu';
import { uniform, attribute, vec3, float, sin, cos, mix, smoothstep, cameraPosition, fract, abs, exp } from 'three/tsl';

import { HELIX_LOOK, helixGeometryData } from './helixMath.js';

export { HELIX_LOOK };

/** @returns {{ object: Mesh, update(mf: object, ctx: { kartS: number, gentle: boolean }): void, dispose(): void }} */
export function createHelix(track, pal, L = HELIX_LOOK) {
  const d = helixGeometryData(track, L);
  const g = new BufferGeometry();
  // six vertex buffers (WebGPU allows eight): the centre rides in position, the four scalars in one vec4
  g.setAttribute('position', new Float32BufferAttribute(d.attrs.aC, 3));
  for (const k of ['aB', 'aN', 'aT']) g.setAttribute(k, new Float32BufferAttribute(d.attrs[k], 3));
  const packed = new Float32Array(d.count * 4);
  for (let i = 0; i < d.count; i++) { packed[i * 4] = d.attrs.aS[i]; packed[i * 4 + 1] = d.attrs.aTh[i]; packed[i * 4 + 2] = d.attrs.aW[i]; packed[i * 4 + 3] = d.attrs.aR[i]; }
  g.setAttribute('aP', new Float32BufferAttribute(packed, 4));
  g.setAttribute('aKind', new Float32BufferAttribute(d.attrs.aKind, 1));
  g.setIndex(d.index);
  const u = { weight: uniform(0), phase: uniform(0), kartS: uniform(0), kick: uniform(0), hue: uniform(0), lap: uniform(track.lengthM) };
  const C = attribute('position', 'vec3'), B = attribute('aB', 'vec3'), N = attribute('aN', 'vec3'), T = attribute('aT', 'vec3');
  const P4 = attribute('aP', 'vec4'), sA = P4.x, th0 = P4.y, w = P4.z, rr = P4.w, kind = attribute('aKind', 'float');
  const th = th0.add(u.phase);
  const radial = B.mul(cos(th)).add(N.mul(sin(th)));
  const across = B.mul(sin(th).negate()).add(N.mul(cos(th)));
  // IN: the strands spiral in from inRadiusMul x radius as the weight rises; OUT is the same in reverse
  const rad = rr.mul(mix(float(L.inRadiusMul), float(1), smoothstep(0, 1, u.weight)));
  const side = mix(across, T, kind);                       // strands are wide across the turn, stubs along the road
  const pos = C.add(radial.mul(rad)).add(side.mul(w));
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, blending: AdditiveBlending, side: DoubleSide });
  m.positionNode = pos;
  // only a stretch round the kart is drawn: ahead to aheadM, behind to behindM, both soft
  const dS = fract(sA.sub(u.kartS).div(u.lap).add(0.5)).sub(0.5).mul(u.lap);
  const span = smoothstep(-L.behindM, -L.behindM * 0.4, dS).mul(float(1).sub(smoothstep(L.aheadM * 0.6, L.aheadM, dS)));
  const camD = cameraPosition.sub(pos).length();
  const nearFade = smoothstep(L.nearFadeM[0], L.nearFadeM[1], camD);
  const ramp = pal && pal.ramp ? (x) => pal.ramp(x) : () => vec3(1, 0.6, 0.85);
  const col = ramp(sA.div(80).add(u.hue).add(kind.mul(0.5)));
  // a kick lights the stubs: brightest at the kart, rolling out along the road
  const pulse = exp(abs(dS).div(35).negate()).mul(u.kick).mul(kind.mul(L.kickGain).add(0.25));
  m.colorNode = col.mul(float(L.gain).add(pulse)).mul(u.weight).mul(span).mul(nearFade);
  const mesh = new Mesh(g, m);
  mesh.frustumCulled = false; mesh.visible = false; mesh.renderOrder = 2;
  return {
    object: mesh,
    update(mf, { kartS = 0, gentle = false } = {}) {
      const wgt = mf && mf.on ? mf.helix : 0;
      mesh.visible = wgt > 0.002;
      u.weight.value = wgt;
      if (!mesh.visible) return;
      u.phase.value = mf.helixPhase;
      u.kartS.value = kartS;
      u.kick.value = gentle ? mf.kick * 0.4 : mf.kick;
      u.hue.value = (mf.barIndex + mf.barPhase) * 0.02;
    },
    dispose() { g.dispose(); m.dispose(); },
  };
}
