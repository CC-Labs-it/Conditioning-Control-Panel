import { Group, Box3, Vector3 } from 'three/webgpu';

import { CHESS_TRAP_IDS } from './chessTrapIds.js';

// Borrow accepted geometry AND painted materials, preserving the approved COLOR_0 detail.
export function chessTrap(art, index, size, side = 0) {
  const source = art?.model?.(CHESS_TRAP_IDS[index % CHESS_TRAP_IDS.length])?.scene;
  if (!source) return null;
  const pivot = new Group(), body = source.clone(true); pivot.name = 'chess-trap-sculpture';
  if (side) body.rotation.z = side * Math.PI / 2;
  body.updateMatrixWorld(true);
  const bounds = new Box3().setFromObject(body), extent = bounds.getSize(new Vector3()), centre = bounds.getCenter(new Vector3());
  const scale = Math.min(...size.map((v, i) => v / extent.getComponent(i))) * .92;
  body.scale.multiplyScalar(scale); body.position.copy(centre).multiplyScalar(-scale);
  body.traverse(o => { if (o.isMesh) o.castShadow = true; });
  pivot.position.y = size[1] / 2; pivot.add(body);
  return pivot;
}

// Decorative only: clock-driven so pause freezes it and reset cannot replay an impulse.
export function chessWobble(age, gentle = false) {
  return gentle || age < 0 || age > .6 ? 0 : Math.sin(age * 28) * Math.exp(-age * 7) * .045;
}
