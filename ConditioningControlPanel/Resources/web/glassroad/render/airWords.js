/* The words themselves are solid, bevelled stepping surfaces. There is no tray or backing. */
import { Group, Mesh, InstancedMesh, Object3D, OctahedronGeometry, BufferGeometry,
  MeshStandardNodeMaterial, MeshBasicNodeMaterial, Matrix4, Vector3, Color, BackSide } from 'three/webgpu';
import { positionLocal, normalLocal, vec3 } from 'three/tsl';
import { FACES } from './fonts.js';
import { wordTheme, wordFace } from './wordTheme.js';
import { letterContours, extrudeLetters, letterCatchPoints } from './wordSolid.js';

export const AIR_WORD_CAPACITY = 12;
// Owner 2026-10-28: thicker letters, vivid colour, an ink line plus a bright rim, and real sparkle.
export const AIR_WORD_MOTES = 40, AIR_WORD_SPARKS = 18, AIR_WORD_TRAIL = 36, AIR_WORD_THICK = 1.3;
const BITS = AIR_WORD_MOTES + AIR_WORD_SPARKS, CW = 1024, CH = 256, CACHE_LIMIT = 24;
const SPARK_S = .7, TRAIL_LIFE_S = .75;
const clamp01 = x => Math.max(0, Math.min(1, Number(x) || 0));

/** Motion settles before contact. Landing compresses depth and bulges the footprint; the top stays planted. */
export function airWordPose(p, timeS, gentle = false, out = {}) {
  const untilCatch = p.catchS - timeS, landed = timeS - p.landedS;
  const approaching = Number.isFinite(untilCatch) && untilCatch > 0 ? clamp01((untilCatch - .12) / .25) : 0;
  const recoil = Number.isFinite(landed) && landed > 0 ? Math.sin(Math.min(1, landed / .36) * Math.PI) ** 2 : 0;
  const gain = gentle ? .25 : 1;
  out.y = approaching ? Math.sin(timeS * 2.2 + (p.index || 0)) * .09 * approaching * gain : 0;
  out.tilt = approaching ? Math.sin(timeS * 1.7 + (p.index || 0)) * .035 * approaching * gain : 0;
  out.depth = 1 - recoil * .5 * gain;
  out.spread = 1 + recoil * .08 * gain;
  return out;
}

export function airWordBit(i, timeS, widthM, lengthM, alpha, gentle, out = {}) {
  const phase = ((timeS * (gentle ? .12 : .4) + i * .61803398875) % 1 + 1) % 1;
  const angle = i * 2.39996323, edge = i % 3 === 0;
  out.x = Math.sin(angle) * widthM * (edge ? .49 : .35);
  out.z = Math.cos(angle) * lengthM * (edge ? .49 : .35);
  out.y = edge ? -.35 + phase * .9 : -2.8 + phase * 2.4;
  out.size = (edge ? .085 : .11) * Math.sin(phase * Math.PI) * clamp01(alpha);
  if (gentle) out.size *= .7;
  return out;
}

/** A spark thrown off the word when it appears (bornS) and again on each contact (landedS). */
export function airWordSpark(i, timeS, p, gentle, out = {}) {
  const ages = [timeS - p.bornS, timeS - p.landedS].filter(a => Number.isFinite(a) && a >= 0 && a < SPARK_S);
  out.x = out.y = out.z = out.size = 0;
  if (!ages.length) return out;
  const age = Math.min(...ages), u = age / SPARK_S, angle = i * 2.39996323 + (p.index || 0);
  const reach = (gentle ? .3 : .55) * (1 - (1 - u) ** 3);
  out.x = Math.cos(angle) * (p.widthM * .5 + 1.5) * reach;
  out.z = Math.sin(angle) * (p.lengthM * .5 + 1.5) * reach;
  out.y = (gentle ? 1.2 : 2.6) * age * (1 + (i % 3) * .4) - 2.4 * age * age;
  // Size rises and falls once: no blink, so Gentle keeps it and only calms it.
  out.size = (gentle ? .1 : .17) * Math.sin(Math.PI * u) * clamp01(p.alpha + .4);
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
  // Vivid course colour: the mid stop of the fill ramp pushed toward saturation, lit from within.
  const vivid = new Color(theme.fill[2][1]).offsetHSL(0, .12, 0), deep = new Color(theme.fill[3][1]);
  const rim = new Color(theme.echo).lerp(new Color('#ffffff'), .25);
  const sparkColours = [vivid, rim, new Color('#ffffff'), new Color(theme.accent)];
  const bitMaterial = new MeshBasicNodeMaterial({ color: 0xffffff, transparent: true, opacity: .92, depthWrite: false });
  materials.push(bitMaterial);
  // Expand each glyph's closed hull along its normals, including the inner counters. One node for every
  // slot's outline: a node per slot gave each outline material its own shader graph build.
  const outlineOffset = positionLocal.add(normalLocal.mul(vec3(.016, .05, .024)));
  const rimOffset = positionLocal.add(normalLocal.mul(vec3(.032, .1, .05)));
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
    const top = new MeshStandardNodeMaterial({ color: vivid, roughness: .22, metalness: .1,
      emissive: vivid, emissiveIntensity: .42, transparent: true, opacity: 0 });
    const side = new MeshStandardNodeMaterial({ color: deep, roughness: .3, metalness: .2,
      emissive: theme.accent, emissiveIntensity: .28, transparent: true, opacity: 0 });
    const outlineMaterial = new MeshBasicNodeMaterial({ color: theme.ink, side: BackSide, transparent: true, opacity: 0 });
    outlineMaterial.positionNode = outlineOffset;
    const rimMaterial = new MeshBasicNodeMaterial({ color: rim, side: BackSide, transparent: true, opacity: 0 });
    rimMaterial.positionNode = rimOffset;
    materials.push(top, side, outlineMaterial, rimMaterial);
    const letters = new Mesh(warm.geometry, [top, side]); letters.name = 'solid-letters'; root.add(letters);
    const bits = new InstancedMesh(bitGeo, bitMaterial, BITS); bits.count = 0; bits.frustumCulled = false; root.add(bits);
    for (let k = 0; k < BITS; k++) bits.setColorAt(k, sparkColours[k % sparkColours.length]);
    const outline = new Mesh(warm.geometry, outlineMaterial); outline.name = 'solid-letter-outline'; root.add(outline);
    const glow = new Mesh(warm.geometry, rimMaterial); glow.name = 'solid-letter-rim'; root.add(glow);
    rows.push({ root, letters, outline, outlineMaterial, glow, rimMaterial, top, side, bits, entry: warm, id: null, text: '', solid: false, safety: false });
  }
  // The flight's sparkle trail lives in world space: one instanced pool for the whole run.
  const trail = new InstancedMesh(bitGeo, bitMaterial, AIR_WORD_TRAIL);
  trail.name = 'air-word-trail'; trail.count = 0; trail.frustumCulled = false;
  const motes = Array.from({ length: AIR_WORD_TRAIL }, (_, k) => {
    trail.setColorAt(k, sparkColours[k % sparkColours.length]); return { x: 0, y: 0, z: 0, born: -Infinity, spin: k * .7 };
  });
  scene.add(object); scene.add(trail);
  let clock = 0, trailClock = 0, visible = 0, solidCount = 0, particles = 0, disposed = false, nextMote = 0, emitS = -Infinity;
  function trim() {
    const used = new Set(rows.map(row => row.letters.geometry));
    for (const [key, value] of cache) {
      if (cache.size <= CACHE_LIMIT) break;
      if (!used.has(value.geometry)) { cache.delete(key); value.geometry.dispose(); }
    }
  }
  function clearTrail() {
    for (const m of motes) m.born = -Infinity;
    trail.count = 0; trailClock = 0; emitS = -Infinity;
  }
  function clear() {
    for (const row of rows) { row.root.visible = false; row.bits.count = 0; row.id = null; row.solid = false; row.safety = false; }
    clearTrail(); visible = solidCount = particles = clock = 0;
  }
  function updateTrail(run, kart, soft, motion) {
    if (motion === 'off') { clearTrail(); return; }
    const flying = !!run.flying && Number.isFinite(kart?.x);
    if (flying && trailClock - emitS >= (soft || motion === 'less' ? 1 / 14 : 1 / 40)) {
      emitS = trailClock; const m = motes[nextMote], a = trailClock * 9.1 + nextMote;
      nextMote = (nextMote + 1) % AIR_WORD_TRAIL;
      m.x = kart.x + Math.cos(a) * .45; m.y = kart.y + .3 + Math.sin(a * 1.3) * .2; m.z = kart.z + Math.sin(a) * .45; m.born = trailClock;
    }
    let n = 0;
    for (const m of motes) {
      const age = trailClock - m.born;
      if (!(age >= 0 && age < TRAIL_LIFE_S)) continue;
      // Gentle rises from zero to its peak in 375 ms, then falls without a blink.
      const size = soft ? .1 * Math.sin(Math.PI * age / TRAIL_LIFE_S) :
        .16 * (1 - age / TRAIL_LIFE_S) * Math.min(1, age * 20 + .2);
      dummy.position.set(m.x, m.y - age * .8, m.z); dummy.rotation.set(m.spin, m.spin + (soft ? 0 : age * 3), 0);
      dummy.scale.set(size, size * 1.8, size); dummy.updateMatrix(); trail.setMatrixAt(n++, dummy.matrix);
    }
    trail.count = n; particles += n; trail.instanceMatrix.needsUpdate = true;
  }
  function update(run, dt, { paused = false, mode = 'race', gentle = !!feel?.get('gentle.on'), kart = null, motion = 'full' } = {}) {
    if (disposed) return;
    if (!run || mode !== 'race') { clear(); return; }
    if (paused) return;
    const step = Math.max(0, Math.min(.1, dt || 0)), nextClock = Number.isFinite(run.timeS) ? run.timeS : clock + step;
    if (nextClock < clock) clearTrail();
    clock = nextClock;
    // The route's clock stops after landing. The drawn trail must finish even with that clock held.
    trailClock += step;
    visible = solidCount = particles = 0;
    for (const row of rows) row.root.visible = false;
    for (const p of (run.platforms || []).slice(-2)) {
      const alpha = clamp01(p.alpha), index = Number(String(p.id).split('-').at(-1));
      if (alpha <= .001 || !Number.isInteger(index) || index < 0 || index >= rows.length) continue;
      const row = rows[index], w = Math.max(.01, p.widthM), l = Math.max(.01, p.lengthM);
      if (row.id !== p.id || row.text !== p.text) {
        row.id = p.id; row.safety = photosafe?.request('phrase') === false;
        if (row.text !== p.text) {
          row.text = p.text; row.entry = geometry(p.text);
          row.letters.geometry = row.outline.geometry = row.glow.geometry = row.entry.geometry;
        }
      }
      p.catchPoints = row.entry.catchPoints; p.catchText = p.text;
      const soft = gentle || row.safety;
      row.root.visible = true; row.root.position.fromArray(p.p);
      matrix.makeBasis(bx.fromArray(p.b), ny.fromArray(p.n), tz.fromArray(p.t).negate());
      row.root.quaternion.setFromRotationMatrix(matrix);
      airWordPose(p, clock, soft, wordPose);
      if (motion === 'off') Object.assign(wordPose, { y: 0, tilt: 0, depth: 1, spread: 1 });
      row.letters.position.y = wordPose.y;
      row.letters.rotation.z = wordPose.tilt;
      row.letters.scale.set(w * wordPose.spread, wordPose.depth * AIR_WORD_THICK, l * wordPose.spread);
      for (const shell of [row.outline, row.glow]) {
        shell.position.copy(row.letters.position); shell.rotation.copy(row.letters.rotation); shell.scale.copy(row.letters.scale);
      }
      row.solid = !!p.solid; visible++; if (p.solid) solidCount++;
      // The simulation's arrival envelope supplies the only reveal, with no positional pop.
      row.top.opacity = row.side.opacity = row.outlineMaterial.opacity = alpha;
      row.rimMaterial.opacity = alpha * .9;
      const floating = motion === 'off' ? 0 : soft ? 12 : AIR_WORD_MOTES;
      const sparks = motion === 'off' ? 0 : soft || motion === 'less' ? 8 : AIR_WORD_SPARKS;
      row.bits.count = floating + sparks; particles += row.bits.count;
      for (let i = 0; i < row.bits.count; i++) {
        if (i < floating) airWordBit(i, clock, w, l, alpha, soft, pose);
        else airWordSpark(i - floating, clock, p, soft, pose);
        dummy.position.set(pose.x, pose.y, pose.z); dummy.rotation.set(i, i * .7 + (soft ? 0 : clock * .2), i * .3);
        dummy.scale.set(pose.size, pose.size * 1.8, pose.size); dummy.updateMatrix(); row.bits.setMatrixAt(i, dummy.matrix);
      }
      row.bits.instanceMatrix.needsUpdate = true;
    }
    updateTrail(run, kart, gentle || rows.some(row => row.root.visible && row.safety), motion);
    trim();
  }
  return { object, trail, update, clear,
    get stats() { return { visible, solid: solidCount, particles, clock, capacity: AIR_WORD_CAPACITY, cachedWords: cache.size }; },
    dispose() {
      if (disposed) return; clear(); disposed = true; object.removeFromParent(); trail.removeFromParent();
      for (const row of rows) row.bits.dispose();
      trail.dispose();
      for (const value of cache.values()) value.geometry.dispose(); cache.clear();
      for (const material of materials) material.dispose();
      empty.dispose(); bitGeo.dispose();
    } };
}
