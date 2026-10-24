import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {loadTrack} from '../track/load.js';
import {goonworksWorldPlan,goonworksBoxesIntersect} from '../render/goonworksWorld.js';
import {goonworksMediaLayout,goonworksSpiralLayout} from '../render/goonworksMedia.js';
import {goonworksTubeShell,goonworksPlatformGeometry} from '../render/goonworksGeometry.js';
import {goonworksScreenVisibility} from '../render/goonworksVisibility.js';
import {selectScrolllerSlots} from '../render/mediaSelection.js';
const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json',import.meta.url),'utf8')));
const plan=goonworksWorldPlan(track);
test('GOONWORKS architecture has distinct physical rooms, windowed tube and suspended local islands',()=>{
  assert.equal(goonworksWorldPlan({id:'keep'}),null);
  assert.equal(plan.rooms.length,3);assert.ok(plan.features.includes('windowed-rabbit-tube'));assert.ok(plan.features.includes('watching-grotto'));
  for(const name of['rabbit-ceiling-conduit','grotto-scalloped-rock','pinball-bumper-cap','aperture-mill-roof-beam','pop-quiz-island-keel','horizon-island','spiral-cameo-painted-face','rabbit-pipe-organ-arch','icing-observatory-tower'])
    assert.ok(plan.rows.some(r=>r.id===name),name);
  assert.ok(plan.rows.length>700);assert.ok(plan.rows.every(r=>r.p.every(Number.isFinite)&&r.size.every(v=>v>0)));
  assert.ok(!plan.rows.some(r=>/foundation|ground-plane/.test(r.id)));
  assert.equal(plan.landmarks.length,3);assert.ok(plan.landmarks.every(site=>!plan.corridor.some(c=>goonworksBoxesIntersect(site,c))));
});
test('GOONWORKS authored instance bases are orthonormal right-handed rotations, never reflections',()=>{
  const dot=(a,b)=>a.reduce((s,v,i)=>s+v*b[i],0),cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
  for(const row of plan.rows){const[x,y,z]=row.axes;
    assert.ok(Math.abs(dot(cross(x,y),z)-1)<1e-6,row.id+' reflected basis');
    for(const axis of row.axes)assert.ok(Math.abs(dot(axis,axis)-1)<1e-6,row.id+' unit axis');
    assert.ok(Math.abs(dot(x,y))+Math.abs(dot(y,z))+Math.abs(dot(z,x))<1e-6,row.id+' orthogonal basis');
  }
});
test('GOONWORKS rabbit tube has shared continuous rings and platforms have no overlapping cap faces',()=>{
  const tube=goonworksTubeShell(track);assert.ok(tube.rings>100);assert.equal(tube.positions.length/3,(tube.rings+1)*(tube.sectors+1));
  for(let row=0;row<=tube.rings;row++){
    const a=row*(tube.sectors+1)*3,b=a+tube.sectors*3;
    assert.ok(Math.hypot(...tube.positions.slice(a,a+3).map((v,k)=>v-tube.positions[b+k]))<.001);
  }
  for(let i=0;i<tube.indices.length;i+=3){const ids=Array.from(tube.indices.slice(i,i+3));
    const stations=ids.map(v=>Math.floor(v/(tube.sectors+1)));assert.ok(Math.max(...stations)-Math.min(...stations)<=1);
  }
  const cap=goonworksPlatformGeometry(30,28);assert.equal(Array.from(cap.normals).filter((v,i)=>i%3===1&&v===1).length,4);
});
test('GOONWORKS does not dress the supported race corridor with scenery',()=>{
  const exceptions=r=>r.id.startsWith('horizon-')||r.id==='white-cartoon-cloud'||r.id.endsWith('-white-bezel');
  const hits=[];
  for(const r of plan.rows){if(exceptions(r))continue;for(const c of plan.corridor)if(goonworksBoxesIntersect(r,c)){hits.push(r.id);break;}}
  assert.deepEqual(hits,[]);
});
test('GOONWORKS broad platform tops match every physical support row without merging gaps',()=>{
  const stones=track.fields.flatMap(f=>f.stones);assert.equal(plan.platforms.length,stones.length);
  for(const p of plan.platforms){assert.equal(p.w,p.source.w);assert.equal(p.len,p.source.len);assert.ok(p.w>=24);assert.ok(p.len>=24);}
  for(let i=1;i<plan.platforms.length;i++){const a=plan.platforms[i-1],b=plan.platforms[i];assert.ok(b.s-(a.s+a.len)>=1.5);}
});
test('GOONWORKS CRT cabinets give the shared media system a balanced clip and Loom layout',()=>{
  const media=goonworksMediaLayout(track);assert.ok(media.slots.length>=30);assert.equal(new Set(media.slots.map(s=>s.id)).size,media.slots.length);
  for(const s of media.slots){assert.ok(s.C.every(Number.isFinite));assert.ok(plan.rows.some(r=>r.id===s.backId));assert.ok([0,3].includes(s.program));}
  assert.equal(selectScrolllerSlots(media.slots).length,Math.ceil(media.slots.length/2));assert.deepEqual(goonworksSpiralLayout(track).gates,[]);
});
test('GOONWORKS screens show meaningful clear picture area from driver height at multiple approaches',()=>{
  const report=goonworksScreenVisibility(track,plan);assert.equal(report.length,plan.mounts.length);
  const hidden=report.filter(r=>r.visible<.5||r.approaches.filter(a=>a.visible>=.5).length<2);
  assert.deepEqual(hidden,[],'Each screen needs at least half of its face probes clear at two approach distances');
  assert.ok(report.reduce((sum,r)=>sum+r.visible,0)/report.length>.8,'Most face area should stay readable while approaching');
  assert.ok(report.every(r=>!r.blockers.some(([id])=>id===r.id+'-cabinet')),'Pictures must sit in front of their own backing');
});
const hooks=(await import('node:module')).registerHooks({resolve(specifier,context,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};return files[specifier]?{url:new URL('../vendor/three/build/'+files[specifier],import.meta.url).href,shortCircuit:true}:next(specifier,context);}});
const{buildGoonworks,goonworksRibbonShell,applyGoonworksRoad}=await import('../render/goonworks.js');
const{FrontSide,DoubleSide,MeshBasicNodeMaterial}=await import('three/webgpu');hooks.deregister();
test('GOONWORKS opaque road and tube skins cull hidden faces and keep outward triangle winding',()=>{
  const road={material:new MeshBasicNodeMaterial({side:DoubleSide})},owner=applyGoonworksRoad(road,null,track);
  assert.equal(road.material.side,FrontSide);assert.equal(road.material.depthWrite,true);owner.dispose();road.material.dispose();
  for(const[name,mesh]of[['road',track.meshes.road],['ribbon underside',goonworksRibbonShell(track)],['inner tube',goonworksTubeShell(track,false)],['outer tube',goonworksTubeShell(track,true)]]){
    const P=mesh.positions,N=mesh.normals,I=mesh.indices;
    for(let i=0;i<I.length;i+=3){const a=I[i]*3,b=I[i+1]*3,c=I[i+2]*3;
      const x=[P[b]-P[a],P[b+1]-P[a+1],P[b+2]-P[a+2]],y=[P[c]-P[a],P[c+1]-P[a+1],P[c+2]-P[a+2]];
      const facing=(x[1]*y[2]-x[2]*y[1])*N[a]+(x[2]*y[0]-x[0]*y[2])*N[a+1]+(x[0]*y[1]-x[1]*y[0])*N[a+2];
      assert.ok(facing>=-.0001,name+' triangle '+i/3+' winds against its outward normal');
    }
  }
});
test('GOONWORKS renderer prebuilds finite batches and has pause-safe platform feedback and disposal',()=>{
  const world=buildGoonworks(track);assert.ok(world.stats.parts>700);assert.equal(world.platforms.length,plan.platforms.length);
  for(const name of['goonworks-thick-ribbon','goonworks-rabbit-inner-shell','goonworks-rabbit-outer-shell'])assert.equal(world.group.getObjectByName(name).material.side,FrontSide,name);
  world.group.traverse(o=>{if(o.isInstancedMesh)assert.ok(o.instanceMatrix.array.every(Number.isFinite),o.name);});
  const p=world.platforms[0],start=p.holder.position.clone();world.hit(p.row.id);world.update(.1,{paused:true});assert.deepEqual(p.holder.position.toArray(),start.toArray());
  world.update(.1,{});assert.ok(p.holder.position.distanceTo(start)>0);world.reset();assert.deepEqual(p.holder.position.toArray(),start.toArray());world.dispose();world.dispose();
  const shell=goonworksRibbonShell(track);assert.ok(shell.indices.length>0);assert.ok(shell.indices.length<track.meshes.road.indices.length);assert.ok(shell.positions.every(Number.isFinite));
});
