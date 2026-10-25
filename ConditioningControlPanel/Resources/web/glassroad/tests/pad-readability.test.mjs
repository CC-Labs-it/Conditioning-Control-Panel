import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadTrack} from '../track/load.js';
import {step} from '../sim/kart.js';
import {S} from '../sim/state.js';
import {qlook} from '../core/math.js';
import {place,makeFeel,recorder,acts,DT} from './sim-fake.mjs';

for(const id of ['threshold','mainframe','keep','boudoir','goonworks']) {
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8')));
  test(id+': enlarged speed pads retain supported, separate physical footprints and exact painted bounds',()=>{
    const mesh=track.meshes.pads, seen=new Set();
    for(let i=0;i<mesh.padDesign.length;i+=4) {
      const w=Math.abs(mesh.padDesign[i]),len=mesh.padDesign[i+1],s=mesh.padDesign[i+2],x=mesh.padDesign[i+3],vertex=i/4;
      const p=track.boosts.find(p=>Math.abs(p.s-s)<.002&&Math.abs(p.x-x)<.002);
      assert.ok(p);seen.add(p);
      assert.ok(Math.abs(w-p.w)<.00001&&Math.abs(len-p.len)<.00001);
      assert.ok(Math.abs(mesh.uvs[vertex*2])<=p.w/2+.0001);
      assert.ok(mesh.uvs[vertex*2+1]>=-.0001&&mesh.uvs[vertex*2+1]<=p.len+.0001);
    }
    assert.equal(seen.size,track.boosts.length);
    for(const p of track.boosts) {
      assert.ok(p.w>=(id==='threshold'?2.25:id==='boudoir'?7.5:id==='goonworks'?7.3:6));
      assert.ok(p.len>=(['boudoir','goonworks'].includes(id)?10:7.5));
      for(let d=0;d<=p.len;d+=1.5)for(const x of[p.x-p.w/2,p.x,p.x+p.w/2]) {
        const f=track.surfaceAt(p.s+d,x),q=track.query(...f.p,f.seg);
        assert.ok(q.onRoad&&Math.abs(q.height)<.06,JSON.stringify({id,node:p.node,d,x}));
      }
    }
    for(let i=0;i<track.boosts.length;i++)for(let j=i+1;j<track.boosts.length;j++) {
      const a=track.boosts[i],b=track.boosts[j],d=(b.s-a.s+track.lengthM)%track.lengthM;
      const along=d<a.len||(track.lengthM-d)<b.len;
      assert.ok(!along||Math.abs(a.x-b.x)>=(a.w+b.w)/2+.1,'separate pad entries');
    }
  });
  test(id+': forgiving painted side boundary is the real contact boundary',()=>{
    const p=track.boosts[0],index=0;
    for(const inside of[true,false]) {
      const x=p.x+p.w/2+(inside?-.25:.25),s=p.s-.5;
      const st=place(track,{s,lateral:x,speed:18}),f=track.surfaceAt(s,x),events=recorder();
      st.set(f.p,S.posX);st.set(qlook([0,0,0,1],track.frameAt(s).t,f.n),S.quatX);
      const feel=makeFeel();
      for(let tick=0;tick<8&&st[S.s]<p.s+.5;tick++) step(st,acts({throttle:1}),track,feel,DT,events);
      assert.equal(events.all('pad').some(e=>e.payload.index===index),inside);
    }
  });
}
