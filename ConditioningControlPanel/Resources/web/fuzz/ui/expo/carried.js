export function visitorHands(api,state) {
  const {THREE:T,camera,scene}=api;
  let held;
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
  return {hold,drop:release,tick,dispose(){if(held)release(state.carry.key);}};
}
