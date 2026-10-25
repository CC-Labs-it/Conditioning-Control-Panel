/* The words themselves are solid, bevelled stepping surfaces. There is no tray or backing. */
import { Group, Mesh, InstancedMesh, Object3D, OctahedronGeometry, BufferGeometry,
  MeshStandardNodeMaterial, MeshBasicNodeMaterial, Matrix4, Vector3, Color } from 'three/webgpu';
import { FACES } from './fonts.js';
import { wordTheme, wordFace } from './wordTheme.js';
import { letterContours, extrudeLetters, letterCatchPoints } from './wordSolid.js';

export const AIR_WORD_CAPACITY = 12;
const BITS = 40, CW = 1024, CH = 256, CACHE_LIMIT = 24;
const clamp01 = x => Math.max(0, Math.min(1, Number(x) || 0));

/** Motion settles before contact. Landing compresses depth while the top stays planted. */
export function airWordPose(p, timeS, gentle = false, out = {}) {
  const untilCatch = p.catchS - timeS, landed = timeS - p.landedS;
  const approaching = Number.isFinite(untilCatch) && untilCatch > 0 ? clamp01((untilCatch - .12) / .25) : 0;
  const recoil = Number.isFinite(landed) && landed > 0 ? Math.sin(Math.min(1, landed / .36) * Math.PI) ** 2 : 0;
  const gain = gentle ? .25 : 1;
  out.y = approaching ? Math.sin(timeS * 2.2 + (p.index || 0)) * .09 * approaching * gain : 0;
  out.tilt = approaching ? Math.sin(timeS * 1.7 + (p.index || 0)) * .035 * approaching * gain : 0;
  out.depth = 1 - recoil * .42 * gain;
  return out;
}

export function airWordBit(i, timeS, widthM, lengthM, alpha, gentle, out = {}) {
  const phase = ((timeS * (gentle ? .12 : .4) + i * .61803398875) % 1 + 1) % 1;
  const angle = i * 2.39996323, edge = i % 3 === 0;
  out.x = Math.sin(angle) * widthM * (edge ? .49 : .35);
  out.z = Math.cos(angle) * lengthM * (edge ? .49 : .35);
  out.y = edge ? -.35 + phase * .9 : -2.8 + phase * 2.4;
  out.size = (edge ? .055 : .075) * Math.sin(phase * Math.PI) * clamp01(alpha);
  if (gentle) out.size *= .7;
  return out;
}

/** Meshes and materials exist during covered warmup. Only cached letter geometry changes later. */
export function createAirWords(scene, track, { feel, photosafe, doc = globalThis.document } = {}) {
  const theme = wordTheme(track), face = wordFace(theme, 0), object = new Group();
  object.name = 'air-word-platforms';
  const bitGeo = new OctahedronGeometry(1), empty = new BufferGeometry();
  const materials = [], cache = new Map(), rows = [], dummy = new Object3D(), pose = {}, wordPose = {};
  const matrix = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
  const canvas = doc?.createElement?.('canvas');
  if (canvas) { canvas.width = CW; canvas.height = CH; }
  const context = canvas?.getContext?.('2d', { willReadFrequently: true });
  const bitMaterial = new MeshBasicNodeMaterial({ color: theme.echo, transparent: true, opacity: .6, depthWrite: false });
  materials.push(bitMaterial);
  function geometry(text) {
    if (cache.has(text)) {
      const entry = cache.get(text); cache.delete(text); cache.set(text, entry); return entry;
    }
    if (!context?.getImageData) return { geometry: empty, catchPoints: [] };
    context.clearRect(0, 0, CW, CH); context.save();
    context.font = FACES[face.face].css(184, face.weight);
    context.textAlign = 'center'; context.textBaseline = 'middle'; context.fillStyle = '#ffffff';
    context.strokeStyle = '#ffffff'; context.lineJoin = 'round'; context.lineWidth = 3.6;
    const width = Math.max(1, context.measureText(text).width);
    context.translate(CW / 2, CH / 2); context.scale(Math.min(1, (CW - 18) / width), 1);
    // A little ink weight protects fine italic stems without outlining the finished word.
    context.strokeText(text, 0, 0); context.fillText(text, 0, 0); context.restore();
    const pixels = context.getImageData(0, 0, CW, CH).data;
    const solid = extrudeLetters(letterContours(pixels, CW, CH));
    const entry = { geometry: solid, catchPoints: letterCatchPoints(pixels, CW, CH, solid) };
    cache.set(text, entry); return entry;
  }
  // Warm actual cap and side material groups while the loading card covers the scene.
  const warm = geometry('O');
  for (let i = 0; i < AIR_WORD_CAPACITY; i++) {
    const root = new Group(); root.name = 'air-word-slot-' + i; root.visible = false; object.add(root);
    const top = new MeshStandardNodeMaterial({ color: theme.fill[1][1], roughness: .3, metalness: .12,
      emissive: theme.accent, emissiveIntensity: .13, transparent: true, opacity: 0 });
    const side = new MeshStandardNodeMaterial({ color: new Color(theme.accent).multiplyScalar(.58),
      roughness: .34, metalness: .2, emissive: theme.accent, emissiveIntensity: .05, transparent: true, opacity: 0 });
    materials.push(top, side);
    const letters = new Mesh(warm.geometry, [top, side]); letters.name = 'solid-letters'; root.add(letters);
    const bits = new InstancedMesh(bitGeo, bitMaterial, BITS); bits.count = 0; bits.frustumCulled = false; root.add(bits);
    rows.push({ root, letters, top, side, bits, entry: warm, id: null, text: '', solid: false, safety: false });
  }
  scene.add(object);
  let clock = 0, visible = 0, solidCount = 0, particles = 0, disposed = false;
  function trim() {
    const used = new Set(rows.map(row => row.letters.geometry));
    for (const [key, value] of cache) {
      if (cache.size <= CACHE_LIMIT) break;
      if (!used.has(value.geometry)) { cache.delete(key); value.geometry.dispose(); }
    }
  }
  function clear() {
    for (const row of rows) { row.root.visible = false; row.bits.count = 0; row.id = null; row.solid = false; row.safety = false; }
    visible = solidCount = particles = clock = 0;
  }
  function update(run, dt, { paused = false, mode = 'race', gentle = !!feel?.get('gentle.on') } = {}) {
    if (disposed) return;
    if (!run || mode !== 'race') { clear(); return; }
    if (paused) return;
    clock = Number.isFinite(run.timeS) ? run.timeS : clock + Math.max(0, Math.min(.1, dt || 0));
    visible = solidCount = particles = 0;
    for (const row of rows) row.root.visible = false;
    for (const p of (run.platforms || []).slice(-2)) {
      const alpha = clamp01(p.alpha), index = Number(String(p.id).split('-').at(-1));
      if (alpha <= .001 || !Number.isInteger(index) || index < 0 || index >= rows.length) continue;
      const row = rows[index], w = Math.max(.01, p.widthM), l = Math.max(.01, p.lengthM);
      if (row.id !== p.id || row.text !== p.text) {
        row.id = p.id; row.safety = photosafe?.request('phrase') === false;
        if (row.text !== p.text) { row.text = p.text; row.entry = geometry(p.text); row.letters.geometry = row.entry.geometry; }
      }
      p.catchPoints = row.entry.catchPoints; p.catchText = p.text;
      const soft = gentle || row.safety;
      row.root.visible = true; row.root.position.fromArray(p.p);
      matrix.makeBasis(bx.fromArray(p.b), ny.fromArray(p.n), tz.fromArray(p.t).negate());
      row.root.quaternion.setFromRotationMatrix(matrix);
      airWordPose(p, clock, soft, wordPose);
      row.letters.position.y = wordPose.y;
      row.letters.rotation.z = wordPose.tilt;
      row.letters.scale.set(w, wordPose.depth, l);
      row.solid = !!p.solid; visible++; if (p.solid) solidCount++;
      // The simulation's arrival envelope supplies the only reveal, with no positional pop.
      row.top.opacity = alpha; row.side.opacity = alpha;
      row.bits.count = soft ? 12 : BITS; particles += row.bits.count;
      for (let i = 0; i < row.bits.count; i++) {
        airWordBit(i, clock, w, l, alpha, soft, pose);
        dummy.position.set(pose.x, pose.y, pose.z); dummy.rotation.set(i, i * .7 + (soft ? 0 : clock * .2), i * .3);
        dummy.scale.set(pose.size, pose.size * 1.8, pose.size); dummy.updateMatrix(); row.bits.setMatrixAt(i, dummy.matrix);
      }
      row.bits.instanceMatrix.needsUpdate = true;
    }
    trim();
  }
  return { object, update, clear,
    get stats() { return { visible, solid: solidCount, particles, clock, capacity: AIR_WORD_CAPACITY, cachedWords: cache.size }; },
    dispose() {
      if (disposed) return; clear(); disposed = true; object.removeFromParent();
      for (const row of rows) row.bits.dispose();
      for (const value of cache.values()) value.geometry.dispose(); cache.clear();
      for (const material of materials) material.dispose();
      empty.dispose(); bitGeo.dispose();
    } };
}
