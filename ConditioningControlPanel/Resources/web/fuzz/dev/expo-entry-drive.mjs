// Cold desktop and touch entries with the ordinary purchase, an active room route and the owner settings.
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/',dir=path.resolve(process.argv[2]||'./dev-captures/entry');
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const expected=process.env.EXPECT_START||'atrium';
const link=Number(process.env.MBPS||20),rtt=Number(process.env.RTT||35);
const integrated=process.env.GPU==='integrated';
const report={expected,integrated,network:{megabitsPerSecond:link,latencyMs:rtt},devices:[],errors:[]};
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required',...(integrated?['--force_low_power_gpu']:[])]});
try{
 for(const phone of [false,true]){
  const name=phone?'phone':'desktop',size=phone?{width:393,height:852}:{width:1920,height:1080};
  const cx=await browser.newContext({viewport:size,deviceScaleFactor:1,hasTouch:phone,isMobile:phone,reducedMotion:phone?'reduce':'no-preference'});
  const page=await cx.newPage();page.on('pageerror',e=>report.errors.push(name+' '+e.message));
  const cdp=await cx.newCDPSession(page);await cdp.send('Network.enable');
  const ids=new Set(),files=new Map(),before=new Set();let arrived=false;
  cdp.on('Network.responseReceived',e=>{
    if(!e.response.url.includes('/assets/expo/zones/'))return;
    ids.add(e.requestId);files.set(e.requestId,{name:new URL(e.response.url).pathname.split('/zones/')[1],bytes:0,beforeArrival:!arrived});
  });
  cdp.on('Network.loadingFinished',e=>{const f=files.get(e.requestId);if(f){f.bytes=e.encodedDataLength;if(!arrived)before.add(e.requestId);}});
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none');
  const button=page.getByRole('button',{name:'Play Llama Expo 2037'});await button.waitFor();
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));await button.click();await frame.locator('.expo-worm #btn').waitFor();
  if(link>0)await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:rtt,downloadThroughput:link*125000,uploadThroughput:link*125000,connectionType:phone?'cellular4g':'wifi'});
  const then=Date.now();
  if(phone)await frame.locator('.expo-worm #btn').tap();else await frame.locator('.expo-worm #btn').click();
  await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});arrived=true;
  const entryMs=Date.now()-then,firstFiles=[...before].map(id=>files.get(id));
  const state=await frame.evaluate(()=>__expo.state());
  if(state.zone!==expected)throw Error('Expected '+expected+' entry but found '+state.zone);
  await page.screenshot({path:path.join(dir,name+'-entry.jpg'),type:'jpeg',quality:92});
  async function frameTiming(){return frame.evaluate(async()=>{
    const e=__expo,intervals=[],d=e.renderer.getContext(),gpuTimes=[];
    const timer=d.getExtension('EXT_disjoint_timer_query_webgl2');
    if(timer){
      for(let sample=0;sample<4;sample++){
        const query=d.createQuery();d.beginQuery(timer.TIME_ELAPSED_EXT,query);
        e.draw();d.endQuery(timer.TIME_ELAPSED_EXT);d.flush();const deadline=performance.now()+600;
        while(!d.getQueryParameter(query,d.QUERY_RESULT_AVAILABLE)&&performance.now()<deadline)await new Promise(r=>requestAnimationFrame(r));
        if(d.getQueryParameter(query,d.QUERY_RESULT_AVAILABLE)&&!d.getParameter(timer.GPU_DISJOINT_EXT))gpuTimes.push(d.getQueryParameter(query,d.QUERY_RESULT)/1e6);
        d.deleteQuery(query);
      }
    }
    let previous=performance.now(),draw=e.renderer.info.render.frame;
    const start=previous;while(performance.now()-start<4000){
      await new Promise(r=>requestAnimationFrame(r));
      const now=performance.now(),drawNow=e.renderer.info.render.frame;
      if(drawNow!==draw){intervals.push(now-previous);previous=now;draw=drawNow;}
    }
    const ext=d.getExtension('WEBGL_debug_renderer_info');gpuTimes.sort((a,b)=>a-b);
    intervals.shift();intervals.sort((a,b)=>a-b);
    return {gpu:ext?d.getParameter(ext.UNMASKED_RENDERER_WEBGL):'Unavailable',medianMs:intervals[Math.floor(intervals.length*.5)],p95Ms:intervals[Math.floor(intervals.length*.95)],
      gpuDrawMs:gpuTimes.length?gpuTimes[Math.floor(gpuTimes.length/2)]:null,frames:intervals.length,pixelRatio:e.state().pixelRatio,textureMemory:e.state().textures};
  });}
  const initialTiming=await frameTiming();
  async function touch(kind,x,y){await cdp.send('Input.dispatchTouchEvent',{type:kind,touchPoints:kind==='touchEnd'?[]:[{x,y,radiusX:5,radiusY:5,id:1}]});}
  await frame.locator('.expo-root').focus();
  if(phone){
    const stick=await frame.locator('.expo-stick').boundingBox();if(!stick)throw Error('Missing phone stick.');
    const x=stick.x+stick.width/2,y=stick.y+stick.height/2;
    await touch('touchStart',x,y);await touch('touchMove',x,y-32);await page.waitForTimeout(900);await touch('touchEnd');
    await touch('touchStart',291,315);await touch('touchMove',241,315);await touch('touchEnd');
  }else{await page.keyboard.down('w');await page.waitForTimeout(900);await page.keyboard.up('w');}
  const walked=await frame.evaluate(()=>__expo.state());
  const distance=Math.hypot(walked.feet[0]-state.feet[0],walked.feet[2]-state.feet[2]);
  if(distance<.3)report.errors.push(name+' physical walk failed');
  await frame.evaluate(async()=>{await __expo.go('dome',[-19,0,-5]);__expo.lookAt(-22,1.45,-5);});
  await frame.waitForFunction(()=>__expo.state().loaded.includes('catwalk'),null,{timeout:90000});
  await frame.evaluate(()=>{__expo.deepen(300);__expo.deepen(125);});
  const dreamTiming=await frameTiming();
  const shared=await frame.evaluate(()=>__expo.state().textures);
  const rooms={};
  if(process.env.ALL_ROOMS==='1'){
    const cameras={atrium:[[0,0,37.2],[1.7,4.1,26.5]],hall:[[0,0,13.2],[0,2.4,-9]],
      hoof:[[-18.46,0,8.92],[-18.4,1.75,10.45]],habitat:[[19.4,0,8.13],[19.4,1.56,9.55]],
      twelve:[[-.83,-.68,-29.28],[-1.24,1.75,-33.7]],shop:[[20.84,0,-9.75],[21.2,1.32,-11.42]],
      service:[[25.02,0,29.59],[25.01,1.04,27.21]],secret:[[81.44,0,40.3],[85.8,1.30,41.19]],
      catwalk:[[-22.45,11.95,-4.62],[-22,6,-5]]};
    const known=await frame.evaluate(async()=>((await(await fetch('assets/expo/zones/zones.json')).json()).zones.map(z=>z.id)));
    for(const zone of known.filter(id=>cameras[id])){
      await frame.evaluate(async({zone,view})=>{await __expo.go(zone,view[0]);__expo.lookAt(...view[1]);},{zone,view:cameras[zone]});
      rooms[zone]=await frameTiming();
    }
  }
  await frame.evaluate(async({zone,at})=>{await __expo.go(zone,at);}, {zone:state.zone,at:state.feet});
  await frame.waitForTimeout(900);
  const returned=await frame.evaluate(()=>__expo.state());
  if(shared.sharedHits===0)report.errors.push(name+' shared textures were not reused');
  const list=[...files.values()],firstBytes=firstFiles.reduce((sum,file)=>sum+file.bytes,0);
  report.devices.push({name,size,entryMs,firstBytes,firstFiles,state,initialTiming,dreamTiming,rooms,walkedMeters:distance,shared,returned,
    usedSmallerTextures:list.some(f=>f.name.includes('-mobile.webp'))});
  await frame.evaluate(()=>fuzz.engine.setSuspended(true,'panic'));await frame.locator('.expo-root').waitFor({state:'detached'});await cx.close();
 }
}catch(e){report.errors.push(e.message);throw e;}
finally{await writeFile(path.join(dir,'result.json'),JSON.stringify(report,null,2));await browser.close();}
console.log(JSON.stringify(report.devices.map(d=>({name:d.name,entryMs:d.entryMs,MB:d.firstBytes/1e6,median:d.initialTiming.medianMs,p95:d.initialTiming.p95Ms,walked:d.walkedMeters}))));
if(report.errors.length)process.exitCode=1;
