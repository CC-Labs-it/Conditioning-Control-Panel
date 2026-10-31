/* The course lives inside a giant dressing room. Furniture is batched before shader warmup. */
import { Group, InstancedMesh, BoxGeometry, CylinderGeometry, SphereGeometry, TorusGeometry, CircleGeometry,
  OctahedronGeometry, MeshStandardMaterial, MeshBasicNodeMaterial, Matrix4, Quaternion, Vector3, Color,
  InstancedInterleavedBuffer, InterleavedBufferAttribute } from 'three/webgpu';
import { vec3, float, positionGeometry, normalGeometry, positionWorld, normalWorld, cameraPosition, normalize,
  dot, mix, smoothstep, fract, sin, cos, max, min, abs, fwidth, uv, attribute, floor } from 'three/tsl';
import { createSoftMaterial } from './materials/colossus.js';
import { boudoirWorldPlan } from './boudoirMedia.js';
import { Q } from './quality.js';
import { beatOf } from '../audio/chart.js';
import { createSpiralBeatClock } from './spiralBeat.js';

function lipstickGeometry() {
  const geometry = new CylinderGeometry(.5, .5, 1, 20, 1), positions = geometry.attributes.position;
  // A diagonal lipstick cut reads as wax, distinct from the straight metal case beneath it.
  for (let i = 0; i < positions.count; i++) if (positions.getY(i) > 0)
    positions.setY(i, .34 + positions.getX(i) * .3);
  positions.needsUpdate = true; geometry.computeVertexNormals(); return geometry;
}

/** Stable source-local patterns, expressed in metres and filtered before subpixel details disappear. */
function districtColour() {
  const size = attribute('boudoirSize', 'vec3'), finish = attribute('boudoirFinish', 'vec3');
  const p = positionGeometry.mul(size), n = abs(normalGeometry);
  const plane = p.xy.mul(n.z).add(p.zy.mul(n.x)).add(p.xz.mul(n.y));
  const x = plane.x, y = plane.y, footprint = max(fwidth(x), fwidth(y));
  const fine = float(1).sub(smoothstep(.12, .7, footprint));
  const medium = float(1).sub(smoothstep(1, 4, footprint));
  const colour = hex => { const c = new Color(hex); return vec3(c.r, c.g, c.b); };
  const edge = (coord, spacing, width) => {
    const cell = fract(coord.div(spacing)), dist = min(cell, float(1).sub(cell)).mul(spacing);
    return float(1).sub(smoothstep(width, fwidth(coord).max(.03).add(width), dist));
  };
  const grain = sin(x.mul(2.8).add(sin(y.mul(.065)).mul(3)).add(finish.z.mul(9)))
    .mul(.18).add(sin(x.mul(7.5).add(y.mul(.18))).mul(.045)).mul(fine).add(.55);
  const woodSeam = max(edge(x, 18, .1), edge(y, 46, .12)).mul(medium);
  const wood = mix(colour('#452326'), colour('#a6654d'), grain.add(finish.y.mul(.14)))
    .mul(float(1).sub(woodSeam.mul(.62)));

  const tileX = x.add(floor(y.div(4.8)).mod(2).mul(2.4));
  const grout = max(edge(tileX, 4.8, .08), edge(y, 4.8, .08)).mul(medium);
  const tileVariation = fract(sin(floor(tileX.div(4.8)).mul(12.9).add(floor(y.div(4.8)).mul(78.2))).mul(437.5)).mul(.14).add(.86);
  const tileBase = mix(colour('#246e70'), colour('#f0e5d0'), finish.y);
  const tile = mix(tileBase.mul(mix(float(1), tileVariation, medium)), colour('#c6c4b2'), grout.mul(.8));

  const folds = cos(x.mul(1.32).add(sin(y.mul(.075)).mul(.2))).mul(.5).add(.5);
  const velvetBase = mix(colour('#35101f'), colour('#a74463'), folds.mul(.67).add(finish.y.mul(.2)));
  const nap = sin(x.mul(19)).mul(sin(y.mul(24))).mul(.035).mul(fine);
  const stitches = edge(x, 8, .06).mul(smoothstep(.25, .4, fract(y.div(1.4)))).mul(fine);
  const velvet = mix(velvetBase.mul(nap.add(.94)), colour('#c5a060'), stitches.mul(.65));

  const inlay = max(edge(x, 17, .14), edge(y, 34, .14)).mul(medium);
  const stoneVein = smoothstep(.85, .98, sin(x.mul(.22).add(sin(y.mul(.14)).mul(2))))
    .mul(.12).mul(medium);
  const lacquer = mix(colour('#211726').add(vec3(.025, .019, .03).mul(sin(x.mul(.07)).add(1))), colour('#c8a35f'), inlay.mul(.85));
  const ivory = mix(colour('#efe3cc'), colour('#9a9a94'), stoneVein)
    .mul(float(1).sub(max(edge(x, 28, .08), edge(y, 40, .08)).mul(.3).mul(medium)));
  let result = vec3(0);
  for (const [id, surface] of [wood, tile, velvet, lacquer, ivory].entries())
    result = result.add(surface.mul(float(1).sub(smoothstep(.25, .5, abs(finish.x.sub(id))))));
  // A restrained edge bead gives thickness to broad boards and prevents adjacent surfaces merging.
  const u = uv(), pixels = min(min(u.x, float(1).sub(u.x)).div(fwidth(u.x).max(.00001)),
    min(u.y, float(1).sub(u.y)).div(fwidth(u.y).max(.00001)));
  return result.mul(smoothstep(.05, 1.2, pixels).mul(.28).add(.72));
}

export function buildBoudoirDressing(track, art, pal, samples) {
  const plan = boudoirWorldPlan(track), group = new Group(); group.name = 'boudoir-dressing-room';
  if (!plan) return { group, update() {}, dispose() { group.removeFromParent(); }, casters: [] };
  const geometries = {
    box: new BoxGeometry(1, 1, 1), sphere: new SphereGeometry(.5, 16, 10),
    cylinder: new CylinderGeometry(.5, .5, 1, 20), cone: new CylinderGeometry(.22, .5, 1, 20),
    torus: new TorusGeometry(.43, .07, 8, 40), disc: new CircleGeometry(.5, 40), lipstick: lipstickGeometry(),
    sparkle: new OctahedronGeometry(.5),
  };
  const swatches = { cream: '#f0e5d0', rose: '#b55673', lavender: '#79628f', gold: '#dfb56a',
    mirror: '#e4def7', pearl: '#fff4db', lip: '#e72e64', glass: '#baa1cf', floor: '#58314f', velvet: '#47182c', mint: '#4aaca4', district: '#ffffff' };
  // Lacquer, satin, velvet and porcelain need different highlight widths at driver distance.
  const roughness = { cream: .28, rose: .3, lavender: .56, gold: .2, mirror: .16,
    pearl: .24, lip: .23, glass: .18, floor: .72, velvet: .9, mint: .29, district: .48 };
  const materials = {};
  for (const [name, hex] of Object.entries(swatches)) {
    const metal = name === 'gold' || name === 'mirror';
    const source = new MeshStandardMaterial({ color: hex, metalness: metal ? (name === 'gold' ? .85 : .97) : .02,
      roughness: roughness[name] });
    source.name = 'boudoir-' + name;
    const material = createSoftMaterial(source, pal, { haze: .025, fill: .5, rim: vec3(.5, .13, .32),
      rimGain: name === 'velvet' ? .18 : metal ? .42 : .28, rimPow: 3 }).material;
    source.dispose(); materials[name] = material;
    if (['rose', 'lavender', 'cream', 'velvet'].includes(name)) {
      const u = uv(), detailFade = float(1).sub(smoothstep(.005, .04, max(fwidth(u.x), fwidth(u.y))));
      const weave = sin(u.x.mul(115)).mul(sin(u.y.mul(140))).mul(.035).mul(detailFade);
      const folds = cos(u.x.mul(Math.PI * 8).add(sin(u.y.mul(5)).mul(.35)));
      const fold = name === 'velvet' ? folds.mul(.17).add(.83)
        : name === 'lavender' ? folds.mul(.06).add(.94) : float(1);
      material.colorNode = material.colorNode.mul(fold.add(weave));
      // A fine edge separates adjacent lacquer boards without heavy black cartoon hulls.
      const pixels = min(min(u.x, float(1).sub(u.x)).div(fwidth(u.x).max(.00001)),
        min(u.y, float(1).sub(u.y)).div(fwidth(u.y).max(.00001)));
      material.colorNode = mix(material.colorNode.mul(.45), material.colorNode, smoothstep(.1, .8, pixels));
    }
    if (name === 'district') material.colorNode = material.colorNode.mul(districtColour());
    if (name === 'mirror') {
      const V = normalize(cameraPosition.sub(positionWorld)), N = normalize(normalWorld), edge = float(1).sub(abs(dot(N, V)));
      const line = smoothstep(.72, .85, sin(positionWorld.x.mul(.028).add(positionWorld.y.mul(.035))).mul(.5).add(.5));
      material.colorNode = mix(vec3(.2, .15, .31), vec3(.77, .65, .8), smoothstep(.08, .92, edge))
        .add(vec3(.36, .26, .3).mul(line));
    }
    if (name === 'floor') {
      const x = fract(positionWorld.x.div(24)), z = fract(positionWorld.z.div(120));
      const seams = max(float(1).sub(smoothstep(.006, .014, x)), float(1).sub(smoothstep(.003, .006, z)));
      const grain = sin(positionWorld.z.mul(.2).add(sin(positionWorld.x.mul(.38)))).mul(.025).add(.975);
      material.colorNode = material.colorNode.mul(grain).mul(float(1).sub(seams.mul(.5)));
    }
  }
  const M = new Matrix4(), B = new Matrix4(), P = new Vector3(), S = new Vector3(), quaternion = new Quaternion();
  const X = new Vector3(), Y = new Vector3(), Z = new Vector3(), batches = new Map(), moving = [];
  const rotation = new Quaternion(), restQ = new Quaternion(), pivot = new Vector3(), axis = new Vector3();
  for (const row of plan.rows) {
    const key = row.shape + ':' + row.material + (row.motion ? ':moving' : '');
    if (!batches.has(key)) batches.set(key, []);
    batches.get(key).push(row);
  }
  const districtGeometries = [];
  for (const [key, rows] of batches) {
    const [shape, material] = key.split(':');
    let geometry = geometries[shape];
    if (material === 'district') {
      geometry = geometry.clone(); districtGeometries.push(geometry);
      // One extra vertex buffer for both attributes keeps WebGPU within its eight-buffer limit.
      const data = new Float32Array(rows.length * 6);
      rows.forEach((row, i) => { data.set(row.size, i * 6); data.set(row.finish, i * 6 + 3); });
      const buffer = new InstancedInterleavedBuffer(data, 6);
      geometry.setAttribute('boudoirSize', new InterleavedBufferAttribute(buffer, 3, 0));
      geometry.setAttribute('boudoirFinish', new InterleavedBufferAttribute(buffer, 3, 3));
    }
    const mesh = new InstancedMesh(geometry, materials[material], rows.length);
    mesh.name = 'boudoir-' + key.replaceAll(':', '-');
    rows.forEach((row, i) => {
      B.makeBasis(X.fromArray(row.axes[0]), Y.fromArray(row.axes[1]), Z.fromArray(row.axes[2]));
      quaternion.setFromRotationMatrix(B);
      mesh.setMatrixAt(i, M.compose(P.fromArray(row.p), quaternion, S.fromArray(row.size)));
      if (row.motion) moving.push({ row, mesh, index: i, restQ: quaternion.clone(), angle: 0, rise: 0 });
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
    if (key.endsWith(':moving') && mesh.boundingSphere) mesh.boundingSphere.radius += 16;
  }
  // Fixed luminance, drifting glitter. One draw covers the whole room, without point lights.
  const sparkleMaterial = new MeshBasicNodeMaterial({ color: new Color('#ffebc7'), fog: false });
  const count = Math.min(Q.name === 'phone' ? 420 : 1800, plan.particles.length * 7);
  const sparkle = new InstancedMesh(geometries.sparkle, sparkleMaterial, Math.max(1, count));
  sparkle.name = 'boudoir-pearl-sparkles'; sparkle.count = count; group.add(sparkle);
  let time = 0, disposed = false;
  const beat = { index: 0, phase: 0, lenS: .5 }, sparkleBeat = createSpiralBeatClock();
  const emptyBeats = [], grid = { bpm: 99, beats: emptyBeats, downbeatS: 0 };
  function update(view = {}) {
    const { dt = 0, gentle = false, paused = false } = view;
    if (disposed || paused) return;
    time += Math.max(0, Math.min(dt, 1)) * (gentle ? .5 : 1);
    const song = view.beatSong || view.song, musical = song?.bpm > 0 && Number.isFinite(view.songS);
    if (musical) {
      grid.bpm = song.bpm; grid.beats = song.beats || emptyBeats; grid.downbeatS = song.downbeatS || 0;
      beatOf(grid, view.songS, beat);
    }
    const pulse = sparkleBeat.update(dt, view, gentle), blend = 1 - Math.exp(-Math.max(0, Math.min(dt, .1)) / (gentle ? .35 : .12));
    for (const item of moving) {
      const { row, mesh, index } = item, m = row.motion;
      const period = musical ? Math.max(4, Math.round(Math.PI * 2 / (m.speed * beat.lenS) / 4) * 4) : 0;
      const phase = musical ? (beat.index + beat.phase) * Math.PI * 2 / period : time * m.speed;
      const angle = Math.sin(phase + m.phase) * m.amp * (musical && gentle ? .5 : 1);
      const rise = Math.sin(musical ? (beat.index + beat.phase) * Math.PI / 8 + m.phase : time * .6 + m.phase) * m.rise;
      // Ease pickup and seeks into the heard bar phase without a snap in the scenery.
      item.angle = musical ? item.angle + (angle - item.angle) * blend : angle;
      item.rise = musical ? item.rise + (rise - item.rise) * blend : rise;
      rotation.setFromAxisAngle(axis.fromArray(m.axis), item.angle);
      pivot.fromArray(m.pivot); P.fromArray(row.p).sub(pivot).applyQuaternion(rotation).add(pivot);
      P.y += item.rise;
      restQ.copy(rotation).multiply(item.restQ); S.fromArray(row.size);
      mesh.setMatrixAt(index, M.compose(P, restQ, S)); mesh.instanceMatrix.needsUpdate = true;
    }
    for (let i = 0; i < count; i++) {
      const seed = plan.particles[Math.floor(i / 7)], phase = seed.phase + i * 1.618;
      const rise = ((time * 1.2 + phase * 2) % 16 + 16) % 16, radius = .16 + (i % 4) * .07;
      P.fromArray(seed.p); P.x += Math.sin(time * .17 + phase) * 5; P.z += Math.cos(time * .13 + phase) * 5; P.y += rise;
      quaternion.setFromAxisAngle(Y.set(0, 1, 0), time * .35 + phase);
      const fade = Math.min(1, rise / 2, (16 - rise) / 2) * (1 + (pulse - 1) * .28);
      S.set(radius * fade, radius * (i % 3 ? 1 : 3) * fade, radius * fade);
      sparkle.setMatrixAt(i, M.compose(P, quaternion, S));
    }
    sparkle.instanceMatrix.needsUpdate = true;
  }
  update(); sparkle.computeBoundingSphere();
  // The bounds include all drift, so the shared batch never disappears at a camera edge.
  if (sparkle.boundingSphere) sparkle.boundingSphere.radius += 24;
  const stats = { draws: group.children.length, solids: plan.rows.length, mirrors: plan.mounts.length,
    sparkles: count, furniture: plan.heroes.length, interiors: plan.interiors.length, rejected: plan.rejected,
    propClusters: plan.props.clusters.length, movingPieces: moving.length, features: plan.props.features,
    districts: plan.districts.districts, districtStations: plan.districts.stations.length };
  return { group, update, plan, stats, casters: [],
    dispose() {
      if (disposed) return; disposed = true; group.removeFromParent();
      group.traverse(object => { if (object.isInstancedMesh) object.dispose(); });
      Object.values(geometries).forEach(g => g.dispose()); Object.values(materials).forEach(m => m.dispose());
      districtGeometries.forEach(g => g.dispose());
      sparkleMaterial.dispose();
    } };
}
