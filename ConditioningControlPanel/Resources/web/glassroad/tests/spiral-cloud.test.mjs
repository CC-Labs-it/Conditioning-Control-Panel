import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks=registerHooks({resolve(id,ctx,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};return files[id]?{url:new URL('../vendor/three/build/'+files[id],import.meta.url).href,shortCircuit:true}:next(id,ctx);}});
const {cloudAnchors,createSpiralCloud,CLOUD_COLOURS}=await import('../render/spiralCloud.js');
const {floorStyleAt}=await import('../render/mainframeFloor.js');
hooks.deregister();
for(const id of ['mainframe','keep','threshold'])test(id+' gate mist follows the road and pauses without losing its theme',()=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8')));
  const s=id==='mainframe'?track.sections.find(z=>z.id==='coolant-canal').fromS+100:120;
  const cloud=createSpiralCloud(track,s,23);
  assert.ok(CLOUD_COLOURS[id]);assert.ok(cloud.rows.length>=280);
  for(const r of cloud.rows){
    const f=track.surfaceAt(r.s,r.x);
    assert.ok(Math.hypot(...r.p.map((v,k)=>v-f.p[k]))<.001);
    assert.ok(Math.abs(r.x)<track.frameAt(r.s).w/2);
  }
  cloud.update(1);assert.equal(cloud.time.value,1);
  cloud.update(3,{paused:true});assert.equal(cloud.time.value,1);
  cloud.update(2,{gentle:true});assert.equal(cloud.time.value,2);cloud.dispose();
  assert.deepEqual(cloudAnchors(track,s,10,23),cloudAnchors(track,s,10,23));
});
test('Mainframe has six different room finishes and wraps the lap consistently',()=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json',import.meta.url),'utf8')));
  const styles=new Set(track.sections.map(z=>floorStyleAt(track,(z.fromS+z.toS)/2)));
  assert.equal(styles.size,7);
  assert.equal(floorStyleAt(track,30),floorStyleAt(track,track.lengthM+30));
});
