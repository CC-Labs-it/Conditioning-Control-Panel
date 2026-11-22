export function visitorHands(api,state) {
  const {THREE:T,camera,scene}=api;
  let held;
  const putDown=new Map();
  function drop(key){
    if(!held||state.carry?.key!==key)return false;
    scene.remove(held.root);held=state.carry=null;return true;
  }
  function hold(key,node,label){
    if(held||!node)return false;
    const clone=node.clone(true);clone.visible=true;
    const root=new T.Group();clone.position.set(0,0,0);clone.rotation.set(0,0,0);root.add(clone);
    const box=new T.Box3().setFromObject(clone,true),size=box.getSize(new T.Vector3()),center=box.getCenter(new T.Vector3());
    clone.position.sub(center);root.scale.setScalar(Math.min(1,.27/Math.max(.01,size.x,size.y,size.z)));
    // Wearables and souvenirs may outlive their streamed room. Keep private material copies.
    const copies=new Map();
    clone.traverse(n=>{if(!n.isMesh)return;const take=m=>{if(!copies.has(m))copies.set(m,m.clone());return copies.get(m);};
      n.material=Array.isArray(n.material)?n.material.map(take):take(n.material);});
    scene.add(root);held={root,copies};state.carry={key,label};tick();return true;
  }
  function tick(){
    if(!held)return;held.root.position.set(Math.min(.22,camera.aspect*.22),-.19,-.49).applyQuaternion(camera.quaternion).add(camera.position);
    held.root.quaternion.copy(camera.quaternion);held.root.rotateY(-.28);
  }
  function release(key){if(!held||state.carry.key!==key)return false;for(const m of held.copies.values())m.dispose();return drop(key);}
  // Loose museum property can be left on a surface, then picked up again in another room.
  function canPlace(){return state.carry?.key==='rope-post';}
  function place(hit){
    if(!canPlace()||!hit)return false;
    const {root,copies}=held,{key,label}=state.carry;held=state.carry=null;
    const child=root.children[0];child.rotation.set(0,0,0);root.rotation.set(0,0,0);root.scale.setScalar(1);
    const box=new T.Box3().setFromObject(child,true);root.position.copy(hit);
    root.updateMatrixWorld(true);box.setFromObject(root,true);root.position.y+=hit.y-box.min.y;
    const off=api.register(root,'Pick up '+label,()=>{
      if(!hold(key,root,label))return;off();root.removeFromParent();for(const m of copies.values())m.dispose();putDown.delete(root);
    });putDown.set(root,{off,copies,zone:api.walker.zone});return true;
  }
  function enter(zone){for(const [node,row] of putDown)node.visible=row.zone===zone;}
  return {hold,drop:release,tick,canPlace,place,enter,dispose(){if(held)release(state.carry.key);
    for(const [node,row] of putDown){row.off();node.removeFromParent();for(const m of row.copies.values())m.dispose();}putDown.clear();}};

}
