import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
const hooks=registerHooks({resolve(id,ctx,next){return id==='three/webgpu'?{url:new URL('../vendor/three/build/three.webgpu.min.js',import.meta.url).href,shortCircuit:true}:next(id,ctx);}});
const {NodeBuilder}=await import('three/webgpu');
const {shareInstancePrograms}=await import('../render/instancePrograms.js');
hooks.deregister();

test('only instance matrices use attributes; skeletons and unrelated renderers retain their real limit',()=>{
  for(const backend of ['webgl2','webgpu']) {
    const limit=()=>65536, renderer={backend:{capabilities:{getUniformBufferLimit:limit}}};
    shareInstancePrograms(renderer);shareInstancePrograms(renderer);
    const builder=Object.create(NodeBuilder.prototype);builder.renderer=renderer;
    builder.object={isInstancedMesh:true};assert.equal(builder.getUniformBufferLimit(),0,backend);
    builder.object={isSkinnedMesh:true};assert.equal(builder.getUniformBufferLimit(),65536,'bone UBOs stay enabled');
    builder.object={isSprite:true};assert.equal(builder.getUniformBufferLimit(),65536,'other shader buffers keep the device limit');
    assert.equal(renderer.backend.capabilities.getUniformBufferLimit,limit,'device capabilities are never overwritten');
    builder.object={isInstancedMesh:true};builder.renderer={backend:{capabilities:{getUniformBufferLimit:()=>32768}}};
    assert.equal(builder.getUniformBufferLimit(),32768,'the Loom and other renderers are untouched');
  }
});
test('instance programs are configured before compilation',()=>{
  const source=readFileSync(new URL('../render/renderer.js',import.meta.url),'utf8');
  assert.ok(source.includes('shareInstancePrograms(three);'));
});
