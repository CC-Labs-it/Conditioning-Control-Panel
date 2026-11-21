import { Box3, DoubleSide, Line3, Ray, Vector3 } from 'three';
import { MeshBVH } from './vendor/three-mesh-bvh.js';

const RADIUS = .24, HEIGHT = 1.74, EYE = 1.6, MAX_RISE = .195, MAX_DROP = .26;
const up = new Vector3(0, 1, 0), down = new Vector3(0, -1, 0);

/** The body stays on connected walkable surfaces. A missing floor stops movement at a streaming boundary. */
export function createWalker(camera, onFootstep) {
  const sectors = new Map(), feet = new Vector3(), proposed = new Vector3(), previous = new Vector3();
  const disabled = new Set(), crossings = new Map();
  const inactive = m => sectors.has(m.portal) || disabled.has(m.label);
  const segment = new Line3(), bodyBox = new Box3(), face = new Vector3();
  const onBody = new Vector3(), onWall = new Vector3(), correction = new Vector3();
  const ray = new Ray(), ground = { y: 0, surface: '', id: '' };
  let zone = '', surface = '', stride = 0, eyeY = 0;

  function add(id, group, gates = []) {
    const meshes = [];
    group.updateMatrixWorld(true);
    group.traverse(node => {
      if (!node.isMesh) return;
      const geometry = node.geometry.clone().applyMatrix4(node.matrixWorld);
      geometry.clearGroups();
      geometry.computeBoundingBox();
      const bvh = new MeshBVH(geometry);
      meshes.push({ geometry, bvh, box: geometry.boundingBox, surface: node.userData.surface || 'stone',
        portal: node.userData.portal, label: node.userData.collider || node.name });
    });
    sectors.set(id, meshes); crossings.set(id,gates);
  }
  function remove(id) {
    for (const item of sectors.get(id) || []) item.geometry.dispose();
    sectors.delete(id); crossings.delete(id);
  }
  function floorAt(point, rise, drop, preferred) {
    ray.origin.copy(point); ray.origin.y += rise + .002;
    ray.direction.copy(down);
    let best = -Infinity;
    for (const [id, meshes] of sectors) for (const m of meshes) {
      if (inactive(m) || !ray.intersectsBox(m.box)) continue;
      const hit = m.bvh.raycastFirst(ray, DoubleSide, 0, rise + drop + .004);
      if (!hit || Math.abs(hit.face.normal.y) < .66) continue;
      const y = hit.point.y;
      if (y < best || (y === best && id !== preferred)) continue;
      best = y; ground.y = y; ground.surface = m.surface; ground.id = id;
    }
    return Number.isFinite(best);
  }
  function resolveWalls(point) {
    segment.start.copy(point); segment.start.y += RADIUS + .004;
    segment.end.copy(point); segment.end.y += HEIGHT - RADIUS;
    bodyBox.makeEmpty().expandByPoint(segment.start).expandByPoint(segment.end).expandByScalar(RADIUS);
    for (const meshes of sectors.values()) for (const m of meshes) {
      if (inactive(m) || !bodyBox.intersectsBox(m.box)) continue;
      m.bvh.shapecast({
        intersectsBounds: box => box.intersectsBox(bodyBox),
        intersectsTriangle: tri => {
          tri.getNormal(face);
          if (Math.abs(face.y) >= .66) return false;
          // A landing's vertical nosing can intersect the lower hemisphere while its top is a legal step up.
          if (Math.max(tri.a.y,tri.b.y,tri.c.y) <= point.y + MAX_RISE) return false;
          const distance = tri.closestPointToSegment(segment, onWall, onBody);
          if (distance >= RADIUS || distance < 1e-9) return false;
          correction.subVectors(onBody, onWall); correction.y = 0;
          if (correction.lengthSq() < 1e-10) return false;
          correction.setLength(RADIUS - distance + .0001);
          point.add(correction); segment.start.add(correction); segment.end.add(correction);
          return false;
        },
      });
    }
  }
  function place(id, at, yaw = 0) {
    proposed.fromArray(at);
    if (!floorAt(proposed, .4, .7, id)) throw new Error('No walkable floor at ' + id + ' ' + at.join(', '));
    feet.copy(proposed); feet.y = ground.y; surface = ground.surface; zone = ground.id;
    camera.position.copy(feet); eyeY = camera.position.y += EYE;
    camera.rotation.set(0, yaw, 0, 'YXZ'); stride = 0;
  }
  function move(dx, dz) {
    const steps = Math.ceil(Math.hypot(dx, dz) / .09);
    for (let i = 0; i < steps; i++) {
      proposed.copy(feet); proposed.x += dx / steps; proposed.z += dz / steps;
      resolveWalls(proposed); resolveWalls(proposed);
      if (!floorAt(proposed, MAX_RISE, MAX_DROP, zone)) break;
      proposed.y = ground.y;
      // Low openings stay visible and physically too small for the player.
      ray.origin.copy(proposed); ray.origin.y += .04; ray.direction.copy(up);
      let headroom = true;
      for (const meshes of sectors.values()) for (const m of meshes) {
        if (!inactive(m) && ray.intersectsBox(m.box) && m.bvh.raycastFirst(ray, DoubleSide, 0, HEIGHT - .045)) headroom = false;
      }
      if (!headroom) break;
      previous.copy(feet); feet.copy(proposed); zone = ground.id; surface = ground.surface;
      stride += feet.distanceTo(previous);
      if (stride > .78) { stride %= .78; onFootstep?.(surface); }
      for (const gate of crossings.get(zone) || []) {
        const axis = gate.axis, side = axis === 'x' ? 'z' : 'x';
        if ((previous[axis]-gate.at)*gate.direction >= 0 || (feet[axis]-gate.at)*gate.direction < 0 ||
            feet[side]<gate.across[0] || feet[side]>gate.across[1] || feet.y<gate.height[0] || feet.y>gate.height[1]) continue;
        proposed.fromArray(gate.offset).add(feet);
        if (floorAt(proposed,.4,.7,zone)) {
          feet.copy(proposed); feet.y=ground.y; surface=ground.surface; zone=ground.id;
          eyeY += feet.y-previous.y;
        }
        break;
      }
    }
    camera.position.x = feet.x; camera.position.z = feet.z;
  }
  function step(dt) {
    eyeY += (feet.y + EYE - eyeY) * (1 - Math.exp(-18 * dt));
    camera.position.y = eyeY;
  }
  function blocked(ray, distance) {
    for (const meshes of sectors.values()) for (const m of meshes) {
      if (!inactive(m) && ray.intersectsBox(m.box) && m.bvh.raycastFirst(ray, DoubleSide, 0, distance - .08)) return true;
    }
    return false;
  }
  function setColliderEnabled(label,on) { if(on)disabled.delete(label);else disabled.add(label); }
  return { add, remove, place, move, step, blocked, setColliderEnabled, feet, get zone() { return zone; }, get surface() { return surface; },
    dispose() { for (const id of sectors.keys()) remove(id); } };
}
