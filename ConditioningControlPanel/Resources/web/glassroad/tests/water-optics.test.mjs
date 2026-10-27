import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three':'three.webgpu.min.js','three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[specifier]?{url:new URL('../vendor/three/build/'+files[specifier],import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {WebGLCoordinateSystem,Group,Scene,Mesh,PlaneGeometry,MeshBasicNodeMaterial,Renderer,DoubleSide,BackSide,FrontSide}=await import('three/webgpu');
const {createWaterReflections}=await import('../render/waterReflections.js');
const {REFLECT_LAYER}=await import('../render/reflectionLayer.js');
const {createWaterFoam}=await import('../render/waterFoam.js');
const {createWaterField}=await import('../render/waterField.js');
hooks.deregister();

function renderer(exclude,{fail=false}={}) {
  const state={target:{name:'main-target'},face:3,mip:2,mrt:{name:'main-mrt'},renders:[],compiles:0};
  const api={coordinateSystem:WebGLCoordinateSystem,xr:{enabled:true},shadowMap:{enabled:true},autoClear:false,
    getRenderTarget:()=>state.target,getActiveCubeFace:()=>state.face,getActiveMipmapLevel:()=>state.mip,
    setRenderTarget:(target,face=0,mip=0)=>Object.assign(state,{target,face,mip}),
    getMRT:()=>state.mrt,setMRT:mrt=>{state.mrt=mrt;},
    async compileAsync(){state.compiles++;},
    render(scene,camera){
      assert.equal(exclude.visible,false);assert.equal(api.shadowMap.enabled,false);assert.equal(api.xr.enabled,false);
      assert.equal(state.mrt,null);assert.equal(api.autoClear,true);
      if(fail)throw Error('capture test failure');
      state.renders.push({face:state.face,camera});
    },
  };
  return {api,state};
}
test('local reflection capture warms every face and restores render state before gameplay',async t=>{
  const exclude=new Group(),r=renderer(exclude),old={...r.state};
  const reflection=createWaterReflections({renderer:r.api,scene:{},exclude});t.after(()=>reflection.dispose());
  const progress=[];
  assert.equal(await reflection.warm({playerPos:[2,3,4],onProgress:(n,total)=>progress.push([n,total])}),true);
  assert.deepEqual(r.state.renders.map(x=>x.face),[0,0,1,2,3,4,5]);assert.equal(r.state.compiles,1,'one unculled compile covers all cube orientations');
  assert.deepEqual(progress,[[1,6],[2,6],[3,6],[4,6],[5,6],[6,6]]);
  assert.equal(reflection.stats.size,64);assert.equal(reflection.stats.ready,true);
  assert.equal(r.state.target,old.target);assert.equal(r.state.face,old.face);assert.equal(r.state.mip,old.mip);assert.equal(r.state.mrt,old.mrt);
  assert.equal(exclude.visible,true);assert.equal(r.api.shadowMap.enabled,true);assert.equal(r.api.xr.enabled,true);assert.equal(r.api.autoClear,false);
  reflection.update(.5,{paused:true});assert.equal(r.state.renders.length,7);
  reflection.update(.5,{wet:false});assert.equal(r.state.renders.length,7);
  reflection.update(.5,{playerPos:[10,4,9]});assert.equal(r.state.renders.length,8,'a slow frame still captures at most one face');
  assert.deepEqual(reflection.camera.position.toArray(),[10,6.4,9]);
});
test('failed reflection capture is reported and leaves no hidden water or stale render target',async t=>{
  const exclude=new Group(),r=renderer(exclude,{fail:true}),old={...r.state};
  const reflection=createWaterReflections({renderer:r.api,scene:{},exclude,quality:'phone'});t.after(()=>reflection.dispose());
  const warnings=[];t.mock.method(console,'warn',message=>warnings.push(message));
  assert.equal(await reflection.warm(),false);assert.equal(reflection.stats.ready,false);assert.equal(reflection.stats.failed,true);
  assert.match(reflection.stats.error,/capture test failure/);assert.equal(warnings.length,1);assert.equal(reflection.stats.size,32);
  assert.equal(exclude.visible,true);assert.equal(r.state.target,old.target);assert.equal(r.state.mrt,old.mrt);
  reflection.update(1);assert.equal(warnings.length,1,'failure does not retry expensive captures each frame');
});
test('reflection warm stages hidden and distant variants, then restores them even after compile failure',async t=>{
  for(const fail of [false,true]) {
    const scene=new Group(),exclude=new Group(),waterChild=new Group(),hidden=new Group(),shadowCatcher=new Group();
    exclude.add(waterChild);scene.add(exclude,hidden,shadowCatcher);shadowCatcher.layers.set(2);
    hidden.visible=false;hidden.count=0;hidden.isInstancedMesh=true;hidden.material={visible:false};
    hidden.position.set(10000,10000,10000);waterChild.visible=false;
    const r=renderer(exclude),normalRender=r.api.render;
    let compiles=0;
    r.api.compileAsync=async(_,camera)=>{
      compiles++;assert.equal(hidden.visible,true);assert.equal(hidden.frustumCulled,false);assert.equal(hidden.count,1);
      assert.equal(hidden.material.visible,true);assert.equal(exclude.visible,false);assert.equal(waterChild.visible,false);
      assert.equal(camera.layers.mask,1<<REFLECT_LAYER,'the probe sees the reflection layer only');
      assert.equal(camera.layers.test(shadowCatcher.layers),false,'no-shadow cube variants exclude shadow-only catcher materials');
      if(fail)throw Error('compile failure');
    };
    let staged=0,clean=0;
    r.api.render=(sc,camera)=>{
      if(hidden.visible) {
        staged++;assert.equal(hidden.frustumCulled,false);assert.equal(hidden.count,1);assert.equal(hidden.material.visible,true);
      } else {
        clean++;assert.equal(hidden.frustumCulled,true);assert.equal(hidden.count,0);assert.equal(hidden.material.visible,false);
      }
      assert.equal(camera.layers.mask,1<<REFLECT_LAYER,'the probe sees the reflection layer only');normalRender(sc,camera);
    };
    const reflection=createWaterReflections({scene,renderer:r.api,exclude});t.after(()=>reflection.dispose());
    t.mock.method(console,'warn',()=>{});
    assert.equal(await reflection.warm(),!fail);assert.equal(compiles,1);
    assert.equal(hidden.visible,false);assert.equal(hidden.frustumCulled,true);assert.equal(hidden.count,0);
    assert.equal(hidden.material.visible,false);assert.equal(exclude.visible,true);assert.equal(waterChild.visible,false);
    if(!fail) {
      assert.equal(staged,1,'one actual unculled draw warms runtime front/back pipeline variants');
      assert.equal(clean,6,'every exported cube face is overwritten with normal visibility');
      reflection.update(.2,{playerPos:[1,2,3]});
    }
    assert.equal(compiles,1,'normal captures never restage or recompile hidden variants');
  }
});
test('garage first: the caller\'s target is handed back around every await and the staging draw is overdrawn in the same task',async t=>{
  const exclude=new Group(),r=renderer(exclude),old={...r.state},seen=[];
  r.api.compileAsync=async()=>{r.state.compiles++;queueMicrotask(()=>seen.push(['compile-gap',r.state.target,r.state.face,r.state.mip]));};
  const reflection=createWaterReflections({renderer:r.api,scene:{},exclude});t.after(()=>reflection.dispose());
  const overdraw=()=>seen.push(['overdraw',r.state.target,r.state.face,r.state.mip,r.state.renders.length]);
  assert.equal(await reflection.warm({overdraw,onProgress:()=>seen.push(['progress',r.state.target,r.state.face,r.state.mip])}),true);
  const gaps=seen.filter(e=>e[0]!=='overdraw');
  assert.equal(gaps.length,7);
  for(const [,target,face,mip] of gaps){assert.equal(target,old.target);assert.equal(face,old.face);assert.equal(mip,old.mip);}
  const over=seen.filter(e=>e[0]==='overdraw');
  assert.deepEqual(over,[['overdraw',old.target,old.face,old.mip,1]],'one overdraw right after the staging draw, on the caller\'s target');
  assert.deepEqual(r.state.renders.map(x=>x.face),[0,0,1,2,3,4,5],'the staging face is still the first draw and gets its clean capture');
});
function track(){return {lengthM:60,nodeS:[0,60],sections:[],
  frameAt(s){return {p:[0,8,-s],b:[1,0,0],n:[0,1,0],t:[0,0,-1],w:16,surface:'stream',flow:[2,.2],flags:[]};},
  surfaceAt(s,x){return {...this.frameAt(s),p:[x,8,-s]};},
};}
test('real r186 draw dispatch warms distant transparent front/back cube pipelines before first wet capture',async t=>{
  const scene=new Scene(),exclude=new Group(),geometry=new PlaneGeometry(),material=new MeshBasicNodeMaterial({transparent:true,side:DoubleSide});
  const distant=new Mesh(geometry,material);distant.visible=false;scene.add(exclude,distant);
  const r=renderer(exclude),pipelines=new Set(),cold=[];
  const dispatcher={_currentSourceMaterial:null,_handleObjectFunction(object,mat){
    const key=mat.side;
    if(!pipelines.has(key)){pipelines.add(key);cold.push(key);}
  }};
  r.api.render=(sc,camera)=>sc.traverseVisible(object=>{
    if(object.isMesh)Renderer.prototype.renderObject.call(dispatcher,object,sc,camera,object.geometry,object.material,null,{});
  });
  const reflection=createWaterReflections({scene,renderer:r.api,exclude});
  t.after(()=>{reflection.dispose();geometry.dispose();material.dispose();});
  assert.equal(await reflection.warm(),true);
  assert.deepEqual(cold,[BackSide,FrontSide],'actual runtime cull states warmed behind the cover');
  assert.equal(material.side,DoubleSide);assert.equal(distant.visible,false);
  cold.length=0;distant.visible=true;
  reflection.update(.2,{playerPos:[800,4,0]});
  assert.deepEqual(cold,[],'first visible distant media requires no new front/back cube pipeline');
});

test('foam is bounded real geometry on wave crests and falling spill lips, with pause and reset',t=>{
  const tr=track(),field=createWaterField(tr);
  for(const s of [12,16,20,24])field.disturb({s,x:0,radius:6,strength:.5,foam:1});
  field.step(0);
  const foam=createWaterFoam(tr,field,[{fromS:12,toS:28,side:1,dropM:18}]);t.after(()=>foam.dispose());
  for(let i=0;i<18;i++)foam.update(.1,{playerS:20});
  assert.ok(foam.stats.surface>0);assert.ok(foam.stats.spill>0);
  assert.ok(foam.object.isInstancedMesh);assert.ok(foam.object.geometry.attributes.position.count>12);
  assert.equal(foam.object.count,foam.stats.active*3);assert.ok(foam.stats.active<=foam.stats.capacity);
  const data=foam.object.instanceMatrix.array;
  let minY=Infinity;
  for(let i=0;i<foam.object.count;i++){minY=Math.min(minY,data[i*16+13]);assert.ok(Math.hypot(data[i*16+4],data[i*16+5],data[i*16+6])>0,'foam has actual height');}
  assert.ok(minY<4,'spill clusters fall metres into the void');
  const before=data.slice();foam.update(.5,{paused:true});assert.deepEqual(data,before);
  foam.reset();assert.equal(foam.object.count,0);assert.equal(foam.stats.active,0);
  const phone=createWaterFoam(tr,field,[],{quality:'phone'});t.after(()=>phone.dispose());
  assert.ok(phone.stats.capacity<foam.stats.capacity);
});
