// Exercise every published mechanical cap with real keyboard and touchscreen actions.
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
const {chromium}=createRequire('C:/wt-fe/e2e/')('playwright');
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8941/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const out=resolve(process.argv[2]),filter=new Set((process.argv[3]||'').split(',').filter(Boolean));
await mkdir(out,{recursive:true});
const points={hall:[0,0,-7.5],atrium:[.22,0,36.21],hoof:[-18,0,8],habitat:[13,0,7],shop:[15,0,-5],twelve:[0,0,-25.15],
 catwalk:[-13.2,6.8,22],service:[13.6,0,32],secret:[83,0,41],dome:[-20,0,-5],
 chronology:[7.8,0,43],forum:[0,0,103],commons:[-66.2,0,95],aquarium:[-59,0,45.5]};
const report={base,devices:[],errors:[]};
const required={
 hall:['press_main','press_stop','expo_replay_ex_press','expo_control_grid'],atrium:['expo_control_greenhouse'],
 hoof:Array.from({length:4},(_,i)=>'expo_press_hoof_checkbox_'+i),
 habitat:['expo_press_habitat_glass','expo_press_feeding_slot','expo_press_habitat_plaque'],
 shop:['expo_press_framed_contract','expo_press_till_display','expo_press_exit_sale'],
 catwalk:['expo_control_terrace'],service:['expo_control_station'],twelve:['photo_release']
};
const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',
 args:['--mute-audio','--force_low_power_gpu']});
try{
 for(const touch of [false,true]){
  const device=touch?'touch':'keyboard',ctx=await browser.newContext({hasTouch:touch,isMobile:touch,
   reducedMotion:touch?'reduce':'no-preference',viewport:touch?{width:393,height:852}:{width:1440,height:810}});
  const page=await ctx.newPage(),rows=[],files=[],pending=[];
  report.devices.push({device,rows,files});page.on('pageerror',err=>report.errors.push(err.message));
  page.on('response',r=>{
   const name=new URL(r.url()).pathname.split('/assets/expo/zones/')[1];
   if(!name||!name.match(/\.glb(?:\.gz)?$/))return;
   const entry={file:name};files.push(entry);pending.push(r.body().then(bytes=>{
    entry.bytes=bytes.length;entry.sha256=createHash('sha256').update(bytes).digest('hex');
   }).catch(error=>{entry.error=error.message;}));
  });
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
  await page.getByRole('button',{name:'Play Llama Expo 2037'}).click();
  const f=page.frames().find(fr=>fr.url().includes('/fuzz/index.html'));await f.locator('.expo-worm #btn').click();
  await f.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
  const use=()=>touch?f.locator('.expo-interact').tap():page.keyboard.press('e');
  const known=await f.evaluate(async()=>(await(await fetch('assets/expo/zones/zones.json')).json()).zones.map(z=>z.id));
  for(const [zone,spawn] of Object.entries(points)){
   if(!known.includes(zone)||filter.size&&!filter.has(zone))continue;
   await f.evaluate(async p=>{await __expo.go(p.zone,p.spawn);},{zone,spawn});
   if(zone==='twelve')await f.waitForTimeout(1300);
   const caps=await f.evaluate(z=>__expo.buttons().filter(c=>c.zone===z),zone);
   for(const name of required[zone]||[])if(!caps.some(c=>c.node===name))report.errors.push(device+' missing '+zone+' '+name);
   if(['chronology','forum','commons','aquarium'].includes(zone)&&caps.filter(c=>c.node.startsWith('expo_replay_ev_')).length!==10)
    report.errors.push(device+' expected ten native dated projector buttons in '+zone);
   if(zone==='twelve'&&caps.filter(c=>c.node.startsWith('expo_replay_log_')).length!==12)
    report.errors.push(device+' expected twelve native herd log buttons');
   if(zone==='twelve'&&caps.filter(c=>c.node.startsWith('log_')).length!==12)
    report.errors.push(device+' expected twelve independently sprung green log buttons');
   // Offer the till/contract before the locked Exit. The main hall switch precedes its rising partner.
   const last=new Set(['press_stop','expo_press_exit_sale']);
   caps.sort((a,b)=>Number(last.has(a.node))-Number(last.has(b.node)));
   for(const cap of caps){
    const row={zone,node:cap.node,authored:cap.authored,depth:cap.depth};rows.push(row);
    try{
     await f.evaluate(async({zone,spawn})=>{if(__expo.state().zone!==zone)await __expo.go(zone,spawn);},{zone,spawn});
     if(cap.node==='press_stop'){
      await f.evaluate(async()=>{const e=__expo,T=await import('three');await e.go('hall',[0,0,-7.48]);
       e.lookAt(...new T.Box3().setFromObject(e.scene.getObjectByName('press_main'),true).getCenter(new T.Vector3()).toArray());});
      for(let i=0;i<54&&await f.evaluate(()=>__expo.exhibits.state.presses<50);i++)await use();
      assert(await f.evaluate(()=>__expo.exhibits.state.presses>=50),'The main switch did not release the second cap');
      await f.waitForTimeout(940);
     }
     if(cap.node==='expo_press_exit_sale'){
      const locked=await f.evaluate(async()=>{
       const e=__expo,T=await import('three');await e.go('shop',[22.77,0,-10.05]);
       const m=e.scene.getObjectByName('expo_press_exit_sale'),c=new T.Box3().setFromObject(m,true).getCenter(new T.Vector3());
       e.lookAt(...c.toArray());return {hidden:!m.visible||!m.parent.visible,focus:e.focusTarget()};
      });
      row.locked=locked.hidden;row.lockedFocus=locked.focus;
      assert(row.locked,'Exit sale cap was available before completing two shop loops');
      assert(!locked.focus?.lineage.includes(cap.node)&&locked.focus?.owner!=='exit_sale','Locked sale cap accepted a hit');
      for(let i=0;i<2;i++){
       await f.evaluate(async()=>{const e=__expo;await e.go('shop',[15.03,0,-12.14]);e.lookAt(15.03,1.14,-13.93);});
       await use();await f.waitForTimeout(200);
      }
     }
     const target=await f.evaluate(async({zone,name})=>{
      const e=__expo,T=await import('three'),n=e.scene.getObjectByName(name);
      if(!n)return null;
      const centre=new T.Box3().setFromObject(n,true).getCenter(new T.Vector3());
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
        e.lookAt(...centre.toArray());const focus=e.focusTarget();
        if(focus&&!nearest)nearest={feet:e.state().feet,focus};
        if(focus?.lineage.includes(name))return {feet:e.state().feet,focus,centre:centre.toArray()};
       }
      }
      return {centre:centre.toArray(),nearest};
     },{zone,name:cap.node});
     row.approach=target;if(!target?.focus)throw Error('No reachable direct cap ray');
     if(cap.node.startsWith('log_'))assert.equal(target.focus.owner,'expo_replay_'+cap.node);
     const before=await f.evaluate(n=>__expo.buttons().find(c=>c.node===n).pressed,cap.node);
     await use();
     await f.waitForFunction(({name,before})=>__expo.buttons().some(c=>c.node===name&&c.pressed>before),{name:cap.node,before},{timeout:2000});
     if(cap.node.startsWith('log_')){
      row.log=await f.evaluate(id=>{
       const n=__expo.narrationState();return {id,heard:n.heard.includes(id),playing:n.speech.playing,queued:n.speech.queue.includes(id)};
      },cap.node);
      assert(row.log.heard||row.log.queued||row.log.playing===cap.node,'The green cap did not enter its recruited recording');
     }
     await f.waitForFunction(n=>__expo.buttons().some(c=>c.node===n&&c.amount===0),cap.node,{timeout:3000});
     row.passed=true;
    }catch(err){row.error=String(err.message).slice(0,400);console.error(device,zone,cap.node,row.error);}
   }
   await writeFile(join(out,'caps.json'),JSON.stringify(report,null,2));
  }
  await Promise.all(pending);await ctx.close();
  if(!filter.size||filter.has('twelve')){
   const visit=await browser.newContext({hasTouch:touch,isMobile:touch,
    reducedMotion:touch?'reduce':'no-preference',viewport:touch?{width:393,height:852}:{width:1440,height:810}});
   const view=await visit.newPage();await view.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=8');
   await view.getByRole('button',{name:'Play Llama Expo 2037'}).click();
   const folded=view.frames().find(fr=>fr.url().includes('/fuzz/index.html'));await folded.locator('.expo-worm #btn').click();
   await folded.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
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
    if(touch)await folded.locator('.expo-interact').tap();else await view.keyboard.press('e');
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
