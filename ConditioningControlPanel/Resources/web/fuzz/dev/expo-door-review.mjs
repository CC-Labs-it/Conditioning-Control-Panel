import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const pkg=process.env.PW_DIR||'playwright';
let chromium;
try{({chromium}=createRequire(import.meta.url)(pkg));}
catch{({chromium}=createRequire('C:/wt-fe/e2e/')(pkg));}
const dir=path.resolve(process.argv[2]||'./dev-captures/doors'),mode=process.argv[3]||'after';
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local Expo preview.');
await mkdir(dir,{recursive:true});
const report={base,mode,devices:[],errors:[]};
const manifestURL=new URL('assets/expo/zones/zones.json',base);
async function current(receipt=false){
 const data=Buffer.from(await (await fetch(manifestURL)).arrayBuffer()),sha=bytes=>createHash('sha256').update(bytes).digest('hex');
 const plan=JSON.parse(data),out={manifest:sha(data),start:plan.start,rooms:Object.fromEntries(plan.zones.map(room=>[room.id,room.file]))};
 if(receipt){
  out.sources={};
  for(const file of ['assets/expo/door-hardware.glb','ui/expo/doors.js','ui/expo/zones.js','ui/expo/shop-door.js']){
   const response=await fetch(new URL(file,base));assert(response.ok);out.sources[file]=sha(Buffer.from(await response.arrayBuffer()));
  }
 }
 return out;
}
report.version=await current(true);
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
 args:['--mute-audio','--force_low_power_gpu']});
try{
 for(const {touch,scenario} of (mode==='desktop1080'?[{touch:false,scenario:'rotunda'},{touch:false,scenario:'staff'}]:
   mode==='boundary'?[{touch:false,scenario:'boundary'}]:mode==='before'?[{touch:false,scenario:'staff'}]:
   [false,true].flatMap(touch=>['rotunda','staff'].map(scenario=>({touch,scenario}))))){
  const device=touch?'touch':'keyboard',profileRun=mode==='isolated'||mode==='desktop1080';
  const viewport=touch?{width:393,height:852}:profileRun?{width:1920,height:1080}:{width:1280,height:720};
  const ctx=await browser.newContext({viewport,
   hasTouch:touch,isMobile:touch,reducedMotion:touch?'reduce':'no-preference'});
  const page=await ctx.newPage(),cdp=await ctx.newCDPSession(page);
  const record={device,scenario,viewport,stages:[],requests:[]};report.devices.push(record);
  page.on('pageerror',e=>report.errors.push(e.message));
  page.on('request',req=>{if(/\/zones\/[^/]+\.glb(?:\.gz)?$/.test(req.url()))record.requests.push(new URL(req.url()).pathname.split('/').at(-1));});
  await cdp.send('Network.enable');await cdp.send('Network.emulateNetworkConditions',{offline:false,latency:35,downloadThroughput:2500000,uploadThroughput:2500000});
  await page.goto(base+'dev/play-ending.html?end=ll_end&herd=12&pictures=none');
  const choose=page.getByRole('button',{name:'Play Llama Expo 2037'});await choose[touch?'tap':'click']();
  const f=page.frames().find(x=>x.url().includes('/fuzz/index.html'));await f.locator('.expo-worm #btn')[touch?'tap':'click']();
  await f.waitForFunction(()=>window.__expo?.state().ready&&!document.querySelector('.expo-crossing'),null,{timeout:90000});
  record.initial=await f.evaluate(()=>__expo.state().loaded);assert.deepEqual(record.initial,['atrium']);
  record.gpu=await f.evaluate(()=>{const gl=__expo.renderer.getContext();return gl.getParameter(gl.getExtension('WEBGL_debug_renderer_info').UNMASKED_RENDERER_WEBGL);});
  assert.match(record.gpu,/Intel/i);
  async function stage(id,zone,at,route,expected){
   if(zone)await f.evaluate(async({zone,at})=>{await __expo.go(zone,at);},{zone,at});
   await f.locator('.expo-root').focus();
   const guide=await f.evaluateHandle(({route,id})=>{
    const e=__expo,begin=performance.now(),data={at:e.state().feet,frames:[],stalls:[],outcome:'moving'};
    const intervals=[],draw=e.renderer.info.render,start=performance.now();
    let step=0,last=e.walker.feet.clone(),startStall=null,previous=-1,lastDraw=draw.frame,lastTime=start;
    function tick(){
     const p=e.walker.feet,now=performance.now(),t=(now-begin)/1000,to=route[step];
     if(draw.frame!==lastDraw){intervals.push(now-lastTime);lastTime=now;lastDraw=draw.frame;}
     if(t>80){data.outcome='blocked';data.target=to;return;}
     if(p.distanceTo(last)<.004){if(startStall===null)startStall=t;}else if(startStall!==null){if(t-startStall>.3)data.stalls.push({seconds:t-startStall,at:p.toArray()});startStall=null;}
     last.copy(p);
     if(t-previous>.08){
      const raw=e.doors?.()||[],doors=raw.filter(x=>x.distance<11||x.progress>0);
      const pre=[];if(!e.doors)e.scene.traverse(n=>{if(n.userData.portal)pre.push([n.name,n.visible]);});
      const s=e.state();data.frames.push({t,feet:p.toArray(),zone:s.zone,loaded:s.loaded,doors,legacy:pre});previous=t;
     }
     const folded=to[2];
     if(folded?Math.hypot(folded[0]-p.x,folded[1]-p.z)<.5:Math.hypot(to[0]-p.x,to[1]-p.z)<.14){
      if(++step>=route.length){
       data.outcome='arrived';data.end=e.state();
       const times=intervals.slice(1).sort((a,b)=>a-b),quantile=n=>times[Math.min(times.length-1,Math.floor(n*times.length))];
       data.cadence={frames:times.length,medianMs:quantile(.5),p95Ms:quantile(.95),p99Ms:quantile(.99),worstMs:times.at(-1),
         over50ms:times.filter(n=>n>50).length,seconds:t};return;
      }
     }
     const target=route[step];e.camera.rotation.set(0,Math.atan2(p.x-target[0],p.z-target[1]),0,'YXZ');requestAnimationFrame(tick);
    }requestAnimationFrame(tick);return data;
   },{route,id});
   async function forward(on){
    if(!touch){await page.keyboard[on?'down':'up']('w');return;}
    if(!on){await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});return;}
    const b=await f.locator('.expo-stick').boundingBox(),x=b.x+b.width/2,y=b.y+b.height/2;
    await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x,y,id:1}]});
    await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:[{x,y:y-40,id:1}]});
   }
   await forward(true);try{await f.waitForFunction(t=>t.outcome!=='moving',guide,{timeout:100000});}finally{await forward(false);}
   const data=await guide.jsonValue();await guide.dispose();record.stages.push({id,...data});
   await writeFile(path.join(dir,'doors.json'),JSON.stringify(report));console.log(device,id,data.outcome,data.end?.zone,data.stalls,data.cadence);
   assert.equal(data.outcome,'arrived',id);assert.equal(data.end.zone,expected,id);
   assert.equal(data.frames.flatMap(sample=>sample.doors).filter(d=>d.progress>0&&!d.ready).length,0,'an open leaf exposed a missing room');
  }
  if(scenario==='boundary'){
   await stage('aquarium-exit-short-right-turn','aquarium',[-7.8,0,43.2],[[-7.8,41],[-7.8,39.99],[-3.6,39.9]],'atrium');
   for(const [room,z,hand] of [['hoof',7,-1],['dome',-5,-1],['habitat',7,1],['shop',-5,1]]){
    await stage('in-and-out-'+room,'hall',[hand*10.25,0,z],[[hand*13.7,z],[hand*10.25,z]],'hall');
   }
   await stage('archive-chair-and-folded-return','secret',[77.7,0,41],[[82.1,41],[79.8,41,[86,51]],[86.45,51,[80.42,41]],
    [81.15,41],[79.8,41,[86,51]],[33.3,51,[34.2,41]],[31.7,41]],'secret');
   await ctx.close();continue;
  }
  if(scenario==='rotunda'){
   await stage('cold-hall-and-four-risers',null,null,[[-2.40,36.77],[-2.90,34.20],[-2.90,22.60],[-.10,19.77],[0,15.6],[0,7.15],[.01,-5.99],[1.99,-5.99],[3.34,-7.34],[3.61,-8.33],[3.61,-11.39],
     [3.25,-11.57],[3.34,-13.10],[3,-13.21],[2.70,-13.55],[2.17,-13.55],[.01,-15.71],[.01,-18.6],[.65,-20.2],[.01,-18.6],[.01,-13.4]],'hall');
   await ctx.close();continue;
  }
  if(profileRun)
   await stage('cold-staff-from-admission',null,null,[[.19,35.00],[.40,34.71],[1.9,34.71],[2.89,35.52],[3.92,35.52],[4.51,36.06],[6.1,36.06],
    [8.3,35.5],[8.3,32],[18,32]],'service');
  else await stage('ground-staff','atrium',[5,0,32],[[18,32]],'service');
  await stage('ground-return',null,null,[[7.6,32]],'atrium');
  await stage('upper-return','service',[27,6.8,23],[[10.4,23]],'atrium');
  if(mode==='before'){await ctx.close();continue;}
  await stage('staff-floor','service',[14,0,32],[[18,32],[18,27.2],[18,32],[23.65,32],[23.65,36],[23.65,32],[25,32],[25,28.7],[25,32],[30.3,32],[30.3,24.8]],'service');
  await stage('extra-stair',null,null,[[30.3,23],[26.2,23],[23.45,23],[22.42,23],[22.42,20.71],[23.45,20.71],[26.25,20.71],[30.2,20.71],[30.2,23],[10.4,23]],'atrium');
  if(profileRun){await ctx.close();continue;}
  await stage('archive-and-return','service',[26,0,32],[[29.18,32],[29.18,41],[36,41],[29.18,41],[29.18,32],[26,32]],'service');
  await stage('catwalk-gate','atrium',[-7.9,6.8,18.5],[[-10.2,18.5],[-10.2,21],[-14.2,21],[-13.1,21],[-10.3,21]],'atrium');
  await stage('exhibit-door','hall',[-6.7,0,7],[[-14,7],[-7,7]],'hall');
  await stage('rotunda-stair','hall',[0,0,-11.8],[[0,-20.2],[0,-12.2]],'hall');
  await stage('chronology-gate','atrium',[5.0,0,38.4],[[7.8,38.4],[7.8,45.3],[7.8,38.4]],'atrium');
  await ctx.close();
 }
 report.versionAtEnd=await current();report.complete=true;
}finally{await browser.close();await writeFile(path.join(dir,'doors.json'),JSON.stringify(report));}
