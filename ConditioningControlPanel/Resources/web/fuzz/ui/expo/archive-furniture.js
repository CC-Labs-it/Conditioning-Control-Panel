import {Group, Matrix4} from 'three';

// Additional bays use their room's freestanding projector, including its moving caps and collision.
export function fitArchiveBays(zone, route) {
  for(const site of route.slots.filter(row=>row.zone===zone.id && row.template)){
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
