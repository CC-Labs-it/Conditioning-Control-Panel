import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadTrack} from '../track/load.js';
import {keepMediaLayout} from '../render/keepMedia.js';
import {selectScrolllerSlots} from '../render/mediaSelection.js';
import {rectPoints,tierSlots,mediaLook} from '../render/mediaMath.js';
const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json',import.meta.url),'utf8')));
const layout=keepMediaLayout(track),M=mediaLook();
test('Keep has close wall panels and overhead arches with a half-video half-Loom mix on each tier',()=>{
  assert.equal(keepMediaLayout({id:'threshold'}),null);
  assert.equal(layout.slots.length,42);assert.equal(new Set(layout.slots.map(s=>s.id)).size,42);
  for(const tier of ['high','mid','phone']){
    const visible=tierSlots(layout.slots,tier,M),chosen=selectScrolllerSlots(visible);
    for(const frame of ['billboard','halo','tunnel']){
      const all=visible.filter(s=>s.frame===frame),videos=chosen.filter(s=>s.frame===frame);
      assert.ok(all.length>=8);assert.equal(videos.length,all.length/2);
    }
    assert.ok(visible.every(s=>[0,3].includes(s.program)),'procedural panels remain real Loom programs');
  }
});
test('Keep panels fit inside fortress wall faces and overhead panels stay above the racing clearance',()=>{
  for(const slot of layout.slots){
    const f=track.frameAt(slot.s),points=rectPoints(slot),project=(p,axis)=>p.reduce((n,v,i)=>n+(v-f.p[i])*axis[i],0);
    assert.ok(points.every(p=>p.every(Number.isFinite)));
    if(slot.id.startsWith('keep-spiral-'))assert.ok(points.every(p=>project(p,f.n)>=5.99),'above crenels');
    if(slot.frame==='halo')assert.ok(points.every(p=>project(p,f.n)>=11.49));
    else for(const p of points){
      assert.ok(Math.abs(Math.abs(project(p,f.b))-(f.w/2+.2))<.001,'inside stone wall '+slot.id);
      assert.ok(project(p,f.n)>=.99,'raised from kart floor '+slot.id);
    }
  }
});