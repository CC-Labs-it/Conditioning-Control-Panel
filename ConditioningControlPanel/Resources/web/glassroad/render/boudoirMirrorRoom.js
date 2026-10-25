/* Two wall views and one approaching shutter view share a two-capture runtime budget. */
import { Group, Mesh, InstancedMesh, MeshBasicNodeMaterial, BoxGeometry, SphereGeometry, Shape, ShapeGeometry,
  Matrix4, Vector2, Vector3, Quaternion, Color } from 'three/webgpu';
import { reflector, vec3, uniform, mix } from 'three/tsl';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getBoudoirSetpieces, mirrorGatePhase, releasedMirrorGatePhase, BOUDOIR_HAZARD } from '../track/boudoir-setpieces.js';
import { Q } from './quality.js';
import { machineryBeatAt } from '../track/machineryBeat.js';

export const MIRROR_ROOM = Object.freeze({ sideM: 19.5, heightM: 20, facetWidthM: 15.5, facetHeightM: 15,
  nearM: 95, maxTexturePx: 512, maxPasses: 2, captureHz: 15 });
export function mirrorRoomNear(local, lengthM, nearM = MIRROR_ROOM.nearM) {
  return Math.hypot(Math.max(0, Math.abs(local.x) - MIRROR_ROOM.sideM), Math.max(0, local.y - 24, -local.y - 5),
    Math.max(0, local.z, -local.z - lengthM)) <= nearM;
}

function facetGeometry(w, h, cut = 1) {
  const shape = new Shape(), x = w / 2, y = h / 2;
  const points = [[-x + cut, -y], [x - cut, -y], [x, -y + cut], [x, y - cut], [x - cut, y], [-x + cut, y], [-x, y - cut], [-x, -y + cut]];
  shape.moveTo(...points[0]); points.slice(1).forEach(p => shape.lineTo(...p)); shape.closePath(); return new ShapeGeometry(shape);
}

export function buildBoudoirMirrorRoom(track, art, pal, { quality = Q.name } = {}) {
  const group = new Group(); group.name = 'boudoir-mirror-room';
  const hall = getBoudoirLayout(track)?.landmarks.find(p => p.id === 'drawer-hall');
  if (!hall) return { group, update() {}, dispose() {}, diagnostics: { mirrors: 0, passes: 0 } };
  const length = hall.s1 - hall.s0, f = track.frameAt(hall.s0), basis = new Matrix4();
  basis.makeBasis(new Vector3(...f.b), new Vector3(...f.n), new Vector3(...f.t).negate());
  group.position.fromArray(f.p); group.quaternion.setFromRotationMatrix(basis); group.updateMatrixWorld(true);
  const geometries = [new BoxGeometry(1, 1, 1), new SphereGeometry(.5, 8, 6), facetGeometry(15.5, 15, 1.3), facetGeometry(6.5, 5.6, .65)];
  const [box, pearl, face, shutterFace] = geometries, materials = [], mirrorMaterials = [], shutterMaterials = [], reflections = [], gates = [];
  const rows = { ink: [], gold: [], rose: [], pearl: [], teal: [] }, M = new Matrix4(), P = new Vector3(), S = new Vector3(), R = new Quaternion(), Y = new Vector3(0, 1, 0);
  const material = (name, color) => { const m = new MeshBasicNodeMaterial({ color }); m.name = name; materials.push(m); return m; };
  const mats = { ink: material('mirror-plum-outline', '#321e40'), gold: material('mirror-gold-carving', '#e6b563'),
    rose: material('mirror-rose-enamel', '#bf376e'), pearl: material('mirror-pearl', '#f5dfc4'), teal: material('mirror-teal-inlay', '#3e9f9f') };
  const backingMat = material('mirror-backplate', '#321e40');
  function block(kind, x, y, z, w, h, d, rz = 0) { rows[kind].push({ x, y, z, w, h, d, rz }); }
  function framedPanel(mesh, x, y, z, yaw, w, h, back = .25) {
    const frame = new Group(); frame.position.set(x, y, z); frame.rotation.y = yaw; group.add(frame);
    const backing = new Mesh(box, backingMat); backing.scale.set(w + .8, h + .8, back); backing.position.z = -back / 2 - .04; frame.add(backing);
    mesh.position.z = .04; frame.add(mesh);
    for (const side of [-1, 1]) {
      const vertical = new Mesh(box, mats.gold); vertical.position.set(side * (w / 2 + .22), 0, .1); vertical.scale.set(.24, h + .65, .24); frame.add(vertical);
      const horizontal = new Mesh(box, mats.gold); horizontal.position.set(0, side * (h / 2 + .22), .1); horizontal.scale.set(w + .65, .24, .24); frame.add(horizontal);
      const enamel = new Mesh(box, mats.rose); enamel.position.set(side * (w / 2 + .52), 0, -.04); enamel.scale.set(.18, h + 1.15, .3); frame.add(enamel);
    }
    for (const sy of [-1, 1]) for (const sx of [-1, 1]) {
      const gem = new Mesh(pearl, mats.pearl); gem.position.set(sx * (w / 2 - .5), sy * (h / 2 - .5), .18); gem.scale.set(.55, .55, .3); frame.add(gem);
    }
    return frame;
  }
  let started = false, frozen = false, enabled = true, elapsed = 0, gentleOn = false, disposed = false, warming = false, frontStation = null;
  const gateRows = getBoudoirSetpieces(track).mirrorGates;
  const localCamera = new Vector3(), drawSize = new Vector2(), gateColor = new Color(), batches = [], diagnostics = { mirrors: 0, shutters: 0, reflectionNodes: 3,
    reflectionPasses: 0, active: true, paused: false, maxTexturePx: quality === 'phone' ? 384 : MIRROR_ROOM.maxTexturePx, targets: [] };
  for (const side of [-1, 1, 0]) {
    const target = new Group(); target.name = 'mirror-plane:' + side;
    target.position.set(side * MIRROR_ROOM.sideM, 0, side ? 0 : -(gateRows[0].s - hall.s0) + .04);
    target.rotation.y = side ? side < 0 ? Math.PI / 2 : -Math.PI / 2 : 0; group.add(target);
    const node = reflector({ target, resolutionScale: quality === 'phone' ? .25 : .5, bounces: false, samples: 0 });
    const mat = material((side ? 'mirror-live-scene:' : 'mirror-prismatic-shutter:') + side, '#ffffff');
    mat.colorNode = node.rgb.mul(.96).add(vec3(.012, .008, .016));
    (side ? mirrorMaterials : shutterMaterials).push(mat);
    if (!side) {
      for (let station = 0; station < 3; station++) {
        const pane = station ? material('mirror-prismatic-shutter:' + station, '#ffffff') : mat;
        pane.userData.reflectionMix = uniform(0);
        pane.colorNode = mix(vec3(.21, .16, .25), node.rgb.mul(.96).add(vec3(.012, .008, .016)), pane.userData.reflectionMix);
        if (station) shutterMaterials.push(pane);
      }
    }
    const base = node.reflector, nativeUpdate = base.updateBefore.bind(base); let lastCapture = -Infinity;
    // Cap actual target dimensions as well as scale, including high-DPI desktop windows.
    base._updateResolution = (renderTarget, renderer) => {
      renderer.getDrawingBufferSize(drawSize); const scale = Math.min(base.resolutionScale, diagnostics.maxTexturePx / Math.max(drawSize.x, drawSize.y));
      renderTarget.setSize(Math.max(1, Math.round(drawSize.x * scale)), Math.max(1, Math.round(drawSize.y * scale)));
    };
    base.updateBefore = frame => {
      // Pipeline discovery must never recurse into a real capture. General boot warming also
      // precedes the first world update; the explicit hall warm owns its reflected variants.
      if (disposed || frame.renderer._isPreCompiling || (!started && !warming)
        || (!warming && started && (!enabled || frozen || (side ? frontStation !== null : frontStation === null)
          || elapsed - lastCapture < 1 / (gentleOn ? 12 : MIRROR_ROOM.captureHz)))) return;
      const excluded = [...mirrorMaterials, ...shutterMaterials, backingMat], saved = excluded.map(m => m.visible), renderer = frame.renderer;
      const renderTarget = renderer.getRenderTarget(), mrt = renderer.getMRT(), autoClear = renderer.autoClear;
      // Both mirror banks and all shutter glass leave the reflected pass. Frames and racers remain.
      excluded.forEach(m => { m.visible = false; });
      try { nativeUpdate(frame); if (base.hasOutput) diagnostics.reflectionPasses++; lastCapture = elapsed; }
      finally { excluded.forEach((m, i) => { m.visible = saved[i]; }); renderer.setMRT(mrt); renderer.setRenderTarget(renderTarget); renderer.autoClear = autoClear; }
    };
    reflections.push(node);
    if (!side) continue;
    const count = Math.floor((length - 10) / 17), gap = (length - 10) / count;
    for (let i = 0; i < count; i++) {
      const z = -(5 + gap * (i + .5)), pane = new Mesh(face, mat); pane.name = 'live-mirror:' + side + ':' + i;
      framedPanel(pane, side * MIRROR_ROOM.sideM, 9.5, z, target.rotation.y, 15.5, 15);
      diagnostics.mirrors++;
      // The crown is a prismatic inlay above each mirror, with repeated pearls down the hall.
      for (let j = 0; j < 5; j++) block(j % 2 ? 'rose' : 'gold', side * 19.9, 18 + Math.sin(j * Math.PI / 4) * 1.2, z + (j - 2) * 1.2, .5, .45, 1.1);
    }
  }
  const ceiling = facetGeometry(10, 13.5, 1.5); geometries.push(ceiling);
  for (let z = -10; z > -length; z -= 18) {
    block('ink', 0, 20.4, z, 43, .55, .9); block('gold', 0, 20, z, 41.5, .2, .26);
    for (const x of [-13, 0, 13]) {
      const pane = new Mesh(ceiling, mirrorMaterials[x > 0 ? 1 : 0]); pane.name = 'ceiling-mirror-facet';
      pane.position.set(x, 20.15, z - 7); pane.rotation.x = Math.PI / 2; pane.rotation.z = x ? Math.sign(x) * .12 : 0; group.add(pane);
      block('rose', x, 20.4, z - 7, 10.8, .3, 14.3);
      for (const edge of [-1, 1]) block('gold', x + edge * 5.25, 20.05, z - 7, .16, .2, 14);
    }
    for (const x of [-18, 18]) { block('ink', x, 1, z, .65, 2, .65); block('pearl', x, 2.35, z, .7, .7, .7); }
  }
  for (const z of [-1, -length + 1]) {
    for (const x of [-18, 18]) { block('ink', x, 10, z, 1.6, 20, 2); block('gold', x, 10, z, .5, 19.8, 2.1); }
    block('ink', 0, 21, z, 39, 2, 2); block('rose', 0, 20.75, z + .2, 36, .8, 2.1);
    block('gold', 0, 19.9, z + .3, 36, .16, 2.15);
  }
  for (const row of gateRows) {
    const pane = new Mesh(shutterFace, shutterMaterials[row.station]); pane.name = 'mirror-shutter-glass:' + row.id;
    const holder = framedPanel(pane, row.x, BOUDOIR_HAZARD.mirrorHeightM / 2, -(row.s - hall.s0), 0, 6.5, 5.6, row.depthM);
    holder.name = 'mirror-shutter:' + row.id;
    const lampMat = material('mirror-lane-signal:' + row.id, '#76d7b4'), lamp = new Mesh(box, lampMat);
    lamp.position.set(row.x, 15.9, -(row.s - hall.s0)); lamp.scale.set(row.w - .8, .35, .3); group.add(lamp);
    const arrow = new Group(); arrow.position.set(row.x, .11, -(row.s - hall.s0) + 9); group.add(arrow);
    for (const side of [-1, 1]) { const bar = new Mesh(box, lampMat); bar.position.x = side * .55; bar.rotation.y = side * -.65; bar.scale.set(.25, .08, 1.7); arrow.add(bar); }
    gates.push({ row, holder, lampMat });
  }
  diagnostics.shutters = gates.length;
  // Architecture stays batched; the small number of moving shutters retain their own transforms.
  for (const [kind, list] of Object.entries(rows)) {
    if (!list.length) continue;
    const mesh = new InstancedMesh(kind === 'pearl' ? pearl : box, mats[kind], list.length); mesh.name = 'mirror-hall:' + kind;
    list.forEach((r, i) => { R.setFromAxisAngle(Y, r.rz); mesh.setMatrixAt(i, M.compose(P.set(r.x, r.y, r.z), R, S.set(r.w, r.h, r.d))); });
    mesh.computeBoundingSphere(); group.add(mesh);
  }
  group.updateMatrixWorld(true);
  function update(dt, view = {}, gentle = false) {
    frozen = !!view.paused; gentleOn = gentle; started = true;
    diagnostics.paused = frozen;
    if (view.camera?.position) { group.worldToLocal(localCamera.copy(view.camera.position)); enabled = mirrorRoomNear(localCamera, length); }
    diagnostics.active = enabled;
    if (frozen || disposed) return;
    // All three panes at a station share one plane. The approaching glass reflects the
    // actual kart and world, including while it lifts to release a bumped driver.
    const next = gateRows.find(row => row.lane === 0 && localCamera.z + row.s - hall.s0 >= 0
      && localCamera.z + row.s - hall.s0 < 48);
    frontStation = enabled && next ? next.station : null;
    diagnostics.frontStation = frontStation;
    shutterMaterials.forEach((mat, station) => {
      const target = station === frontStation ? 1 : 0, u = mat.userData.reflectionMix;
      u.value += (target - u.value) * Math.min(1, Math.max(0, dt || 0) / .35);
    });
    if (next) group.getObjectByName('mirror-plane:0').position.z = -(next.s - hall.s0) + .04;
    elapsed += Math.max(0, Math.min(.1, dt || 0));
    const timeS = view.raceTimeS ?? view.timeS ?? 0;
    for (const gate of gates) {
      const phase = releasedMirrorGatePhase(track, gate.row, timeS, machineryBeatAt(track,timeS)); gate.holder.position.y = phase.liftM + BOUDOIR_HAZARD.mirrorHeightM / 2;
      gateColor.set(phase.liftM >= 6.8 ? '#76d7b4' : phase.phase === 'warn' ? '#e6b563' : '#9b4177');
      gate.lampMat.color.lerp(gateColor, Math.min(1, (dt || 0) / .35));
    }
    updateBatches();
    diagnostics.targets = reflections.flatMap(node => [...node.reflector.renderTargets.values()].map(rt => ({ width: rt.width, height: rt.height })));
  }
  // Initial static poses also exist behind the loading card.
  for (const gate of gates) gate.holder.position.y = mirrorGatePhase(gate.row, 0).liftM + BOUDOIR_HAZARD.mirrorHeightM / 2;
  // Collapse all wall frames, pearls, glass and moving shutter details by geometry/material.
  // Empty shutter transforms remain as owners; only their instance matrices change each tick.
  group.updateMatrixWorld(true);
  const inverseRoom = group.matrixWorld.clone().invert(), collect = [], byMaterial = new Map(), temp = new Matrix4();
  group.traverse(o => { if (o.isMesh && !o.isInstancedMesh) collect.push(o); });
  for (const mesh of collect) {
    let owner = mesh.parent;
    while (owner && owner !== group && !owner.name.startsWith('mirror-shutter:')) owner = owner.parent;
    if (owner === group) owner = null;
    const local = owner ? owner.matrixWorld.clone().invert().multiply(mesh.matrixWorld) : inverseRoom.clone().multiply(mesh.matrixWorld);
    const key = mesh.geometry.id + ':' + mesh.material.id;
    if (!byMaterial.has(key)) byMaterial.set(key, { geometry: mesh.geometry, material: mesh.material, rows: [] });
    byMaterial.get(key).rows.push({ owner, local }); mesh.removeFromParent();
  }
  for (const batch of byMaterial.values()) {
    batch.mesh = new InstancedMesh(batch.geometry, batch.material, batch.rows.length);
    batch.mesh.name = 'mirror-batch:' + batch.material.name + ':' + batch.geometry.id;
    batch.dynamic = batch.rows.some(r => r.owner); group.add(batch.mesh); batches.push(batch);
  }
  function updateBatches(first = false) {
    group.updateMatrixWorld(true);
    for (const batch of batches) {
      if (!first && !batch.dynamic) continue;
      for (const [i, row] of batch.rows.entries()) {
        const matrix = row.owner ? temp.multiplyMatrices(inverseRoom, row.owner.matrixWorld).multiply(row.local) : row.local;
        batch.mesh.setMatrixAt(i, matrix);
      }
      batch.mesh.instanceMatrix.needsUpdate = true; batch.mesh.computeBoundingSphere();
    }
  }
  updateBatches(true);
  diagnostics.baseDraws = batches.length + Object.values(rows).filter(r => r.length).length;
  async function warmCapture(renderer, scene, camera) {
    if (disposed) return;
    const quaternion = camera.quaternion.clone(), up = camera.up.clone(), raw = renderer.three;
    const renderTarget = raw.getRenderTarget(), mrt = raw.getMRT(), autoClear = raw.autoClear;
    const pass = renderer.post(scene, camera), look = new Vector3();
    const front = group.getObjectByName('mirror-plane:0'), frontZ = front.position.z;
    const warmCamera = group.worldToLocal(camera.position.clone());
    const warmStation = gateRows.find(row => row.lane === 0 && warmCamera.z + row.s - hall.s0 > 0);
    if (warmStation) front.position.z = -(warmStation.s - hall.s0) + .04;
    // Use the live camera and restored runtime visibility. The grouped boot warm uses
    // a different visibility/shadow state and cannot cover these reflected target programs.
    warming = true;
    try {
      for (const side of [0, -1, 1]) {
        if (side) camera.lookAt(group.localToWorld(look.set(side * MIRROR_ROOM.sideM, 8, -length / 2)));
        camera.updateMatrixWorld(true);
        const saved = [], hiddenMaterials = new Set();
        try {
          // The two targets share the live camera. One unculled real capture discovers hidden,
          // distant and empty pooled variants in their actual reflected post/shadow context.
          if (!side) scene.traverse(object => {
            saved.push([object, object.visible, object.frustumCulled, object.count]);
            object.visible = true; object.frustumCulled = false;
            if (object.count === 0 && (object.isInstancedMesh || object.isSprite || object.isBatchedMesh)) object.count = 1;
            for (const material of [].concat(object.material || [])) if (material?.visible === false) {
              hiddenMaterials.add(material); material.visible = true;
            }
          });
          scene.updateMatrixWorld(true);
          await pass.compileAsync();
          if (!side) {
            // The post quad enters depth 1, its scene depth 2, and each real mirror
            // depth 3. Prepare those target/camera variants asynchronously before
            // the first capture would otherwise link their programs synchronously.
            const excluded = [...mirrorMaterials, ...shutterMaterials, backingMat];
            const visible = excluded.map(material => material.visible);
            const previousTarget = raw.getRenderTarget(), previousMrt = raw.getMRT();
            try {
              excluded.forEach(material => { material.visible = false; });
              raw.setMRT(null);
              for (const reflection of reflections) {
                const base = reflection.reflector, reflectedCamera = base.getVirtualCamera(camera);
                const target = base.getRenderTarget(reflectedCamera);
                base._updateResolution(target, raw);
                raw.setRenderTarget(target);
                await raw.compileAsync(scene, reflectedCamera, null, null, raw._callDepth + 3);
              }
            } finally {
              excluded.forEach((material, i) => { material.visible = visible[i]; });
              raw.setMRT(previousMrt); raw.setRenderTarget(previousTarget);
            }
          }
          // Reflectors update once per renderer frame. Capture at the real nested post depth.
          await new Promise(resolve => globalThis.requestAnimationFrame ? requestAnimationFrame(resolve) : setTimeout(resolve, 0));
          pass.render();
        } finally {
          for (const [object, visible, culled, count] of saved) {
            object.visible = visible; object.frustumCulled = culled; if (object.count !== count) object.count = count;
          }
          for (const material of hiddenMaterials) material.visible = false;
          scene.updateMatrixWorld(true);
        }
        // The remaining two captures use normal visibility, clearing warm-only ghosts.
        await new Promise(resolve => setTimeout(resolve, 0));
      }
    } finally {
      warming = false; front.position.z = frontZ; group.updateMatrixWorld(true);
      camera.quaternion.copy(quaternion); camera.up.copy(up); camera.updateMatrixWorld(true);
      raw.setMRT(mrt); raw.setRenderTarget(renderTarget); raw.autoClear = autoClear;
    }
  }
  return { group, update, warmCapture, diagnostics, reflections,
    dispose() { if (disposed) return; disposed = true; group.removeFromParent();
      group.traverse(o => { if (o.isInstancedMesh) o.dispose(); }); reflections.forEach(n => n.dispose());
      geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); } };
}
