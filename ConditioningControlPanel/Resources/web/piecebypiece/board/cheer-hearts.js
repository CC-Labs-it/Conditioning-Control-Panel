import * as THREE from 'three';

export function createCheerHearts(root) {
  const group = new THREE.Group(); group.name = 'emi-pet-hearts'; root.add(group);
  const heart = new THREE.Shape();
  heart.moveTo(0,-.55); heart.bezierCurveTo(-1,.08,-.65,.85,0,.36);
  heart.bezierCurveTo(.65,.85,1,.08,0,-.55);
  const geometry = new THREE.ShapeGeometry(heart,12), particles=[];
  const inverse = new THREE.Quaternion(), facing = new THREE.Quaternion();
  for(let i=0;i<10;i++) {
    const mesh=new THREE.Mesh(geometry,new THREE.MeshBasicMaterial({color:0xff83bb,transparent:true,opacity:0,depthWrite:false,side:THREE.DoubleSide}));
    mesh.raycast=()=>{};mesh.visible=false;group.add(mesh);particles.push({mesh,age:10,seed:i});
  }
  function clear(){for(const p of particles){p.age=10;p.mesh.visible=false;}}
  return {
    emit(){particles.forEach((p,i)=>{p.age=-i*.065;p.mesh.visible=false;});},
    clear,
    update(dt,camera){
      if(!(dt>0))return;
      root.getWorldQuaternion(inverse).invert();
      if(camera)camera.getWorldQuaternion(facing);else facing.identity();
      facing.premultiply(inverse);
      for(const p of particles){
        p.age+=Math.min(.1,dt);const t=p.age/1.5;
        p.mesh.visible=t>=0&&t<1;if(!p.mesh.visible)continue;
        const spread=(p.seed%5-2)*.065;
        p.mesh.position.set(spread+Math.sin(t*3+p.seed)*.035,.55+t*.48,-.08+Math.cos(p.seed)*.06);
        p.mesh.quaternion.copy(facing);p.mesh.scale.setScalar(.045+Math.sin(t*Math.PI)*.025);
        p.mesh.material.opacity=Math.min(1,t*8)*(1-t)*.85;
      }
    },
    debug(){return particles.filter(p=>p.mesh.visible).length;},
    dispose(){geometry.dispose();particles.forEach(p=>p.mesh.material.dispose());group.removeFromParent();},
  };
}
