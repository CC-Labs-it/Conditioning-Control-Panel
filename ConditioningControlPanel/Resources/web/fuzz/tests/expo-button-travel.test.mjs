import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import test from 'node:test';
import {createVisit} from '../ui/expo/visit.js';
const three=new URL('../../vendor/three/three.module.min.js',import.meta.url).href;
const hook=registerHooks({resolve(specifier,context,next){
  return specifier==='three'?{shortCircuit:true,url:three}:next(specifier,context);
}});
const {createButtonTravel}=await import('../ui/expo/button-travel.js');
const {BoxGeometry,Group,Mesh,MeshBasicMaterial,Vector3}=await import(three);
hook.deregister();

function desk(settings={}){
  const buttons=createButtonTravel(settings),group=new Group(),surface=new Group();
  surface.name='hoof_console';group.add(surface);
  const caps=[0,1].map(i=>{
    const node=new Mesh(new BoxGeometry(.11,.025,.09),new MeshBasicMaterial());
    node.name='expo_press_hoof_checkbox_'+i;node.position.set(i*.35,.8,.12);
    node.userData={pressTarget:'hoof_console',pressAxis:[0,-1,0],travelMetres:.012};
    group.add(node);return node;
  });
  group.position.set(10,1,-3);group.rotation.y=1.2;group.scale.setScalar(1.35);
  buttons.mount('hoof',group);
  return {buttons,group,surface,caps,world:n=>n.getWorldPosition(new Vector3())};
}
test('hitting a physical key selects only that key and keeps its independent native linkage',()=>{
  const {buttons,surface,caps,world}=desk(),rows=buttons.resolve(surface),rest=world(caps[1]);
  assert(buttons.press(rows,{object:caps[1],point:world(caps[0])}));
  buttons.tick(.09);
  assert.deepEqual(buttons.debug().map(a=>a.amount),[0,1]);
  assert(Math.abs(world(caps[1]).distanceTo(rest)-.012)<1e-9);
  caps[1].position.x+=.2;
  buttons.tick(.4);
  assert.deepEqual(buttons.debug().map(a=>a.amount),[0,0]);assert(Math.abs(caps[1].position.x-.55)<1e-10);
  buttons.unmount('hoof');assert.equal(buttons.resolve(surface).length,0);
});
test('an adjoining display hit uses the nearest physical checkbox and coalesces repeats',()=>{
  const {buttons,surface,caps,world}=desk(),rows=buttons.resolve(surface);
  const hit={object:surface,point:world(caps[0]).add(new Vector3(.02,.16,0))};
  assert(buttons.press(rows,hit));assert.equal(buttons.press(rows,hit),false);
  buttons.tick(.12);
  assert.deepEqual(buttons.debug().map(n=>[n.pressed,n.coalesced,n.amount]),[[1,1,1],[0,0,0]]);
});
test('conditional sale hardware hides with its listing and Gentle uses two fixed poses',()=>{
  const {buttons,caps}=desk({gentle:true});
  buttons.setAvailable('hoof_console',false);assert(caps.every(n=>!n.parent.visible));
  buttons.setAvailable('hoof_console',true);assert(caps.every(n=>n.parent.visible));
  buttons.press(buttons.resolve(caps[1]),{object:caps[1]});
  for(let i=0;i<4;i++){buttons.tick(.04);assert.equal(buttons.debug()[1].amount,1);}
  buttons.tick(.04);assert.equal(buttons.debug()[1].amount,0);
});
test('both portrait stand caps use the existing replay with independent native travel',()=>{
  const buttons=createButtonTravel({}),room=new Group();
  const keys=['log_reginald','expo_replay_log_reginald'].map((name,index)=>{
    const mesh=new Mesh(new BoxGeometry(.15,.15,.06),new MeshBasicMaterial());
    mesh.name=name;mesh.position.x=index*.26;
    mesh.userData={pressAxis:[0,0,1],travelMetres:index?.02352:.022};
    room.add(mesh);return mesh;
  });
  buttons.mount('twelve',room);
  let replays=0;const action=buttons.bind(keys[1],()=>++replays);
  assert.deepEqual(new Set(action.roots),new Set(keys));
  action.activate({object:keys[0]});buttons.tick(.09);
  assert.deepEqual(buttons.debug().map(c=>[c.pressed,c.travel]),[[1,.022],[0,0]]);
  action.activate({object:keys[1]});buttons.tick(.09);
  assert.equal(buttons.debug()[1].travel,.02352);assert.equal(replays,2);
  buttons.tick(.4);assert(buttons.debug().every(c=>c.travel===0));
  buttons.unmount('twelve');assert.equal(buttons.resolve(keys[1]).length,0);
});
test('each interaction reaches the exhibit during a single mechanical excursion',()=>{
  const {buttons,surface,caps}=desk(),visit=createVisit({state:{}});
  let inputs=0;
  const hit={object:caps[0]},hoof=buttons.bind(surface,()=>++inputs);
  for(let i=0;i<6;i++)hoof.activate(hit);
  assert.equal(inputs,6);assert.equal(buttons.debug()[0].pressed,1);
  buttons.tick(.4);
  const glass=buttons.bind(surface,()=>visit.glass()),cues=[];
  for(let i=0;i<10;i++)cues.push(glass.activate(hit));
  assert.equal(visit.taps,10);
  assert.deepEqual(cues.filter(Boolean),['keep_2','keep_3','keep_4']);
  assert.equal(buttons.debug()[0].pressed,2);
});
