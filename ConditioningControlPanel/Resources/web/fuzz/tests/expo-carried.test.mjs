import {strict as assert} from 'node:assert';
import test from 'node:test';
import * as THREE from '../../vendor/three/three.module.min.js';
import {visitorHands} from '../ui/expo/carried.js';
import {createWornHooves} from '../ui/expo/hoof-lab.js';
import {createStaffWing} from '../ui/expo/staff-wing.js';

function fixture(){
  const scene=new THREE.Scene(),camera=new THREE.PerspectiveCamera(),state={},walker={zone:'service'};
  const root={ownerDocument:{createElement:()=>({style:{},remove(){}})},append(){}};
  const api={THREE,camera,scene,root,walker,register:()=>()=>{}};
  const {lighting}=createStaffWing(api,state);
  let step=0;
  const hands=visitorHands(api,state,{...lighting,step:()=>step});
  const source=new THREE.MeshStandardMaterial({color:0xf5dfae,metalness:.1});
  const kit=new THREE.Mesh(new THREE.BoxGeometry(.52,.20,.32),source);
  function finish(){return scene.children.at(-1).children[0].material;}
  return {api,scene,state,walker,hands,kit,source,lighting,finish,setStep:s=>{step=s;hands.tick();}};
}

test('carried tea and a stowed tray use the current room after the pickup room unloads',()=>{
  const {state,hands,kit,source,finish}=fixture();
  const [canteen,atrium,balcony]=Array.from({length:3},()=>new THREE.Texture());
  source.envMap=canteen;
  hands.enter('service',canteen);hands.hold('tea',kit,'Tea');
  const tea=finish();
  hands.enter('atrium',atrium);canteen.dispose();
  assert.equal(tea.envMap,atrium);
  assert.equal(source.envMap,canteen);
  hands.exchange('signed-form',kit,'Form');
  assert.equal(tea.envMap,null);
  assert.equal(hands.has('tea'),true);
  hands.enter('hall',balcony);atrium.dispose();
  assert.equal(finish().envMap,balcony);
  hands.drop('signed-form');
  assert.equal(state.carry.key,'tea');
  assert.equal(finish().envMap,balcony);
  hands.drop('tea');
  assert.equal(hands.has('tea'),false);
});

test('picking up during emergency light applies the room tint once and restores the ceramic',()=>{
  const {hands,kit,source,lighting,finish,setStep}=fixture();
  const emergency=lighting.copy(source);
  lighting.shade(emergency,2);kit.material=emergency;setStep(2);
  hands.enter('service',new THREE.Texture());hands.hold('tea',kit,'Tea');
  assert.ok(finish().color.equals(emergency.color));
  setStep(0);assert.ok(finish().color.equals(source.color));
  setStep(2);assert.ok(finish().color.equals(emergency.color));
});

test('a rope post left on the floor picks up its room lighting again on return',()=>{
  const {scene,hands,kit}=fixture();
  const first=new THREE.Texture(),returned=new THREE.Texture();
  hands.enter('service',first);hands.hold('rope-post',kit,'Post');
  hands.place(new THREE.Vector3(2,0,5));
  const post=scene.children[0],material=post.children[0].material;
  hands.enter('hall',new THREE.Texture());first.dispose();
  assert.equal(post.visible,false);assert.equal(material.envMap,null);
  hands.enter('service',returned);
  assert.equal(post.visible,true);assert.equal(material.envMap,returned);
  hands.dispose();
});

test('fitting or returning the hooves preserves the tray and frees the right hoof to press',()=>{
  for(const aspect of [16/9,393/852]){
    const {api,scene,state,hands,kit,lighting}=fixture();
    api.ctx={};api.camera.aspect=aspect;
    const worn=createWornHooves(api,state,lighting);
    const gloves=['left','right'].map(side=>{
      const model=kit.clone();model.name='hoof_hand_'+side;return model;
    });
    const probe=new THREE.Texture();hands.enter('hoof',probe);worn.light('hoof',probe);
    worn.fit(gloves);hands.hold('tea',kit,'Tea');
    const tray=scene.children.at(-1),left=scene.getObjectByName('worn_hoof_hand_left');
    const right=scene.getObjectByName('worn_hoof_hand_right');
    state.hoofWorn=true;hands.tick();worn.tick(0);
    assert.equal(left.position.x,tray.position.x);
    assert.ok(left.position.y<tray.position.y&&right.position.x>tray.position.x);
    const support=left.position.clone(),rest=right.position.clone();
    worn.press();worn.tick(.23);
    assert.ok(right.position.y>rest.y&&right.position.z<rest.z);
    assert.ok(left.position.equals(support));
    state.hoofWorn=false;hands.tick();worn.tick(0);
    assert.equal(left.visible,false);assert.equal(right.visible,false);
    assert.ok(tray.position.x>0);assert.equal(state.carry.key,'tea');
    hands.dispose();worn.dispose();
  }
});
