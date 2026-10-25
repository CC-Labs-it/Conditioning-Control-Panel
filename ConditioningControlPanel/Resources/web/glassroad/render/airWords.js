/* Sung words form real temporary stepping surfaces. The simulation owns each exact top plane,
 * footprint and lifetime; this view never grows collision or animates a solid surface away from it. */
import { Group, Mesh, InstancedMesh, Object3D, BoxGeometry, PlaneGeometry, OctahedronGeometry,
  CanvasTexture, MeshBasicNodeMaterial, DoubleSide, Matrix4, Vector3, SRGBColorSpace } from 'three/webgpu';
import { FACES } from './fonts.js';
import { wordTheme, wordFace } from './wordTheme.js';

export const AIR_WORD_CAPACITY = 12;
const BITS = 40, CW = 1024, CH = 384;
const clamp01 = x => Math.max(0, Math.min(1, Number(x) || 0));

// A seeded, non-flashing fountain under the word and little assembling/dissolving edge fragments.
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

/** Every mesh, glyph texture and material exists before covered shader warmup. */
export function createAirWords(scene, track, { feel, photosafe, doc = globalThis.document } = {}) {
  const theme = wordTheme(track), face = wordFace(theme, 0), object = new Group();
  object.name = 'air-word-platforms';
  const box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1), bitGeo = new OctahedronGeometry(1);
  const materials = [], textures = [], rows = [], dummy = new Object3D(), pose = {};
  const matrix = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
  const makeMaterial = options => { const m = new MeshBasicNodeMaterial(options); materials.push(m); return m; };
  const bitMaterial = makeMaterial({ color: theme.echo, transparent: true, opacity: .6, depthWrite: false });
  const addBox = (root, material, x, y, z, w, h, d) => {
    const m = new Mesh(box, material); m.position.set(x, y, z); m.scale.set(w, h, d); root.add(m); return m;
  };
  for (let i = 0; i < AIR_WORD_CAPACITY; i++) {
    const root = new Group(), deck = new Group(), type = new Group();
    root.name = 'air-word-slot-' + i; root.visible = false; root.add(deck, type); object.add(root);
    const glass = makeMaterial({ color: theme.echo, transparent: true, opacity: 0, depthWrite: false });
    const rim = makeMaterial({ color: theme.accent, transparent: true, opacity: 0, depthWrite: false });
    const underside = makeMaterial({ color: theme.ink, transparent: true, opacity: 0, depthWrite: false });
    // The complete glass tray makes the rectangular support readable, including spaces between letters.
    // Its top is exactly local y=0, with no inflated invisible support beyond the published dimensions.
    addBox(deck, glass, 0, -.18, 0, 1, .36, 1);
    addBox(deck, underside, 0, -.39, 0, .98, .07, .98);
    for (const side of [-1, 1]) {
      addBox(deck, rim, side * .49, -.08, 0, .02, .16, 1);
      addBox(deck, rim, 0, -.08, side * .49, .96, .16, .02);
    }
    let canvas = null, context = null, top = null, depth = null;
    const glyphs = [], fronts = [];
    if (doc?.createElement) {
      canvas = doc.createElement('canvas'); canvas.width = CW; canvas.height = CH; context = canvas.getContext?.('2d');
      if (context) {
        const texture = new CanvasTexture(canvas); texture.colorSpace = SRGBColorSpace; textures.push(texture);
        top = makeMaterial({ map: texture, transparent: true, opacity: 0, alphaTest: .08, side: DoubleSide });
        depth = makeMaterial({ map: texture, color: theme.accent, transparent: true, opacity: 0, alphaTest: .08, side: DoubleSide });
        // Thin alpha-cut slices give the letters their own chunky silhouette inside the glass.
        for (let layer = 0; layer < 6; layer++) {
          const m = new Mesh(plane, layer === 5 ? top : depth);
          m.rotation.x = -Math.PI / 2; m.position.y = -.31 + layer * .07;
          type.add(m); glyphs.push(m);
        }
        // An approach-facing word cut into the leading edge stays legible before the camera sees its top.
        for (const side of [-1, 1]) {
          const m = new Mesh(plane, top); m.position.y = -.18; m.rotation.y = side > 0 ? 0 : Math.PI;
          type.add(m); fronts.push(m);
        }
      }
    }
    const bits = new InstancedMesh(bitGeo, bitMaterial, BITS); bits.count = 0; bits.frustumCulled = false; root.add(bits);
    rows.push({ root, deck, type, glass, rim, underside, canvas, context, top, depth, glyphs, fronts, bits,
      id: null, text: '', solid: false, safety: false });
  }
  scene.add(object);
  let clock = 0, visible = 0, solidCount = 0, particles = 0, disposed = false;
  function letter(row, text) {
    row.text = text;
    if (!row.context) return;
    const c = row.context;
    c.clearRect(0, 0, CW, CH); c.save();
    c.font = FACES[face.face].css(200, face.weight); c.textAlign = 'center'; c.textBaseline = 'middle'; c.lineJoin = 'round';
    const metrics = c.measureText(text), measured = Math.max(1, metrics.width);
    // Long floor lettering counteracts the chase view's foreshortening. Keep the actual lyric verbatim.
    c.translate(CW / 2, CH / 2); c.scale((CW - 90) / measured, 1.3);
    c.lineWidth = 10; c.strokeStyle = theme.ink; c.strokeText(text, 0, 0);
    const gradient = c.createLinearGradient(0, -100, 0, 100);
    for (const [at, color] of theme.fill) gradient.addColorStop(at, color);
    c.fillStyle = gradient; c.fillText(text, 0, 0);
    c.globalCompositeOperation = 'source-atop';
    c.fillStyle = '#ffffff'; c.globalAlpha = .32; c.fillRect(-measured, -92, measured * 2, 15);
    c.fillStyle = theme.ink; c.globalAlpha = theme.id === 'mainframe' ? .3 : .13;
    if (theme.motif === 'comic') {
      for (let y = -100; y <= 100; y += 20) for (let x = -measured / 2; x < measured / 2; x += 24) {
        c.beginPath(); c.arc(x + (y % 40 ? 12 : 0), y, 2.5, 0, Math.PI * 2); c.fill();
      }
    } else for (let y = -90; y <= 100; y += theme.id === 'mainframe' ? 11 : 24) c.fillRect(-measured, y, measured * 2, 2);
    c.restore(); row.top.map.needsUpdate = true;
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
    for (const p of run.platforms || []) {
      const alpha = clamp01(p.alpha), index = Number(String(p.id).split('-').at(-1));
      if (alpha <= .001 || !Number.isInteger(index) || index < 0 || index >= rows.length) continue;
      const row = rows[index], w = Math.max(.01, p.widthM), l = Math.max(.01, p.lengthM);
      if (row.id !== p.id || row.text !== p.text) {
        row.id = p.id; row.safety = photosafe?.request('phrase') === false;
        if (row.text !== p.text) letter(row, p.text);
      }
      const soft = gentle || row.safety;
      row.root.visible = true; row.root.position.fromArray(p.p);
      matrix.makeBasis(bx.fromArray(p.b), ny.fromArray(p.n), tz.fromArray(p.t).negate());
      row.root.quaternion.setFromRotationMatrix(matrix);
      row.deck.scale.set(w, 1, l);
      row.solid = !!p.solid; visible++; if (p.solid) solidCount++;
      // Strength follows the same smooth envelope as physical solidity. A boolean change must not flash.
      row.glass.opacity = alpha * alpha * .28;
      row.rim.opacity = alpha * alpha * .95;
      row.underside.opacity = alpha * alpha * .65;
      if (row.top) {
        row.top.opacity = alpha; row.depth.opacity = alpha * alpha * .9;
        for (const m of row.glyphs) m.scale.set(w * .91, l * .72, 1);
        for (let i = 0; i < row.fronts.length; i++) {
          row.fronts[i].position.z = (i ? -1 : 1) * (l / 2 + .012);
          row.fronts[i].scale.set(w * .87, .42, 1);
        }
      }
      row.bits.count = soft ? 12 : BITS; particles += row.bits.count;
      for (let i = 0; i < row.bits.count; i++) {
        airWordBit(i, clock, w, l, alpha, soft, pose);
        dummy.position.set(pose.x, pose.y, pose.z); dummy.rotation.set(i, i * .7 + (soft ? 0 : clock * .2), i * .3);
        dummy.scale.set(pose.size, pose.size * 1.8, pose.size); dummy.updateMatrix(); row.bits.setMatrixAt(i, dummy.matrix);
      }
      row.bits.instanceMatrix.needsUpdate = true;
    }
  }
  return { object, update, clear,
    get stats() { return { visible, solid: solidCount, particles, clock, capacity: AIR_WORD_CAPACITY }; },
    dispose() {
      if (disposed) return; clear(); disposed = true; object.removeFromParent();
      for (const row of rows) row.bits.dispose();
      for (const texture of textures) texture.dispose();
      for (const material of materials) material.dispose();
      box.dispose(); plane.dispose(); bitGeo.dispose();
    } };
}
