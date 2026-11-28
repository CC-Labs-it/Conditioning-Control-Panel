import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const dir=path.resolve(process.argv[2]||'dev-captures/instruments'),mode=process.argv[3]||'native';
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Local review only.');
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
const report={mode,visits:[],errors:[]};
try{
 if(mode==='plates'){
  const page=await browser.newPage({viewport:{width:1560,height:1336}});
  await page.route('**/dev/instrument-samples',r=>r.fulfill({contentType:'text/html',body:'<!doctype html><html><body style="margin:8px;background:#34483d;display:grid;grid-template-columns:repeat(2,768px);gap:8px"></body></html>'}));
  await page.goto(base+'dev/instrument-samples');
  const faces=await page.evaluate(async()=>{
    const {createBuildingPlant}=await import('../ui/expo/building-plant.js');
    const {paintInstrument}=await import('../ui/expo/plant-ink.js'),plant=createBuildingPlant(),p=plant.state;
    for(let i=0;i<80;i++)plant.advance(1,{zone:i<24?'atrium':'hall',projectors:i>25&&i<56?2:0,voices:i%17<7?1:0});
    plant.pulse();plant.advance(1,{zone:'hall',projectors:1,voices:1});
    return ['greenhouse','grid','terrace','circuits','station'].map(kind=>{
      const c=document.createElement('canvas');c.width=768;c.height=432;document.body.append(c);
      paintInstrument(c.getContext('2d'),768,432,kind==='circuits'?'grid':kind,p,kind==='circuits');
      return {kind,bytes:c.toDataURL().split(',')[1]};
    });
  });
  for(const f of faces)await writeFile(path.join(dir,f.kind+'.png'),Buffer.from(f.bytes,'base64'));
  await page.screenshot({path:path.join(dir,'instrument-faces.png')});await page.close();
 }else{
  for(const phone of [false,true]){
   const device=phone?'touch':'desktop';
   const cx=await browser.newContext({viewport:phone?{width:393,height:852}:{width:1440,height:900},
     deviceScaleFactor:1,hasTouch:phone,isMobile:phone,reducedMotion:phone?'reduce':'no-preference'});
   const page=await cx.newPage();page.on('pageerror',e=>report.errors.push(e.message));
   await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
   const enter=page.getByRole('button',{name:'Play Llama Expo 2037'});await enter.waitFor();await enter.click();
   const fr=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
   if(phone)await fr.locator('.expo-worm #btn').tap();else await fr.locator('.expo-worm #btn').click();
   await fr.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
   await fr.waitForFunction(()=>__expo.narrationState().finished.pa_arrive_1>=1,null,{timeout:30000});
   const status=()=>fr.evaluate(()=>__expo.plantState());
   const visit={device};report.visits.push(visit);
   const interact=async()=>{if(phone)await fr.locator('.expo-interact').tap();else await page.keyboard.press('e');};
   async function go(zone,kind,at){
     await fr.evaluate(async({zone,kind,at})=>{
       const e=__expo,T=await import('three');await e.go(zone,at);
       const mesh=e.scene.getObjectByName('expo_display_'+kind);
       if(!mesh)throw Error('Missing native instrument '+kind);
       const p=new T.Box3().setFromObject(mesh,true).getCenter(new T.Vector3());e.lookAt(...p.toArray());e.root.focus();
     },{zone,kind,at});
   }
   async function aimButton(kind){
     return fr.evaluate(async kind=>{
       const e=__expo,T=await import('three'),b=e.scene.getObjectByName('expo_control_'+kind);
       if(!b)throw Error('Missing native push-button '+kind);
       const p=new T.Box3().setFromObject(b,true).getCenter(new T.Vector3());e.lookAt(...p.toArray());e.root.focus();
       return document.querySelector('.expo-prompt').textContent;
     },kind);
   }
   await go('atrium','greenhouse',[9.16,0,23.86]);
   visit.start=await status();const garden=visit.start.panels.find(p=>p.kind==='greenhouse');
   assert(garden.physicalControl);assert.equal(await aimButton('greenhouse'),'Run irrigation pulse');
   await interact();await fr.waitForFunction(()=>__expo.plantState().pumpLpm===6);
   visit.pulse=await status();assert(visit.pulse.pulseSeconds>8);
   await go('atrium','greenhouse',[9.16,0,23.86]);await page.waitForTimeout(300);
   await page.screenshot({path:path.join(dir,device+'-greenhouse.jpg'),type:'jpeg',quality:94});
   if(!phone){
     await page.keyboard.press('Escape');await fr.waitForFunction(()=>fuzz.engine.paused);
     const held=(await status()).time;await page.waitForTimeout(650);assert.equal((await status()).time,held);
     await fr.locator('#resume').click();visit.pausedAt=held;
   }
   await fr.waitForFunction(()=>__expo.plantState().pulseSeconds===0,null,{timeout:16000});
   visit.recovered=await status();const after=visit.recovered.panels.find(p=>p.kind==='greenhouse');
   assert(after.uploads-garden.uploads<=(phone?4:9));assert(Math.abs(visit.recovered.waterLitres-96)<.0001);
   await go('hall','grid',[9.34,0,-8.22]);await page.waitForTimeout(450);
   await page.screenshot({path:path.join(dir,device+'-grid.jpg'),type:'jpeg',quality:94});
   assert.equal(await aimButton('grid'),'Inspect circuit loads');await interact();
   await fr.waitForFunction(()=>__expo.plantState().panels.some(p=>p.kind==='grid'&&p.detail));
   await go('hall','grid',[9.34,0,-8.22]);await page.screenshot({path:path.join(dir,device+'-circuits.jpg'),type:'jpeg',quality:94});
   visit.grid=await status();assert.equal(visit.grid.roofWatts+visit.grid.batteryWatts,visit.grid.watts);
   if(process.env.ALL_INSTRUMENTS==='1'){
     await go('service','station',[25.30,0,29.34]);await page.screenshot({path:path.join(dir,device+'-reserve.jpg'),type:'jpeg',quality:94});
     assert.equal(await aimButton('station'),'Inspect circuit loads');
     await interact();await fr.waitForFunction(()=>__expo.plantState().panels.some(p=>p.kind==='station'&&p.detail));
     visit.station=await status();await page.screenshot({path:path.join(dir,device+'-station.jpg'),type:'jpeg',quality:94});
     const hinges=()=>fr.evaluate(()=>__expo.scene.getObjectByName('expo_louvre_0').quaternion.toArray());
     await go('catwalk','terrace',[-13.57,6.8,25.08]);assert.equal(await aimButton('terrace'),'Test intake louvres');
     const before=await hinges();
     await interact();await fr.waitForFunction(()=>__expo.plantState().ventTestSeconds>0);
     await go('catwalk','terrace',[-13.57,6.8,25.08]);await page.waitForTimeout(1300);
     visit.terrace=await status();const after=await hinges();
     visit.terrace.bladeTravelDegrees=2*Math.acos(Math.min(1,Math.abs(before.reduce((s,n,i)=>s+n*after[i],0))))*180/Math.PI;
     assert(visit.terrace.panels.find(d=>d.kind==='terrace').louvreCount===7);assert(visit.terrace.bladeTravelDegrees>1);
     await page.screenshot({path:path.join(dir,device+'-terrace.jpg'),type:'jpeg',quality:94});
   }
   await fr.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await fr.locator('.expo-root').waitFor({state:'detached'});
   visit.panic=true;await cx.close();
  }
  assert.equal(report.errors.length,0);
 }
}finally{await writeFile(path.join(dir,'checks.json'),JSON.stringify(report,null,2));await browser.close();}
