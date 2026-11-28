import { Box3, DoubleSide, Line3, Ray, Vector3 } from 'three';
import { MeshBVH } from './vendor/three-mesh-bvh.js';
import { createPlanterVisit, isPlanterCollider } from './planter.js';

const RADIUS = .24, HEIGHT = 1.74, EYE = 1.6, MAX_RISE = .195, MAX_DROP = .26;
const JUMP_SPEED = 3.08, GRAVITY = 12.8;
const up = new Vector3(0, 1, 0), down = new Vector3(0, -1, 0);
const HEAD_SAMPLES = [[0,0], [.16,0], [-.16,0], [0,.16], [0,-.16]];

/** The body stays on connected walkable surfaces. A missing floor stops movement at a streaming boundary. */
export function createWalker(camera, onFootstep, onPlanterCue, claimPlanter) {
  const sectors = new Map(), feet = new Vector3(), proposed = new Vector3(), previous = new Vector3();
  const disabled = new Set(), crossings = new Map();
  let doors=null,rail=null;
  const inactive = m => (m.portal&&(doors||sectors.has(m.portal))) || disabled.has(m.label);
  const segment = new Line3(), bodyBox = new Box3(), face = new Vector3();
  const onBody = new Vector3(), onWall = new Vector3(), correction = new Vector3();
  const ray = new Ray(), ground = { y: 0, surface: '', id: '', collider:null };
  const rayBox = new Box3(), rayEnd = new Vector3();
  const planter = createPlanterVisit(onPlanterCue,claimPlanter);
  let zone = '', surface = '', stride = 0, eyeY = 0;
  let supportY = 0, velocityY = 0, airborne = false;
  let support = null, takeoff = null;

  function add(id, group, gates = [], room = id) {
    const meshes = [], bounds = new Box3();
    group.updateMatrixWorld(true);
    group.traverse(node => {
      if (!node.isMesh) return;
      const geometry = node.geometry.clone().applyMatrix4(node.matrixWorld);
      geometry.clearGroups();
      geometry.computeBoundingBox();
      const bvh = new MeshBVH(geometry);
      bounds.union(geometry.boundingBox);
      meshes.push({ geometry, bvh, box: geometry.boundingBox, room, surface: node.userData.surface || 'stone',
        portal: node.userData.portal, label: node.userData.collider || node.name, planter:isPlanterCollider(node) });
    });
    sectors.set(id, {meshes,bounds}); crossings.set(id,gates);
  }
  function remove(id) {
    for (const item of sectors.get(id)?.meshes || []) item.geometry.dispose();
    sectors.delete(id); crossings.delete(id);
  }
  function* overlapping(box) {
    for (const sector of sectors.values()) {
      if (!box || box.intersectsBox(sector.bounds)) yield sector.meshes;
    }
  }
  // Test only loaded sectors touched by the actual reach, including adjacent rooms and stacked floors.
  function rayBounds(path,limit) {
    if (!Number.isFinite(limit)) return null;
    path.at(Math.max(0,limit),rayEnd);
    return rayBox.makeEmpty().expandByPoint(path.origin).expandByPoint(rayEnd).expandByScalar(.000001);
  }
  function floorAt(point, rise, drop, preferred) {
    ray.origin.copy(point); ray.origin.y += rise + .002;
    ray.direction.copy(down);
    let best = -Infinity;
    const bounds=rayBounds(ray,rise+drop+.004);
    for (const meshes of overlapping(bounds)) for (const m of meshes) {
      if (inactive(m) || !bounds.intersectsBox(m.box)) continue;
      const hit = m.bvh.raycastFirst(ray, DoubleSide, 0, rise + drop + .004);
      if (!hit || Math.abs(hit.face.normal.y) < .66) continue;
      const y = hit.point.y;
      if (y < best || (y === best && m.room !== preferred)) continue;
      best = y; ground.y = y; ground.surface = m.surface; ground.id = m.room; ground.collider = m;
    }
    return Number.isFinite(best);
  }
  function resolveWalls(point) {
    doors?.pushOut(point,RADIUS,HEIGHT,feet);
    segment.start.copy(point); segment.start.y += RADIUS + .004;
    segment.end.copy(point); segment.end.y += HEIGHT - RADIUS;
    bodyBox.makeEmpty().expandByPoint(segment.start).expandByPoint(segment.end).expandByScalar(RADIUS);
    for (const meshes of overlapping(bodyBox)) for (const m of meshes) {
      if (inactive(m) || !bodyBox.intersectsBox(m.box)) continue;
      m.bvh.shapecast({
        intersectsBounds: box => box.intersectsBox(bodyBox),
        intersectsTriangle: tri => {
          tri.getNormal(face);
          if (Math.abs(face.y) >= .66) return false;
          // A landing's vertical nosing can intersect the lower hemisphere while its top is a legal step up.
          if (Math.max(tri.a.y,tri.b.y,tri.c.y) <= point.y + (airborne ? .008 : MAX_RISE)) return false;
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
    supportY = feet.y; velocityY = 0; airborne = false;
    support = ground.collider; takeoff=null; planter.leave();
  }
  function move(dx, dz) {
    const steps = Math.ceil(Math.hypot(dx, dz) / .09);
    for (let i = 0; i < steps; i++) {
      proposed.copy(feet); proposed.x += dx / steps; proposed.z += dz / steps;
      resolveWalls(proposed); resolveWalls(proposed);
      rail?.constrain(proposed,feet);
      const clearance = Math.max(0, feet.y-supportY);
      // A ramp is a walkable face, never a wall, so a hop up a flight that drops back below its surface would
      // otherwise fall through it to the floor underneath. Look as far up in the air as on foot.
      const rise = MAX_RISE;
      const closeFloor = floorAt(proposed,rise,MAX_DROP+clearance,zone);
      if (!closeFloor && !floorAt(proposed,rise,1.05+clearance,zone)) break;
      if (planter.held && ground.collider !== planter.support) break;
      if(ground.id!==zone&&rail&&!rail.allows(ground.id))break;
      const floorY = ground.y;
      proposed.y = (airborne || !closeFloor) ? Math.max(feet.y,floorY) : floorY;
      // Low openings stay visible and physically too small for the player.
      ray.origin.copy(proposed); ray.origin.y += .04; ray.direction.copy(up);
      if(solidAlong(ray,HEIGHT-.045))break;
      previous.copy(feet); feet.copy(proposed); zone = ground.id; surface = ground.surface; supportY = floorY;
      support = ground.collider;
      if (planter.released && support !== planter.support) planter.leave();
      if (!airborne && !closeFloor) { airborne=true; velocityY=0; stride=0; }
      if (!airborne) stride += feet.distanceTo(previous);
      if (stride >= 1.67) { stride %= 1.67; onFootstep?.(surface); }
      for (const gate of crossings.get(zone) || []) {
        const axis = gate.axis, side = axis === 'x' ? 'z' : 'x';
        if ((previous[axis]-gate.at)*gate.direction >= 0 || (feet[axis]-gate.at)*gate.direction < 0 ||
            feet[side]<gate.across[0] || feet[side]>gate.across[1] || supportY<gate.height[0] || supportY>gate.height[1]) continue;
        const lift = feet.y-supportY;
        proposed.fromArray(gate.offset).add(feet);
        proposed.y -= lift;
        if (floorAt(proposed,.4,.7,zone)) {
          feet.copy(proposed); supportY=ground.y; feet.y=supportY+lift; surface=ground.surface; zone=ground.id; support=ground.collider;
          eyeY += feet.y-previous.y;
        }
        break;
      }
    }
    camera.position.x = feet.x; camera.position.z = feet.z;
  }
  function headTravel(wanted) {
    let free = wanted;
    for (const [x,z] of HEAD_SAMPLES) {
      // The four off-centre rays sample the rounded shoulder of the capsule.
      const cap = HEIGHT - (x || z ? .06 : 0);
      ray.origin.set(feet.x+x,feet.y+cap-.045,feet.z+z);
      ray.direction.copy(up);
      const bounds=rayBounds(ray,free+.047);
      for (const meshes of overlapping(bounds)) for (const m of meshes) {
        if (inactive(m) || !bounds.intersectsBox(m.box)) continue;
        const hit=m.bvh.raycastFirst(ray,DoubleSide,0,free+.047);
        if (hit) free=Math.min(free,Math.max(0,hit.distance-.047));
      }
    }
    return free;
  }
  function jump() {
    if (!zone || airborne || planter.held || headTravel(.02)<.015) return false;
    takeoff = {at:feet.toArray(),zone,support};
    airborne=true; velocityY=JUMP_SPEED; stride=0;
    return true;
  }
  function step(dt) {
    if (!Number.isFinite(dt) || dt<=0) return;
    planter.tick(dt);
    const wasAirborne=airborne;
    let remaining=Math.min(dt,1);
    while (airborne && remaining>0) {
      const h=Math.min(remaining,1/90);
      remaining-=h;
      const dy=velocityY*h-.5*GRAVITY*h*h;
      velocityY-=GRAVITY*h;
      if (dy>0) {
        const allowed=headTravel(dy);
        feet.y+=allowed;
        if (allowed<dy-.000001) velocityY=0;
      } else {
        feet.y+=dy;
        if (feet.y<=supportY) {
          feet.y=supportY; airborne=false; velocityY=0;
          planter.land(support,takeoff); takeoff=null;
          onFootstep?.(surface);
        }
      }
    }
    if (wasAirborne) eyeY=feet.y+EYE;
    else eyeY += (feet.y + EYE - eyeY) * (1 - Math.exp(-18 * dt));
    camera.position.y = eyeY;
  }
  function solidAlong(ray,limit,stockingShelf=false) {
    const bounds = rayBounds(ray,limit);
    for (const meshes of overlapping(bounds)) for (const m of meshes) {
      if (inactive(m) || (bounds && !bounds.intersectsBox(m.box))) continue;
      if (stockingShelf && m.label.startsWith('shop_shelves_')) continue;
      if (ray.intersectsBox(m.box) && m.bvh.raycastFirst(ray, DoubleSide, 0, limit)) return true;
    }
    return false;
  }
  function blocked(ray, distance, stockingShelf=false) {
    const reach = distance - .08;
    return !!doors?.blocked(ray,reach) || solidAlong(ray,reach,stockingShelf);
  }
  function trace(path,limit,ignore){
    let hit=null;const bounds=rayBounds(path,limit);
    for(const meshes of overlapping(bounds))for(const m of meshes){
      if(inactive(m)||(bounds&&!bounds.intersectsBox(m.box))||ignore?.(m.label)||!path.intersectsBox(m.box))continue;
      const candidate=m.bvh.raycastFirst(path,DoubleSide,0,hit?.distance??limit);
      if(candidate)hit={point:candidate.point,normal:candidate.face.normal,distance:candidate.distance};
    }
    return hit;
  }
  function setColliderEnabled(label,on) { if(on)disabled.delete(label);else disabled.add(label); }
  function stepOffPlanter() {
    if (!planter.released || !planter.origin) return false;
    const {zone:id,at} = planter.origin;
    proposed.fromArray(at);
    if (!sectors.has(id) || !floorAt(proposed,.08,.12,id) || ground.collider.planter) return false;
    const pitch=camera.rotation.x;
    place(id,at,camera.rotation.y); camera.rotation.x=pitch;
    return true;
  }
  return { add, remove, place, move, jump, step, blocked, trace, setColliderEnabled, feet, planter, stepOffPlanter,setDoors(value){doors=value;},setRail(value){rail=value;},
    get onPlanter(){return !airborne&&!!support?.planter;},
    get airborne(){return airborne;}, get zone() { return zone; }, get surface() { return surface; },
    dispose() { for (const id of sectors.keys()) remove(id); } };
}
