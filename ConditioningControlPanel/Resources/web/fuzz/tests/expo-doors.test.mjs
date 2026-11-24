import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {readFile} from 'node:fs/promises';
import test from 'node:test';

const library=new URL('../../vendor/three/',import.meta.url);
const hook=registerHooks({resolve(name,context,next){
  if(name==='three')return {shortCircuit:true,url:new URL('three.module.min.js',library).href};
  if(name.startsWith('three/addons/'))return {shortCircuit:true,url:new URL(name.slice(6),library).href};
  return next(name,context);
}});
const {createDoors,streamingPriority}=await import('../ui/expo/doors.js');
const {Box3,BoxGeometry,Group,Mesh,MeshStandardMaterial,PerspectiveCamera,Ray,Raycaster,Vector3}=await import('three');
const {GLTFLoader}=await import('three/addons/loaders/GLTFLoader.js');
const {MeshoptDecoder}=await import('three/addons/libs/meshopt_decoder.module.js');
hook.deregister();

function room(){return {view:new Group(),environment:{texture:null}};}
function installation(gentle=false){
  const camera=new PerspectiveCamera(),walker={feet:new Vector3(7,0,32)},parts=new Map();
  for(const kind of ['staff','civic'])for(const part of ['Hinge','Leaf']){
    const source=new Mesh(undefined,new MeshStandardMaterial());
    source.userData.finish='oak';parts.set(part+'_'+kind,[source]);
  }
  parts.set('CrownShutter',[new Mesh(undefined,new MeshStandardMaterial())]);
  parts.set('Backing',[new Mesh(new BoxGeometry(1,2.18,.011),new MeshStandardMaterial())]);
  const sounds=[],scene=new Group(),doors=createDoors(scene,walker,{gentle},camera,(key)=>sounds.push(key));
  doors.setHardware({parts,dispose(){}});
  doors.mount('atrium',room());
  const state=()=>doors.debug().find(d=>d.id==='staff-entrance');
  const origin=new Vector3(10,1.2,31.94),ray=new Ray(origin,new Vector3(1,0,0));
  function approach(x){walker.feet.set(x,0,32);camera.position.copy(walker.feet);camera.lookAt(12,0,32);}
  function wait(seconds){for(let i=0;i<Math.ceil(seconds*60);i++)doors.tick(1/60);}
  return {doors,state,approach,wait,ray,sounds,walker,camera,scene};
}

test('the unloaded staff passage stays physically shut until its counterpart arrives',()=>{
  const {doors,state,approach,wait,ray,walker}=installation();
  approach(10);wait(2);
  assert.equal(state().ready,false);assert.equal(state().progress,0);assert(doors.blocked(ray,4));
  const trial=new Vector3(11.99,0,31.94);
  doors.pushOut(trial,.24,1.6,walker.feet);assert(trial.x<11.72);
  doors.mount('service',room());wait(.2);
  assert(state().progress>0&&state().progress<1);
  wait(.6);assert.equal(state().progress,1);assert(!doors.blocked(ray,4));
  doors.dispose();
});

for(const gentle of [false,true])test('jambs stay fixed, reverse approach opens away and the occupied sweep stays clear in '+(gentle?'Gentle':'Standard'),()=>{
  const {doors,state,approach,wait,sounds}=installation(gentle);
  doors.mount('service',room());const hinges=state().leaves.map(leaf=>leaf.hinge);
  approach(9);wait(.18);
  assert(state().progress>0&&state().progress<1);
  wait(1);assert(state().leaves.every(leaf=>leaf.tip[0]>12.8));
  approach(12.2);wait(2.4);assert.equal(state().progress,1);
  assert(doors.holds('service'));assert.deepEqual(state().leaves.map(leaf=>leaf.hinge),hinges);
  approach(18);wait(2.9);assert.equal(state().progress,0);
  approach(15);wait(1.2);assert(state().leaves.every(leaf=>leaf.tip[0]<11.2));
  assert.deepEqual(state().leaves.map(leaf=>leaf.hinge),hinges);assert.equal(sounds.length,2);
  doors.dispose();
});

test('streaming distinguishes stacked levels, view direction and the two annex doors',()=>{
  const priority=(a,b,p,f=[0,0,-1])=>streamingPriority(a,b,new Vector3(...p),false,new Vector3(...f));
  const wants=(a,b,p,f)=>{const s=priority(a,b,p,f);return s.distance<s.near;};
  for(const wing of ['chronology','aquarium'])assert(!wants('atrium',wing,[.22,0,36.21]));
  assert(wants('atrium','chronology',[4.2,0,38.8]));assert(!wants('atrium','aquarium',[4.2,0,38.8]));
  assert(wants('service','atrium',[24,5.1,23.2]));assert(!wants('service','atrium',[29.1,0,41]));
  assert(wants('catwalk','dome',[-22,11.95,-5]));assert(wants('hall','twelve',[0,0,14]));
  assert(!wants('hall','twelve',[0,0,14],[0,0,1]));
  assert(!wants('service','aquarium',[0,0,0]));
});

test('all three gallery elbows have a doorway before the neighboring assets arrive',()=>{
  const {doors,scene}=installation();
  const pairs=[['chronology','forum','archive-to-forum',4.70],['forum','commons','forum-to-commons',4.70],['commons','aquarium','aquarium-climate-doors',4.25]];
  for(const [inside,outside,id,width] of pairs){
    doors.mount(inside,room());const sealed=doors.debug().find(d=>d.id===id);
    assert(sealed);assert.equal(sealed.ready,false);assert.equal(sealed.progress,0);
    const a=new Vector3().fromArray(sealed.leaves[0].hinge),b=new Vector3().fromArray(sealed.leaves[1].hinge);
    // A 75 mm rebate on each side leaves the hinge barrel clear throughout its swing.
    assert(Math.abs(a.distanceTo(b)-(width-.15))<1e-8);
    assert(scene.getObjectByName('threshold_liner_'+id));
    assert(doors.debug().some(d=>d.id===id+'-jamb-0'));
    doors.mount(outside,room());assert(doors.debug().find(d=>d.id===id).ready);
    doors.unmount(inside);doors.unmount(outside);
  }
  doors.dispose();
});

test('the small viewing hatch stays sealed from the atrium while the catwalk is missing',()=>{
  const {doors,walker,camera,wait}=installation(),ray=new Ray(new Vector3(-10,8.16,28),new Vector3(-1,0,0));
  assert(!doors.debug().find(d=>d.id==='inspection-shutter').ready);
  assert(doors.blocked(ray,2.8));
  doors.mount('catwalk',room());walker.feet.set(-9.9,6.8,28.3);camera.position.copy(walker.feet);camera.lookAt(-12,6.8,28);
  wait(1);const lifted=doors.debug().find(d=>d.id==='inspection-shutter');
  assert.equal(lifted.leaves[0].degrees,0);assert(Math.abs(lifted.leaves[0].hinge[1]-8.68)<1e-8);
  assert(!doors.blocked(ray,2.8));
  doors.dispose();
});

test('the aquarium return leaves the near-side right turn clear when arriving from the tunnel',()=>{
  const {doors,camera,walker,wait}=installation();
  doors.mount('aquarium',room());
  walker.feet.set(-7.8,0,43.1);camera.position.copy(walker.feet);camera.lookAt(-7.8,0,41);wait(1);
  const pair=doors.debug().find(d=>d.id==='aquarium-return');assert.equal(pair.progress,1);
  assert(pair.leaves.every(leaf=>leaf.tip[2]>42.4));
  const aisle=new Ray(new Vector3(-7.8,1.2,39.99),new Vector3(1,0,0));assert(!doors.blocked(aisle,4.2));
  doors.dispose();
});

test('the upper inspection return and its finish stay fixed as the atrium streams',()=>{
  const {doors,scene}=installation(),corridor=room();
  const material=new MeshStandardMaterial();material.name='pale_shell';
  const wall=new Mesh(new BoxGeometry(.15,3.07,2.49),material);
  wall.name='catwalk_corridor_endcap';wall.userData.solidStairEnd=true;corridor.view.add(wall);
  const lining=()=>scene.getObjectByName('catwalk_inspection_wall_return');
  const side=new Ray(new Vector3(-13.3,8.40,26.55),new Vector3(1,0,0));
  doors.mount('catwalk',corridor);const installed=lining();assert(installed);
  for(let load=0;load<2;load++){
    doors.unmount('atrium');assert.equal(lining().count,5);assert.equal(lining().material,material);
    assert(doors.blocked(side,2));const visitor=new Vector3(-12,6.8,26.55);
    doors.pushOut(visitor,.24,1.6,new Vector3(-12.4,6.8,26.55));assert(visitor.x<-12.24);
    doors.mount('atrium',room());assert.equal(lining(),installed);assert(doors.blocked(side,2));
  }
  doors.unmount('atrium');doors.unmount('catwalk');assert.equal(lining(),undefined);assert(!doors.blocked(side,2));
  doors.dispose();wall.geometry.dispose();material.dispose();
});

test('a catwalk mounted before hardware receives its endcap and lining together',()=>{
  const scene=new Group(),doors=createDoors(scene,{feet:new Vector3()},{}),source=new Mesh(new BoxGeometry(2.49,3.07,.15),new MeshStandardMaterial());
  source.userData.finish='plaster';doors.mount('catwalk',room());
  assert(!scene.getObjectByName('catwalk_corridor_endcap'));doors.setHardware({parts:new Map([['StairEnd',[source]]]),dispose(){}});
  assert(scene.getObjectByName('catwalk_corridor_endcap'));const lining=scene.getObjectByName('catwalk_inspection_wall_return');assert(lining);
  doors.mount('atrium',room());assert.equal(scene.getObjectByName('catwalk_inspection_wall_return'),lining);
  doors.unmount('catwalk');assert(!scene.getObjectByName('catwalk_inspection_wall_return'));doors.dispose();
  source.geometry.dispose();source.material.dispose();
});

test('the shipped main hall frame and plaster keep their visible surfaces on both sides',async()=>{
  const assets=new URL('../assets/expo/',import.meta.url),zones=new URL('zones/',assets);
  const plan=JSON.parse(await readFile(new URL('zones.json',zones)));
  const loader=new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  loader.register(parser=>({name:'geometryOnly',beforeRoot(){
    parser.json.materials=parser.json.materials.map(({name,doubleSided})=>({name,doubleSided}));
  }}));
  async function mesh(url){
    const bytes=await readFile(url);
    return (await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'')).scene;
  }
  const rooms={};
  for(const id of ['hall','atrium'])rooms[id]=await mesh(new URL(plan.zones.find(z=>z.id===id).file,zones));
  const parts=new Map();(await mesh(new URL('door-hardware.glb',assets))).traverse(node=>{
    const key=node.userData.assembly;if(!node.isMesh||!key)return;
    if(!parts.has(key))parts.set(key,[]);parts.get(key).push(node);
  });
  for(const [home,away,side] of [['hall','atrium',-1],['atrium','hall',1]]){
    const scene=new Group(),doors=createDoors(scene,{feet:new Vector3()},{});
    doors.setHardware({parts,dispose(){}});
    function mount(id){const view=rooms[id];scene.add(view);view.updateMatrixWorld(true);doors.mount(id,{view,environment:{texture:null}});}
    const probes=[[1.60,1.51],[1.49,1.05],[0,2.353],[.15,2.41],[.19,3.41],[4.01,4.11],[-4.7,6.61]];
    function surfaces(){
      scene.updateMatrixWorld(true);
      const meshes=[];
      for(const group of [rooms[home].getObjectByName('Architecture'),scene.getObjectByName('swing_hall-entrance'),
                         rooms[away].parent===scene&&rooms[away].getObjectByName('Architecture')]){
        if(group)group.traverseVisible(n=>{if(n.isMesh)meshes.push(n);});
      }
      return probes.map(([x,y])=>new Raycaster(new Vector3(x,y,16+side*.65),new Vector3(0,0,-side),0,1.3).intersectObjects(meshes,false)[0]);
    }
    mount(home);const single=surfaces();mount(away);const both=surfaces();
    for(let i=0;i<probes.length;i++){
      assert(single[i]&&both[i],home+' uncovered frame '+probes[i]);
      assert.equal(both[i].object.uuid,single[i].object.uuid,home+' foreign finish '+probes[i]);
      assert.equal(both[i].distance,single[i].distance);
    }
    const guide=rooms[home].getObjectByName('Listening_route_to_'+away);
    const b=new Box3().setFromObject(guide);
    assert(b.max.y<.044,'raised floor lettering must clear the moving bottom rail');
    assert(Math.abs((b.min.z+b.max.z)*.5-(16+side*1.01))<.02,'the printed floor marker must keep its baked footprint');
    doors.dispose();scene.clear();
  }
});

test('the door waits for a visitor on the opening side of a fixed swing',()=>{
  const {doors,walker,camera,wait}=installation();doors.mount('aquarium',room());
  walker.zone='aquarium';walker.feet.set(-8.45,0,41.55);camera.position.copy(walker.feet);camera.lookAt(-8.45,0,41.10);
  wait(1.5);const swinging=()=>doors.debug().find(d=>d.id==='aquarium-return');
  const partial=swinging().progress;assert(partial>0&&partial<1);
  wait(.6);assert.equal(swinging().progress,partial);
  walker.feet.z=43.7;wait(1.5);assert.equal(swinging().progress,1);doors.dispose();
});

test('the resident side, the opaque seal and transitive door sightlines follow the actual swing',()=>{
  const {scene,doors,walker,camera,wait,approach}=installation();
  const hallDoor=scene.getObjectByName('swing_hall-entrance'),seal=hallDoor.children[0];
  assert(seal.visible);doors.mount('hall',room());doors.mount('service',room());assert(seal.visible);
  const reach=id=>[...doors.connectVisibleRooms(new Set([id]))].sort();
  walker.zone='service';walker.feet.set(0,0,17.9);camera.position.copy(walker.feet);camera.lookAt(0,0,16);
  wait(.3);assert.deepEqual(reach('hall'),['hall']);assert(seal.visible);
  walker.zone='atrium';wait(.12);assert.deepEqual(reach('hall'),['atrium','hall']);assert(!seal.visible);
  approach(10);wait(.15);assert.deepEqual(reach('hall'),['atrium','hall','service']);
  doors.unmount('atrium');assert(seal.visible);assert.deepEqual(reach('hall'),['hall']);doors.dispose();
});

test('the crown aperture retains its chamber until the moving shades have closed',()=>{
  const {doors,walker,wait}=installation(true),shade=()=>doors.debug().find(d=>d.id==='crown-shutter');
  walker.zone='catwalk';walker.feet.set(-22,11.95,-5);doors.mount('catwalk',room());wait(1);
  assert.equal(shade().progress,0);
  doors.mount('dome',room());wait(.2);
  assert(shade().progress>0&&shade().progress<1);
  assert(doors.connectVisibleRooms(new Set(['catwalk'])).has('dome'));wait(1);assert.equal(shade().progress,1);
  walker.feet.set(-13,6.8,21);wait(.15);assert(doors.holds('dome'));
  wait(1.2);assert.equal(shade().progress,0);assert(!doors.holds('dome'));
  assert(!doors.connectVisibleRooms(new Set(['dome'])).has('catwalk'));
  doors.dispose();
});
