import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {railRoutes,routeFor,routeGates,threshold,penSegments,distanceToBar,exemptRecord} from '../ui/expo/rail-routes.js';
import {railPlate,railLines} from '../ui/expo/rail-lines.js';
import {readFile} from 'node:fs/promises';
const three=new URL('../../vendor/three/',import.meta.url);
const hooks=registerHooks({resolve(id,ctx,next){
  if(id==='three'||id.startsWith('three/addons/'))return {shortCircuit:true,url:new URL(id==='three'?'three.module.min.js':id.slice(6),three).href};
  return next(id,ctx);
}});
const {createRail}=await import('../ui/expo/rail.js');
const {createNarration}=await import('../ui/expo/narration.js');
const {PerspectiveCamera}=await import('three');hooks.deregister();

function rig(flags=new Set()){
  const pending=[],spoken=[],walker={feet:{x:7.8,y:0,z:45},zone:'chronology'};
  const state={ticket:{count:0,ledger:new Map()}},voice={available:()=>true,cue(id){
    spoken.push(id);return new Promise(r=>pending.push(r));}};
  const ctx={store:{get(){},async claimLifetime(key){if(flags.has(key))return false;flags.add(key);return true;}}};
  const models={build:()=>({root:{},pose(){},dispose(){}}),dispose(){}};
  const rail=createRail({ctx,walker,state,models,voice:()=>voice,play(){}});
  return {rail,state,walker,spoken,pending,voice,up:id=>rail.debug().some(r=>r.up&&r.id===id)};
}
test('welcome belongs to the player, clears on completion and drops on pause and exit',async()=>{
  const flags=new Set(),a=rig(flags);assert(await a.rail.firstArrival());
  a.rail.greet();a.rail.tick(.4);assert(a.up('welcome'));assert.equal(a.state.railGuide,'Listen to the welcome');
  a.rail.pause(true);assert.deepEqual(a.rail.debug(),[]);a.rail.pause(false);assert(a.up('welcome'));
  a.pending.shift()(true);await Promise.resolve();assert(!a.up('welcome'));a.rail.tick(.31);assert.deepEqual(a.rail.debug(),[]);
  const b=rig(flags);assert(!await b.rail.firstArrival());assert.deepEqual(b.rail.debug(),[]);
  const legacy=rig();legacy.state.ticket.issued=true;assert(!await legacy.rail.firstArrival());
});
test('every task has a reachable origin and the staff notice can cross the service exit',()=>{
  for(const [key,from,to] of [['print-0','chronology','chronology'],['tea','service','hall'],['stapler','twelve','service'],
    ['signed-form','twelve','shop'],['souvenir:Pressed','shop','shop'],['staff-sign-0','service','twelve']]){
    const route=routeFor(key);assert(route.back&&route.rooms.includes(from)&&route.rooms.includes(to));
    const rigged=rig();rigged.walker.zone=from;rigged.state.carry={key};rigged.rail.tick(.4);
    assert(rigged.rail.debug().some(r=>r.up));rigged.state.carry=null;rigged.rail.tick(.31);assert.deepEqual(rigged.rail.debug(),[]);
  }
  assert.equal(routeFor('rope-post'),undefined);assert.equal(railRoutes.length,6);
  assert(!routeGates(routeFor('staff-sign-2')).some(d=>d.id==='staff-entrance'));
  const tea=routeGates(routeFor('tea')).map(d=>d.id);assert(!tea.includes('staff-breakroom')&&!tea.includes('hall-entrance')&&!tea.includes('staff-entrance'));
  assert(tea.includes('staff-store')&&tea.includes('keeper-passage'));
  assert(exemptRecord('ex_press')&&exemptRecord('log_lashes'));
});
test('a gate needs no streamed neighbour, blocks a jump, and waits to rise if occupied',()=>{
  const {rail,state,walker,up}=rig();walker.feet.z=41.1;state.carry={key:'print-1'};rail.tick(.1);
  assert(!up('gate:chronology-entrance'));walker.feet.z=41.45;rail.tick(.4);assert(up('gate:chronology-entrance'));
  for(const y of [0,.37,1.9]){
    const previous={x:7.8,y,z:41.41},next={...previous,z:41.38};rail.constrain(next,previous);assert.deepEqual(next,previous);
  }
  assert(!rail.allows('atrium'));state.carry=null;rail.tick(.01);assert(rail.allows('atrium'));
});
test('the arc leaves the exhibit ahead and releases only its own playback',()=>{
  const {rail,walker,up}=rig(),entry={zone:walker.zone,station:{pos:{x:7.8,z:42}}};
  assert(penSegments(walker.feet,entry.station.pos).every(s=>distanceToBar(s,walker.feet)>.3));
  rail.view(entry);assert(up('view'));rail.clearView({});assert(up('view'));
  rail.clearView(entry);assert(!up('view'));rail.tick(.31);assert.deepEqual(rail.debug(),[]);
});
test('lean and idle nudges wait for silence and share a three-line budget',async()=>{
  const {rail,state,walker,voice,spoken}=rig();state.carry={key:'tea'};walker.zone='service';rail.tick(0);
  await Promise.resolve();rail.tick(0);
  voice.available=()=>false;rail.tick(46);assert(!spoken.includes('rail_8'));
  voice.available=()=>true;rail.tick(0);assert(spoken.includes('rail_8'));
  const gate=threshold(routeGates(routeFor('tea')).find(d=>d.id==='staff-store'));
  const x=(gate.ax+gate.bx)/2,z=(gate.az+gate.bz)/2;
  for(let n=0;n<5;n++){rail.constrain({x,y:0,z:z-.27},{x,y:0,z:z-.32});rail.tick(1);}
  assert.deepEqual(spoken.filter(id=>/^rail_\d+$/.test(id)),['rail_8','rail_1','rail_2']);
  state.carry=null;rail.tick(46);assert.equal(spoken.filter(id=>id==='rail_8').length,1);
});
test('failed playback releases the viewing pen and the caption finishes on its own clock',async()=>{
  class Element extends EventTarget{
    dataset={};children=[];classList={toggle(){}};textContent='';
    append(...nodes){this.children.push(...nodes);}
    replaceChildren(){this.children=[];}
    hasAttribute(){return false;}
    setAttribute(){}
    remove(){}
  }
  const root=new Element();root.ownerDocument={createElement:()=>new Element()};
  const camera=new PerspectiveCamera(),media=new EventTarget(),holds=[];
  media.currentTime=0;
  const handle={el:media,play(){},stop(){},setGain(){}};
  const ctx={store:{get(){},set(){}},activeMs:0,audio:{stream:()=>handle}};
  const row=railLines.find(r=>r.id==='rail_welcome');
  const story=[{id:'ev_test',set:'event',where:'Example (2037)',subtitle:'Example',slides:[]}];
  const narration=createNarration({ctx,camera,root,walker:{zone:'atrium'},owned:()=>[],
    assetURL:file=>'data:application/json,'+encodeURIComponent(JSON.stringify(file.endsWith('manifest.json')?story:[])),
    viewing:e=>holds.push(e),releaseView:()=>holds.pop()});
  try{
    const station={id:'demo',zone:'atrium',pos:camera.position.clone(),project:{setActive(){},setPaused(){}}};
    const done=narration.cue(row.id,{station,projector:true});
    media.dispatchEvent(new Event('playing'));assert.equal(holds.length,1);
    media.dispatchEvent(new Event('error'));assert.equal(holds.length,0);
    const caption=root.children.at(-1).children[0];assert.equal(caption.children[0].textContent,row.subtitle);
    narration.tick(row.seconds-.01);assert.equal(narration.debug().voice.id,row.id);
    narration.tick(.02);assert(await done);assert.equal(narration.debug().voice.id,undefined);
  }finally{narration.dispose();}
});
test('all eleven lines have a packaged MP3 and a finite caption clock',async()=>{
  assert.equal(railLines.length,11);assert(railLines.find(r=>r.id==='rail_lore').subtitle.endsWith(railPlate));
  for(const r of railLines){assert(r.seconds>0&&Number.isFinite(r.seconds));const b=await readFile(new URL('../assets/expo/narration/'+r.audio,import.meta.url));assert(b.length>4000);}
});
