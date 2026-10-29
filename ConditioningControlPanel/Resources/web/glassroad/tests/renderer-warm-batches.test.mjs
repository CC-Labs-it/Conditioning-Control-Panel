import {test} from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three':'build/three.webgpu.min.js','three/webgpu':'build/three.webgpu.min.js','three/tsl':'build/three.tsl.min.js'};
  const path=files[specifier]||(specifier.startsWith('three/addons/')?specifier.slice(6):null);
  return path?{url:new URL('../vendor/three/'+path,import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {warmAll,warmInset,WARM}=await import('../render/renderer.js');
const {Scene,Group,LOD,Mesh,InstancedMesh,BoxGeometry,MeshBasicNodeMaterial,PerspectiveCamera}=await import('three/webgpu');
hooks.deregister();

test('shadowless hit-camera warm retains its real layers and restores renderer switches',async()=>{
  const scene=new Scene(),camera=new PerspectiveCamera(),geometry=new BoxGeometry();
  const road=new Mesh(geometry,new MeshBasicNodeMaterial()),catcher=new Mesh(geometry,new MeshBasicNodeMaterial());
  catcher.layers.set(2);catcher.visible=false;scene.add(road,catcher);
  const original={name:'main'},inset={name:'inset'};let target=original,mrt='main',compiled=0;
  const three={shadowMap:{enabled:true},autoClear:false,getRenderTarget:()=>target,getMRT:()=>mrt,
    setRenderTarget:value=>{target=value;},setMRT:value=>{mrt=value;},clear(){},
    async compileAsync(){
      assert.equal(target,inset);assert.equal(this.shadowMap.enabled,false);assert.equal(mrt,null);
      assert.ok(road.layers.test(camera.layers));assert.equal(catcher.layers.test(camera.layers),false);
      compiled++;
    }};
  await warmInset(three,{inset:{target:inset}},scene,camera);
  assert.equal(compiled,1);assert.equal(camera.layers.mask,1);assert.equal(catcher.visible,false);
  assert.equal(target,original);assert.equal(mrt,'main');assert.equal(three.shadowMap.enabled,true);assert.equal(three.autoClear,false);
  geometry.dispose();road.material.dispose();catcher.material.dispose();
});

test('hit-camera warm prepares every distance variant before its first rival cut',async()=>{
  const scene=new Scene(),camera=new PerspectiveCamera(),geometry=new BoxGeometry(),lod=new LOD();
  const near=new Mesh(geometry,new MeshBasicNodeMaterial()),far=new Mesh(geometry,new MeshBasicNodeMaterial());
  lod.addLevel(near,0);lod.addLevel(far,200);scene.add(lod);lod.update(camera);
  const seen=new Set(),target={};
  const three={getRenderTarget:()=>null,setRenderTarget(){},clear(){},async compileAsync(){
    // Three's projection selects the current LOD during compileAsync, just as it does in render.
    if(lod.autoUpdate)lod.update(camera);
    scene.traverseVisible(o=>{if(o.isMesh)seen.add(o);});
  }};
  await warmInset(three,{inset:{target}},scene,camera);
  assert.ok(seen.has(near));
  assert.ok(seen.has(far),'the first distant hit must not link a new inset program');
  assert.equal(lod.autoUpdate,true);assert.equal(near.visible,true);assert.equal(far.visible,false);
  camera.position.z=300;camera.updateMatrixWorld();lod.update(camera);
  assert.equal(near.visible,false);assert.equal(far.visible,true);
  near.material.dispose();far.material.dispose();geometry.dispose();
});

test('a failed hit-camera warm restores automatic and manual LOD policies',async()=>{
  const scene=new Scene(),camera=new PerspectiveCamera(),auto=new LOD(),manual=new LOD();
  manual.autoUpdate=false;scene.add(auto,manual);
  const warn=console.warn;let during;
  try{
    console.warn=()=>{};
    await warmInset({getRenderTarget:()=>null,setRenderTarget(){},async compileAsync(){
      during=[auto.autoUpdate,manual.autoUpdate];
      throw Error('test inset compilation failure');
    }},{inset:{target:{}}},scene,camera);
  }finally{console.warn=warn;}
  assert.deepEqual(during,[false,false]);
  assert.equal(auto.autoUpdate,true);assert.equal(manual.autoUpdate,false);
});

test('larger WebGPU warm batches keep every render variant and restore hidden pools, materials and layers',async()=>{
  const results={};
  for(const backend of ['webgpu','webgl2','unknown']){
    const scene=new Scene(),holder=new Group(),camera=new PerspectiveCamera(),geometry=new BoxGeometry();
    holder.visible=false;scene.add(holder);camera.layers.set(4);
    const objects=Array.from({length:130},()=>new Mesh(geometry,new MeshBasicNodeMaterial()));
    holder.add(...objects);
    const receiver=new Mesh(geometry,new MeshBasicNodeMaterial());receiver.receiveShadow=true;scene.add(receiver);
    const pooled=new InstancedMesh(geometry,new MeshBasicNodeMaterial(),1);
    pooled.count=0;pooled.visible=false;pooled.material.visible=false;scene.add(pooled);
    const warmed=new Set(),groups=[];let first=true;
    const post={
      async compileAsync(){
        assert.equal(camera.layers.mask,-1,'every layer is prepared behind the cover');
        let fresh=0;
        scene.traverseVisible(object=>{
          if(!object.isMesh)return;
          assert.equal(object.frustumCulled,false);
          if(object===receiver)return;
          if(!warmed.has(object.material)){warmed.add(object.material);fresh++;}
          if(object===pooled){assert.equal(object.count,1);assert.equal(object.material.visible,true);}
        });
        assert.ok(receiver.visible,'shadow receivers stay in each preparation pass');
        if(first){assert.equal(fresh,0);first=false;}else groups.push(fresh);
      },render(){},
    };
    const progress=[];
    const result=await warmAll({backend,post:()=>post},scene,camera,[],{onProgress:(done,total)=>progress.push([done,total])});
    const limit=backend==='webgpu'?WARM.webgpuPerGroup:WARM.perGroup;
    assert.equal(result.groups,Math.ceil(131/limit));assert.equal(warmed.size,131);
    assert.ok(groups.every(n=>n<=limit));assert.deepEqual(progress.at(-1),[result.groups+1,result.groups+1]);
    assert.equal(holder.visible,false);assert.equal(pooled.visible,false);assert.equal(pooled.material.visible,false);
    assert.equal(pooled.count,0);assert.equal(camera.layers.mask,16);assert.equal(scene.children.length,3);
    for(const object of [...objects,receiver,pooled]){assert.equal(object.frustumCulled,true);object.material.dispose();}
    pooled.dispose();geometry.dispose();results[backend]=result.groups;
  }
  assert.ok(results.webgpu<results.webgl2);assert.equal(results.unknown,results.webgl2,'unknown backends retain the serial budget');
});

test('covered warm freezes distance selection so the intro can cut to every LOD without a new shader',async()=>{
  const scene=new Scene(),camera=new PerspectiveCamera(),geometry=new BoxGeometry(),lod=new LOD();
  const near=new Mesh(geometry,new MeshBasicNodeMaterial()),far=new Mesh(geometry,new MeshBasicNodeMaterial());
  lod.addLevel(near,0);lod.addLevel(far,200);scene.add(lod);lod.update(camera);
  assert.equal(far.visible,false);
  const seen=new Set();
  const project=()=>{
    if(lod.autoUpdate)lod.update(camera);
    scene.traverseVisible(o=>{if(o.isMesh)seen.add(o);});
  };
  await warmAll({backend:'webgpu',post:()=>({async compileAsync(){project();},render:project})},scene,camera);
  assert.ok(seen.has(near));assert.ok(seen.has(far),'the distant finish portal is prepared under the cover');
  assert.equal(lod.autoUpdate,true);assert.equal(near.visible,true);assert.equal(far.visible,false);
  camera.position.z=300;camera.updateMatrixWorld();lod.update(camera);
  assert.equal(near.visible,false);assert.equal(far.visible,true,'normal distance selection resumes');
  near.material.dispose();far.material.dispose();geometry.dispose();
});

test('garage first: compileStart is preferred, every warm frame is overdrawn in the same task, a dead lifetime stops and restores',async()=>{
  const scene=new Scene(),holder=new Group(),camera=new PerspectiveCamera(),geometry=new BoxGeometry();
  holder.visible=false;scene.add(holder);
  holder.add(...Array.from({length:30},()=>new Mesh(geometry,new MeshBasicNodeMaterial())));
  const log=[];
  const post={async compileAsync(){log.push('compileAsync');},async compileStart(){log.push('compileStart');},render(){log.push('render');}};
  const result=await warmAll({backend:'webgpu',post:()=>post},scene,camera,[],{overdraw:()=>log.push('overdraw')});
  assert.ok(!log.includes('compileAsync'),'the start form hands the canvas back before the pipelines build');
  assert.ok(log.filter(e=>e==='compileStart').length>=2);
  for(let i=0;i<log.length;i++)if(log[i]==='render')assert.equal(log[i+1],'overdraw','a warm frame is never the presented one');
  assert.equal(result.stopped,false);assert.equal(holder.visible,false);
  // the page went away (course switch) mid-warm: the warm stops after its current group and leaves the scene as it found it
  let alive=true;const stopLog=[];
  const stopped=await warmAll({backend:'webgpu',post:()=>({async compileStart(){stopLog.push('c');alive=false;},render(){stopLog.push('r');}})},scene,camera,[],{alive:()=>alive});
  assert.equal(stopped.stopped,true);assert.ok(!stopLog.includes('r'),'nothing drawn after the stop');
  assert.equal(holder.visible,false);scene.traverse(o=>{if(o.isMesh)assert.equal(o.frustumCulled,true);});
  holder.children.forEach(o=>o.material.dispose());geometry.dispose();
});

test('a failed warm returns each LOD to its original automatic or manual selection policy',async()=>{
  const scene=new Scene(),camera=new PerspectiveCamera(),auto=new LOD(),manual=new LOD();
  manual.autoUpdate=false;scene.add(auto,manual);
  const oldWarn=console.warn;
  try{
    console.warn=()=>{};
    await warmAll({backend:'webgpu',post:()=>({async compileAsync(){
      assert.equal(auto.autoUpdate,false);assert.equal(manual.autoUpdate,false);throw Error('test compilation failure');
    },render(){}})},scene,camera);
  }finally{console.warn=oldWarn;}
  assert.equal(auto.autoUpdate,true);assert.equal(manual.autoUpdate,false);
});
