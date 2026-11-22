import {reviewPack, packFile} from './expo-review-pack.mjs';
// Exercise every published mechanical cap with real keyboard and touchscreen actions.
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {ARCHIVE_SCREENS} from '../ui/expo/archive-screens.js';
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright');
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8942/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const out=resolve(process.argv[2]),filter=new Set((process.argv[3]||'').split(',').filter(Boolean));
// Every archive press plays a whole record and its cap stays down until the record ends: speed the recordings up.
const rate=Number(process.env.EXPO_REVIEW_RATE||8);
assert(Number.isFinite(rate)&&rate>=.25&&rate<=8,'Playback rate must be between 0.25 and 8');
await mkdir(out,{recursive:true});
const points={hall:[0,0,-7.5],atrium:[.22,0,36.21],hoof:[-18,0,8],habitat:[13,0,7],shop:[15,0,-5],twelve:[0,0,-25.15],
 catwalk:[-13.2,6.8,22],service:[13.6,0,32],secret:[83,0,41],dome:[-20,0,-5],
 chronology:[7.8,0,43],forum:[0,0,103],commons:[-66.2,0,95],aquarium:[-59,0,45.5]};
const report={base,rate,devices:[],errors:[]};
// photo_release shows when the floor hatch opens.
const required={
 hall:['press_main','expo_replay_ex_press','expo_control_grid'],atrium:['expo_control_greenhouse'],
 habitat:['expo_press_habitat_glass','expo_press_feeding_slot','expo_press_habitat_plaque'],
 shop:['expo_press_till_display'],
 catwalk:['expo_control_terrace'],service:['expo_control_station'],twelve:['photo_release']
};
const revealed=new Set(['photo_release']);
// Caps the building still carries but no longer offers: the counter keeps one button (the till's), every archive
// pedestal one (its replay cap), the hoof lab plays on its picture board, and the First Press closes on its small
// switch, so the old stop cap beside the main one stays hidden.
const retired=name=>/^expo_review_slot_/.test(name)||/^expo_press_hoof_checkbox_/.test(name)||
 ['expo_press_framed_contract','expo_press_exit_sale','press_stop'].includes(name);
const speed=context=>context.addInitScript(rate=>{
 const play=HTMLMediaElement.prototype.play;
 HTMLMediaElement.prototype.play=function(){
  if(this.src.includes('/narration/')){this.playbackRate=rate;this.addEventListener('playing',()=>{this.playbackRate=rate;},{once:true});}
  return play.call(this);
 };
},rate);
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',
 args:['--mute-audio','--autoplay-policy=no-user-gesture-required','--force_low_power_gpu']});
try{
 for(const touch of [false,true]){
  const device=touch?'touch':'keyboard',ctx=await browser.newContext({hasTouch:touch,isMobile:touch,
   reducedMotion:touch?'reduce':'no-preference',viewport:touch?{width:393,height:852}:{width:1440,height:810}});
  await speed(ctx);
  let packRoot;
  const page=await ctx.newPage(),rows=[],files=[],pending=[];
  report.devices.push({device,rows,files});page.on('pageerror',err=>report.errors.push(err.message));
  page.on('response',r=>{
   const name=packFile(r.url(),packRoot,'zones/');
   if(!name||!name.match(/\.glb(?:\.gz)?$/))return;
   const entry={file:name};files.push(entry);pending.push(r.body().then(bytes=>{
    entry.bytes=bytes.length;entry.sha256=createHash('sha256').update(bytes).digest('hex');
   }).catch(error=>{entry.error=error.message;}));
  });
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
  packRoot=await reviewPack(page);
  await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
  // A mouse click on the way in would put a touch visit into keyboard mode and hide the Use button.
  const f=page.frames().find(fr=>fr.url().includes('/fuzz/index.html'));const warp=f.locator('.expo-worm #btn');
  if(touch)await warp.tap();else await warp.click();
  await f.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
  // Every control answers "Listen to the welcome" until the welcome rail lowers.
  await f.waitForFunction(()=>!__expo.rails().some(r=>r.up&&r.id==='welcome'),null,{timeout:90000});
  const use=()=>touch?f.locator('.expo-interact').tap():page.keyboard.press('e');
  const known=await f.evaluate(async()=>(await(await fetch(window.expoReviewAssetURL('zones/zones.json'))).json()).zones.map(z=>z.id));
  const route=await f.evaluate(async()=>(await(await fetch(window.expoReviewAssetURL('station-route.json'))).json()).slots.map(s=>({id:s.id,zone:s.zone})));
  // The screens that are built, by room: the route file may list more bays than the building puts up.
  const screensIn=zone=>route.filter(s=>s.zone===zone&&ARCHIVE_SCREENS.includes(s.id)).map(s=>'expo_replay_'+s.id).sort();
  const story=()=>f.evaluate(()=>__expo.narrationState().archive);
  const capRow=name=>f.evaluate(n=>__expo.buttons().find(c=>c.node===n),name);
  // Stand where a direct ray reaches the cap, or report the nearest thing the reticle found instead.
  const approach=(zone,name)=>f.evaluate(async({zone,name})=>{
   const e=__expo,T=await import('three'),n=e.scene.getObjectByName(name);
   if(!n)return null;
   const centre=new T.Box3().setFromObject(n,true).getCenter(new T.Vector3());
   if(centre.lengthSq()===0&&!n.visible)n.getWorldPosition(centre);
   const inward=new T.Vector3().fromArray(n.userData.pressAxis||[0,-1,0]).transformDirection(n.matrixWorld);
   if(!n.userData.pressAxis&&name.startsWith('expo_replay_')){
    const screen=e.scene.getObjectByName(name.replace('expo_replay_','expo_screen_'));
    let mesh;screen?.traverse(m=>{if(!mesh&&m.isMesh)mesh=m;});
    if(mesh?.geometry.attributes.normal)inward.fromBufferAttribute(mesh.geometry.attributes.normal,0)
     .applyNormalMatrix(new T.Matrix3().getNormalMatrix(mesh.matrixWorld)).negate().normalize();
   }
   const primary=Math.hypot(inward.x,inward.z)>.3?Math.atan2(-inward.x,-inward.z):0;
   const floor=[...new Set([0,3.4,6.8,11.95,Math.round((centre.y-1)*2)/2])].sort((a,b)=>Math.abs(a+1.1-centre.y)-Math.abs(b+1.1-centre.y));
   let nearest=null;
   for(const angle of [0,.31,-.31,.69,-.69,1.57,-1.57,Math.PI])for(const dist of [1.12,1.45,.74,1.86,2.2]){
    const x=centre.x+dist*Math.sin(primary+angle),z=centre.z+dist*Math.cos(primary+angle);
    for(const y of floor){
     if(Math.abs(centre.y-(y+1.15))>1.9)continue;
     try{await e.go(zone,[x,y,z]);}catch{continue;}
     e.lookAt(...centre.toArray());e.root.focus();const focus=e.focusTarget();
     if(focus&&!nearest)nearest={feet:e.state().feet,focus};
     if(focus?.lineage.includes(name))return {feet:e.state().feet,focus,centre:centre.toArray()};
    }
   }
   return {centre:centre.toArray(),nearest};
  },{zone,name});
  for(const [zone,spawn] of Object.entries(points)){
   if(!known.includes(zone)||filter.size&&!filter.has(zone))continue;
   await f.evaluate(async p=>{await __expo.go(p.zone,p.spawn);},{zone,spawn});
   if(zone==='twelve')await f.waitForTimeout(1300);
   const all=await f.evaluate(z=>__expo.buttons().filter(c=>c.zone===z),zone);
   for(const name of required[zone]||[])if(!all.some(c=>c.node===name))report.errors.push(device+' missing '+zone+' '+name);
   for(const cap of all.filter(c=>retired(c.node)&&c.shown))report.errors.push(device+' '+zone+' still offers the retired cap '+cap.node);
   const caps=all.filter(c=>!retired(c.node)&&(c.shown||revealed.has(c.node)));
   const present=caps.filter(c=>/^expo_replay_slot_/.test(c.node)).map(c=>c.node).sort();
   const expected=screensIn(zone);
   if(JSON.stringify(present)!==JSON.stringify(expected))report.errors.push(device+' '+zone+' screen caps expected '+expected+'; found '+present);
   if(caps.some(c=>c.node.startsWith('expo_replay_ev_')))report.errors.push(device+' obsolete fixed chronological cap in '+zone);
   if(zone==='twelve'&&caps.filter(c=>c.node.startsWith('expo_replay_log_')).length!==12)
    report.errors.push(device+' expected twelve native herd log buttons');
   if(zone==='twelve'&&caps.filter(c=>c.node.startsWith('log_')).length!==12)
    report.errors.push(device+' expected twelve independently sprung herd log buttons');
   // The till goes last in its room: an empty-handed visitor gets a day ticket there and the green door carries them
   // out to the Atrium, which would cut a record short.
   caps.sort((a,b)=>Number(a.node==='expo_press_till_display')-Number(b.node==='expo_press_till_display'));
   for(const cap of caps){
    const row={zone,node:cap.node,authored:cap.authored,depth:cap.depth};rows.push(row);
    try{
     await f.evaluate(async({zone,spawn})=>{if(__expo.state().zone!==zone)await __expo.go(zone,spawn);},{zone,spawn});
     if(cap.node==='photo_release'){
      await f.evaluate(async()=>{await __expo.go('twelve',[0,-.68,-24.03]);});
      await f.waitForFunction(()=>__expo.scene.getObjectByName('photo_release')?.visible,null,{timeout:5000});
      row.floorHatchOpened=true;
     }
     // The herd lines are recordings and only one line waits for the voice: a press while one already waits is
     // dropped, so let the queue clear first.
     if(/^(?:expo_replay_)?log_/.test(cap.node))
      await f.waitForFunction(()=>!__expo.narrationState().speech.queue.length,null,{timeout:60000});
     const screen=/^expo_replay_slot_/.test(cap.node);
     // Walking up to a room's screen can start a record by itself, and its viewing rail turns the visitor to it.
     // While any record plays, every screen cap is still and dark and a press on it moves nothing. Once the record
     // ends, stand at the cap again.
     let target;
     for(let attempt=0;;attempt++){
      target=await approach(zone,cap.node);
      row.approach=target;if(!target?.focus)throw Error('No reachable direct cap ray');
      await f.waitForTimeout(350);
      const busy=await story();
      if(!busy.playingAt)break;
      if(attempt>2)throw Error('A record is still playing at '+busy.playingAt);
      if(screen&&!row.whileBusy){
       const still=await capRow(cap.node);
       await f.evaluate(async({zone,feet,centre})=>{await __expo.go(zone,feet);__expo.lookAt(...centre);__expo.root.focus();},{zone,...target});
       await use();await f.waitForTimeout(450);
       const after=await capRow(cap.node);
       row.whileBusy={playingAt:busy.playingAt,before:still,after};
       assert(!after.lit&&after.pressed===still.pressed&&Math.abs(after.travel-still.travel)<.002,'A screen cap moved while '+busy.playingAt+' played');
       if(busy.playingAt===cap.node.slice(12))assert(after.held,'The screen that plays does not hold its cap down');
      }
      await f.waitForFunction(()=>!__expo.narrationState().archive.playingAt,null,{timeout:90000});
      await f.waitForFunction(n=>__expo.buttons().find(c=>c.node===n)?.amount===0,cap.node,{timeout:3000});
     }
     if(cap.node.startsWith('log_'))assert.equal(target.focus.owner,'expo_replay_'+cap.node);
     if(screen){
      assert.equal(target.focus.owner,cap.node,'Each screen cap needs its own action');
      assert(cap.authored&&cap.depth>=.015,'Listening control lacks its physical stroke');
      row.archiveBefore=await story();
     }
     // The 350 ms wait may have let something turn the visitor: aim once more.
     await f.evaluate(async({zone,feet,centre})=>{await __expo.go(zone,feet);__expo.lookAt(...centre);__expo.root.focus();},{zone,...target});
     row.focusAtPress=await f.evaluate(()=>__expo.focusTarget()?.owner);
     const before=await f.evaluate(n=>__expo.buttons().find(c=>c.node===n).pressed,cap.node);
     await use();
     await f.waitForFunction(({name,before})=>__expo.buttons().some(c=>c.node===name&&c.pressed>before),{name:cap.node,before},{timeout:2000});
     if(screen){
      const slot=cap.node.slice(12);
      await f.waitForFunction(id=>__expo.narrationState().archive.playingAt===id,slot,{timeout:3000});
      await f.waitForFunction(n=>{const c=__expo.buttons().find(c=>c.node===n);return c.held&&!c.lit&&c.amount===1;},cap.node,{timeout:3000});
      await f.waitForFunction(()=>!__expo.narrationState().archive.playingAt,null,{timeout:90000});
      row.archiveAfter=await story();
      if(row.archiveBefore.next)assert.equal(row.archiveAfter.completed,row.archiveBefore.completed+1,'The screen did not count its record');
      const up=await capRow(cap.node);assert(!up.held&&up.lit,'The cap did not come back up, lit, after its record');
     }
     if(cap.node.startsWith('log_')){
      row.log=await f.evaluate(id=>{
       const n=__expo.narrationState();return {id,heard:n.heard.includes(id),playing:n.speech.playing,queued:n.speech.queue.includes(id)};
      },cap.node);
      assert(row.log.heard||row.log.queued||row.log.playing===cap.node,'The herd cap did not enter its recruited recording');
     }
     if(cap.node==='alpaca_button'){
      // The doctor asks once ("Is he an alpaca?"); the second press readmits the visitor at the Atrium.
      await f.waitForFunction(n=>__expo.buttons().some(c=>c.node===n&&c.amount===0),cap.node,{timeout:3000});
      await f.evaluate(async({feet,centre})=>{await __expo.go('twelve',feet);__expo.lookAt(...centre);__expo.root.focus();},target);
      await use();
      const moved=await f.waitForFunction(()=>__expo.state().zone==='atrium',null,{timeout:15000}).then(()=>true,()=>false);
      row.doctor=await f.evaluate(()=>({zone:__expo.state().zone,caption:__expo.root.querySelector('.expo-caption')?.textContent,
       rails:__expo.rails().filter(r=>r.up).map(r=>r.id),readmitted:__expo.exhibits.state.visitorNumber}));
      assert(moved,'The Doctor readmitted the visitor ('+row.doctor.caption+') but left them in '+row.doctor.zone);
      row.returnedTo='atrium';await f.evaluate(async p=>{await __expo.go('twelve',p);},points.twelve);
     }
     await f.waitForFunction(n=>__expo.buttons().some(c=>c.node===n&&c.amount===0),cap.node,{timeout:3000});
     row.passed=true;
    }catch(err){row.error=String(err.message).slice(0,400);console.error(device,zone,cap.node,row.error);}
   }
   // A retired cap takes no hit from anywhere a visitor can stand.
   for(const cap of all.filter(c=>retired(c.node))){
    const row={zone,node:cap.node,retired:true};rows.push(row);
    try{
     await f.evaluate(async({zone,spawn})=>{if(__expo.state().zone!==zone)await __expo.go(zone,spawn);},{zone,spawn});
     const target=await approach(zone,cap.node);row.nearest=target?.nearest?.focus?.owner;
     assert(!target?.focus,'The retired cap '+cap.node+' takes a hit');
     row.passed=true;
    }catch(err){row.error=String(err.message).slice(0,400);console.error(device,zone,cap.node,row.error);}
   }
   await writeFile(join(out,'caps.json'),JSON.stringify(report,null,2));
  }
  await Promise.all(pending);await ctx.close();
  if(!filter.size||filter.has('twelve')){
   const visit=await browser.newContext({hasTouch:touch,isMobile:touch,
    reducedMotion:touch?'reduce':'no-preference',viewport:touch?{width:393,height:852}:{width:1440,height:810}});
   await speed(visit);
   const view=await visit.newPage();await view.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=8');
   await view.getByRole('button',{name:'Play Llama Expo 2037'}).click();
   const folded=view.frames().find(fr=>fr.url().includes('/fuzz/index.html'));
   if(touch)await folded.locator('.expo-worm #btn').tap();else await folded.locator('.expo-worm #btn').click();
   await folded.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
   await folded.waitForFunction(()=>!__expo.rails().some(r=>r.up&&r.id==='welcome'),null,{timeout:90000});
   await folded.evaluate(async p=>{await __expo.go('twelve',p);},points.twelve);
   const recruited=await folded.evaluate(()=>__expo.exhibits.state.file.herd);
   assert.equal(recruited.length,8);
   const hidden=[];
   for(const row of rows.filter(r=>r.zone==='twelve'&&/^(?:expo_replay_)?log_/.test(r.node)&&!recruited.includes(r.node.split('_').at(-1)))){
    assert(row.approach?.feet,'Missing full-roster approach for '+row.node);
    const check=await folded.evaluate(async({node,feet,centre})=>{
     const e=__expo;await e.go('twelve',feet);e.lookAt(...centre);
     const mesh=e.scene.getObjectByName(node);
     let visible=true;for(let n=mesh;n;n=n.parent)visible&&=n.visible;
     return {node,visible,focus:e.focusTarget(),rosterStations:e.narrationState().stations.map(s=>s.id)};
    },{node:row.node,...row.approach});
    // With nothing to operate the touch Use button is not offered at all, which is the pass.
    if(touch){const button=folded.locator('.expo-interact');check.useOffered=await button.isVisible();if(check.useOffered)await button.tap();}
    else await view.keyboard.press('e');
    check.strokes=await folded.evaluate(name=>__expo.buttons().find(c=>c.node===name).pressed,row.node);
    const id='log_'+row.node.split('_').at(-1);
    assert(!check.visible&&!check.strokes&&!check.rosterStations.includes(id)&&!check.focus?.lineage.includes(row.node),'Missing herd control was accessible: '+row.node);
    hidden.push(check);
   }
   assert.equal(hidden.length,8);report.devices.at(-1).unrecruited={recruited,hidden};await visit.close();
  }
 }
}finally{await browser.close();await writeFile(join(out,'caps.json'),JSON.stringify(report,null,2));}
const failed=report.devices.flatMap(d=>d.rows.filter(r=>!r.passed));console.log('Caps',report.devices.map(d=>d.rows.length),'failed',failed.length);
if(failed.length||report.errors.length)process.exitCode=1;
