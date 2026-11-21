import {Box3,Group,Matrix3,Vector3} from 'three';

const down=new Vector3(0,-1,0);
const floorCaps=new Set(['press_main','press_stop','photo_release']);
const aliases={hoof_display:'hoof_console',hoof_mosaic:'hoof_console',framed_contract:'till_display'};
function frontNormal(root){
  let mesh;
  root?.traverse(n=>{if(!mesh&&n.isMesh&&n.geometry.attributes.normal)mesh=n;});
  return mesh?new Vector3().fromBufferAttribute(mesh.geometry.attributes.normal,0)
    .applyNormalMatrix(new Matrix3().getNormalMatrix(mesh.matrixWorld)).normalize():null;
}
function travelGroup(node){
  // The authored object keeps its own rest transform and any independent linkage, such as a rising plinth.
  const parent=node.parent,group=new Group();parent.add(group);group.add(node);
  return group;
}
export function createButtonTravel(ctx){
  const zones=new Map(),moving=new Set(),all=new Map(),shared=new Map(),point=new Vector3();
  let time=0;
  function mount(zone,group){
    group.updateMatrixWorld(true);
    const caps=[];group.traverse(node=>{
      if(node.userData.pressTarget||node.name.startsWith('expo_press_')||node.name.startsWith('expo_replay_')||
        node.name.startsWith('expo_control_')||floorCaps.has(node.name)||node.name==='alpaca_button')caps.push(node);
    });
    const rows=[];
    for(const node of caps){
      // GLTFLoader names children of a multi-material assembly after their parent too.
      if(caps.includes(node.parent))continue;
      const data=node.userData,target=data.pressTarget||node.name.replace(/^expo_press_/,'');
      const native=Array.isArray(data.pressAxis)&&data.pressAxis.length===3;
      const parent=node.parent,defaultDepth=floorCaps.has(node.name) ? .029 : node.name.startsWith('expo_control_') ? .009 : .010;
      const centre=node.worldToLocal(new Box3().setFromObject(node,true).getCenter(new Vector3()));
      const cap={node,target,group:travelGroup(node),parent,explicit:native,centre,zone,
        depth:data.travelMetres??defaultDepth,
        axis:new Vector3(),amount:0,from:0,start:-Infinity,presses:0,merged:0};
      const outward=node.name.startsWith('expo_replay_')?frontNormal(group.getObjectByName(node.name.replace('expo_replay_','expo_screen_'))):
        node.name.startsWith('expo_control_')?frontNormal(group.getObjectByName(node.name.replace('expo_control_','expo_display_'))):null;
      if(native)cap.axis.fromArray(data.pressAxis).transformDirection(node.matrixWorld);
      else if(floorCaps.has(node.name))cap.axis.copy(down);
      else if(outward)cap.axis.copy(outward).negate();
      cap.worldAxis=cap.axis.clone();rows.push(cap);all.set(node,cap);
      for(const name of [target].flat()){
        if(!shared.has(name))shared.set(name,[]);
        shared.get(name).push(cap);
      }
    }
    zones.set(zone,rows);
  }
  function displacement(cap,world){
    const origin=cap.parent.worldToLocal(new Vector3());
    return cap.parent.worldToLocal(world.clone().multiplyScalar(cap.depth)).sub(origin);
  }
  function resolve(mesh){
    if(all.has(mesh))return [all.get(mesh)];
    const alias=aliases[mesh.name]||mesh.name.replace(/^expo_display_/,'expo_control_');
    return shared.get(mesh.name)||shared.get(alias)||[];
  }
  function struckCap(caps,hit){
    if(caps.length<=1)return caps[0];
    for(let node=hit?.object;node;node=node.parent){
      const direct=all.get(node);
      if(direct&&caps.includes(direct))return direct;
    }
    let closest=caps[0],distance=Infinity;
    if(hit?.point)for(const cap of caps){
      const d=cap.node.localToWorld(point.copy(cap.centre)).distanceToSquared(hit.point);
      if(d<distance){distance=d;closest=cap;}
    }
    return closest;
  }
  function press(caps,hit){
    const c=struckCap(caps,hit);
    if(!c)return false;
    if(time-c.start<.085){++c.merged;return false;}
    if(c.worldAxis.lengthSq()===0){
      const direction=hit?.face?.normal?.clone().applyNormalMatrix(new Matrix3().getNormalMatrix(hit.object.matrixWorld)).negate();
      c.worldAxis.copy(direction||down);
    }
    c.axis.copy(displacement(c,c.worldAxis));c.from=c.amount;c.start=time;++c.presses;moving.add(c);
    c.amount=ctx.gentle||ctx.reduced?1:Math.max(.38,c.from);c.group.position.copy(c.axis).multiplyScalar(c.amount);
    return true;
  }
  function tick(dt){
    time+=dt;
    for(const c of moving){
      const age=time-c.start,calm=ctx.gentle||ctx.reduced;
      if(age>=(calm?.18:.31)){c.amount=0;c.group.position.set(0,0,0);moving.delete(c);continue;}
      if(calm)c.amount=1;
      else if(age<.065)c.amount=c.from+(1-c.from)*(.38+.62*Math.sin(age/.065*Math.PI*.5));
      else if(age<.13)c.amount=1;
      else{const t=(age-.13)/.18;c.amount=1-t*t*(3-2*t);}
      c.group.position.copy(c.axis).multiplyScalar(c.amount);
    }
  }
  function unmount(zone){
    for(const c of zones.get(zone)||[]){
      moving.delete(c);all.delete(c.node);
      for(const name of [c.target].flat()){
        const list=shared.get(name)?.filter(other=>other!==c)||[];
        if(list.length)shared.set(name,list);else shared.delete(name);
      }
      c.parent.add(c.node);c.group.removeFromParent();
    }
    zones.delete(zone);
  }
  function debug(){
    return [...all.values()].map(c=>({node:c.node.name,zone:c.zone,target:c.target,depth:c.depth,amount:c.amount,
      travel:c.group.position.length(),pressed:c.presses,coalesced:c.merged,authored:c.explicit}));
  }
  function setAvailable(target,on){
    for(const cap of shared.get(target)||[])cap.group.visible=on;
  }
  function bind(mesh,action){
    const caps=resolve(mesh);
    return {
      roots:[...new Set([mesh,...caps.map(c=>c.node)])],
      activate(hit){press(caps,hit);return action(hit);}
    };
  }
  return {mount,unmount,resolve,press,bind,tick,debug,setAvailable};
}
