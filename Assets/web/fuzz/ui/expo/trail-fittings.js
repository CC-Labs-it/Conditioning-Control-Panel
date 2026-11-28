import {dial,layThread,write} from './staff-fixtures.js';

export const teaPath={
  service:[[16.65,.008,26.10],[16.74,.008,28.90],[18.03,.008,29.40],[18.03,.008,30.56],[18.08,.008,31.58],[17.65,.008,32.05],[12,.008,32.05]],
  atrium:[[12,.008,32.05],[10.55,.008,32.05],[9.97,.008,30.76],[9.97,.008,22.0],[6.60,.008,22.0],[1.16,.008,16.56],[.56,.008,16.56],[.56,.008,16]],
  hall:[[.56,.008,16],[.23,.008,14.85],[.23,.008,7.92],[7.75,.008,7.92],[8.08,.008,7.45],[8.08,.008,6.21]]
};
// Lay the wool across each actual tread and down its riser.
for(let i=0;i<20;i++){
  const z=6.2-i*.3,y=(i+1)*.17+.008;
  teaPath.hall.push([8.08,y,z],[8.08,y,z-.295]);
}
teaPath.hall.push([8.08,3.408,-.76],[10.22,3.408,-.76],[10.22,3.408,8.4],
  [10.12,3.408,12.10],[10.78,3.408,11.95]);

export const pickupText='Carry the tea to the reading balcony. Grand Hall, upper gallery. Stamp: Delivered.';
export const corridorText='Contract corridor. Length: until read. Stop at a frame and look at it. Read eight clauses.';

export function teaThread(api,room,place,id,clean){
  return layThread(api,room,place,teaPath[id],clean,true);
}

export function trailDocket(api,place,clean,where,position,yaw=0){
  const frame=place('annex_record',position,yaw);
  frame.name=where==='tea'?'Tea round docket':'Contract corridor sign';
  frame.scale.set(1.70,.80,1);
  const panel=dial(api,frame,'record_face',clean,720,385);
  if(where==='tea')write(panel,'Tea round  /  Staff only','Carry the tea to the reading balcony.','Grand Hall, upper gallery   /   Stamp: Delivered');
  else write(panel,'Contract corridor','Length: until read.','Stop at a frame and look at it. Read eight clauses.');
  return frame;
}

export function balconyLamp(T,room,stand,clean){
  // A turned brass socket with an amber lens screwed to the lip of the docket.
  const points=[[0,0],[.065,0],[.069,.008],[.067,.020],[.049,.024],[.047,.048],[.053,.056],[.052,.063],[0,.063]];
  let finish;
  stand.traverse(n=>{if(n.material?.name==='Brass')finish=n.material;});
  const brass=finish?.clone()??new T.MeshStandardMaterial({color:0xb58d55,metalness:.60,roughness:.32});
  const levels=48,filament=new Uint8Array(levels*4);
  for(let i=0;i<levels;i++){
    const u=i/(levels-1),light=.23+.77*Math.sin(u*Math.PI*.9)**.65;
    const rib=1-.13*Math.cos(u*7*Math.PI)**10;
    filament.set([255*light*rib,232*light*rib,166*light*rib,255],i*4);
  }
  const enamel=new T.DataTexture(filament,1,levels);
  enamel.magFilter=T.LinearFilter;enamel.minFilter=T.LinearFilter;enamel.needsUpdate=true;
  const globe=new T.MeshStandardMaterial({color:0xf4d79a,emissive:0xffcf78,emissiveIntensity:0,emissiveMap:enamel,
    envMap:brass.envMap,envMapIntensity:1.25,roughness:.25,metalness:.09});
  const foot=new T.Mesh(new T.LatheGeometry(points.map(p=>new T.Vector2(...p)),24),brass);
  const lens=new T.Mesh(new T.SphereGeometry(.046,24,12),globe);
  foot.name='Balcony docket lamp';foot.position.set(10.862,4.258,12.154);
  lens.position.y=.084;lens.scale.y=1.12;lens.name='Balcony lamp lens';lens.userData.expoGuideLight=true;
  foot.add(lens);room.add(foot);
  clean.push(()=>{foot.removeFromParent();foot.geometry.dispose();lens.geometry.dispose();brass.dispose();globe.dispose();enamel.dispose();});
  return globe;
}

export function clauseLight(T,room,clean){
  const brass=new T.MeshBasicMaterial({color:0xffdb83});
  const geom=new T.BoxGeometry(1,1,.012);
  const frame=new T.InstancedMesh(geom,brass,4),part=new T.Object3D();
  frame.name='Clause reading light';frame.userData.expoGuideLight=true;frame.visible=false;room.add(frame);
  let previous;
  clean.push(()=>{frame.removeFromParent();frame.dispose();geom.dispose();brass.dispose();});
  return (found,fraction,completed)=>{
    frame.visible=!!found;
    if(!found){previous=null;return;}
    if(previous!==found){
      const w=found.custom?.60:.764,h=found.custom?.69:.987;
      const toward=found.side===-1?1:-1;
      frame.position.copy(found.pos);frame.position.y+=found.custom?.012:0;
      frame.position.z+=toward*(found.custom?.051:.018);
      const bars=[[0,h/2,w,.014],[0,-h/2,w,.014],[-w/2,0,.014,h],[w/2,0,.014,h]];
      bars.forEach(([x,y,sx,sy],i)=>{part.position.set(x,y,0);part.scale.set(sx,sy,1);part.updateMatrix();frame.setMatrixAt(i,part.matrix);});
      frame.instanceMatrix.needsUpdate=true;frame.computeBoundingSphere();previous=found;
    }
    brass.color.setHex(completed?0xd2d6a0:0xffca70);
    if(!completed)brass.color.multiplyScalar(.78+.60*Math.min(1,fraction));
  };
}
