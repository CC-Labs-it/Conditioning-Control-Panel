/* render/nearDressing.js - close roadside scale cues (polish 6, VIEW lane).
 *
 * Lamp posts stand just outside every railed edge at a steady rhythm (render/viewMath.js nearCuePlan), each with
 * a short arm over the road and a warm lamp head, and every other post hangs a slim banner in the section's
 * colours facing the oncoming karts. They stream past close to the kart, so the eye reads speed and a human
 * scale beside the road. Static scenery: no motion, no flashing, fog fades them with distance.
 *
 * Perf: four InstancedMesh draws for the whole lap (pole + arm merged, lamp heads, banners), matrices written
 * once at build, unlit node materials, no shadow casting. The object joins the scene before the boot warm
 * (render/renderer.js warmAll walks the whole scene), so its programs are linked before the race (Firefox trap).
 */
import {
  Group, InstancedMesh, MeshBasicNodeMaterial, CylinderGeometry, IcosahedronGeometry, PlaneGeometry, Matrix4, Color, DoubleSide,
} from 'three/webgpu';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { nearCuePlan, basisMatrix, NEAR_LOOK } from './viewMath.js';

/**
 * @param {object} track the loaded track
 * @param {{ phone?: boolean, L?: object }} opts phone thins the posts (quality tier at build)
 * @returns {{ object: Group, count: number, banners: number, dispose(): void }}
 */
export function createNearDressing(track, { phone = false, L = NEAR_LOOK } = {}) {
  const plan = nearCuePlan(track, { phone, L });
  const group = new Group();
  group.name = 'near-dressing';
  const owned = [];
  const keep = (x) => { owned.push(x); return x; };
  if (!plan.length) return { object: group, count: 0, banners: 0, dispose() {} };

  // local frame: x toward the road, y road up, z along x cross y; the foot at the origin
  const pole = new CylinderGeometry(L.poleR, L.poleR * 1.35, L.poleM + L.footM, L.poleSides, 1, true).translate(0, (L.poleM - L.footM) / 2, 0);
  const arm = new CylinderGeometry(L.armR, L.armR, L.armM, 5, 1, true).rotateZ(Math.PI / 2).translate(L.armM / 2, L.poleM - 0.08, 0);
  const postGeo = keep(mergeGeometries([pole, arm]));
  pole.dispose(); arm.dispose();
  const lampGeo = keep(new IcosahedronGeometry(L.lampR, 0).translate(L.armM, L.poleM - 0.08 - L.lampR * 0.6, 0));
  // the banner hangs under the arm, its face toward the oncoming karts (normal along local z)
  const bannerGeo = keep(new PlaneGeometry(L.bannerW, L.bannerH).translate(L.bannerInM, L.poleM - L.bannerDropM - L.bannerH / 2, 0));

  const postMat = keep(new MeshBasicNodeMaterial({ color: new Color(L.poleHex) }));
  const lampMat = keep(new MeshBasicNodeMaterial({ color: new Color(L.lampHex) }));
  const bannerMat = keep(new MeshBasicNodeMaterial({ color: 0xffffff, side: DoubleSide }));

  const banners = plan.filter((c) => c.banner);
  const posts = new InstancedMesh(postGeo, postMat, plan.length);
  const lamps = new InstancedMesh(lampGeo, lampMat, plan.length);
  const flags = banners.length ? new InstancedMesh(bannerGeo, bannerMat, banners.length) : null;
  const M = new Matrix4(), C = new Color();
  plan.forEach((c, i) => {
    basisMatrix(M.elements, c.x, c.y, c.z, c.p);
    posts.setMatrixAt(i, M); lamps.setMatrixAt(i, M);
  });
  banners.forEach((c, i) => {
    basisMatrix(M.elements, c.x, c.y, c.z, c.p);
    flags.setMatrixAt(i, M); flags.setColorAt(i, C.set(c.hex));
  });
  for (const im of [posts, lamps, flags]) {
    if (!im) continue;
    im.instanceMatrix.needsUpdate = true;
    if (im.instanceColor) im.instanceColor.needsUpdate = true;
    im.castShadow = false; im.receiveShadow = false;
    im.computeBoundingSphere();
    group.add(im);
  }
  return {
    object: group,
    count: plan.length,
    banners: banners.length,
    dispose() {
      group.removeFromParent();
      for (const im of [posts, lamps, flags]) if (im) im.dispose();
      for (const x of owned) x.dispose();
    },
  };
}
