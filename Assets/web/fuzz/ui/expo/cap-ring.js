// A cap a room retires (api.setButtonAvailable(name,false)) hides only the cap. The room's own mesh still carries the
// base ring it sat on, which reads as a dark disc with a cream rim where nothing can be pressed (the Habitat glass and
// plaque, 2026-11-24). liftCapRing drops the room triangles of the cap materials that lie wholly inside the cap's own
// box, padded a little, and returns the function that puts them back. Same idea as settlement.js liftCapBase, found
// from the cap itself instead of a hand-measured point, so it works for wall caps as well as counter caps.
const CAP_MATERIALS=['action_ink','action_cream','action_coral'];

export function liftCapRing(T,room,cap,{owner='Architecture',pad=.03}={}){
  if(!room||!cap)return ()=>{};
  room.updateMatrixWorld(true);
  const box=new T.Box3().setFromObject(cap,true).expandByScalar(pad),point=new T.Vector3(),undo=[];
  if(box.isEmpty())return ()=>{};
  room.traverse(mesh=>{
    if(!mesh.isMesh||!(mesh.name===owner||mesh.parent?.name===owner))return;
    if(!CAP_MATERIALS.includes(mesh.material?.name))return;
    for(let up=mesh;up;up=up.parent)if(up===cap)return;
    const geom=mesh.geometry,position=geom.attributes.position,index=geom.index;
    if(!index||!position)return;
    const inside=i=>box.containsPoint(point.fromBufferAttribute(position,i).applyMatrix4(mesh.matrixWorld));
    const keep=[];
    for(let i=0;i<index.count;i+=3){
      const a=index.getX(i),b=index.getX(i+1),c=index.getX(i+2);
      if(!(inside(a)&&inside(b)&&inside(c)))keep.push(a,b,c);
    }
    if(keep.length===index.count)return;
    geom.setIndex(keep);undo.push(()=>geom.setIndex(index));
  });
  return ()=>undo.forEach(restore=>restore());
}
