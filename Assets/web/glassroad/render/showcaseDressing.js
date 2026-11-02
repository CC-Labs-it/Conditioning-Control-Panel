/* A constructed ribbon: enamel fascia, brass reveal and porcelain cross-members. Three static
 * instanced draws, no textures, shadows, animation or extra passes. Add before renderer.compile. */
import { Group, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial, Matrix4, Vector3, Quaternion, Color } from 'three/webgpu';
import { vec3, mix, normalWorld, normalize, dot, clamp, cameraPosition, positionWorld, pow, max } from 'three/tsl';
import { showcaseLayout } from './showcaseDressingMath.js';
import { LOOK } from './look.js';

function constructionMaterial(pal, hex, finish) {
  const c = new Color(hex), base = vec3(c.r, c.g, c.b);
  const N = normalize(normalWorld), L = normalize(vec3(...LOOK.lightDir));
  const light = clamp(dot(N, L).mul(0.5).add(0.5), 0, 1);
  const V = normalize(cameraPosition.sub(positionWorld)), H = normalize(L.add(V));
  const highlight = pow(max(dot(N, H), 0), finish === 'metal' ? 42 : 24);
  const tint = pal && pal.col ? pal.col(1) : base;
  const material = new MeshBasicNodeMaterial();
  material.name = 'showcase-' + finish;
  material.colorNode = mix(base, tint, finish === 'enamel' ? 0.14 : 0.045)
    .mul(light.mul(0.5).add(0.46))
    .add(vec3(1, 0.97, 0.93).mul(highlight.mul(finish === 'metal' ? 0.3 : 0.12)));
  return material;
}

export function buildShowcaseDressing(track, pal, art = null) {
  const group = new Group(); group.name = 'showcase-track-construction';
  const layout = showcaseLayout(track), geometry = new BoxGeometry(1, 1, 1), materials = [];
  const matrix = new Matrix4(), basis = new Matrix4(), rotation = new Quaternion();
  const x = new Vector3(), y = new Vector3(), z = new Vector3(), position = new Vector3(), scale = new Vector3();
  const specs = track.id === 'mainframe'
    ? [['fascia', '#183735', 'enamel'], ['trim', '#53bba0', 'metal'], ['sleeper', '#244a47', 'porcelain']]
    : [['fascia', '#452653', 'enamel'], ['trim', '#d7a85d', 'metal'], ['sleeper', '#e5dce9', 'porcelain']];
  for (const [key, colour, finish] of specs) {
    const rows = layout[key];
    if (!rows.length) continue;
    const material = constructionMaterial(pal, colour, finish); materials.push(material);
    const mesh = new InstancedMesh(geometry, material, rows.length); mesh.name = 'showcase-' + key;
    rows.forEach((r, i) => {
      basis.makeBasis(x.fromArray(r.b), y.fromArray(r.n), z.fromArray(r.t).negate());
      rotation.setFromRotationMatrix(basis);
      matrix.compose(position.fromArray(r.p), rotation, scale.fromArray(r.size)); mesh.setMatrixAt(i, matrix);
    });
    mesh.instanceMatrix.needsUpdate = true;
    mesh.computeBoundingSphere(); mesh.castShadow = false; mesh.receiveShadow = false;
    group.add(mesh);
  }
  return { group, counts: Object.fromEntries(Object.entries(layout).map(([k, rows]) => [k, rows.length])),
    dispose() { group.removeFromParent(); group.traverse((o) => { if (o.isInstancedMesh) o.dispose(); }); geometry.dispose(); materials.forEach((m) => m.dispose()); } };
}
