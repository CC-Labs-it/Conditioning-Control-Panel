import {Group,Mesh,Shape,ExtrudeGeometry,MeshStandardNodeMaterial,Vector3} from 'three/webgpu';

/** Solid enamel chevrons. The invisible HTML twins provide focus and controller access. */
export function garageArrows(scene) {
  const shape=new Shape();
  [[-.45,0],[.02,.44],[.20,.26],[-.09,0],[.20,-.26],[.02,-.44]].forEach(([x,y],i)=>i?shape.lineTo(x,y):shape.moveTo(x,y));shape.closePath();
  const geo=new ExtrudeGeometry(shape,{depth:.12,bevelEnabled:true,bevelSize:.045,bevelThickness:.035,bevelSegments:3,steps:1});
  const arrows=[-1,1].map(dir=>{
    const root=new Group(),material=new MeshStandardNodeMaterial({color:0xe9a7bc,metalness:.25,roughness:.3});
    const mesh=new Mesh(geo,material);mesh.rotation.y=Math.PI;mesh.rotation.z=dir===-1?Math.PI:0;root.add(mesh);scene.add(root);
    root.userData.garageDirection=dir;
    return {root,material,dir,hover:0,target:0,kick:0};
  });
  const v=new Vector3();
  return {
    arrows,
    hover(dir,on){arrows.find(a=>a.dir===dir).target=on?1:0;},
    press(dir){arrows.find(a=>a.dir===dir).kick=1;},
    update(dt,time,camera,{custom,reduced,busy}){
      for(const a of arrows){
        a.root.visible=!custom;
        a.hover+=(a.target-a.hover)*(1-Math.exp(-dt*14));a.kick*=Math.exp(-dt*8);
        v.set(a.dir*.78,-.46,.5).unproject(camera).sub(camera.position).normalize().multiplyScalar(3.5);
        a.root.position.copy(camera.position).add(v);
        if(!reduced)a.root.position.x+=a.dir*(Math.sin(time*2.5)*.018+a.kick*.09);
        a.root.quaternion.copy(camera.quaternion);
        a.root.rotation.z+=reduced?0:Math.sin(time*3)*.035*a.hover+a.kick*.12*a.dir;
        a.root.scale.setScalar(reduced?.25:.25*(1+a.hover*.16-a.kick*.17));
        a.material.color.set(busy?0x9a7a84:a.target?0xffd8a1:0xe9a7bc);
      }
    },
    dispose(){geo.dispose();for(const a of arrows){scene.remove(a.root);a.material.dispose();}},
  };
}
