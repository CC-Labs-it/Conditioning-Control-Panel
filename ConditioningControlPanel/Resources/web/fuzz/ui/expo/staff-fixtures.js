import {livePanel,placard} from './display.js';

export function dial(api,root,name,clean,width=640,height=280) {
  let mesh=root?.getObjectByName(name);
  if(!mesh?.isMesh){
    let first;
    mesh?.traverse(child=>{if(!first&&child.isMesh)first=child;});
    mesh=first;
  }
  if(!mesh)return null;
  const panel=livePanel(api.THREE,mesh,{width,height,emission:.16});
  clean.push(()=>panel.dispose());return panel;
}

export function write(panel,heading,line,foot='') { placard(panel,heading,line,foot); }

export function sight(api) {
  const projected=new api.THREE.Vector3(), offset=new api.THREE.Vector3(),direction=new api.THREE.Vector3();
  return {
    visible(at,margin=1.10) {
      projected.copy(at).project(api.camera);
      return projected.z>=-1&&projected.z<=1&&Math.abs(projected.x)<margin&&Math.abs(projected.y)<margin;
    },
    gaze(at,reach,angle=.15) {
      offset.copy(at).sub(api.camera.position);
      const length=offset.length();
      if(length>reach||length<.001)return false;
      api.camera.getWorldDirection(direction);
      return direction.dot(offset)>Math.cos(angle)*length;
    },
  };
}

// Follow the skirting with one instanced draw for each finish, including sloping landings.
export function layThread(api,group,place,points,clean) {
  const T=api.THREE,source=place('annex_thread',[0,0,0]);
  source.visible=false;
  const chunks=[],a=new T.Vector3(),b=new T.Vector3(),axis=new T.Vector3(1,0,0),transform=new T.Object3D();
  for(let i=1;i<points.length;i++){
    a.fromArray(points[i-1]);b.fromArray(points[i]);
    const distance=a.distanceTo(b),n=Math.ceil(distance/1.5);
    if(!n)continue;
    transform.quaternion.setFromUnitVectors(axis,b.clone().sub(a).normalize());
    transform.scale.set(distance/n,1,1);
    for(let k=0;k<n;k++){
      transform.position.lerpVectors(a,b,(k+.5)/n);transform.updateMatrix();chunks.push(transform.matrix.clone());
    }
  }
  source.traverse(piece=>{
    if(!piece.isMesh||!chunks.length)return;
    const material=piece.material.clone();
    if(material.emissive){
      material.emissive.setHex(0xecbb9c);material.emissiveIntensity=group.userData.emergencyWool ? .55 : 0;
      (group.userData.woolFinishes??=new Set()).add(material);
    }
    const copies=new T.InstancedMesh(piece.geometry,material,chunks.length);
    copies.name='Wool along the skirting';
    chunks.forEach((matrix,i)=>copies.setMatrixAt(i,matrix));
    copies.computeBoundingSphere();group.add(copies);
    clean.push(()=>{group.userData.woolFinishes?.delete(material);copies.removeFromParent();copies.dispose();material.dispose();});
  });
}

export function useCarry(api,state,key,node,label) {
  if(!api.hold(key,node,label)){api.say('Your hand is occupied.');return false;}
  node.visible=false;return true;
}
