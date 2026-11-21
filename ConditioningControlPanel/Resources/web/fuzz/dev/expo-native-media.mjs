// Enter the normal ending, walk up to the physical archive machine, and operate its replay control.
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import path from 'node:path';
import {installGameAudio,beginGameAudio,finishGameAudio} from './expo-film.mjs';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const dir=path.resolve(process.argv[2]||'dev-captures/native-narration'),base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const report={devices:[],errors:[]};await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required']});
try {
 for(const phone of [false,true]){
  const device=phone?'touch':'desktop',cx=await browser.newContext({viewport:phone?{width:393,height:852}:{width:1500,height:900},
    hasTouch:phone,isMobile:phone,deviceScaleFactor:1,reducedMotion:phone?'reduce':'no-preference'});
  await installGameAudio(cx);
  const page=await cx.newPage(),requests=[];
  page.on('response',r=>{if(r.url().includes('/assets/expo/narration/'))requests.push({status:r.status(),name:r.url().split('/narration/')[1]});});
  page.on('console',m=>{if(/Shader Error|INVALID_OPERATION|recording unavailable/.test(m.text()))report.errors.push(m.text());});
  page.on('pageerror',e=>report.errors.push(e.message));
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd=12');
  const play=page.getByRole('button',{name:'Play Llama Expo 2037'});await play.waitFor();await play.click();
  const fr=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  const warp=fr.locator('.expo-worm #btn');if(phone)await warp.tap();else await warp.click();
  await fr.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
  const n=()=>fr.evaluate(()=>__expo.narrationState());const state=()=>fr.evaluate(()=>__expo.state());
  const wait=(fn,arg,ms=30000)=>fr.waitForFunction(fn,arg,{timeout:ms});
  await wait(()=>__expo.narrationState().pa.seconds>.25);
  const expanded=await fr.evaluate(async()=>(await(await fetch('assets/expo/zones/zones.json')).json()).zones.some(z=>z.id==='chronology'));
  const stationZone=expanded?'chronology':'atrium';
  const start=await n();assert.equal(start.stations.filter(s=>s.id.startsWith('ev_')).length,expanded?0:20);
  assert(start.heard.includes('pa_arrive_1'));assert(!start.heard.some(id=>id.startsWith('ev_')));
  const item={device,arrival:(await state()).feet,arrivalHeard:start.heard,stations:start.stations.length,requests};
  report.devices.push(item);
  await page.screenshot({path:path.join(dir,device+'-arrival.jpg'),type:'jpeg',quality:91});
  await beginGameAudio(fr);
  async function viewStation(feet,object='expo_screen_ev_h0'){
    await fr.evaluate(async({zone,feet,object})=>{
      const e=__expo,T=await import('three');await e.go(zone,feet);
      const aim=new T.Box3().setFromObject(e.scene.getObjectByName(object),true).getCenter(new T.Vector3());
      e.lookAt(...aim.toArray());e.root.focus();
    },{zone:stationZone,feet,object});
  }
  await viewStation(expanded?[8.72,0,46.85]:[10.83,0,37.08]);
  await wait(()=>__expo.narrationState().voice.id==='ev_h0'&&__expo.narrationState().voice.seconds>.5);
  const row=await fr.evaluate(async()=>(await(await fetch('assets/expo/narration/manifest.json')).json()).find(r=>r.id==='ev_h0'));
  let started=await n();assert.equal(started.voice.subtitle,row.subtitle);
  item.near=started.voice;
  await viewStation(expanded?[9.0,0,46.85]:[10.87,0,38.97]);
  await wait(()=>__expo.narrationState().stations.find(s=>s.id==='ev_h0').project.full);
  await page.waitForTimeout(480);
  const full=(await n()).stations.find(s=>s.id==='ev_h0');assert(full.project.lamp>.60);
  assert(full.project.zoom>=0&&full.project.zoom<=.04);if(phone)assert.equal(full.project.zoom,0);
  item.projector=full;
  await page.screenshot({path:path.join(dir,device+'-projection.jpg'),type:'jpeg',quality:94});
  if(!phone){
    item.stereo=await fr.evaluate(async()=>{
      const e=__expo,bus=window.__expoRecordingBus,ac=bus.context;
      const input=ac.createMediaStreamSource(bus.stream),split=ac.createChannelSplitter(2);
      const analysers=[ac.createAnalyser(),ac.createAnalyser()];input.connect(split);
      analysers.forEach((a,i)=>{a.fftSize=1024;split.connect(a,i);});
      const sample=new Float32Array(1024),speaker=e.narrationState().stations.find(s=>s.id==='ev_h0').pos;
      const pose=e.camera.quaternion.clone(),dx=speaker[0]-e.camera.position.x,dz=speaker[2]-e.camera.position.z;
      const yaw=Math.atan2(-dz,dx),results=[];
      for(const offset of [0,Math.PI]){
        e.camera.rotation.set(0,yaw+offset,0,'YXZ');
        await new Promise(resolve=>setTimeout(resolve,150));
        const totals=[0,0],start=performance.now();let frames=0;
        while(performance.now()-start<850){await new Promise(r=>requestAnimationFrame(r));frames++;
          analysers.forEach((a,i)=>{a.getFloatTimeDomainData(sample);for(const x of sample)totals[i]+=x*x;});}
        const state=e.narrationState();results.push({voice:state.voice.id,pan:state.voice.pan,
          rmsDbFS:totals.map(n=>10*Math.log10(Math.max(1e-12,n/(frames*sample.length))))});
      }
      e.camera.quaternion.copy(pose);input.disconnect();split.disconnect();analysers.forEach(a=>a.disconnect());return results;
    });
    const [right,left]=item.stereo;
    assert(right.pan>.8&&left.pan<-.8&&right.rmsDbFS[1]>right.rmsDbFS[0]&&left.rmsDbFS[0]>left.rmsDbFS[1]);
  }
  await viewStation(expanded?[6.1,0,53.4]:[5,0,31]);
  await page.waitForTimeout(240);
  const far=(await n()).voice;assert.equal(far.id,'ev_h0');assert(far.seconds>started.voice.seconds);
  item.away=far;
  if(!phone){
    await page.keyboard.press('Escape');await wait(()=>fuzz.engine.paused);
    const paused=(await n()).voice.seconds;await page.waitForTimeout(650);
    assert.equal((await n()).voice.seconds,paused);item.pauseSeconds=paused;
    await fr.locator('#resume').click();
  }
  await wait(()=>__expo.narrationState().finished.ev_h0>=1,null,31000);
  await viewStation(expanded?[8.72,0,46.85]:[10.6,0,39.26],'expo_replay_ev_h0');
  await wait(()=>document.querySelector('.expo-prompt').textContent==='Replay narration');
  assert(!(await n()).voice.id);
  if(phone){
    const center=await fr.locator('.expo-canvas').boundingBox();
    await page.touchscreen.tap(center.x+center.width/2,center.y+center.height/2);
  }else await page.keyboard.press('e');
  await wait(()=>__expo.narrationState().voice.id==='ev_h0'&&__expo.narrationState().voice.seconds>.1);
  item.replay=(await n()).voice;
  await finishGameAudio(fr,path.join(dir,device+'-visit.webm'));
  await fr.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await fr.locator('.expo-root').waitFor({state:'detached'});
  item.panic=true;await cx.close();
 }
 assert.equal(report.errors.length,0);
}finally{await writeFile(path.join(dir,'native-media-checks.json'),JSON.stringify(report,null,2));await browser.close();}
