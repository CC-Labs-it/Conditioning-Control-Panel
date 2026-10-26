import { Group, Mesh, InstancedMesh, Color, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Vector3 } from 'three/webgpu';
import { movingPoseAt, bridgePose, bridgeSlabX } from '../track/movingHazards.js';
import { movingHazardColours, movingHazardFinish } from './movingHazardFinish.js';

export function createMovingHazardView(scene, track, pal) {
  const group = new Group(); group.name = 'moving-piston-traps'; scene.add(group);
  const colours = movingHazardColours(track.id, pal);
  const box = new BoxGeometry(1, 1, 1), materials = [
    movingHazardFinish(colours.paint),
    movingHazardFinish(colours.dark, 'rubber'),
    movingHazardFinish(colours.metal, 'metal'),
    new MeshBasicNodeMaterial({ color: colours.accent }),
    new MeshBasicNodeMaterial({ color: '#ffffff' }),
    movingHazardFinish(colours.pale),
  ];
  const part = (parent, name, scale, pos, material = 0) => {
    const m = new Mesh(box, materials[material]); m.name = name; m.scale.set(...scale); m.position.set(...pos); parent.add(m); return m;
  };
  const frameGroup = (f, name) => {
    const g = new Group(); g.name = name;
    const basis = new Matrix4().makeBasis(new Vector3(...f.b), new Vector3(...f.n), new Vector3(...f.t).negate());
    g.quaternion.setFromRotationMatrix(basis); g.position.set(...f.p); group.add(g); return g;
  };
  const traps = (track.moving?.traps || []).map(row => {
    const root = frameGroup(row, row.id), anchor = row.side * (row.roadWidth / 2 + 1);
    part(root, 'piston-housing', [2.5, 4, row.len + 1], [anchor, 1.8, 0], 1);
    const head = new Group(); root.add(head);
    const slider = row.kind === 'slider';
    part(head, slider ? 'sliding-block' : 'crusher-head', [row.w, 3.2, row.len], [0, 1.6, 0], slider ? 5 : 0);
    // Recesses, seals and fasteners stay within the physical head footprint.
    for (const z of [-1, 1]) {
      for (const y of [.24, 2.96]) part(head, 'face-rim', [row.w - .16, .16, .12], [0, y, z * (row.len / 2 - .04)], 2);
      for (const x of [-1, 1]) {
        part(head, 'rubber-face-seal', [.14, 2.52, .10], [x * (row.w / 2 - .16), 1.6, z * (row.len / 2 - .035)], 1);
        for (const y of [.45, 2.75]) part(head, 'recessed-fastener', [.18, .18, .06], [x * (row.w / 2 - .39), y, z * (row.len / 2 - .025)], 2);
      }
      for (const x of [-1, 0, 1]) {
        const stripe = part(head, 'warning-inlay', [.32, 2.1, .06], [x * row.w * .27, 1.6, z * (row.len / 2 - .025)], 3);
        stripe.rotation.z = row.side * -.27;
      }
    }
    for (let i = 0; i < 4; i++) part(root, 'housing-vent', [2.1, .12, .10], [anchor, .9 + i * .45, -row.len / 2 - .51], 2);
    const rod = part(root, 'connected-piston-shaft', [1, .65, .65], [anchor, 1.6, 0], 2);
    const light = part(root, 'approach-warning', [row.w, .08, 1.3], [row.x - row.side * row.travelM, .065, -row.len / 2 - 5], 3);
    return { row, anchor, head, rod, light };
  });
  const bridge = track.moving?.bridge, fork = track.forks?.find(f => f.id === bridge?.forkId), slabs = [];
  let light = null;
  if (fork?.movingBridge) {
    for (let i = 0; i < bridge.slabs; i++) {
      const f = fork.frameAt(bridge.fromM + (i + .5) * bridge.slabM), root = frameGroup(f, 'bridge-piston-' + i), side = i % 2 ? -1 : 1;
      part(root, 'bridge-housing', [2, 1.5, bridge.slabM], [side * (f.w + 1), -1.1, 0], 1);
      const slab = new Group(); root.add(slab);
      part(slab, 'forming-road', [f.w + .06, .7, bridge.slabM + .16], [0, -.35, 0]);
      for (const x of [-1, 1]) part(slab, 'bridge-edge', [.16, .12, bridge.slabM + .16], [x * (f.w / 2 - .2), .035, 0], 3);
      const rod = part(root, 'bridge-piston-rod', [1, .35, .35], [0, -.65, 0], 2);
      slabs.push({ slab, rod, side, width: f.w, i });
    }
    const entry = frameGroup(fork.frameAt(bridge.fromM - 9), 'bridge-timer');
    light = part(entry, 'bridge-clearance-signal', [5, .35, .5], [0, 4, 0], 4);
    for (const x of [-1, 1]) part(entry, 'bridge-signal-post', [.3, 4, .3], [x * 3.4, 2, 0], 1);
  }
  // All machinery shares one cube and a handful of instanced draws, including moving parts.
  const green = new Color('#62db94'), amber = new Color('#e6b464');
  const batches = materials.map((material, index) => {
    const sources = []; group.traverse(o => { if (o.isMesh && o.geometry === box && o.material === material) sources.push(o); });
    if (!sources.length) return null;
    const batch = new InstancedMesh(box, material, sources.length); batch.frustumCulled = false; group.add(batch);
    for (const [i, source] of sources.entries()) {
      source.visible = false;
      if (index === 4) batch.setColorAt(i, green);
    }
    return { batch, sources, index };
  }).filter(Boolean);
  function update(timeS = 0) {
    for (const item of traps) {
      const p = movingPoseAt(track, item.row, timeS), headEdge = p.x + item.row.side * item.row.w / 2;
      item.head.position.x = p.x; item.rod.position.x = (item.anchor + headEdge) / 2;
      item.rod.scale.x = Math.max(.1, Math.abs(item.anchor - headEdge));
      // Permanent inlays show the strike zone without introducing beat-rate light flashes.
    }
    for (const item of slabs) {
      const x = bridgeSlabX(bridge, timeS, item.i, item.width);
      item.slab.position.x = x;
      const anchor = item.side * (item.width + 1), end = x + item.side * item.width / 2;
      item.rod.position.x = (anchor + end) / 2; item.rod.scale.x = Math.max(.1, Math.abs(anchor - end));
    }
    for (const { batch, sources, index } of batches) {
      for (const [i, source] of sources.entries()) {
        source.updateWorldMatrix(true, false); batch.setMatrixAt(i, source.matrixWorld);
        if (index === 4) batch.setColorAt(i, bridgePose(bridge, timeS).remainingS > 3 ? green : amber);
      }
      batch.instanceMatrix.needsUpdate = true;
      if (batch.instanceColor) batch.instanceColor.needsUpdate = true;
    }
  }
  update(0);
  return { group, update, inspect: () => ({ traps: traps.length, slabs: slabs.length, draws: batches.length, colours }),
    dispose() { group.removeFromParent(); for (const { batch } of batches) batch.dispose(); box.dispose(); for (const material of materials) material.dispose(); } };
}
