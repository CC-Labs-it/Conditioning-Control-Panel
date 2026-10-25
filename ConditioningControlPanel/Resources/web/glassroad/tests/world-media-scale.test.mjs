import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { largeMediaLayout, mediaRoadCorridors, WORLD_MEDIA_SCALE } from '../render/worldMediaScale.js';
import { worldScreenExistingSlots, worldScreenBlockers, worldScreenLayout, worldScreenSightlines } from '../render/worldScreens.js';
import { keepMediaLayout } from '../render/keepMedia.js';
import { boudoirMediaLayout, boudoirBoxesIntersect } from '../render/boudoirMedia.js';
import { goonworksMediaLayout } from '../render/goonworksMedia.js';
import { goonworksWorldPlan } from '../render/goonworksWorld.js';
const read = id => JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
const box = s => ({ p:s.C, size:[s.w,s.h,.1], axes:[s.X,s.Y,s.Z] });
for (const id of ['threshold','mainframe','keep','boudoir','goonworks']) test(id + ': larger authored pictures and spirals reserve the whole road and architecture', () => {
  const track=loadTrack(read(id));
  const original=id==='keep'?keepMediaLayout(track):id==='boudoir'?boudoirMediaLayout(track):id==='goonworks'?goonworksMediaLayout(track):read(id+'.media');
  const solids=id==='goonworks'?goonworksWorldPlan(track).rows:worldScreenBlockers(track);
  const before=worldScreenExistingSlots(track,original), layout=largeMediaLayout(track,original,{solids});
  const after=worldScreenExistingSlots(track,layout), corridors=mediaRoadCorridors(track);
  const originalById=new Map(before.map(s=>[s.id,s]));
  assert.equal(largeMediaLayout(track,layout),layout,'preparation is idempotent');
  assert.ok(layout.slots.length >= (original.slots?.length || original.billboards?.length || 0)*.8, 'retains authored coverage');
  for(const slot of layout.slots) {
    const old=originalById.get(slot.id);
    // Floating envelopes include fixed sway margins, so compare actual non-floating picture faces.
    if(!slot.flight){assert.ok(Math.abs(slot.w/old.w-WORLD_MEDIA_SCALE)<1e-8);assert.ok(Math.abs(slot.h/old.h-WORLD_MEDIA_SCALE)<1e-8);}
    assert.ok(![...corridors,...solids].some(row=>boudoirBoxesIntersect(box(slot),row)),slot.id+' clearance');
    assert.ok(worldScreenSightlines(track,slot,solids)>=1,slot.id+' full picture visible from the driving line');
  }
  for(const slot of layout.tunnelSlots || [])assert.ok(worldScreenSightlines(track,slot,solids)>=1,slot.id+' enlarged tunnel picture visible');
  for(let i=0;i<after.length;i++)for(let j=0;j<i;j++)assert.ok(!boudoirBoxesIntersect(box(after[i]),box(after[j])),after[i].id+' duplicate plane');
  const supplemental=worldScreenLayout(track,{existingSlots:after,solids});
  assert.ok(supplemental.slots.length>20,'large supplements still cover the lap');
  assert.ok(supplemental.slots.every(s=>s.w>=6*WORLD_MEDIA_SCALE&&s.h>=4.5*WORLD_MEDIA_SCALE));
  const heights=layout.slots.map(s=>s.h).sort((a,b)=>a-b), oldHeights=before.filter(s=>s.frame==='billboard').map(s=>s.h).sort((a,b)=>a-b);
  console.log(JSON.stringify({id,before:before.length,after:after.length,supplemental:supplemental.slots.length,medianBefore:oldHeights[Math.floor(oldHeights.length/2)],medianAfter:heights[Math.floor(heights.length/2)],visible:layout.slots.filter(s=>worldScreenSightlines(track,s,solids)>0).length}));
});
