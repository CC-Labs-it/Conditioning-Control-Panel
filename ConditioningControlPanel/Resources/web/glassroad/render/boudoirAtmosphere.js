/* Floating vanity keepsakes: dimensional ribbon, lace, pearls and cosmetics. */
import { Group, InstancedMesh, BufferGeometry, Float32BufferAttribute, SphereGeometry,
  CylinderGeometry, TorusGeometry, BoxGeometry, CircleGeometry, MeshBasicNodeMaterial,
  Object3D, Matrix4, Color, DoubleSide } from 'three/webgpu';
import { materialColor, positionGeometry, normalWorld, normalView, positionView,
  normalize, dot, clamp, mix, vec3, sin, color, fwidth, smoothstep, max } from 'three/tsl';

const TAU = Math.PI * 2;
export const BOUDOIR_SKY_COLOURS = Object.freeze(['#ff4e9f', '#ff8bc6', '#cf66d5', '#ff7595']);
const KINDS = ['bow', 'lace', 'lipstick', 'compact', 'pearls'];

/** Decorations and their full motion envelopes stay outside every sampled road deck. */
export function boudoirAtmosphereLayout(track, { phone = false } = {}) {
  const decorations = [];
  if (track.id !== 'boudoir') return { decorations };
  let seed = 261031;
  const rnd = () => ((seed = Math.imul(seed, 1664525) + 1013904223 >>> 0) / 4294967296);
  const samples = [];
  for (let s = 0; s < track.lengthM; s += 10) samples.push(track.frameAt(s));
  for (const fork of track.forks || []) samples.push(...fork.samples(10));
  const clear = (p, radius) => samples.every(f => {
    const d = p.map((v, i) => v - f.p[i]);
    const along = d.reduce((v, x, i) => v + x * f.t[i], 0);
    const up = d.reduce((v, x, i) => v + x * f.n[i], 0);
    const side = d.reduce((v, x, i) => v + x * f.b[i], 0);
    return Math.abs(along) > radius + 12 || up > radius + 14 || up < -radius - 6 || Math.abs(side) > f.w / 2 + radius + 8;
  });
  const count = phone ? 45 : 90;
  for (let i = 0; i < count; i++) {
    const s = (i + .35) / count * track.lengthM, f = track.frameAt(s), side = i % 2 ? -1 : 1;
    const scale = 3.6 + rnd() * 2.6, radius = scale * 2.7 + 1.6;
    const near = i % 3 !== 0, phase = rnd() * TAU, yaw = rnd() * TAU;
    for (let attempt = 0; attempt < 4; attempt++) {
      const offset = side * (f.w / 2 + radius + (near ? 14 : 35) + rnd() * 30 + attempt * 24);
      const p = [f.p[0] + f.b[0] * offset, f.p[1] + 14 + rnd() * 26 + attempt * 12, f.p[2] + f.b[2] * offset];
      if (!clear(p, radius)) continue;
      decorations.push({ kind: KINDS[i % KINDS.length], s, p, scale, radius, phase, yaw, colour: i % 4, near });
      break;
    }
  }
  return { decorations };
}

function combine(parts) {
  const positions = [], normals = [];
  for (const geometry of parts) {
    const g = geometry.index ? geometry.toNonIndexed() : geometry;
    positions.push(...g.attributes.position.array); normals.push(...g.attributes.normal.array);
    if (g !== geometry) g.dispose(); geometry.dispose();
  }
  const out = new BufferGeometry();
  out.setAttribute('position', new Float32BufferAttribute(positions, 3));
  out.setAttribute('normal', new Float32BufferAttribute(normals, 3)); out.computeBoundingSphere(); return out;
}

function bowGeometry() {
  const parts = [], positions = [];
  // Each loop is a broad hollow ribbon with a round cross-section and a pinched root.
  for (const side of [-1, 1]) for (let i = 0; i < 40; i++) {
    const at = (step, edge) => {
      const t = step / 40 * TAU, width = .2 + .14 * (1 - Math.cos(t));
      return [side * (1 - Math.cos(t)) * .9, Math.sin(t) * .67 + edge * width,
        Math.sin(t * 2) * .13 + (1 - edge * edge) * .1];
    };
    for (let j = 0; j < 4; j++) {
      const a = at(i, j / 2 - 1), b = at(i + 1, j / 2 - 1), c = at(i + 1, (j + 1) / 2 - 1), d = at(i, (j + 1) / 2 - 1);
      positions.push(...a, ...b, ...c, ...a, ...c, ...d);
    }
  }
  // Two folded hanging tails end in a visible swallowtail notch.
  for (const side of [-1, 1]) {
    const a = [side * .15, -.1, 0], b = [side * .58, -.12, 0], c = [side * .92, -1.63, .12];
    const d = [side * .58, -1.43, .16], e = [side * .32, -1.7, .12];
    positions.push(...a, ...b, ...d, ...b, ...c, ...d, ...a, ...d, ...e);
  }
  const ribbon = new BufferGeometry(); ribbon.setAttribute('position', new Float32BufferAttribute(positions, 3)); ribbon.computeVertexNormals();
  parts.push(ribbon, new SphereGeometry(.32, 12, 8).scale(.8, 1.1, .8));
  return combine(parts);
}

function laceGeometry() {
  const parts = [new TorusGeometry(1.1, .035, 4, 64), new TorusGeometry(.8, .027, 4, 48)];
  for (let i = 0; i < 18; i++) {
    const a = i / 18 * TAU;
    parts.push(new TorusGeometry(.23, .035, 4, 12).translate(Math.cos(a) * 1.23, Math.sin(a) * 1.23, 0));
    const bar = new BoxGeometry(.39, .025, .035); bar.rotateZ(a); bar.translate(Math.cos(a) * .94, Math.sin(a) * .94, 0); parts.push(bar);
  }
  return combine(parts);
}

function pearlGeometry() {
  const parts = [];
  for (let i = 0; i < 24; i++) {
    const a = i / 24 * TAU;
    parts.push(new SphereGeometry(.12, 8, 6).translate(Math.cos(a) * 1.15, Math.sin(a) * 1.4, 0));
  }
  parts.push(new SphereGeometry(.19, 10, 6).scale(.8, 1.25, .7).translate(0, -1.65, 0));
  return combine(parts);
}

function finish(kind) {
  const m = new MeshBasicNodeMaterial({ color: '#ffffff', side: DoubleSide });
  const p = positionGeometry, light = clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .85, .35))).mul(.27).add(.77), .45, 1);
  const fresnel = dot(normalize(normalView), normalize(positionView.negate())).abs().oneMinus();
  const frequency = kind === 'satin' ? 145 : 90;
  const footprint = max(fwidth(p.y).mul(frequency), fwidth(p.x).mul(110));
  const detail = smoothstep(.5, 1.8, footprint).oneMinus();
  const weave = sin(p.y.mul(frequency)).mul(sin(p.x.mul(110))).mul(detail).mul(kind === 'satin' ? .045 : .02).add(.96);
  m.colorNode = mix(materialColor.rgb.mul(light).mul(weave), color(kind === 'gold' ? '#fff3bf' : '#fff5fc'), fresnel.pow(3).mul(kind === 'satin' ? .4 : .55));
  return m;
}

export function createBoudoirAtmosphere(track, options = {}) {
  const layout = boudoirAtmosphereLayout(track, options), object = new Group(); object.name = 'boudoir-vanity-atmosphere';
  const satin = finish('satin'), gold = finish('gold'), pearl = finish('pearl');
  const dark = new MeshBasicNodeMaterial({ color: '#4b294c' });
  const geometries = [], batches = [], pose = new Object3D(), local = new Matrix4(), result = new Matrix4(), tint = new Color();
  function batch(name, geometry, material, kind, colour = null, transform = null) {
    const rows = layout.decorations.filter(row => row.kind === kind), mesh = new InstancedMesh(geometry, material, rows.length);
    geometry.computeBoundingSphere(); geometries.push(geometry); mesh.name = name; object.add(mesh);
    rows.forEach((row, i) => { tint.set(colour || BOUDOIR_SKY_COLOURS[row.colour]); mesh.setColorAt(i, tint); });
    batches.push({ mesh, rows, transform }); return mesh;
  }
  batch('boudoir-satin-bows', bowGeometry(), satin, 'bow');
  batch('boudoir-scalloped-lace', laceGeometry(), pearl, 'lace', '#fff2e8');
  batch('boudoir-pearl-necklaces', pearlGeometry(), pearl, 'pearls', '#fff6ed');
  batch('boudoir-lipstick-cases', new CylinderGeometry(.32, .32, 1.05, 16).translate(0, -.45, 0), gold, 'lipstick', '#f9cf79');
  batch('boudoir-lipstick-bands', new CylinderGeometry(.333, .333, .2, 16).translate(0, .04, 0), dark, 'lipstick', '#ffffff');
  const bullet = new SphereGeometry(.27, 12, 8).scale(1, 2.65, 1).translate(0, .7, 0);
  batch('boudoir-lipstick-tips', bullet, satin, 'lipstick', '#ff397d');
  const compact = combine([new CylinderGeometry(.95, .95, .18, 32).rotateX(Math.PI / 2),
    new TorusGeometry(.95, .08, 6, 32)]);
  batch('boudoir-gold-compacts', compact, gold, 'compact', '#edbf73');
  batch('boudoir-compact-powder', new CircleGeometry(.82, 32).translate(0, 0, .105), pearl, 'compact', '#f4a8b7');
  local.makeRotationX(-.65); local.setPosition(0, 1.66, -.35);
  const lid = combine([new CylinderGeometry(.95, .95, .12, 32).rotateX(Math.PI / 2), new TorusGeometry(.95, .08, 6, 32)]);
  batch('boudoir-open-compact-lids', lid, gold, 'compact', '#edbf73', local.clone());
  batch('boudoir-compact-mirrors', new CircleGeometry(.82, 32).translate(0, 0, .075), pearl, 'compact', '#c8f5ed', local.clone());
  let time = 0, disposed = false;
  function place(quiet = false) {
    for (const { mesh, rows, transform } of batches) {
      for (let i = 0; i < rows.length; i++) {
        const row = rows[i]; pose.position.fromArray(row.p); pose.scale.setScalar(row.scale);
        pose.rotation.set(.08, row.yaw + (quiet ? 0 : Math.sin(time * .09 + row.phase) * .22), quiet ? 0 : Math.sin(time * .13 + row.phase) * .08);
        if (!quiet) pose.position.y += Math.sin(time * .2 + row.phase) * 1.2;
        pose.updateMatrix();
        mesh.setMatrixAt(i, transform ? result.multiplyMatrices(pose.matrix, transform) : pose.matrix);
      }
      mesh.instanceMatrix.needsUpdate = true;
    }
  }
  place();
  for (const { mesh } of batches) { mesh.computeBoundingSphere(); if (mesh.boundingSphere) mesh.boundingSphere.radius += 6; }
  return { object, layout, stats: { draws: batches.length, decorations: layout.decorations.length },
    update(dt, view = {}) {
      if (disposed || view.paused) return;
      const quiet = !!(view.gentle || view.reducedMotion);
      if (!quiet) time += Math.max(0, Math.min(.1, Number.isFinite(dt) ? dt : 0));
      place(quiet);
    },
    reset() { if (!disposed) { time = 0; place(); } },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent();
      for (const { mesh } of batches) mesh.dispose();
      for (const g of geometries) g.dispose();
      for (const m of [satin, gold, pearl, dark]) m.dispose();
    },
  };
}
