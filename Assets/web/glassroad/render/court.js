/* Open apertures, porcelain lips and paired exit pylons. Three static batches, no floor or clock. */
import { Group, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
import { vec3, normalize, normalWorld, dot, clamp, mix } from 'three/tsl';
import { LOOK } from './look.js';

export const COURT_KIT = Object.freeze({ rimW: 0.28, rimH: 0.075, wallW: 0.22, depth: 2.8,
  wallTop: -0.25, collarH: 0.16, exitInset: 6, pylonH: 4.2, pylonGap: 0.9 });

/** Every rim and wall lies outside the exact aperture rectangle. The bottom stays open. */
export function courtLayout(track) {
  const rows = { rim: [], shaft: [], brass: [] }, K = COURT_KIT;
  const add = (key, s, x, h, size, extra = {}) => {
    const f = track.frameAt(s);
    rows[key].push({ s, x, h, size, p: f.p.map((v, i) => v + f.b[i] * x + f.n[i] * h),
      b: [...f.b], n: [...f.n], t: [...f.t], ...extra });
  };
  for (const c of track?.courts || []) {
    for (const hole of c.holes) {
      const ring = (key, width, h, height) => {
        const mid = hole.s + hole.len / 2;
        for (const side of [-1, 1]) {
          add(key, mid, hole.x + side * (hole.w + width) / 2, h, [width, height, hole.len], { hole: hole.id });
          add(key, mid + side * (hole.len + width) / 2, hole.x, h,
            [hole.w + width * 2, height, width], { hole: hole.id });
        }
      };
      if (track.id === 'keep') {
        // Short chipped blocks follow curved slabs; every piece stays outside the missing stone.
        const pieces = Math.ceil(hole.len / 1.6), dz = hole.len / pieces;
        for (const side of [-1, 1]) for (let i = 0; i < pieces; i++) {
          const s = hole.s + (i + .5) * dz, width = .32 + (i % 3) * .09;
          add('rim', s, hole.x + side * (hole.w / 2 + width / 2 + .12), -.015,
            [width, .10 + (i % 3) * .025, dz - .12], { hole: hole.id });
          add('shaft', s, hole.x + side * (hole.w / 2 + .3), -2.55,
            [.32, 4.7, dz + .03], { hole: hole.id });
        }
        const columns = Math.ceil(hole.w / 1.5), dx = hole.w / columns;
        for (const side of [-1, 1]) for (let i = 0; i < columns; i++) {
          const x = hole.x - hole.w / 2 + (i + .5) * dx;
          add('rim', hole.s + (side > 0 ? hole.len : 0) + side * .3, x, -.01,
            [dx - .1, .11 + (i % 2) * .035, .35], { hole: hole.id });
          add('shaft', hole.s + (side > 0 ? hole.len : 0) + side * .3, x, -2.55,
            [dx + .03, 4.7, .35], { hole: hole.id });
        }
        continue;
      }
      ring('rim', K.rimW, K.rimH / 2, K.rimH);
      // The brass collar owns the upper wall slice, avoiding coplanar inner faces.
      const shaftTop = K.wallTop - K.collarH;
      ring('shaft', K.wallW, (shaftTop - K.depth) / 2, K.depth + shaftTop);
      ring('brass', K.wallW + 0.015, K.wallTop - K.collarH / 2, K.collarH);
    }
    if (track.id === 'keep') continue;
    // A paired silhouette marks the way out without a beam over the driving line.
    const s = c.toS - K.exitInset, f = track.frameAt(s);
    for (const side of [-1, 1]) {
      const x = side * (f.w / 2 + K.pylonGap);
      add('shaft', s, x, K.pylonH / 2, [0.55, K.pylonH, 0.7], { destination: true });
      add('brass', s, x, K.pylonH, [0.85, 0.22, 1.1], { destination: true });
      add('rim', s, x, K.pylonH * 0.72, [0.61, 0.8, 0.76], { destination: true });
      for (const h of [0.75, 1.45]) add('brass', s, x, h, [0.61, 0.12, 0.76], { destination: true });
    }
  }
  return rows;
}

export function buildCourt(track, pal) {
  const group = new Group(); group.name = 'aperture-court';
  const layout = courtLayout(track), geometry = new BoxGeometry(1, 1, 1), materials = [];
  const basis = new Matrix4(), matrix = new Matrix4(), rotation = new Quaternion();
  const x = new Vector3(), y = new Vector3(), z = new Vector3(), position = new Vector3(), scale = new Vector3();
  for (const [key, hex] of [['rim', '#f3e2ce'], ['shaft', '#271f38'], ['brass', '#cda15f']]) {
    const rows = layout[key];
    if (!rows.length) continue;
    const c = new Color(track.id === 'keep' ? key === 'rim' ? '#b99caf' : '#28172f' : hex), base = vec3(c.r, c.g, c.b), material = new MeshBasicNodeMaterial();
    const shade = clamp(dot(normalize(normalWorld), normalize(vec3(...LOOK.lightDir))).mul(0.38).add(0.62), 0.24, 1);
    material.name = 'court-' + key;
    material.colorNode = mix(base, pal?.col ? pal.col(1) : base, key === 'shaft' ? 0.035 : 0.015).mul(shade);
    materials.push(material);
    const mesh = new InstancedMesh(geometry, material, rows.length); mesh.name = 'court-' + key;
    rows.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      rotation.setFromRotationMatrix(basis);
      matrix.compose(position.fromArray(r.p), rotation, scale.fromArray(r.size)); mesh.setMatrixAt(i, matrix);
    });
    mesh.instanceMatrix.needsUpdate = true; mesh.computeBoundingSphere(); group.add(mesh);
  }
  let disposed = false;
  return { group, counts: Object.fromEntries(Object.entries(layout).map(([key, rows]) => [key, rows.length])),
    dispose() {
      if (disposed) return; disposed = true;
      group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); });
      geometry.dispose(); materials.forEach((m) => m.dispose());
    } };
}
