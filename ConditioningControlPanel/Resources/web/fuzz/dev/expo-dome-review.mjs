import {createRequire} from 'node:module';
import {mkdir,writeFile,unlink} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import path from 'node:path';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
const exec=promisify(execFile),output=process.argv[2];
if(!output)throw Error('Pass a directory for dome evidence.');
const dir=path.resolve(output),film=process.env.FILM==='1',gentle=process.env.REDUCED==='1';
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const phases=[
  [[-14.86,0,-5],[-22,1.4,-5],[-19.95,0,-5.15],[-22,1.4,-5]],
  [[-18.5,0,-5.7],[-23.5,.05,-4.4],[-20.15,0,-4.88],[-22,1.43,-5]],
  [[-18.7,0,-6.5],[-22,5.8,-5],[-20.0,0,-8.0],[-22.2,1.5,-4.4]],
  [[-15.6,0,-6.2],[-22.5,1.3,-5.4],[-21,0,-10.9],[-23.8,2.65,-4]],
  [[-18.8,0,-6.6],[-23.4,2.35,-4.1],[-22.2,0,-6.9],[-25.8,2.5,-1.7]],
  [[-19.4,0,-4.5],[-22,1.42,-5],[-21.2,0,-7.25],[-16.3,.45,-5]],
];
const trigger=[0,20,45,90,150,210], inspect=[0,29,58,110,175,219];
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--mute-audio','--autoplay-policy=no-user-gesture-required','--enable-unsafe-swiftshader']});
try {
 const context=await browser.newContext({viewport:{width:Number(process.env.W||1366),height:Number(process.env.H||768)},
   reducedMotion:gentle?'reduce':'no-preference'});
 if(film)await installGameAudio(context);
 const page=await context.newPage(),issues=[],rows=[],parts=[];
 page.on('pageerror',e=>issues.push(e.message));
 page.on('console',m=>{if(m.type()==='error'&&!/404|navigator.vibrate/.test(m.text()))issues.push(m.text());});
 const cdp=await context.newCDPSession(page);
 await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none');
 const button=page.getByRole('button',{name:'Play Llama Expo 2037'});await button.waitFor({timeout:60000});
 const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
 await frame.evaluate(async()=>{
   const pics=['reginald','margaret','gary','nana'];
   const files=await Promise.all(pics.map(async id=>new File([await (await fetch('assets/llamas/'+id+'.webp')).blob()],id+'.webp',{type:'image/webp'})));
   fuzz.engine.pictures.addOwn(files);fuzz.engine.pictures.setSource('own');
 });
 await button.click();await frame.locator('.expo-worm #btn').click();
 await frame.waitForFunction(()=>!!window.__expo,null,{timeout:120000});
 async function pose(at,target){
   await frame.evaluate(async({at,target})=>{await __expo.go('dome',at);__expo.lookAt(...target);},{at,target});
 }
 async function reach(seconds,real){
   if(real)await frame.waitForFunction(n=>__expo.state().seconds>=Math.min(n,210),seconds,{timeout:600000});
   else await frame.evaluate(n=>{const delta=n-__expo.state().seconds;if(delta>0)__expo.deepen(delta*(document.body.classList.contains('gentle')||matchMedia('(prefers-reduced-motion:reduce)').matches?1/.65:1));},seconds);
 }
 const begin=Date.now();await pose(phases[0][0],phases[0][1]);
 if(!film)await frame.waitForFunction(()=>__expo.state().elapsed>7);
 for(let i=0;i<6;i++){
   const [a,ta,b,tb]=phases[i];
   await reach(film?Math.max(0,trigger[i]-1):inspect[i],film);
   await pose(a,ta);
   if(film){
     const raw=path.join(dir,'stage-'+i+'-raw.mp4'),aud=path.join(dir,'stage-'+i+'-audio.webm'),part=path.join(dir,'stage-'+i+'.mp4');
     const stop=await beginTabVideo(page,raw);await beginGameAudio(frame);
     await frame.evaluate(async({i,aim})=>{
       const ex=__expo,t0=performance.now();let prev=t0;
       while(performance.now()-t0<10520){
         await new Promise(r=>requestAnimationFrame(r));const now=performance.now(),dt=Math.min(.08,(now-prev)/1000),t=(now-t0)/1000;prev=now;
         const dz=Math.cos(i+1)*.09,dx=Math.sin(i+.6)*.09;ex.walker.move(dx*dt,dz*dt);
         ex.camera.lookAt(aim[0]+.38*Math.sin(t*.17),aim[1]+.14*Math.sin(t*.22),aim[2]);
       }
     },{i,aim:ta});
     await finishGameAudio(frame,aud);await stop();await muxWalk(raw,aud,part,10);await unlink(raw);await unlink(aud);parts.push(part);
     await reach(inspect[i],true);
   }
   for(const [j,at,targ] of [[0,a,ta],[1,b,tb]]){
     await pose(at,targ);
     await frame.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
     const shot=await cdp.send('Page.captureScreenshot',{format:'png',fromSurface:true}),name='phase-'+i+'-'+(j?'detail':'room')+'.png';
     await writeFile(path.join(dir,name),Buffer.from(shot.data,'base64'));
     const obs=await frame.evaluate(()=>{const x=__expo,s=x.state();return {...s,eye:x.camera.position.toArray(),yaw:x.camera.rotation.y,
       roll:x.camera.rotation.z,visibleSourcePanes:[...document.querySelectorAll('.expo-media-face')].filter(n=>!n.hidden&&n.querySelector('img,video')).length,
       visibleFrames:x.scene.children.flatMap(g=>g.children).filter(o=>/^dome_frame_/.test(o.name)&&o.visible).length};});
     if(obs.phase!==i || Math.abs(obs.roll)>.000001)issues.push(name+' wrong phase or rolled horizon');
     rows.push({name,at,targ,...obs});
   }
 }
 if(film){
   const list=path.join(dir,'film-input.txt');await writeFile(list,parts.map(p=>"file '"+path.basename(p)+"'").join('\n')+'\n');
   await exec('ffmpeg',['-hide_banner','-loglevel','error','-y','-f','concat','-safe','1','-i',list,'-c','copy','-movflags','+faststart',path.join(dir,'walk.mp4')]);
   for(const p of parts)await unlink(p);await unlink(list);
 }
 // The doorway remains where it was at full depth. Use collision movement for the crossing.
 const escape=await frame.evaluate(async()=>{
   const ex=__expo;await ex.go('dome',[-14.2,0,-5]);ex.lookAt(-10.1,1.6,-5);
   for(let i=0;i<70;i++){ex.walker.move(.065,0);ex.walker.step(.016);await new Promise(r=>requestAnimationFrame(r));}
   return ex.state();
 });
 if(escape.zone!=='hall')issues.push('Door did not lead back to hall at full depth.');
 const result={timeMode:film?'continuous real clock':'phase checkpoints',gentle,wallSeconds:(Date.now()-begin)/1000,rows,escape,issues};
 await writeFile(path.join(dir,'capture.json'),JSON.stringify(result,null,2));
 console.log(JSON.stringify({wallSeconds:result.wallSeconds,shots:rows.length,escape:escape.zone,issues}));
 if(issues.length)process.exitCode=1;
 await context.close();
}finally{await browser.close();}
