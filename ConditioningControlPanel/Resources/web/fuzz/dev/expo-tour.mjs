// node dev/expo-tour.mjs M5 ./dev-captures/tour
import {createRequire} from 'node:module';
import {mkdir,readFile,writeFile,unlink} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import path from 'node:path';
import {tourShots} from './expo-tour-shots.mjs';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';

const [profile='M5',destination='./dev-captures/tour',customPlan]=process.argv.slice(2);
const shots=customPlan?JSON.parse(await readFile(customPlan,'utf8')):tourShots(profile);
const duration=shots.reduce((sum,shot)=>sum+shot.seconds,0),rehearse=process.env.REHEARSE==='1';
if(process.env.PLAN_ONLY==='1'){console.log(JSON.stringify({profile,duration,shots},null,2));process.exit(0);}
const exec=promisify(execFile),dir=path.resolve(destination),parts=[],audit={profile,rehearse,duration,edits:[],faults:[]};
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--autoplay-policy=no-user-gesture-required','--mute-audio']});
try{
  const context=await browser.newContext({viewport:{width:Number(process.env.W||1600),height:Number(process.env.H||900)}});
  await installGameAudio(context);
  const page=await context.newPage();page.on('pageerror',e=>audit.faults.push(e.message));
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd='+encodeURIComponent(process.env.HERD||'12'));
  const admit=page.getByRole('button',{name:'Play Llama Expo 2037'});await admit.waitFor({timeout:90000});
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  const available=await frame.evaluate(async()=>await(await fetch('assets/expo/zones/zones.json')).json());
  const missing=[...new Set(shots.map(s=>s.zone).filter(id=>id&&!available.zones.some(z=>z.id===id)))];
  if(missing.length)throw Error('The film needs finished rooms: '+missing.join(', '));
  if(profile==='M5'&&!customPlan&&available.start.id!=='atrium')throw Error('The final film requires the finished atrium admission.');
  for(let i=0;i<3;i++)await frame.locator('#btn').press('Enter');
  await frame.evaluate(async()=>{
    const files=await Promise.all(['margaret','gary','reginald','nana'].map(async id=>
      new File([await(await fetch('assets/llamas/'+id+'.webp')).blob()],id+'.webp',{type:'image/webp'})));
    fuzz.engine.pictures.addOwn(files);fuzz.engine.pictures.setSource('own');
  });
  audit.manualPresses=await frame.evaluate(()=>fuzz.state.taps);
  async function openGate(){
    await admit.click();const yes=frame.locator('.expo-worm #btn');await yes.waitFor();
    await page.waitForTimeout(rehearse?30:1220);await yes.click();
    await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
  }
  const objectNames={press:'press_main',hoof:'hoof_display',habitat:'habitat_glass',till:'till_display',loop:'shop_door',sale:'exit_sale',home:'shop_wormhole'};
  const activeState=()=>frame.evaluate(()=>window.__expo?.state()||{recap:!!document.querySelector('.fz-recap,.recap,#recap,.expo-ending-recap')});
  if(!shots[0].admission)await openGate();
  for(const [i,shot] of shots.entries()){
    if(shot.zone){
      await frame.evaluate(async({zone,at,aim})=>{const e=__expo;await e.go(zone,at);e.lookAt(...aim);e.root.focus();},{...shot});
      if(shot.dwell){
        if(rehearse)await frame.evaluate(t=>__expo.deepen(Math.max(0,t-__expo.state().seconds)),shot.dwell);
        else await frame.waitForFunction(t=>__expo.state().seconds>=t,shot.dwell,{timeout:600000});
      }
      if(shot.action){
        const prompt=await frame.evaluate(async({shot,nodeName})=>{
          const e=__expo,labels=[shot.action].flat(),prompt=()=>document.querySelector('.expo-prompt').textContent;
          const match=()=>labels.some(x=>prompt().includes(x));
          if(!match()){
            const obj=e.scene.getObjectByName(nodeName);
            if(obj){const T=await import('three'),box=new T.Box3().setFromObject(obj,true),c=box.getCenter(new T.Vector3());
              for(const fy of [.5,.66,.35,.78]){e.lookAt(c.x,box.min.y+(box.max.y-box.min.y)*fy,c.z);if(match())break;}
            }
          }
          return prompt();
        },{shot,nodeName:shot.object||objectNames[shot.id]});
        if(![shot.action].flat().some(a=>prompt.includes(a)))throw Error(shot.id+' missing expected reachable control, got '+JSON.stringify(prompt));
      }
    }
    const name=String(i+1).padStart(2,'0')+'-'+shot.id;
    const raw=path.join(dir,name+'-raw.mp4'),audio=path.join(dir,name+'-audio.webm'),part=path.join(dir,name+'.mp4');
    const item={id:shot.id,seconds:shot.seconds,cutTo:shot.at||null,before:await activeState(),inputs:[]};audit.edits.push(item);
    const stop=rehearse?null:await beginTabVideo(page,raw);
    if(!rehearse)await beginGameAudio(frame);
    const start=performance.now(),sec=()=>((performance.now()-start)/1000);
    if(shot.admission)await openGate();
    const cueTimes=rehearse?(shot.action?[.06]:[]):shot.repeat|| (shot.action?[1.1]:[]);
    for(const t of cueTimes){
      await page.waitForTimeout(Math.max(0,(t-sec())*1000));
      item.inputs.push({key:'E',at:sec(),prompt:await frame.locator('.expo-prompt').textContent()});
      await page.keyboard.press('e');
    }
    const remaining=(rehearse?.2:shot.seconds+.68)-sec();
    if(remaining>0 && shot.to){
      await frame.evaluate(async({to,aim,remaining})=>{
        const e=__expo,begin=performance.now(),metersPerSecond=Math.min(1.95,
          Math.hypot(to[0]-e.walker.feet.x,to[1]-e.walker.feet.z)/remaining);
        let prev=begin;
        while(performance.now()-begin<remaining*1000){
          await new Promise(r=>requestAnimationFrame(r));const now=performance.now(),dt=Math.min(.07,(now-prev)/1000);prev=now;
          const x=to[0]-e.walker.feet.x,z=to[1]-e.walker.feet.z,l=Math.hypot(x,z);
          if(l>.015){const d=Math.min(l,dt*metersPerSecond);e.walker.move(x/l*d,z/l*d);}
          e.camera.lookAt(...aim);e.camera.rotation.z=0;
        }
      },{...shot,remaining});
    }else if(remaining>0)await page.waitForTimeout(remaining*1000);
    if(shot.home)await frame.locator('.expo-root').waitFor({state:'detached',timeout:16000});
    item.after=await activeState();
    if(!rehearse){
      await finishGameAudio(frame,audio);await stop();await muxWalk(raw,audio,part,shot.seconds);
      await unlink(raw);await unlink(audio);parts.push(part);
    }
    await writeFile(path.join(dir,'edit-log.json'),JSON.stringify(audit,null,2));console.log(name);
  }
  if(!rehearse){
    const list=path.join(dir,'shots.txt');await writeFile(list,parts.map(p=>"file '"+path.basename(p)+"'").join('\n')+'\n');
    await exec('ffmpeg',['-v','error','-nostdin','-y','-f','concat','-safe','1','-i',list,'-vf','setpts=PTS-STARTPTS',
      '-af','asetpts=PTS-STARTPTS','-t',String(duration),'-c:v','libx264','-crf','23','-preset','fast','-threads','4',
      '-pix_fmt','yuv420p','-c:a','aac','-b:a','144k','-movflags','+faststart',path.join(dir,'walk.mp4')]);
    for(const p of parts)await unlink(p);await unlink(list);
  }
}catch(error){audit.faults.push(error.message);throw error;}
finally{await writeFile(path.join(dir,'edit-log.json'),JSON.stringify(audit,null,2));await browser.close();}
