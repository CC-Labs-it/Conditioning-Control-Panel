import {reviewPack, packFile} from './expo-review-pack.mjs';
// node dev/expo-tour.mjs M5 ./dev-captures/tour
import {createRequire} from 'node:module';
import {mkdir,readFile,writeFile,unlink} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {createHash} from 'node:crypto';
import {gunzipSync} from 'node:zlib';
import {promisify} from 'node:util';
import path from 'node:path';
import {tourShots} from './expo-tour-shots.mjs';
import {installGameAudio,beginGameAudio,finishGameAudio,beginTabVideo,muxWalk} from './expo-film.mjs';

const [profile='M5',destination='./dev-captures/tour',customPlan]=process.argv.slice(2);
const shots=customPlan?JSON.parse(await readFile(customPlan,'utf8')):tourShots(profile);
const duration=shots.reduce((sum,shot)=>sum+shot.seconds,0),rehearse=process.env.REHEARSE==='1';
if(process.env.PLAN_ONLY==='1'){console.log(JSON.stringify({profile,duration,shots},null,2));process.exit(0);}
const exec=promisify(execFile),dir=path.resolve(destination),parts=[],audit={profile,rehearse,duration,startedUtc:new Date().toISOString(),
  cameraCuts:'Cuts reposition the player between shots. Movement, interactions, sound and room transitions within each shot are live.',edits:[],faults:[]};
const base=process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/';
if(!['localhost','127.0.0.1','[::1]'].includes(new URL(base).hostname))throw Error('Use a local preview.');
const {chromium}=createRequire('C:/wt-fe/e2e/')(process.env.PW_DIR||'playwright');
await mkdir(dir,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--autoplay-policy=no-user-gesture-required','--mute-audio']});
try{
  const context=await browser.newContext({viewport:{width:Number(process.env.W||1600),height:Number(process.env.H||900)}});
  await installGameAudio(context);
  let packRoot;
  const page=await context.newPage();page.on('pageerror',e=>audit.faults.push(e.message));
  audit.mediaRequests=[];audit.roomGeometry={};audit.collectionGeometry={};
  const geometryReads=[];
  page.on('response',response=>{
    const name=packFile(response.url(),packRoot,'narration/');
    if(name&&!audit.mediaRequests.includes(name))audit.mediaRequests.push(name);
    const piece=new URL(response.url()).pathname.split('/').pop();
    if(/^(?:collection-[a-f0-9]+|guest-(?:chess|classifieds))\.glb$/.test(piece)&&!audit.collectionGeometry[piece]){
      const row=audit.collectionGeometry[piece]={};
      geometryReads.push(response.body().then(bytes=>{
        row.sha256=createHash('sha256').update(bytes).digest('hex');row.bytes=bytes.length;
      }).catch(error=>audit.faults.push('Reading '+piece+': '+error.message)));
    }
    const room=packFile(response.url(),packRoot,'zones/');
    if(room&&/\.glb(?:\.gz)?$/.test(room)&&!room.includes('collision')&&!audit.roomGeometry[room]){
      const row=audit.roomGeometry[room]={};
      geometryReads.push(response.body().then(bytes=>{
        const native=bytes[0]===0x1f&&bytes[1]===0x8b?gunzipSync(bytes):bytes;
        row.bytes=native.byteLength;row.sha256=createHash('sha256').update(native).digest('hex');
      }).catch(error=>audit.faults.push('Reading '+room+': '+error.message)));
    }
    if(response.status()>=400&&packFile(response.url(),packRoot))audit.faults.push(response.status()+' '+new URL(response.url()).pathname);
  });
  page.on('console',m=>{if(/(WebGL|GL_).*INVALID_(OPERATION|VALUE|ENUM)|THREE\.WebGLProgram: Shader Error/i.test(m.text()))audit.faults.push(m.text());});
  await page.goto(base+'dev/play-ending.html?end=ll_end&pictures=none&herd='+encodeURIComponent(process.env.HERD||'12'));
  packRoot=await reviewPack(page);
  const admit=page.getByRole('button',{name:'Play Llama Expo 2037'});await admit.waitFor({timeout:90000});
  const frame=page.frames().find(f=>f.url().includes('/fuzz/index.html'));
  const available=await frame.evaluate(async()=>await(await fetch(window.expoReviewAssetURL('zones/zones.json'))).json());
  audit.viewport=page.viewportSize();audit.browser=browser.version();audit.entrance=available.start.id;audit.stagedRooms=available.zones.map(z=>z.id);
  const missing=[...new Set(shots.map(s=>s.zone).filter(id=>id&&!available.zones.some(z=>z.id===id)))];
  if(missing.length)throw Error('The film needs finished rooms: '+missing.join(', '));
  if(profile==='M5'&&!customPlan&&available.start.id!=='atrium')throw Error('The final film requires the finished atrium admission.');
  if(shots.some(shot=>shot.guideEntrance||shot.archiveSlot)){
    const route=await frame.evaluate(async()=>(await(await fetch(window.expoReviewAssetURL('station-route.json'))).json()));
    for(const shot of shots){
      if(shot.guideEntrance){
        shot.at=route.start.at;shot.aim=route.slots[0].screen;
        shot.path=route.route[0].points.slice(1).map(([x,y,z])=>[x,z]);
      }else if(shot.archiveSlot){
        shot.at=route.slots.find(slot=>slot.id===shot.archiveSlot).standing;
      }
    }
  }
  for(let i=0;i<3;i++)await frame.locator('#btn').press('Enter');
  await frame.evaluate(async()=>{
    const files=await Promise.all(['margaret','gary','reginald','nana'].map(async id=>
      new File([await(await fetch('assets/llamas/'+id+'.webp')).blob()],id+'.webp',{type:'image/webp'})));
    fuzz.engine.pictures.addOwn(files);fuzz.engine.pictures.setSource('own');
  });
  audit.manualPresses=await frame.evaluate(()=>fuzz.state.taps);
  if(process.env.EXPO_CLEAN_PLATES==='1'){
    // In this isolated review context the real mixer keeps the environment and control sounds.
    // Hide the corresponding captions together with the muted voice stem.
    await frame.evaluate(()=>fuzz.engine.audio.mixer.setMuted('voices',true));
    await frame.addStyleTag({content:'.expo-narration{visibility:hidden!important}'});
    audit.recordedMix='Room tone, music, movement and controls with the Voices mixer source muted';
  }
  let purchased=false;
  async function purchase(){
    if(!purchased){await admit.click();await frame.locator('.expo-worm #btn').waitFor();purchased=true;}
  }
  async function openGate(){
    await purchase();const yes=frame.locator('.expo-worm #btn');
    await page.waitForTimeout(rehearse?30:1220);await yes.click();
    await frame.waitForFunction(()=>window.__expo?.state().ready,null,{timeout:120000});
  }
  const objectNames={press:'press_main',hoof:'hoof_display',habitat:'habitat_glass',photo:'photo_release',till:'till_display',loop:'shop_door',sale:'exit_sale',home:'shop_wormhole'};
  const activeState=()=>frame.evaluate(()=>{
    if(window.__expo){const s=__expo.narrationState();return {...__expo.state(),voice:s.voice,pa:s.pa,finished:s.finished};}
    return {recap:!!document.querySelector('.rr'),ending:fuzz.state.ended?.id,photoCharacters:fuzz.state.store?.ll_end?.groupPhoto?.length||0};
  });
  if(shots[0].admission)await purchase();
  else{await openGate();await frame.waitForFunction(()=>!__expo.state().textures.pendingDetailMaps);}
  for(const [i,shot] of shots.entries()){
    if(shot.zone){
      await frame.evaluate(async({zone,at})=>__expo.go(zone,at),shot);
      if(shot.visibleObject)await frame.waitForFunction(name=>{
        const node=__expo.scene.getObjectByName(name);return node?.visible&&(node.isMesh||node.children.length>0);
      },shot.visibleObject,{timeout:30000});
      const aim=await frame.evaluate(async({zone,at,aim,expectedStation,archiveSlot,aimObject})=>{
        const e=__expo;
        if(expectedStation||archiveSlot||aimObject){const T=await import('three'),face=e.scene.getObjectByName(aimObject||'expo_screen_'+(archiveSlot||expectedStation));
          if(!face)throw Error('Missing filmed object '+(aimObject||archiveSlot||expectedStation));
          aim=new T.Box3().setFromObject(face,true).getCenter(new T.Vector3()).toArray();}
        e.lookAt(...aim);e.root.focus();return aim;
      },{...shot});
      shot.aim=aim;
      if(shot.visibleRooms)await frame.waitForFunction(ids=>ids.every(id=>__expo.state().loaded.includes(id)),shot.visibleRooms,{timeout:45000});
      if(shot.waitForCue)await frame.waitForFunction(id=>{
        const s=__expo.narrationState();return s.pa.id===id||s.voice.id===id;
      },shot.waitForCue,{timeout:180000});
      if(shot.archiveSlot){
        // The visitor may already be listening at admission. Bring that record to this speaker.
        await frame.evaluate(async id=>{
          const e=__expo,T=await import('three');
          e.lookAt(...new T.Box3().setFromObject(e.scene.getObjectByName('expo_replay_'+id),true).getCenter(new T.Vector3()).toArray());
          e.root.focus();
        },shot.archiveSlot);
        await page.keyboard.press('e');
        await frame.evaluate(aim=>__expo.lookAt(...aim),aim);
        await frame.waitForFunction(id=>{
          const state=__expo.narrationState();
          return state.archive.playingAt===id && state.voice.id?.startsWith('ev_') &&
            state.stations.find(s=>s.id===id)?.project?.full;
        },shot.archiveSlot,{timeout:90000});
      }
      if(shot.expectedStation){
        if(shot.aimObject)await frame.evaluate(async id=>{
          const T=await import('three'),face=__expo.scene.getObjectByName('expo_screen_'+id);
          __expo.lookAt(...new T.Box3().setFromObject(face,true).getCenter(new T.Vector3()).toArray());
        },shot.expectedStation);
        try{
          await frame.waitForFunction(id=>{
            const s=__expo.narrationState();return s[id.startsWith('pa_')?'pa':'voice'].id===id;
          },shot.expectedStation,{timeout:160000});
        }catch(error){
          audit.waitingForStation=await frame.evaluate(id=>{
            const e=__expo,n=e.narrationState();
            return {id,feet:e.state().feet,speech:n.speech,station:n.stations.find(s=>s.id===id),heard:n.heard,ended:n.finished};
          },shot.expectedStation);
          throw error;
        }
        if(shot.aimObject)await frame.evaluate(at=>__expo.lookAt(...at),aim);
      }
      if(shot.settleSeconds)await page.waitForTimeout(shot.settleSeconds*1000);
      if(shot.dwell){
        if(rehearse)await frame.evaluate(t=>__expo.deepen(Math.max(0,t-__expo.state().seconds)),shot.dwell);
        else await frame.waitForFunction(t=>__expo.state().seconds>=t,shot.dwell,{timeout:600000});
      }
      if(shot.action){
        if(shot.id==='photo')await frame.waitForFunction(()=>__expo.scene.getObjectByName('photo_release')?.visible,null,{timeout:5000});
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
    if(process.env.EXPO_CLEAN_PLATES==='1') {
      const correct=await frame.evaluate(()=>{
        const node=document.querySelector('.expo-narration');
        return fuzz.engine.audio.mixer.snapshot().voices?.muted && (!node||getComputedStyle(node).visibility==='hidden');
      });
      if(!correct)throw Error('Clean plate requires a muted Voices source and hidden voice captions.');
    }
    const raw=path.join(dir,name+'-raw.mp4'),audio=path.join(dir,name+'-audio.webm'),part=path.join(dir,name+'.mp4');
    const item={id:shot.id,seconds:shot.seconds,cutTo:shot.at||null,before:await activeState(),inputs:[]};audit.edits.push(item);
    const stop=rehearse?null:await beginTabVideo(page,raw);
    const audioAfterVideo=rehearse?null:((await beginGameAudio(frame))-stop.startedAt)/1000;
    if(!rehearse)item.audioAfterVideoSeconds=Number(audioAfterVideo.toFixed(3));
    const trackAt=performance.now();
    await frame.evaluate(()=>{
      const readings=[],start=performance.now();
      function sample(){
        const subtitle=channel=>document.querySelector('.expo-narration [data-channel="'+channel+'"]')?.dataset.lineId||'';
        const voice=subtitle('voice'),pa=subtitle('pa'),previous=readings.at(-1);
        if(!previous||voice!==previous.voice||pa!==previous.pa)
          readings.push({at:(performance.now()-start)/1000,voice,pa});
      }
      sample();
      const timer=setInterval(sample,200);
      window.__expoTourSpeech=()=>{clearInterval(timer);sample();return readings;};
    });
    const start=performance.now(),sec=()=>((performance.now()-start)/1000);
    if(shot.admission)await openGate();
    const cueTimes=rehearse?(shot.action?[.06]:[]):shot.repeat|| (shot.action?[1.1]:[]);
    for(const t of cueTimes){
      await page.waitForTimeout(Math.max(0,(t-sec())*1000));
      item.inputs.push({key:'E',at:sec(),prompt:await frame.locator('.expo-prompt').textContent()});
      await page.keyboard.press('e');
    }
    const remaining=(rehearse?.2:shot.seconds+.68)-sec();
    if(remaining>0 && (shot.to||shot.afterAim||shot.path||shot.travel)){
      item.crossings=await frame.evaluate(async({to,aim,afterAim,path,pathLook,travel,remaining})=>{
        const e=__expo,begin=performance.now(),feet=e.walker.feet,start=feet.clone(),crossings=[];
        const stops=path||(to?[to]:[]);let index=0,length=0,from=[start.x,start.z];
        for(const point of stops){length+=Math.hypot(point[0]-from[0],point[1]-from[1]);from=point;}
        const metersPerSecond=Math.min(1.95,length/remaining);
        const turnStart=e.camera.quaternion.clone(),turnEnd=e.camera.clone();
        if(afterAim)turnEnd.lookAt(...afterAim);
        let prev=begin;
        while(performance.now()-begin<remaining*1000){
          await new Promise(r=>requestAnimationFrame(r));const now=performance.now(),dt=Math.min(.07,(now-prev)/1000);prev=now;
          const before=feet.clone();
          if(travel)e.walker.move(travel[0]*dt/remaining,travel[1]*dt/remaining);
          else if(index<stops.length){const point=stops[index],x=point[0]-feet.x,z=point[1]-feet.z,l=Math.hypot(x,z);
            if(l>.015){const d=Math.min(l,dt*metersPerSecond);e.walker.move(x/l*d,z/l*d);}else index++;}
          if(feet.distanceTo(before)>1.5)crossings.push({from:before.toArray(),to:feet.toArray()});
          if(afterAim){const t=Math.min(1,(now-begin)/(Math.min(2,remaining)*1000));
            e.camera.quaternion.slerpQuaternions(turnStart,turnEnd.quaternion,t*t*(3-2*t));}
          else if(pathLook){const point=stops[Math.min(index,stops.length-1)],x=point[0]-feet.x,z=point[1]-feet.z;
            if(Math.hypot(x,z)>.02){const yaw=Math.atan2(-x,-z),delta=yaw-e.camera.rotation.y;
              e.camera.rotation.y+=Math.atan2(Math.sin(delta),Math.cos(delta))*Math.min(1,dt*5);e.camera.rotation.x=.035;}}
          else if(travel)e.camera.lookAt(aim[0]+feet.x-start.x,aim[1]+feet.y-start.y,aim[2]+feet.z-start.z);
          else e.camera.lookAt(...aim);
          e.camera.rotation.z=0;
        }
        return crossings;
      },{...shot,remaining});
      const expected=shot.expectCrossings??(shot.travel?1:0);
      if(expected&&!rehearse&&item.crossings.length!==expected)throw Error(shot.id+' missed its architectural crossing.');
    }else if(remaining>0)await page.waitForTimeout(remaining*1000);
    if(shot.id==='loop'){
      await frame.waitForFunction(()=>Math.abs(__expo.walker.feet.z+5)<.1,null,{timeout:8000});
      await frame.waitForFunction(()=>__expo.doors().find(d=>d.id==='shop-exit')?.progress<.01,null,{timeout:8000});
    }
    if(shot.home){
      await frame.locator('.expo-root').waitFor({state:'detached',timeout:160000});
      await frame.locator('.rr').waitFor({state:'attached',timeout:16000});
      if(shots.some(s=>s.id==='photo')){
        const photo=frame.locator('.rr img[alt="Group photograph in the Hall of the Twelve"]');
        await photo.waitFor({state:'attached',timeout:10000});
        item.recapPhoto=await photo.evaluate(async img=>{await img.decode();return {width:img.naturalWidth,height:img.naturalHeight,characters:img.src.length};});
        await frame.waitForFunction(()=>document.querySelector('.rr.settled,.rr-skip'),null,{timeout:30000});
        const skip=frame.locator('.rr-skip');
        if(await skip.count()){await skip.click();item.inputs.push({action:'Skip the recap introduction after the natural departure',at:sec()});}
        await frame.locator('.rr.settled').waitFor({state:'visible',timeout:10000});
        if(!rehearse)await page.waitForTimeout(2550);
      }
    }
    item.after=await activeState();
    if(shot.id==='photo'){
      const photo=await frame.evaluate(()=>fuzz.state.store?.ll_end?.groupPhoto||'');item.photoCharacters=photo.length;
      if(!item.photoCharacters || item.photoCharacters>240023)throw Error('The group photograph did not fit its recap slot.');
      await writeFile(path.join(dir,'group-photo.webp'),Buffer.from(photo.slice(photo.indexOf(',')+1),'base64'));
    }
    const speech=await frame.evaluate(()=>{const data=window.__expoTourSpeech();delete window.__expoTourSpeech;return data;});
    const offset=rehearse?0:(trackAt-stop.startedAt)/1000;
    item.speechChanges=speech.map(point=>({...point,at:Number((point.at+offset).toFixed(3))}));
    if(!rehearse){
      await finishGameAudio(frame,audio);await stop();item.cut=await muxWalk(raw,audio,part,shot.seconds,audioAfterVideo);
      await unlink(raw);await unlink(audio);parts.push(part);
    }
    if(shot.home)await page.screenshot({path:path.join(dir,'return-recap.jpg'),quality:91,type:'jpeg'});
    await writeFile(path.join(dir,'edit-log.json'),JSON.stringify(audit,null,2));console.log(name);
  }
  await Promise.all(geometryReads);
  if(audit.faults.length)throw Error('The film has recorded game or resource errors.');
  if(!rehearse&&!(process.env.EXPO_KEEP_PARTS==='1'&&process.env.EXPO_CLEAN_PLATES==='1')){
    const list=path.join(dir,'shots.txt');await writeFile(list,parts.map(p=>"file '"+path.basename(p)+"'").join('\n')+'\n');
    await exec('ffmpeg',['-v','error','-nostdin','-y','-f','concat','-safe','1','-i',list,'-vf','setpts=PTS-STARTPTS',
      '-af','asetpts=PTS-STARTPTS','-t',String(duration),'-c:v','libx264','-crf','23','-preset','fast','-threads','4',
      '-pix_fmt','yuv420p','-c:a','aac','-b:a','144k','-movflags','+faststart',path.join(dir,'walk.mp4')]);
    if(process.env.EXPO_KEEP_PARTS!=='1')for(const p of parts)await unlink(p);
    await unlink(list);
  }
}catch(error){audit.faults.push(error.message);throw error;}
finally{await writeFile(path.join(dir,'edit-log.json'),JSON.stringify(audit,null,2));await browser.close();}
