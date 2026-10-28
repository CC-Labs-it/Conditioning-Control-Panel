import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks=registerHooks({resolve(id,ctx,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};return files[id]?{url:new URL('../vendor/three/build/'+files[id],import.meta.url).href,shortCircuit:true}:next(id,ctx);}});
const {cloudAnchors,createSpiralCloud,createSpiralClouds,CLOUD_COLOURS}=await import('../render/spiralCloud.js');
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

test('a shared cloud batch submits only active gates and reuses its material across transitions',()=>{
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json',import.meta.url),'utf8')));
  const cloud=createSpiralClouds(track,[{s:120},{s:800},{s:1600}]);
  const material=cloud.object.material, each=cloud.rows.length/3;
  cloud.setActive(0,false);cloud.setActive(2,false);cloud.update(1/60);
  assert.equal(cloud.object.count,each,'distant transparent particles must not reach the draw');
  cloud.setActive(1,false);cloud.update(0);
  assert.equal(cloud.object.count,0);assert.equal(cloud.object.visible,false);
  cloud.setActive(2,true);cloud.update(1/60,{gentle:true});
  assert.equal(cloud.object.count,each);assert.equal(cloud.object.visible,true);
  assert.equal(cloud.object.material,material,'reactivation never creates another shader graph');
  cloud.setActive(0,true);cloud.update(1,{paused:true});
  assert.equal(cloud.object.count,each,'paused updates freeze the submitted batch');
  cloud.update(0);assert.equal(cloud.object.count,each*2);
  cloud.dispose();
});