// Exercise the real narration module with a controllable media element for network failure cases.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {mkdir,writeFile} from 'node:fs/promises';
import path from 'node:path';

const {chromium}=createRequire(process.env.PW_DIR||'C:/wt-fe/e2e/')('playwright');
const base=new URL(process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/');
assert(['localhost','127.0.0.1','[::1]'].includes(base.hostname));
const output=path.resolve(process.argv[2]||'dev-captures/audio-recovery');
await mkdir(output,{recursive:true});
const browser=await chromium.launch({executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--disable-gpu','--disable-webgl','--mute-audio']});
try {
  const page=await browser.newPage();
  await page.route('**/main.js',route=>route.fulfill({contentType:'text/javascript',body:''}));
  await page.goto(new URL('index.html',base).href);
  const report=await page.evaluate(async()=>{
    const T=await import('three'),{createNarration}=await import('./ui/expo/narration.js');
    const saved=new Map(),handles=[],buttons=new Map(),labels=new Map(),root=document.createElement('div');
    document.body.append(root);
    const camera=new T.PerspectiveCamera(50,1,.1,200),scene=new T.Scene(),group=new T.Group();
    camera.position.set(0,1.4,2);scene.add(group);
    for(const [i,id] of ['slot_01','slot_02','log_kevin'].entries()){
      for(const prefix of ['station','screen','lens','replay','review','label','caption']){
        const mesh=new T.Mesh(new T.BoxGeometry(1,1,.1),new T.MeshStandardMaterial());
        mesh.name='expo_'+prefix+'_'+id;mesh.position.set((i-1)*1.25,1.4,0);
        mesh.userData.expoWidth=1;mesh.userData.expoHeight=.3;group.add(mesh);
      }
    }
    const media={stream(url){
      const el=Object.assign(new EventTarget(),{paused:true,ended:false,readyState:0,currentTime:0,duration:20.49});
      const handle={url,el,setGain(){},setPan(){},play(){el.paused=false;el.dispatchEvent(new Event('playing'));},
        pause(){el.paused=true;},stop(){el.paused=true;handle.disposed=true;}};
      handles.push(handle);return handle;
    }};
    const ctx={audio:media,activeMs:0,store:{get:k=>saved.get(k),set:(k,v)=>saved.set(k,v)}};
    const n=createNarration({ctx,camera,scene,root,
      register:(mesh,label,action)=>{buttons.set(mesh.name,action);labels.set(mesh.name,label);return ()=>buttons.delete(mesh.name);},walker:{zone:'atrium',blocked:()=>false},
      sound:{play(){}},assetURL:file=>new URL('assets/expo/'+file,location.href).href,lite:true,owned:()=>['kevin']});
    n.mount('atrium',group);
    const blank=id=>group.getObjectByName('expo_screen_'+id).material.uniforms.hasProgram.value===0;
    if(!blank('slot_01')||!blank('slot_02')||!blank('log_kevin'))throw Error('screen exposed before catalogue load');
    const cue=n.cue('pa_arrive_1',{zone:'atrium'});
    while(!handles.length)await new Promise(resolve=>setTimeout(resolve,10));
    const require=(ok,label)=>{if(!ok)throw Error(label);};
    const advance=seconds=>{for(let i=0;i<seconds*10;i++){ctx.activeMs+=100;n.tick(.1);}};
    const end=el=>{el.currentTime=el.duration;el.ended=true;el.dispatchEvent(new Event('ended'));};
    const press=buttons.get('expo_replay_slot_01');
    const first=handles[0].el;first.duration=10.33;first.currentTime=3.21;
    camera.lookAt(1.25,1.4,0);advance(2);
    require(!n.debug().heard.includes('log_kevin')&&blank('log_kevin'),'looking started a projector');
    require(labels.get('expo_replay_slot_01')()==='Start projector','first Play advertises unrevealed content');
    buttons.get('expo_caption_slot_01')();
    require(root.querySelector('.expo-reading-card').hidden,'unstarted plaque opens');
    n.tick(.1);press();
    advance(24);
    const buffered=n.debug();
    require(blank('slot_01')&&blank('slot_02')&&!buffered.archive.displayed,'queued record exposed before playing');
    require(buffered.pa.id==='pa_arrive_1' && buffered.pa.seconds===3.21 && !buffered.finished.pa_arrive_1,'stalled recording was retired');
    require(root.querySelector('[data-channel=pa]').textContent.endsWith('where we can use them.'),'subtitle lost during buffering');
    first.currentTime=9.92;n.tick(.1);
    require(n.debug().pa.id==='pa_arrive_1','welcome retired at catalogue time');
    end(first);await cue;
    if(handles.at(-1).url.endsWith('/pa_timeline.mp3'))end(handles.at(-1).el);
    const original=handles.at(-1);
    require(original.url.endsWith('/ev_h0.mp3'),'first record did not start');
    original.el.currentTime=6.4;n.tick(.1);original.el.dispatchEvent(new Event('error'));
    const retry=handles.at(-1);require(original.disposed && retry!==original,'missing recovery handle');
    advance(22);require(n.debug().archive.completed===0 && retry.el.paused,'retry advanced before metadata');
    retry.el.readyState=1;retry.el.dispatchEvent(new Event('loadedmetadata'));
    require(retry.el.currentTime===6.4 && !retry.el.paused,'retry lost the last position');n.tick(.1);
    retry.el.dispatchEvent(new Event('error'));
    require(n.debug().archive.completed===0 && n.debug().archive.next==='ev_h0' && !saved.size,'failed record was marked heard');
    press();
    if(handles.at(-1).url.endsWith('/pa_timeline.mp3'))end(handles.at(-1).el);
    const replay=handles.at(-1).el;replay.currentTime=19.9;n.tick(.1);
    require(!blank('slot_01')&&blank('slot_02'),'projection lit the wrong station');
    buttons.get('expo_caption_slot_01')();
    require(!root.querySelector('.expo-reading-card').hidden,'started record unreadable');
    require(n.debug().voice.id==='ev_h0' && root.querySelector('[data-channel=voice]').textContent.endsWith('write it for.'),'ending subtitle missing');
    end(replay);
    require(n.debug().archive.completed===1 && saved.get('lastArchiveRecord')==='ev_h0','real end did not advance archive');
    n.unmount('atrium');n.mount('atrium',group);n.tick(.1);
    require(!blank('slot_01')&&blank('slot_02')&&blank('log_kevin'),'re-entry exposed an untouched station');
    require(labels.get('expo_replay_slot_02')()==='Start projector','unused Play advertises the next record');
    const welcome=n.cue('pa_arrive_1',{replay:true});
    const roomSpeaker=handles.at(-1).el;
    const log=buttons.get('expo_replay_log_kevin')();
    advance(5);
    require(blank('log_kevin'),'queued log appeared during the welcome');
    end(roomSpeaker);await welcome;
    n.tick(.1);
    require(n.debug().voice.id==='log_kevin'&&!blank('log_kevin'),'manual log start expired during the welcome');
    n.dispose();await log;
    return {stalledSeconds:24,frozenAt:buffered.pa.seconds,welcomeStillActiveAt:9.92,recoveredPosition:6.4,
      failedRecordStayedAvailable:true,firstRecordSubtitleAt:19.9,advancedOnEnded:true,passed:true};
  });
  await writeFile(path.join(output,'recovery.json'),JSON.stringify(report,null,2)+'\n');
  console.log(report);
} finally {await browser.close();}
