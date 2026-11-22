// Camera-local contact point between the left hoof and the underside of a carried item.
export function hoofSupport(aspect,position){
  const scale=Math.min(.85,aspect*.85);
  position.set(-Math.min(.22,aspect*.155),-.17-.045*scale,-.54);
  return scale;
}

export function visitorHands(api,state,lighting) {
  const {THREE:T,camera,scene}=api;
  let held,stowed,zone,environment=null,lastStep;
  const putDown=new Map();
  function light(item,probe,step){
    if(!item)return;
    for(const material of item.copies){
      if(material.isMeshStandardMaterial&&material.envMap!==probe){
        material.envMap=probe;material.needsUpdate=true;
      }
      lighting.shade(material,step);
    }
  }
  function refreshLight(){
    lastStep=lighting.step(zone);
    light(held,environment,lastStep);
    for(const [node,row] of putDown)if(node.visible)light(row,environment,lastStep);
  }
  function drop(key){
    if(!held||state.carry?.key!==key)return false;
    scene.remove(held.root);held=state.carry=null;return true;
  }
  function hold(key,node,label){
    if(held||!node)return false;
    const clone=node.clone(true);clone.visible=true;
    const root=new T.Group();clone.position.set(0,0,0);clone.rotation.set(0,0,0);root.add(clone);
    const box=new T.Box3().setFromObject(clone,true),size=box.getSize(new T.Vector3()),center=box.getCenter(new T.Vector3());
    clone.position.sub(center);
    const scale=Math.min(1,.27/Math.max(.01,size.x,size.y,size.z));
    // The kit owns the model and its artwork. Only the room lighting follows the visitor.
    const materials=new Map();
    const take=m=>{
      if(!materials.has(m)){
        const copy=lighting.copy(m);
        if(copy.isMeshStandardMaterial)copy.lightMap=null;
        materials.set(m,copy);
      }
      return materials.get(m);
    };
    clone.traverse(n=>{if(!n.isMesh)return;
      n.material=Array.isArray(n.material)?n.material.map(take):take(n.material);});
    scene.add(root);held={root,copies:[...materials.values()],scale,height:size.y*scale};
    state.carry={key,label};refreshLight();tick();return true;
  }
  // The signed form temporarily takes the visitor's hand until the till files it.
  function exchange(key,node,label){
    if(!held)return hold(key,node,label);
    if(!node||stowed)return false;
    stowed={held,carry:state.carry};scene.remove(held.root);light(held,null,0);held=state.carry=null;
    return hold(key,node,label);
  }
  function tick(){
    if(lastStep!==lighting.step(zone))refreshLight();
    if(!held)return;
    const {root,scale,height}=held;
    if(state.hoofWorn){
      const factor=hoofSupport(camera.aspect,root.position);
      root.position.y+=height*factor/2;
      root.scale.setScalar(scale*factor);
    }else{
      root.position.set(Math.min(.22,camera.aspect*.22),-.19,-.49);
      root.scale.setScalar(scale);
    }
    root.position.applyQuaternion(camera.quaternion).add(camera.position);
    root.quaternion.copy(camera.quaternion);root.rotateY(-.28);
  }
  function release(key){
    if(!held||state.carry.key!==key)return false;
    for(const m of held.copies)m.dispose();
    drop(key);
    if(stowed){held=stowed.held;state.carry=stowed.carry;stowed=null;scene.add(held.root);refreshLight();tick();}
    return true;
  }
  // Loose museum property can be left on a surface, then picked up again in another room.
  function canPlace(){return state.carry?.key==='rope-post';}
  function place(hit){
    if(!canPlace()||!hit)return false;
    const {root,copies}=held,{key,label}=state.carry;held=state.carry=null;
    const child=root.children[0];child.rotation.set(0,0,0);root.rotation.set(0,0,0);root.scale.setScalar(1);
    const box=new T.Box3().setFromObject(child,true);root.position.copy(hit);
    root.updateMatrixWorld(true);box.setFromObject(root,true);root.position.y+=hit.y-box.min.y;
    const off=api.register(root,'Pick up '+label,()=>{
      if(!hold(key,root,label))return;off();root.removeFromParent();for(const m of copies)m.dispose();putDown.delete(root);
    });putDown.set(root,{off,copies,zone:api.walker.zone});return true;
  }
  function enter(id,probe){
    zone=id;environment=probe;
    for(const [node,row] of putDown){
      node.visible=row.zone===id;
      if(!node.visible)light(row,null,0);
    }
    refreshLight();
  }
  function has(key){return state.carry?.key===key||stowed?.carry.key===key;}
  return {hold,exchange,drop:release,tick,canPlace,place,enter,has,dispose(){while(held)release(state.carry.key);
    for(const [node,row] of putDown){row.off();node.removeFromParent();for(const m of row.copies)m.dispose();}putDown.clear();}};

}
