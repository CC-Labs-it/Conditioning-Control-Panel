import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(specifier,context,next){
  const file={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'}[specifier];
  return file?{url:new URL('../vendor/three/build/'+file,import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {buildShadows}=await import('../render/shadows.js');
const T=await import('three/webgpu');hooks.deregister();

for(const provided of [false,true])test('course shadow teardown releases native VSM ownership once, '+(provided?'scene lamp':'internal lamp'),()=>{
  const scene=new T.Scene(),source=provided?new T.DirectionalLight():null;
  if(source)scene.add(source);
  const receiver=new T.Mesh(new T.PlaneGeometry(10,10),new T.MeshBasicNodeMaterial());scene.add(receiver);
  const shadows=buildShadows({three:{shadowMap:{}}},scene,source,{name:'high',shadows:'map'},
    {receivers:[receiver],karts:[new T.Group()]});
  const lamp=shadows.light,blob=scene.children.find(o=>o.isInstancedMesh);
  let lampDisposals=0,blobDisposals=0,receiverDisposals=0;
  lamp.addEventListener('dispose',()=>lampDisposals++);blob.addEventListener('dispose',()=>blobDisposals++);
  receiver.geometry.addEventListener('dispose',()=>receiverDisposals++);
  // Use the real r186 event listener and ShadowNode disposal, with GPU allocation replaced
  // by ordinary RenderTargets. All three are invisible to LightShadow.dispose().
  const lighting=new T.DirectionalLightNode(lamp),shadow=new T.ShadowNode(lamp);
  lighting.shadowNode=shadow;
  const targets=[new T.RenderTarget(8,8),new T.RenderTarget(8,8),new T.RenderTarget(8,8)];
  const materials=[new T.NodeMaterial(),new T.NodeMaterial()],released=[0,0,0],materialReleased=[0,0];
  targets.forEach((rt,i)=>rt.addEventListener('dispose',()=>released[i]++));
  materials.forEach((m,i)=>m.addEventListener('dispose',()=>materialReleased[i]++));
  [shadow.shadowMap,shadow.vsmShadowMapVertical,shadow.vsmShadowMapHorizontal]=targets;
  [shadow.vsmMaterialVertical,shadow.vsmMaterialHorizontal]=materials;
  shadows.dispose();shadows.dispose();
  assert.deepEqual(released,[1,1,1]);assert.deepEqual(materialReleased,[1,1]);
  assert.equal(lighting.shadowNode,null);assert.equal(lampDisposals,1);assert.equal(blobDisposals,1);
  assert.equal(receiverDisposals,0,'receiver geometry belongs to the road, not its shadow catcher');
  assert.equal(lamp.target.parent,null);assert.equal(blob.parent,null);assert.equal(lamp.castShadow,false);
  shadows.setEnabled(true);shadows.retier();shadows.update([0,0,0]);assert.equal(lamp.castShadow,false);
  assert.equal(shadows.mode,'off','late quality callbacks cannot rearm a disposed course');
  lighting.dispose();receiver.geometry.dispose();receiver.material.dispose();
});
