/* Soft dream clouds, fractured pixel banks and bright padlocks above the Keep. */
import { Group, InstancedMesh, SphereGeometry, BoxGeometry, TorusGeometry, CylinderGeometry,
  MeshBasicNodeMaterial, Object3D, Matrix4, Color } from 'three/webgpu';
import { vec3, color, materialColor, positionGeometry, normalWorld, normalView, positionView,
  normalize, dot, clamp, mix, sin, smoothstep, fwidth } from 'three/tsl';

const TAU = Math.PI * 2;
export const KEEP_DREAM_COLOURS = Object.freeze(['#ffd361', '#64ffe4', '#ff72bb', '#c8a6ff']);

/** All decorations live above the highest deck, including their complete motion envelope. */
export function keepAtmosphereLayout(track, { phone = false } = {}) {
  const clouds = [], pixels = [], locks = [];
  if (track.id !== 'keep') return { clouds, pixels, locks };
  let seed = 261026;
  const rnd = () => ((seed = Math.imul(seed, 1664525) + 1013904223 >>> 0) / 4294967296);
  let roof = -Infinity;
  for (let s = 0; s < track.lengthM; s += 10) roof = Math.max(roof, track.frameAt(s).p[1]);
  const count = phone ? 28 : 56;
  for (let i = 0; i < count; i++) {
    const f = track.frameAt((i + .35) / count * track.lengthM), side = i % 2 ? -1 : 1;
    const offset = side * (f.w / 2 + 50 + rnd() * 100);
    const p = [f.p[0] + f.b[0] * offset, roof + 100 + rnd() * 90, f.p[2] + f.b[2] * offset];
    const radius = 15 + rnd() * 13, phase = rnd() * TAU;
    for (let j = 0; j < 4; j++) clouds.push({
      p: [p[0] + (j - 1.5) * radius * .8, p[1] + (j % 2) * radius * .18, p[2] + (rnd() - .5) * radius],
      size: [radius * (1 + rnd() * .55), radius * .43, radius * .78], phase, colour: i % 4,
    });
    for (let j = 0; j < 5; j++) {
      const cell = 4 + rnd() * 4;
      pixels.push({ p: [p[0] + (j - 2) * 9, p[1] - 8 + Math.round(rnd() * 2) * 4, p[2] + Math.round(rnd() * 3) * 7],
        size: [cell * 1.5, cell * .55, cell], phase, colour: i % 4 });
    }
    const lockSide = side * (f.w / 2 + 28 + rnd() * 40);
    locks.push({ p: [f.p[0] + f.b[0] * lockSide, roof + 38 + rnd() * 44, f.p[2] + f.b[2] * lockSide],
      r: 4 + rnd() * 3.5, phase, yaw: rnd() * TAU, colour: i % 4 });
  }
  return { clouds, pixels, locks, roof };
}

function metal() {
  const m = new MeshBasicNodeMaterial({ color: '#ffffff' });
  const p = positionGeometry;
  const light = clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .85, .3))).mul(.3).add(.7), .4, 1);
  const brushPhase = p.y.mul(145).add(sin(p.x.mul(41)).mul(.22));
  const brushFilter = smoothstep(.35, 1.2, fwidth(brushPhase)).oneMinus();
  const brush = sin(brushPhase).mul(brushFilter).mul(.035).add(.965);
  const seam = smoothstep(.32, .35, p.y.abs()).mul(.12);
  m.colorNode = materialColor.rgb.mul(light).mul(brush).mul(seam.oneMinus());
  return m;
}

export function createKeepAtmosphere(track, options = {}) {
  const layout = keepAtmosphereLayout(track, options), object = new Group(); object.name = 'keep-dream-atmosphere';
  const cloudGeo = new SphereGeometry(1, 16, 10), pixelGeo = new BoxGeometry(1, 1, 1);
  const bodyGeo = new BoxGeometry(1, .76, .4, 1, 1, 1);
  const shackleGeo = new TorusGeometry(.34, .085, 6, 18, Math.PI); shackleGeo.translate(0, .36, 0);
  const holeGeo = new CylinderGeometry(.075, .075, .012, 12); holeGeo.rotateX(Math.PI / 2); holeGeo.translate(0, .035, .207);
  const slotGeo = new BoxGeometry(.075, .16, .012); slotGeo.translate(0, -.055, .207);
  const trimGeo = new BoxGeometry(.94, .026, .416); trimGeo.translate(0, -.28, 0);
  const cloudMat = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, fog: false });
  const facing = dot(normalize(normalView), normalize(positionView.negate())).abs();
  cloudMat.opacityNode = smoothstep(0, .8, facing).mul(.3);
  cloudMat.colorNode = mix(materialColor.rgb, color('#f4d8ff'), facing.mul(.38));
  const pixelMat = new MeshBasicNodeMaterial({ color: '#dab2ff', transparent: true, opacity: .22, depthWrite: false, fog: false });
  const bodyMat = metal(), shackleMat = metal();
  const darkMat = new MeshBasicNodeMaterial({ color: '#25192f' });
  const trimMat = new MeshBasicNodeMaterial({ color: '#fff0c1' });
  const geometries = [cloudGeo, pixelGeo, bodyGeo, shackleGeo, holeGeo, slotGeo, trimGeo];
  const materials = [cloudMat, pixelMat, bodyMat, shackleMat, darkMat, trimMat];
  const batches = [], pose = new Object3D(), local = new Matrix4(), combined = new Matrix4(), tint = new Color();
  function batch(name, geo, mat, rows, coloured = true) {
    const mesh = new InstancedMesh(geo, mat, rows.length); mesh.name = name;
    if (coloured) rows.forEach((row, i) => { tint.set(KEEP_DREAM_COLOURS[row.colour]); mesh.setColorAt(i, tint); });
    object.add(mesh); batches.push(mesh); return mesh;
  }
  const clouds = batch('keep-soft-clouds', cloudGeo, cloudMat, layout.clouds);
  const pixels = batch('keep-pixel-clouds', pixelGeo, pixelMat, layout.pixels);
  const bodies = batch('keep-floating-lock-bodies', bodyGeo, bodyMat, layout.locks);
  const shackles = batch('keep-floating-lock-shackles', shackleGeo, shackleMat, layout.locks);
  const holes = batch('keep-lock-keyholes', holeGeo, darkMat, layout.locks, false);
  const slots = batch('keep-lock-keyhole-slots', slotGeo, darkMat, layout.locks, false);
  const backs = batch('keep-lock-reverse-keyholes', holeGeo, darkMat, layout.locks, false);
  const backSlots = batch('keep-lock-reverse-slots', slotGeo, darkMat, layout.locks, false);
  const trim = batch('keep-lock-brass-inlay', trimGeo, trimMat, layout.locks, false);
  const cloudBatches = [[clouds, layout.clouds], [pixels, layout.pixels]];
  const lockFrontBatches = [bodies, shackles, holes, slots, trim];
  local.makeRotationY(Math.PI);
  let time = 0, disposed = false;
  function place(quiet = false) {
    for (let b = 0; b < cloudBatches.length; b++) {
      const mesh = cloudBatches[b][0], rows = cloudBatches[b][1];
      for (let i = 0; i < rows.length; i++) {
        const row = rows[i];
        pose.position.fromArray(row.p); pose.rotation.set(0, 0, 0); pose.scale.fromArray(row.size);
        if (!quiet) { pose.position.x += Math.sin(time * .07 + row.phase) * 2; pose.position.y += Math.sin(time * .1 + row.phase) * 1.1; }
        pose.updateMatrix(); mesh.setMatrixAt(i, pose.matrix);
      }
    }
    for (let i = 0; i < layout.locks.length; i++) {
      const row = layout.locks[i];
      pose.position.fromArray(row.p); pose.scale.setScalar(row.r);
      pose.rotation.set(quiet ? 0 : Math.sin(time * .14 + row.phase) * .08, row.yaw + (quiet ? 0 : Math.sin(time * .08 + row.phase) * .35), quiet ? 0 : Math.sin(time * .12 + row.phase) * .1);
      if (!quiet) pose.position.y += Math.sin(time * .19 + row.phase) * 1.8;
      pose.updateMatrix();
      for (let b = 0; b < lockFrontBatches.length; b++) lockFrontBatches[b].setMatrixAt(i, pose.matrix);
      combined.multiplyMatrices(pose.matrix, local); backs.setMatrixAt(i, combined); backSlots.setMatrixAt(i, combined);
    }
    for (const mesh of batches) mesh.instanceMatrix.needsUpdate = true;
  }
  place();
  for (const mesh of batches) { mesh.computeBoundingSphere(); if (mesh.boundingSphere) mesh.boundingSphere.radius += 5; }
  return { object, layout, stats: { draws: batches.length, locks: layout.locks.length, clouds: layout.clouds.length, pixels: layout.pixels.length },
    update(dt, view = {}) {
      if (disposed || view.paused) return;
      const quiet = !!(view.gentle || view.reducedMotion);
      if (!quiet) time += Math.max(0, Math.min(.1, Number.isFinite(dt) ? dt : 0));
      place(quiet);
    },
    reset() { if (!disposed) { time = 0; place(); } },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent();
      for (const mesh of batches) mesh.dispose();
      for (const geo of geometries) geo.dispose();
      for (const mat of materials) mat.dispose();
    },
  };
}
