import { Group, Mesh, InstancedMesh, Color, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Vector3 } from 'three/webgpu';
import { movingPoseAt, bridgePose, bridgeSlabX } from '../track/movingHazards.js';
import { movingHazardColours, movingHazardFinish } from './movingHazardFinish.js';
import { chessTrap, chessWobble } from './chessTrap.js';
import { matrixInto } from './instanceWrite.js';
import { telegraphKey, TELEGRAPH } from './trapTelegraph.js';

export function createMovingHazardView(scene, track, pal, art = null) {
  const group = new Group(); group.name = 'moving-piston-traps'; scene.add(group);
  const colours = movingHazardColours(track.id, pal);
  const box = new BoxGeometry(1, 1, 1), materials = [
    movingHazardFinish(colours.paint),
    movingHazardFinish(colours.dark, 'rubber'),
    movingHazardFinish(colours.metal, 'metal'),
    new MeshBasicNodeMaterial({ color: '#ffffff' }),   // the accent rides on instance colours: the telegraph recolours per trap
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
  const traps = (track.moving?.traps || []).map((row, index) => {
    const root = frameGroup(row, row.id), anchor = row.side * (row.roadWidth / 2 + 1);
    part(root, 'piston-housing', [2.5, 4, row.len + 1], [anchor, 1.8, 0], 1);
    const head = new Group(); root.add(head);
    const slider = row.kind === 'slider';
    const sculpture = chessTrap(art, index, [row.w, 3.2, row.len], row.side);
    if (sculpture) head.add(sculpture);
    if (!sculpture) {
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
        stripe.rotation.z = row.side * -.27; stripe.userData.telegraph = index;
      }
    }
    }
    for (let i = 0; i < 4; i++) part(root, 'housing-vent', [2.1, .12, .10], [anchor, .9 + i * .45, -row.len / 2 - .51], 2);
    const rod = part(root, 'connected-piston-shaft', [1, .65, .65], [anchor, 1.6, 0], 2);
    const light = part(root, 'approach-warning', [row.w, .08, 1.3], [row.x - row.side * row.travelM, .065, -row.len / 2 - 5], 3);
    light.userData.telegraph = index;
    return { row, anchor, head, rod, light, sculpture };
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
  // Housings, rods and bridges share cube batches; chess sculptures borrow the art lease.
  const green = new Color('#62db94'), amber = new Color('#e6b464'), accent = new Color(colours.accent), tint = new Color();
  const batches = materials.map((material, index) => {
    const sources = []; group.traverse(o => { if (o.isMesh && o.geometry === box && o.material === material) sources.push(o); });
    if (!sources.length) return null;
    const batch = new InstancedMesh(box, material, sources.length); batch.frustumCulled = false; group.add(batch);
    for (const [i, source] of sources.entries()) {
      source.visible = false;
      if (index === 4) batch.setColorAt(i, green);
      if (index === 3) batch.setColorAt(i, accent);
    }
    return { batch, sources, index };
  }).filter(Boolean);
  // Only the heads, slabs and rods move, and only along their local x (position, and a rod's length). A mover
  // whose x and length are unchanged since it was last written is skipped whole: traps hold fully out and fully
  // in for long stretches. A moved one refreshes its own subtree once (its frame root is fixed) and rewrites
  // just the instances under it. Housings, vents and signals are written when the whole group is primed (the
  // first update, or whenever the group's own world matrix changes).
  const movers = [...traps.flatMap(t => [t.head, t.rod]), ...slabs.flatMap(s => [s.slab, s.rod])]
    .map(node => ({ node, x: NaN, sx: NaN, entries: [] }));
  const moverOf = new Map(movers.map(m => [m.node, m])), groupWorld = new Matrix4();
  const moverAbove = o => { for (let x = o; x && x !== group; x = x.parent) if (moverOf.has(x)) return moverOf.get(x); return null; };
  for (const b of batches) {
    b.sources.forEach((source, i) => { const m = moverAbove(source); if (m) m.entries.push(b, i); });
    b.signal = null;
  }
  // Telegraph (render/trapTelegraph.js): each trap's inlays and floor lamp take one colour key; rewritten only on a change.
  const telegraph = traps.map(() => ({ key: 'calm', slots: [] })), accentBatch = batches.find(b => b.index === 3) || null;
  accentBatch?.sources.forEach((s, i) => { const t = s.userData.telegraph; if (t !== undefined) telegraph[t].slots.push(i); });
  let primed = false;
  function update(timeS = 0, { gentle = false, paused = false } = {}) {
    if (paused) return;
    for (const [index, item] of traps.entries()) {
      const p = movingPoseAt(track, item.row, timeS), headEdge = p.x + item.row.side * item.row.w / 2;
      if (item.sculpture) {
        if (timeS < item.lastTime || timeS - item.lastTime > .25) item.settleS = undefined;
        else if (item.extension !== undefined && item.extension !== p.extension && (p.extension === 0 || p.extension === 1)) item.settleS = timeS;
        item.sculpture.rotation.x = chessWobble(timeS - (item.settleS ?? Infinity), gentle);
        item.sculpture.updateMatrix();
        item.extension = p.extension; item.lastTime = timeS;
      }
      item.head.position.x = p.x; item.rod.position.x = (item.anchor + headEdge) / 2;
      item.rod.scale.x = Math.max(.1, Math.abs(item.anchor - headEdge));
      const key = telegraphKey(p, gentle);
      if (accentBatch && key !== telegraph[index].key) {
        telegraph[index].key = key;
        if (key === 'dim') tint.copy(accent).multiplyScalar(.35); else tint.set(TELEGRAPH[key] || colours.accent);
        for (const slot of telegraph[index].slots) accentBatch.batch.setColorAt(slot, tint);
        accentBatch.batch.instanceColor.needsUpdate = true;
      }
    }
    for (const item of slabs) {
      const x = bridgeSlabX(bridge, timeS, item.i, item.width);
      item.slab.position.x = x;
      const anchor = item.side * (item.width + 1), end = x + item.side * item.width / 2;
      item.rod.position.x = (anchor + end) / 2; item.rod.scale.x = Math.max(.1, Math.abs(anchor - end));
    }
    group.updateWorldMatrix(true, false);
    const full = !primed || !groupWorld.equals(group.matrixWorld);
    if (full) {
      group.updateWorldMatrix(false, true); groupWorld.copy(group.matrixWorld); primed = true;
      for (const { batch, sources } of batches) {
        const array = batch.instanceMatrix.array;
        for (let i = 0; i < sources.length; i++) matrixInto(array, i, sources[i].matrixWorld);
        batch.instanceMatrix.needsUpdate = true;
      }
    }
    for (const m of movers) {
      const node = m.node;
      if (!full && node.position.x === m.x && node.scale.x === m.sx) continue;
      m.x = node.position.x; m.sx = node.scale.x;
      if (full) continue;
      node.updateWorldMatrix(false, true);
      for (let k = 0; k < m.entries.length; k += 2) {
        const b = m.entries[k], i = m.entries[k + 1];
        matrixInto(b.batch.instanceMatrix.array, i, b.sources[i].matrixWorld); b.batch.instanceMatrix.needsUpdate = true;
      }
    }
    const clear = bridge && bridgePose(bridge, timeS).remainingS > 3;
    for (const b of batches) {
      if (b.index === 4 && b.signal !== clear) {
        for (let i = 0; i < b.sources.length; i++) b.batch.setColorAt(i, clear ? green : amber);
        b.signal = clear; b.batch.instanceColor.needsUpdate = true;
      }
    }
  }
  update(0);
  return { group, update, inspect: () => ({ traps: traps.length, slabs: slabs.length, draws: batches.length, colours, telegraph: telegraph.map(t => t.key) }),
    dispose() { group.removeFromParent(); for (const { batch } of batches) batch.dispose(); box.dispose(); for (const material of materials) material.dispose(); } };
}
