import {Group, InstancedMesh, Matrix4} from 'three';
import {isArchiveScreen} from './archive-screens.js';

// Only the fixed case and the station body share a draw. Screens, lenses,
// labels, replay keys and their original interaction targets stay independent.
export function batchArchiveHousings(room) {
  room.updateWorldMatrix(true,false);
  const groups = new Map(), inverse = room.matrixWorld.clone().invert();
  for (const bay of room.children) {
    if (!bay.name.startsWith('archive_bay_')) continue;
    bay.updateWorldMatrix(true, true);
    for (const part of bay.children) {
      if (!/^expo_(fixture|station)_slot_\d+$/.test(part.name)) continue;
      part.traverseVisible(mesh => {
        if (!mesh.isMesh || mesh.material.transparent || Array.isArray(mesh.material) || mesh.matrixWorld.determinant() <= 0) return;
        const key = mesh.geometry.uuid + ':' + mesh.material.uuid;
        if (!groups.has(key)) groups.set(key, []);
        groups.get(key).push(mesh);
      });
    }
  }
  const batches=[];
  for (const meshes of groups.values()) {
    if (meshes.length < 2) continue;
    const {geometry, material} = meshes[0];
    const batch = new InstancedMesh(geometry, material, meshes.length);
    batch.name = 'archive_bay_housings';
    const local = new Matrix4();
    for (const [i, mesh] of meshes.entries()) {
      batch.setMatrixAt(i, local.multiplyMatrices(inverse,mesh.matrixWorld));
      mesh.visible = false;
    }
    batch.computeBoundingSphere();
    room.add(batch);
    batches.push({batch,meshes});
  }
  return () => {
    for (const {batch,meshes} of batches) {
      batch.removeFromParent();batch.dispose();
      for (const mesh of meshes) mesh.visible=true;
    }
  };
}

/** Every node of one projector stand in a room's view or collision tree: screen, housing, lens, caps, plaque, label. */
function standParts(root,id){
  const parts=[];
  root.traverse(node=>{
    const name=node.userData.collider || node.name;
    if(name.endsWith(id) && (name.startsWith('expo_') || name.startsWith('Screen_support_') || name.startsWith('Screen support ')))parts.push(node);
  });
  return parts;
}

// A room with its own game has no projector (archive-screens.js). Its authored stand leaves the room: unseen, and
// nothing to walk into. Run before the room's stations, caps and colliders are read.
export function retireArchiveBays(zone, route, keep=isArchiveScreen) {
  const gone=[];
  for(const site of route.slots.filter(row=>row.zone===zone.id && !row.template && !keep(row.id))){
    for(const node of standParts(zone.view,site.id))node.visible=false;
    for(const node of standParts(zone.collider,site.id))node.removeFromParent();
    gone.push(site.id);
  }
  return gone;
}

// Additional bays use their room's freestanding projector, including its moving caps and collision.
// Only the archive's screens are built (archive-screens.js): the route file lists more bays than the building has.
export function fitArchiveBays(zone, route, keep=isArchiveScreen) {
  for(const site of route.slots.filter(row=>row.zone===zone.id && row.template && keep(row.id))){
    const source=route.slots.find(row=>row.id===site.template);
    const yaw=row=>Math.atan2(row.front[0],row.front[2]);
    const transform=new Matrix4().makeTranslation(...site.screen)
      .multiply(new Matrix4().makeRotationY(yaw(site)-yaw(source)))
      .multiply(new Matrix4().makeScale(site.scale,site.scale,site.scale))
      .multiply(new Matrix4().makeTranslation(...source.screen.map(v=>-v)));
    for(const root of [zone.view,zone.collider]){
      root.updateWorldMatrix(true,true);
      const parts=[];
      root.traverse(node=>{
        const name=node.userData.collider || node.name;
        if(name.endsWith(source.id) && (name.startsWith('expo_') || name.startsWith('Screen_support_') || name.startsWith('Screen support ')))parts.push(node);
      });
      if(!parts.length)throw Error('Missing archive projector '+source.id+' in '+zone.id);
      const bay=new Group();bay.name='archive_bay_'+site.id;
      const inverse=new Matrix4().copy(root.matrixWorld).invert();
      for(const original of parts){
        const copy=original.clone(true);
        copy.matrix.multiplyMatrices(inverse,original.matrixWorld);
        copy.matrix.decompose(copy.position,copy.quaternion,copy.scale);
        copy.traverse(node=>{
          node.name=node.name.replaceAll(source.id,site.id);
          for(const [key,value] of Object.entries(node.userData))
            if(typeof value==='string' && value.includes(source.id))node.userData[key]=value.replaceAll(source.id,site.id);
          if(node.isMesh)node.userData.lightmap=false;
        });
        bay.add(copy);
      }
      root.add(bay);bay.applyMatrix4(transform);
    }
  }
}
