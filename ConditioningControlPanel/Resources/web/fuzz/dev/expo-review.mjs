// Start the game with node dev/serve.mjs 8937, then run this script with a review output directory.
import { createRequire } from 'node:module';
import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk } from './expo-film.mjs';
const require = createRequire('C:/wt-fe/e2e/');
const { chromium } = require(process.env.PW_DIR || 'playwright');
const [output, zone = 'hall', routeFile] = process.argv.slice(2);
if (!output) throw new Error('Usage: node dev/expo-review.mjs <output-directory> [zone] [route.json]');
const height = Number(process.env.H || 810), width = Number(process.env.W || 1440);
const base = process.env.FUZZ_BASE || 'http://127.0.0.1:8937/fuzz/';
const local = new URL(base);
if (!['localhost','127.0.0.1','[::1]'].includes(local.hostname)) throw new Error('Review requires a local preview.');
const target = path.resolve(output);
await mkdir(target, { recursive: true });
const hall = [
  ['01-entrance',[0,0,13.2],[0,2.35,-6.3]],
  ['02-roof',[-1.5,0,9],[1.5,8.4,-3]],
  ['03-west-aisle',[-7.7,0,10.8],[-7.1,2,-6.5]],
  ['04-east-aisle',[8.1,0,10.5],[8.4,2.25,-6.2]],
  ['05-mezzanine',[-9.8,3.4,11],[.5,2.9,-6.5]],
  ['06-information',[3.4,0,12.8],[5.5,.88,10]],
  ['07-first-press-approach',[-2.9,0,-4.25],[0,1.7,-10]],
  ['08-button',[.08,0,-7.73],[0,1.03,-9]],
  ['09-accession',[1.46,0,-7.68],[1.46,.92,-8.874]],
  ['10-west-reverse',[-7.25,0,-10.8],[1.1,3,12.4]],
  ['11-east-gallery',[9.8,3.4,-11.5],[-3.3,3.15,9]],
  ['12-north-axis',[0,3.4,-14],[1,2.4,7.4]],
];
const custom = routeFile ? JSON.parse(await (await import('node:fs/promises')).readFile(routeFile,'utf8')) : null;
if (!custom && zone !== 'hall') throw new Error('A route JSON file must supply the fixed cameras for ' + zone);
const shots = custom?.shots || hall;
const coarse = process.env.TOUCH === '1', film = process.env.FILM === '1';
const browser = await chromium.launch({ executablePath: process.env.CHROME_EXE || 'C:/Program Files/Google/Chrome/Application/chrome.exe',
  headless: true, args: ['--enable-unsafe-swiftshader','--autoplay-policy=no-user-gesture-required','--mute-audio', '--disable-background-timer-throttling'] });
const issues = [], results = [];
try {
  const context = await browser.newContext({ viewport: {width,height}, isMobile: coarse, hasTouch: coarse,
    reducedMotion: process.env.REDUCED === '1' ? 'reduce' : 'no-preference' });
  if(film)await installGameAudio(context);
  const page = await context.newPage();
  page.on('pageerror', error => issues.push(error.message));
  page.on('console', msg => { if (msg.type() === 'error' && !msg.text().includes('404') && !msg.text().includes('Blocked call to navigator.vibrate')) issues.push(msg.text()); });
  const cdp = await context.newCDPSession(page);
  const resources = new Set(); let zoneBytes = 0;
  await cdp.send('Network.enable');
  cdp.on('Network.responseReceived',event=>{if(event.response.url.includes('/assets/expo/zones/')) resources.add(event.requestId);});
  cdp.on('Network.loadingFinished',event=>{if(resources.has(event.requestId))zoneBytes+=event.encodedDataLength;});
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none'+(coarse?'&lite=1':''));
  const admission=page.getByRole('button',{name:'Play Llama Expo 2037'});
  await admission.waitFor({timeout:90000});
  const frame = page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  if(process.env.EXPO_VISITOR==='1'){
    for(let i=0;i<12;i++)await frame.locator('#btn').press('Enter');
    await frame.evaluate(async()=>{
      const {noteAcceptedTerms}=await import('./ui/unlocks/fate/museum/terms.js');
      const {CLAUSES}=await import('./data/content/clauses.js');
      for(const id of Object.keys(CLAUSES).slice(0,3))noteAcceptedTerms(fuzz.state,id,[],1,false);
    });
  }
  await admission.click();
  const admitted = Date.now();
  if(process.env.EXPO_MISSING==='1'){
    await frame.locator('.expo-worm #btn').waitFor({timeout:30000});
    await frame.evaluate(()=>{fuzz.engine.life.llamas=8;});
  }
  await frame.locator('.expo-worm #btn').click({timeout:30000});
  await frame.waitForFunction(()=>!!window.__expo, null, {timeout:120000});
  const entryMs = Date.now()-admitted;
  const firstViewBytes=zoneBytes;
  const available=await frame.evaluate(async()=>((await(await fetch('assets/expo/zones/zones.json')).json()).zones.map(z=>z.id)));
  await frame.evaluate(()=>document.fonts.ready);
  const timing=await frame.evaluate(async()=>{
    const ex=window.__expo,dt=[];
    let stamp=performance.now();const begin=stamp;
    for(let i=0;i<180&&stamp-begin<9500;i++) {
      const now=await new Promise(resolve=>requestAnimationFrame(resolve));
      if(i>1)dt.push(now-stamp);stamp=now;
    }
    dt.sort((a,b)=>a-b);
    const gl=ex.renderer.getContext(),ext=gl.getExtension('WEBGL_debug_renderer_info');
    return {gpu:ext?gl.getParameter(ext.UNMASKED_RENDERER_WEBGL):gl.getParameter(gl.RENDERER),frames:dt.length,
      medianMs:dt[Math.floor(dt.length*.5)],p95Ms:dt[Math.floor(dt.length*.95)]};
  });
  timing.firstViewBytes=firstViewBytes;
  await frame.waitForFunction(()=>document.querySelector('.expo-guide')?.hidden,null,{timeout:30000});
  async function useAt(at,look,expected,repeats=1){
    const label=await frame.evaluate(async({zone,at,look})=>{
      const e=window.__expo;await e.go(zone,at);e.lookAt(...look);e.root.focus();
      return document.querySelector('.expo-prompt').textContent;
    },{zone,at,look});
    if(!label.includes(expected))throw Error('Expected '+expected+' at action position, saw '+label);
    for(let i=0;i<repeats;i++)await page.keyboard.press('KeyE');
  }
  let domeReviewTime=0;
  for (const [name, at, look] of shots) {
    const number=Number(name.slice(0,2));
    const needsAtrium=available.includes('atrium')&&((zone==='service'&&number===12)||(zone==='catwalk'&&(number===2||number===3)));
    const cold=needsAtrium&&process.env.EXPO_COLD_NEIGHBORS==='1';
    if(cold){
      await frame.evaluate(()=>__expo.go('secret',[84.2,0,41]));
      await frame.waitForFunction(()=>!__expo.state().loaded.includes('atrium'),null,{timeout:60000});
    }
    if(process.env.EXPO_EXHIBIT_STATES==='1'){
      const n=Number(name.slice(0,2));
      if(zone==='hoof'){
        const consoleAt=[-18.42,0,8.93],consoleLook=[-18.4,1.77,10.468];
        if(n===7)await useAt(consoleAt,consoleLook,'Try',1);
        if(n===9){
          await useAt(consoleAt,consoleLook,'Try',5);
          await frame.waitForFunction(()=>__expo.exhibits.state.hoof==='pictures');
        }
        if(n===10){
          await useAt(consoleAt,[-18.4,1.67,10.454],'picture',1);
          await frame.waitForFunction(()=>__expo.exhibits.state.hoof==='assistant');
        }
        if(n===11)await frame.waitForFunction(()=>__expo.exhibits.state.hoof==='complete');
      }
      if(zone==='shop'){
        if(n===6)await useAt([19.67,0,-10.14],[19.67,1.53,-11.53],'contract');
        if(n===8 || n===10){await useAt([15.03,0,-12.14],[15.03,1.14,-13.93],'exit door');await page.waitForTimeout(320);}
        if(n===11)await useAt([22.77,0,-10.05],[22.77,1.37,-11.43],'Buy Exit');
      }
      if(zone==='dome'){
        const target=[0,0,22,25,47,52,92,110,153,180,213,219][n-1];
        await frame.evaluate(async({at,seconds})=>{const e=window.__expo;await e.go('dome',at);e.deepen(seconds);},
          {at,seconds:Math.max(0,target-domeReviewTime)});
        domeReviewTime=target;
      }
    }
    await frame.evaluate(async ({zone,at,look})=>{
      const ex = window.__expo; await ex.go(zone,at); ex.lookAt(...look); ex.draw();
      await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));
    },{zone,at,look});
    if(needsAtrium){
      await frame.waitForFunction(()=>__expo.state().loaded.includes('atrium'),null,{timeout:60000});
      await frame.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
    }
    const state = await frame.evaluate(()=>window.__expo.state());
    const shot = await cdp.send('Page.captureScreenshot',{format:'png', fromSurface:true});
    await writeFile(path.join(target, name+'.png'),Buffer.from(shot.data,'base64'));
    results.push({ name, at, look, ...state,requiredNeighbors:needsAtrium?['atrium']:[],coldNeighborCheck:cold });
  }
  if (film) {
    const nodes = custom?.walk || [[0,13],[0,-7.67],[0,-3],[3.1,0],[3.1,7.65],[8.35,7.65],[8.35,-.9],[10.3,-.9],[10.3,-14.55],[0,-14.55]];
    const raw=path.join(target,'walk-raw.mp4'),audio=path.join(target,'walk-audio.webm');
    const stopVideo=await beginTabVideo(page,raw);
    await beginGameAudio(frame);
    const walk=await frame.evaluate(async ({nodes,zone})=>{
      const ex = window.__expo; await ex.go(zone,[nodes[0][0],0,nodes[0][1]]);
      const start = performance.now(); let previous = start, segment = 1, wait=0, pressed=false;const checkpoints=[];
      while (performance.now()-start < 60500) {
        await new Promise(r=>requestAnimationFrame(r));
        const now = performance.now(), dt = Math.min((now-previous)/1000,.1); previous=now;
        const dst=nodes[Math.min(segment,nodes.length-1)],dx=dst[0]-ex.walker.feet.x,dz=dst[1]-ex.walker.feet.z,dist=Math.hypot(dx,dz);
        let aimX=dst[0],aimY=ex.camera.position.y,aimZ=dst[1];
        if(segment>=nodes.length){aimX=0;aimY=1.8;aimZ=8;}
        else if(zone==='hall' && segment===1 && dist<.18){aimX=0;aimY=1.04;aimZ=-9;wait+=dt;if(wait>2.3&&!pressed){pressed=ex.interact();} }
        else if(zone==='hall' && segment===8){aimX=0;aimY=2.5;aimZ=-7;}
        const yaw=Math.atan2(ex.camera.position.x-aimX,ex.camera.position.z-aimZ);
        const pitch=Math.atan2(aimY-ex.camera.position.y,Math.max(.01,Math.hypot(aimX-ex.camera.position.x,aimZ-ex.camera.position.z)));
        ex.camera.rotation.y+=Math.atan2(Math.sin(yaw-ex.camera.rotation.y),Math.cos(yaw-ex.camera.rotation.y))*Math.min(1,dt*3);
        ex.camera.rotation.x+=(pitch-ex.camera.rotation.x)*Math.min(1,dt*3);
        if (segment>=nodes.length) continue;
        if (dist<.18) {
          if(zone==='hall'&&segment===1&&wait<5)continue;
          checkpoints.push({segment,feet:ex.walker.feet.toArray()});segment++;continue;
        }
        const moved=Math.min(dist,dt*1.6);ex.walker.move(dx/dist*moved,dz/dist*moved);
      }
      return {checkpoints,pressed,routeComplete:segment>=nodes.length,feet:ex.walker.feet.toArray()};
    },{nodes,zone});
    await finishGameAudio(frame,audio);
    await stopVideo();
    await muxWalk(raw,audio,path.join(target,'walk.mp4'));
    const {unlink}=await import('node:fs/promises');await unlink(raw);await unlink(audio);
    await writeFile(path.join(target,'walk.json'),JSON.stringify(walk,null,2));
    if(!walk.routeComplete || (zone==='hall'&&!walk.pressed))issues.push('Recorded walk missed a waypoint or button press; see walk.json.');
  }
  const summary = {zone,entryMs,width,height,coarse,timing,shots:results,issues};
  await writeFile(path.join(target,'capture.json'),JSON.stringify(summary,null,2));
  await context.close();
  console.log(JSON.stringify({zone,entryMs,timing,shots:results.length,issues}));
  if(issues.length) process.exitCode=1;
} finally { await browser.close(); }
