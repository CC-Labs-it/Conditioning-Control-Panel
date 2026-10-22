/* render/routes.js - the drop shortcut's markings (polish 4 ROUTES), drawn from track.meshes.routeEdge and
 * routeLand (track/mesh.js builds them three-free).
 *
 * The open edge: a hot glowing kerb, chevrons pointing off it and a tall post at each end of the break,
 * so the gap in the rail reads from the approach. The landing: a cyan ring round the pad with forward
 * arrows and the soft catch zone, bright enough to read from the helix 13 m above. A slow wave of glow
 * runs outward over the chevrons and round the ring (well under 3 Hz per point; the mean brightness never
 * moves). A clean landing swells the ring once, if fx/photosafe.js grants it, rising over at least 300 ms.
 * Gentle halves the wave's speed and depth. Pause freezes the wave (update gets paused).
 *
 * createRoutes(scene, track, { photosafe, gentle }) -> { object, update(dt, { gentle, paused, scroll }),
 *   land({ clean }), dispose } | null when the track has no routes.
 */
import { Group, Mesh, BufferGeometry, BufferAttribute, MeshBasicNodeMaterial, AdditiveBlending, DoubleSide, Color } from 'three/webgpu';
import { uniform, uv, vec3, sin, float, clamp, mix } from 'three/tsl';

/** Route look numbers (this lane's look table). */
export const ROUTE_LOOK = Object.freeze({
  edgeColour: 0xff4fd8, landColour: 0x5ff6ff,
  edgeGain: 1.25, landGain: 1.1,
  waveHz: 0.6,          // wave cycles per second along uv.x (each point: waveHz x waveK times a second)
  waveK: 2,             // bumps along a piece
  waveDepth: 0.45,      // share of the brightness the wave moves (mean held)
  swellGain: 0.9, swellRiseS: 0.35, swellFallS: 0.9,
  gentleMul: 0.5,
});

function meshOf(d, material) {
  if (!d || !d.indices || !d.indices.length) return null;
  const g = new BufferGeometry();
  g.setAttribute('position', new BufferAttribute(d.positions, 3));
  g.setAttribute('normal', new BufferAttribute(d.normals, 3));
  g.setAttribute('uv', new BufferAttribute(d.uvs, 2));
  g.setIndex(new BufferAttribute(d.indices, 1));
  g.computeBoundingSphere();
  const m = new Mesh(g, material);
  m.renderOrder = 2;
  return m;
}

/**
 * solid = false: additive glow (colour x gain x wave). solid = true: an opaque-ish paint over the busy road
 * (a white-hot core toward the colour, alpha from gain), so the marks hold their shape on any palette.
 */
function glowMaterial(hex, gain, solid = false) {
  const u = { phase: uniform(0), depth: uniform(ROUTE_LOOK.waveDepth), swell: uniform(0) };
  const c = new Color(hex);
  const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide, ...(solid ? {} : { blending: AdditiveBlending }) });
  const p = uv();
  // a travelling bump along uv.x; uv.y is the piece's own gain (fill, lines, posts)
  const wave = sin(p.x.mul(ROUTE_LOOK.waveK * Math.PI * 2).sub(u.phase)).mul(0.5).add(0.5);
  const k = clamp(float(1).sub(u.depth).add(wave.mul(u.depth).mul(2)), 0, 2).add(u.swell);
  const col = vec3(c.r, c.g, c.b);
  if (solid) {
    m.colorNode = mix(col, vec3(1, 0.97, 1), clamp(k.mul(0.45), 0, 0.85));
    m.opacityNode = clamp(p.y.mul(gain).mul(float(0.55).add(k.mul(0.35))), 0, 1);
  } else m.colorNode = col.mul(p.y.mul(gain)).mul(k);
  return { material: m, uniforms: u };
}

export function createRoutes(scene, track, { photosafe = null } = {}) {
  if (!track.routes || !track.routes.length || !track.meshes) return null;
  const object = new Group();
  object.name = 'routes';
  const edge = glowMaterial(ROUTE_LOOK.edgeColour, ROUTE_LOOK.edgeGain, true), land = glowMaterial(ROUTE_LOOK.landColour, ROUTE_LOOK.landGain, true);
  const meshes = [meshOf(track.meshes.routeEdge, edge.material), meshOf(track.meshes.routeLand, land.material)].filter(Boolean);
  for (const m of meshes) object.add(m);
  scene.add(object);
  let phase = 0, swellT = -1;

  function update(dt, { gentle = false, paused = false, scroll = 1 } = {}) {
    if (paused) return;
    const g = gentle ? ROUTE_LOOK.gentleMul : 1;
    phase = (phase + dt * ROUTE_LOOK.waveHz * Math.PI * 2 * g * scroll) % (Math.PI * 2 * 64);
    edge.uniforms.phase.value = land.uniforms.phase.value = phase;
    edge.uniforms.depth.value = land.uniforms.depth.value = ROUTE_LOOK.waveDepth * g;
    let s = 0;
    if (swellT >= 0) {
      swellT += dt;
      const rise = Math.max(ROUTE_LOOK.swellRiseS, 0.3), fall = ROUTE_LOOK.swellFallS * (gentle ? 1.5 : 1);
      s = swellT < rise ? swellT / rise : Math.max(0, 1 - (swellT - rise) / fall);
      if (swellT > rise + fall) swellT = -1;
    }
    land.uniforms.swell.value = s * ROUTE_LOOK.swellGain * g;
  }

  /** A route landing (sim route-land): a clean one swells the ring once, if the luminance budget allows. */
  function onLand({ clean = false } = {}) {
    if (!clean || swellT >= 0) return;
    if (photosafe && !photosafe.request('route-land')) return;
    swellT = 0;
  }

  function dispose() {
    scene.remove(object);
    for (const m of meshes) m.geometry.dispose();
    edge.material.dispose(); land.material.dispose();
  }
  return { object, update, land: onLand, dispose };
}
