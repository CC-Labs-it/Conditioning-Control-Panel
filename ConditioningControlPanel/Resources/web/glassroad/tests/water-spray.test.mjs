import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {createState,S} from '../sim/state.js';
const hooks=registerHooks({resolve(s,c,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[s]?{url:new URL('../vendor/three/build/'+files[s],import.meta.url).href,shortCircuit:true}:next(s,c);}});
const {createWaterSpray}=await import('../render/waterSpray.js');
const {createWaterfall}=await import('../render/waterfall.js');
const {vec3}=await import('three/tsl');hooks.deregister();
const pal={col:()=>vec3(.2,.5,.7)};
const track={lengthM:60,frameAt:s=>({p:[0,5,-s],n:[0,1,0],b:[1,0,0],t:[0,0,-1],w:12,surface:s<40?'stream':'glass'}),
  surfaceAt(s,x){return {...this.frameAt(s),p:[x,5+x*x*.1,-s],n:[-.2*x,1,0]};}};

test('water droplets are bounded real geometry that rises, stretches, falls and expires',t=>{
  const spray=createWaterSpray({capacity:32,fanCapacity:4});t.after(()=>spray.dispose());
  const buffers=[spray.data,spray.fanData,spray.drops.instanceMatrix.array,spray.fans.instanceMatrix.array];
  spray.emit({p:[0,3,0],count:12,lift:3,dirX:4,dirS:-2,life:2,size:.05});
  assert.equal(spray.object.children.length,2);assert.ok(spray.drops.geometry.attributes.normal);
  assert.ok(spray.fans.geometry.attributes.uv&&spray.fans.material.opacityNode,'fan sheets soften edges, ends and camera proximity');
  assert.ok(spray.fans.geometry.attributes.position.count>=150,'curled fan strips have curved subdivided surfaces');
  assert.equal(spray.drops.material.map,null,'no illustrated atlas or billboard texture');
  assert.ok(spray.stats().active===12&&spray.stats().fanActive===1);
  const velocity=spray.data[4],size=spray.data[8];assert.ok(size<.1);
  spray.update(.2);assert.ok(spray.drops.instanceMatrix.array[13]>3);
  assert.ok(Math.abs(spray.drops.instanceMatrix.array[13]-(3+velocity*.2-4.9*.04))<.001);
  const held=[...spray.data];spray.update(1,{paused:true});assert.deepEqual([...spray.data],held);
  spray.update(1);assert.ok(spray.drops.instanceMatrix.array[13]<3,'gravity returns the jet below its source');
  spray.update(3);assert.equal(spray.stats().active,0);assert.equal(spray.stats().fanActive,0);
  for(let i=0;i<30;i++)spray.emit({p:[0,3,0],count:20});
  assert.equal(spray.stats().active,32);assert.equal(spray.stats().fanActive,4);
  buffers.forEach((b,i)=>assert.equal(b,[spray.data,spray.fanData,spray.drops.instanceMatrix.array,spray.fans.instanceMatrix.array][i]));
  spray.reset();assert.equal(spray.stats().active,0);assert.equal(spray.stats().fanActive,0);
});

test('wet driving displaces the field and side steering creates a directed persistent wake',t=>{
  const impulses=[],fluid={sample(s,x,out){return Object.assign(out,{wet:s<40,height:0});},disturb(p){impulses.push(p);}};
  const water=createWaterfall(track,pal,{fluid});t.after(()=>water.dispose());
  const st=createState();st[S.s]=10;st[S.lateral]=2;st[S.grounded]=1;st[S.velZ]=-12;
  water.kart(0,st,.12);assert.equal(impulses.length,3);
  assert.ok(impulses[0].strength>0&&impulses[1].strength<0&&impulses[2].strength<0);
  const straight=impulses[2].dirX,initial=water.spray.stats().emitted;
  st[S.velX]=6;st[S.steerSlew]=.8;st[S.driftDir]=1;water.kart(0,st,.12);
  assert.ok(impulses.at(-1).dirX>straight,'lateral momentum and steering feed the outward sheet');
  assert.ok(water.spray.stats().emitted>initial*2,'steering/drift sheds more liquid');
  assert.ok(water.spray.data[1]>5.1,'contact geometry starts on the curved bank');
  const count=impulses.length,emitted=water.spray.stats().emitted;
  water.kart(0,st,.2,{paused:true});st[S.airborne]=1;water.kart(0,st,.2);st[S.airborne]=0;
  st[S.respawnPhase]=1;water.kart(0,st,.2);st[S.respawnPhase]=0;st[S.s]=45;water.kart(0,st,.2);
  st[S.s]=10;st[S.velX]=st[S.velZ]=0;for(let i=0;i<60;i++)water.kart(0,st,.1);
  assert.equal(impulses.length,count);assert.equal(water.spray.stats().emitted,emitted,'standing still never bombs the water');
  water.reset();assert.equal(water.spray.stats().active,0);
});

test('hop and landing inject opposite impulses, use real contact and preserve pause',t=>{
  const impulses=[],water=createWaterfall(track,pal,{fluid:{disturb:p=>impulses.push(p)}});t.after(()=>water.dispose());
  water.splash({s:10,x:0,kind:'hop',speed:10,p:[100,30,20],normal:[0,1,0]});
  assert.ok(impulses[0].strength<0);assert.ok(Math.abs(water.spray.data[1]-30.12)<.001);
  water.splash({s:10,x:0,down:12,speed:25});assert.ok(impulses[1].strength>.6);
  const data=[...water.spray.data];water.update(.5,{paused:true});assert.deepEqual([...water.spray.data],data);
  water.update(.2);assert.notDeepEqual([...water.spray.data],data);
  assert.equal(water.object.type,'Group');water.reset();assert.equal(water.uniforms.t.value,0);
});

test('reactive field owns steep sheets and full canal banks, including grip support',t=>{
  const steep={lengthM:20,frameAt:s=>({p:[0,20-s,-s],n:[0,1,0],b:[1,0,0],t:[0,0,-1],w:12,surface:'stream'})};
  const impulses=[],fluid={atlasUV:()=>({wet:true}),sample:(s,x,out)=>Object.assign(out,{wet:true,height:0}),disturb:p=>impulses.push(p)};
  const water=createWaterfall(steep,pal,{fluid});t.after(()=>water.dispose());
  assert.ok(water.runs.length>0&&water.points.length>0);
  assert.equal(water.object.children.length,1,'only spray exists, no flat chute overlay masks the reactive troughs');
  const frame=steep.frameAt;steep.frameAt=s=>({...frame(s),surface:'grip'});
  const st=createState();st[S.s]=4;st[S.grounded]=1;st[S.velZ]=-10;water.kart(0,st,.1);
  assert.equal(impulses.length,3,'field wetness enables fluid interaction on a curved grip bank');
  water.update(.2,{gentle:true});assert.ok(Math.abs(water.spray.data[6]-.1)<.00001);
});

test('spray contact follows the full reactive trough and crest height',t=>{
  let height=-.75;
  const water=createWaterfall(track,pal,{fluid:{sample:(s,x,out)=>Object.assign(out,{wet:true,height})}});
  t.after(()=>water.dispose());
  water.splash({s:10,x:0,kind:'hop'});
  assert.ok(Math.abs(water.spray.data[1]-(5-.75+.12))<.001);
  water.reset();height=.75;water.splash({s:10,x:0,kind:'hop'});
  assert.ok(Math.abs(water.spray.data[1]-(5+.75+.12))<.001);
});

test('wet grip-bank airborne transitions create hop and landing impulses without duplicating stream events',t=>{
  const impulses=[],fluid={sample:(s,x,out)=>Object.assign(out,{wet:true,height:0}),disturb:p=>impulses.push(p)};
  const bank={...track,frameAt:s=>({...track.frameAt(s),surface:'grip'})};
  const water=createWaterfall(bank,pal,{fluid});t.after(()=>water.dispose());
  const st=createState();st[S.s]=10;st[S.grounded]=1;st[S.velZ]=-12;water.kart(0,st,.01);
  st[S.grounded]=0;st[S.airborne]=1;st[S.velY]=4;water.kart(0,st,.01);
  assert.equal(impulses.length,1);assert.ok(impulses[0].strength<0);
  st[S.velY]=-10;water.kart(0,st,.01);st[S.grounded]=1;st[S.airborne]=0;st[S.velY]=0;water.kart(0,st,.01);
  assert.equal(impulses.length,2);assert.ok(impulses[1].strength>.6,'landing retains pre-contact downward velocity');
  water.reset();bank.frameAt=s=>track.frameAt(s);impulses.length=0;
  water.kart(0,st,.01);st[S.grounded]=0;st[S.airborne]=1;water.kart(0,st,.01);
  st[S.grounded]=1;st[S.airborne]=0;water.kart(0,st,.01);
  assert.equal(impulses.length,0,'stream/pool hops and landings stay owned by sim splash events');
});
