import {test} from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three':'build/three.webgpu.min.js','three/webgpu':'build/three.webgpu.min.js','three/tsl':'build/three.tsl.min.js'};
  const path=files[specifier]||(specifier.startsWith('three/addons/')?specifier.slice(6):null);
  return path?{url:new URL('../vendor/three/'+path,import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {warmAll,WARM}=await import('../render/renderer.js');
const {Scene,Group,Mesh,InstancedMesh,BoxGeometry,MeshBasicNodeMaterial,PerspectiveCamera}=await import('three/webgpu');
hooks.deregister();

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
