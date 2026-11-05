/* render/ramps.js - the jump ramp wedges (track/ramps.js, track/rampMesh.js). One draw per layer for every ramp
 * of a course: the solid wedge in the live course palette (shade per face, brighter toward the lip), the boost-pad
 * chevron shader running up its top in the course accent, and the lit lip band (the scene's shared jump-lip
 * material). Built once at course load, before the warm; update() only writes uniforms (no per-frame allocation).
 * Gentle / reduced motion: the chevrons scroll on the scene's pad phase, which Gentle already slows; nothing pulses.
 */
import { Group, Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial } from 'three/webgpu';
import { attribute, uv, mix, smoothstep, step, float } from 'three/tsl';
import { buildRampMeshes } from '../track/rampMesh.js';
import { createPadMaterial } from './materials/pads.js';
import { RAMP_LOOK } from './rampLook.js';
export { RAMP_LOOK, floatingBob } from './rampLook.js';

function geometry(d, extra = null) {
  if (!d || !d.indices.length) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(d.positions, 3));
  g.setAttribute('normal', new BufferAttribute(d.normals, 3));
  g.setAttribute('uv', new BufferAttribute(d.uvs, 2));
  if (extra) g.setAttribute(extra, new BufferAttribute(d[extra], 1));
  g.setIndex(new BufferAttribute(d.indices, 1));
  g.computeBoundingSphere();
  return g;
}

/**
 * @param track loaded track (track.ramps)
 * @param pal render/materials/palette.js palette (col(0) primary, col(2) dark: the live section colours)
 * @param opts.chevronMask the boost-chevron mask texture (art), or null for plain stripes
 * @param opts.lipMaterial the scene's jump-lip pad material (shared, owned by the scene)
 * @param opts.accent hex for the chevrons (the course accent)
 * @returns {{ group, materials, update(phase, pulse), dispose() } | null} null without ramps
 */
export function createRampView(track, pal, { chevronMask = null, lipMaterial = null, accent = '#ffffff' } = {}) {
  if (!track.ramps || !track.ramps.length) return null;
  const data = buildRampMeshes(track);
  const group = new Group();
  group.name = 'glassroad-ramps';
  const body = new MeshBasicNodeMaterial();
  const shade = attribute('shade', 'float'), along = uv().y;
  // dark side walls and back, a lit top that warms toward the lip (v runs foot -> lip; the back face carries v past it)
  const base = mix(pal.col(2), pal.col(0), shade.mul(1 - RAMP_LOOK.dark).add(RAMP_LOOK.dark));
  const lipward = smoothstep(float(0), float(9), along).mul(step(float(0.9), shade)).mul(RAMP_LOOK.lipGlow);
  body.colorNode = mix(base, pal.col(1), lipward);
  const chevrons = createPadMaterial(accent, chevronMask);
  const owned = [body, chevrons.material];
  const add = (g, m, order) => { if (!g || !m) return null; const mesh = new Mesh(g, m); mesh.renderOrder = order; group.add(mesh); return mesh; };
  const bodyMesh = add(geometry(data.body, 'shade'), body, 0);
  if (bodyMesh) bodyMesh.name = 'ramp-wedges';
  add(geometry(data.chevrons), chevrons.material, 0.91);
  add(geometry(data.lip), lipMaterial, 0.92);
  return {
    group, data, materials: { body, chevrons: chevrons.material, lip: lipMaterial },
    /** the scene's pad phase (Gentle-slowed) and pad pulse */
    update(phase, pulse = 0) {
      chevrons.uniforms.phase.value = phase;
      chevrons.uniforms.pulse.value = pulse * RAMP_LOOK.chevronGain;
    },
    dispose() {
      group.traverse((m) => { if (m.geometry) m.geometry.dispose(); });
      for (const m of owned) m.dispose();
      group.removeFromParent();
    },
  };
}
