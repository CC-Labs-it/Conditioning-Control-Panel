// Play each release recording to its own native media-ended event, without a 3D renderer.
import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {beginGameAudio,finishGameAudio,installGameAudio} from './expo-film.mjs';

const require=createRequire(import.meta.url);
const {chromium}=process.env.PW_DIR?require(process.env.PW_DIR):createRequire('C:/wt-fe/e2e/')('playwright');
const dir=path.resolve(process.argv[2]||'dev-captures/complete-audio');
const base=new URL(process.env.FUZZ_BASE||'http://127.0.0.1:8937/fuzz/');
assert(['localhost','127.0.0.1','[::1]'].includes(base.hostname));
const speed=Number(process.env.EXPO_AUDIO_RATE||4);
assert(speed>=1 && speed<=4);
await mkdir(dir,{recursive:true});
const report={started:new Date().toISOString(),base:base.href,acceleratedRate:speed,
  methodology:'One visitor, real createNarration and AudioKit, HTMLMediaElement decode, no programmatic seeks. A pair of test room roots supply speaker positions. Chrome WebGL and GPU acceleration disabled.',
  errors:[],recordings:[],requests:{}};
const browser=await chromium.launch({headless:true,executablePath:process.env.CHROME_EXE||'C:/Program Files/Google/Chrome/Application/chrome.exe',
  args:['--disable-gpu','--disable-3d-apis','--disable-webgl','--disable-software-rasterizer','--disable-accelerated-2d-canvas','--disable-extensions','--mute-audio',
    '--disable-background-timer-throttling','--disable-renderer-backgrounding','--autoplay-policy=no-user-gesture-required']});
const sum=value=>createHash('sha256').update(value).digest('hex');
const stamp=time=>new Date(Math.round(time*1000)).toISOString().slice(11,23).replace('.',',');
const save=()=>writeFile(path.join(dir,'complete-audio.json'),JSON.stringify(report,null,2)+'\n');
let page;
try{
  const context=await browser.newContext({viewport:{width:960,height:540},reducedMotion:'reduce',serviceWorkers:'block'});
  await installGameAudio(context);
  await context.addInitScript(()=>{
    const get=HTMLCanvasElement.prototype.getContext;
    window.__expoGlRequests=0;
    HTMLCanvasElement.prototype.getContext=function(kind,...args){
      if(/webgl/i.test(kind)){window.__expoGlRequests++;throw Error('WebGL requested in rendererless test');}
      return get.call(this,kind,...args);
    };
  });
  page=await context.newPage();
  page.on('pageerror',error=>report.errors.push(error.message));
  page.on('response',response=>{
    const match=/\/narration\/hall\/([^/?]+)\.mp3/.exec(response.url());
    if(match)(report.requests[match[1]]||=[]).push(response.status());
  });
  // Only bypass ordinary game boot. Every tested module, asset, and language string is served by the game.
  await page.route(new URL('index.html',base).href,async route=>{
    const original=await route.fetch(), markup=await original.text();
    const map=markup.match(/<script type="importmap">[\s\S]*?<\/script>/)?.[0];
    if(!map)throw Error('Missing production import map.');
    return route.fulfill({contentType:'text/html',body:'<!doctype html><html><head><meta charset=utf-8>'+map+
      '<style>body{margin:0;background:#14211f;color:#fff;font-family:Segoe UI,sans-serif}.expo-narration{padding:20px 45px;font-size:25px;line-height:1.3}.expo-narration p:empty{display:none}</style></head><body><main></main></body></html>'});
  });
  await page.goto(new URL('index.html',base).href);
  for(const file of ['ui/expo/narration.js','ui/expo/speech-sequence.js','ui/expo/archive-story.js','audio/kit.js','assets/expo/narration/manifest.json']){
    const response=await context.request.get(new URL(file,base).href);
    assert.equal(response.status(),200);
    (report.inputSha256||={})[file]=sum(await response.body());
  }
  const manifest=await page.evaluate(async playbackRate=>{
    const T=await import('three');
    const {createNarration}=await import('./ui/expo/narration.js');
    const {createKit}=await import('./audio/kit.js');
    const manifest=await (await fetch('assets/expo/narration/manifest.json')).json();
    const byId=new Map(manifest.map(row=>[row.id,row]));
    const firstId=manifest.find(row=>row.archive?.number===1).id;
    const realTime=new Set(['pa_arrive_1','pa_timeline',firstId]);
    const root=document.querySelector('main'),handles=new Set(),results=new Map(),problems=new Set(),marks=[];
    let peakSpeech=0,peakVoiceElements=0,peakCaptions=0,recordingSince=null,latestCaption=null,lowestWelcomeGain=1;
    const roomTone={frames:0,seconds:0};
    const camera=new T.PerspectiveCamera(55,16/9,.01,200),scene=new T.Scene();
    camera.position.set(0,1.62,1.75);camera.lookAt(0,1.62,8);
    const walker={zone:'atrium',blocked:()=>true},buttons=new Map(),memory=new Map();
    const getCaption=()=>[...root.querySelectorAll('.expo-narration p')].filter(line=>{
      const r=line.getBoundingClientRect(),s=getComputedStyle(line);
      return line.textContent && s.display!=='none' && s.visibility!=='hidden' && s.opacity!=='0' && r.width>0 && r.height>0 && r.top<innerHeight&&r.bottom>0;
    });
    const entryFor=el=>results.get(el.__expoClip);
    const nameOf=el=>/\/narration\/hall\/([^/?]+)\.mp3/.exec(el.currentSrc||el.src)?.[1];
    function identify(el){
      const id=nameOf(el);
      if(!id)return;
      el.__expoClip=id;
      if(!results.has(id))results.set(id,{id,set:byId.get(id)?.set,manifestSeconds:byId.get(id)?.seconds,nativeEnded:0,
        playingEvents:0,seekEvents:0,samples:0,captionMismatches:0,minimumGain:1,peakMasterRms:0,lastNonemptySubtitle:'',rate:realTime.has(id)?1:playbackRate});
      return results.get(id);
    }
    const kit=createKit({makeMedia(){
      const el=new Audio();
      el.addEventListener('loadedmetadata',()=>{
        const info=identify(el);
        el.playbackRate=info?.rate||playbackRate;
        if(info)info.decodedSeconds=el.duration;
      });
      el.addEventListener('seeking',()=>{const info=entryFor(el);if(info)info.seekEvents++;});
      el.addEventListener('playing',()=>{
        const info=identify(el);
        if(info){info.playingEvents++;el.playbackRate=info.rate;}
      });
      el.addEventListener('error',()=>{if(el.src)problems.add('Decoder error for '+(el.currentSrc||el.src));});
      el.addEventListener('ended',()=>{
        const info=identify(el);if(!info)return;
        const lines=getCaption(),text=lines.find(line=>line.dataset.lineId===info.id)?.textContent||'';
        info.nativeEnded++;info.endedWithFlag=el.ended;info.endedAt=el.currentTime;info.decodedSeconds=el.duration;
        info.visibleSubtitleAtEnd=text;info.finalSubtitleExact=text===byId.get(info.id).subtitle;
        if(text)info.lastNonemptySubtitle=text;
        if(!info.finalSubtitleExact)problems.add('Missing complete visible subtitle at native end of '+info.id);
      });
      return el;
    }});
    assertUserAudio();
    function assertUserAudio(){if(!kit.arm())throw Error('AudioKit could not arm the real audio context');}
    const output=window.__expoRecordingBus;
    const analyser=output.context.createAnalyser();
    analyser.fftSize=1024;
    const masterTap=output.context.createMediaStreamSource(output.stream);
    masterTap.connect(analyser);
    const signal=new Float32Array(analyser.fftSize);
    const ctx={audio:{...kit,stream(url,options){
      const h=kit.stream(url,options),el=h.el;
      const meta={el,gain:options.gain};handles.add(meta);
      return {...h,setGain(value){meta.gain=value;h.setGain(value);},stop(ms){if(!ms)handles.delete(meta);h.stop(ms);}};
    }},gentle:true,reduced:true,paused:false,store:{get:key=>memory.get(key),set:(key,v)=>memory.set(key,v)}};
    const registration=(mesh,label,callback)=>{buttons.set(mesh.name,callback);return()=>buttons.delete(mesh.name);};
    const n=createNarration({ctx,camera,scene,root,register:registration,walker,sound:{play(){}},assetURL:key=>'assets/expo/'+key,lite:true,
      owned:()=>manifest.filter(row=>row.set==='log').map(row=>row.id.slice(4))});
    const groupById=new Map();
    function mount(zone,slot,x){
      const group=new T.Group();
      for(const [type,z] of [['screen',0],['lens',.3],['replay',.35],['station',.3],['horn',.6]]){
        const obj=new T.Mesh(new T.PlaneGeometry(type==='screen'?1.7:.13,type==='screen'?1.25:.11),new T.MeshBasicMaterial());
        obj.name=type==='horn'?'expo_horn_'+zone:'expo_'+type+'_'+slot;obj.position.set(x,1.5,z);group.add(obj);
      }
      scene.add(group);groupById.set(zone,group);n.mount(zone,group);
    }
    function unmount(zone){n.unmount(zone);const group=groupById.get(zone);if(group){scene.remove(group);groupById.delete(zone);}}
    function sample(){
      const visible=getCaption();peakCaptions=Math.max(peakCaptions,visible.length);
      if(visible.length>1)problems.add('Overlapping narrator subtitles');
      if(recordingSince!==null){
        const active=visible[0],id=active?.dataset.lineId||'';
        if((latestCaption?.id||'')!==id){
          const time=(performance.now()-recordingSince)/1000;
          if(latestCaption)latestCaption.end=time;
          latestCaption=active?{id,text:active.textContent,start:time}:null;
          if(latestCaption)marks.push(latestCaption);
        }
      }
      let spoken=0,activeMedia=0;
      analyser.getFloatTimeDomainData(signal);
      let sumSquares=0;
      for(const value of signal)sumSquares+=value*value;
      const rms=Math.sqrt(sumSquares/signal.length);
      for(const {el,gain} of handles){
        if(el.paused||el.ended||el.readyState<2)continue;
        activeMedia++;
        const info=identify(el);
        if(!info){
          if(el.currentSrc.endsWith('/twelve.ogg')){
            roomTone.frames++;
            if(!visible.some(line=>line.textContent==='[Kevin is speaking]'))problems.add('Kevin room tone lost its caption');
          }
          continue;
        }
        spoken++;info.samples++;info.minimumGain=Math.min(info.minimumGain,gain);
        info.peakMasterRms=Math.max(info.peakMasterRms,rms);
        const text=visible.find(line=>line.dataset.lineId===info.id)?.textContent||'';
        if(info.id==='pa_arrive_1')lowestWelcomeGain=Math.min(lowestWelcomeGain,gain);
        if(text)info.lastNonemptySubtitle=text;
        if(text!==byId.get(info.id).subtitle)info.captionMismatches++;
        info.lastSampleTime=el.currentTime;
      }
      peakSpeech=Math.max(peakSpeech,spoken);
      peakVoiceElements=Math.max(peakVoiceElements,activeMedia);
      if(spoken>1)problems.add('Simultaneously playing speech elements');
      const s=n.debug();const seconds=s.voice?.roomToneSeconds;
      if(Number.isFinite(seconds))roomTone.seconds=Math.max(roomTone.seconds,seconds);
    }
    let prev=performance.now();
    const timer=setInterval(()=>{const now=performance.now();n.tick(Math.min(.15,(now-prev)/1000));prev=now;sample();},30);
    mount('atrium','slot_01',0);mount('hall','slot_02',30);
    const press=slot=>buttons.get('expo_replay_'+slot)();
    window.completeAudio={
      n,ready:()=>n.available(),startSample(){recordingSince=performance.now();sample();},
      stopSample(){sample();if(latestCaption)latestCaption.end=(performance.now()-recordingSince)/1000;recordingSince=null;return marks;},
      begin(){void n.cue('pa_arrive_1',{zone:'atrium'});void press('slot_01');},
      cross(){walker.zone='hall';camera.position.set(30,1.62,1.75);camera.updateMatrixWorld(true);unmount('atrium');},
      listenHere(){void press('slot_02');},
      async record(id){const row=byId.get(id);if(row.set==='event')await press('slot_02');else await n.cue(id,{replay:true,zone:walker.zone});return results.get(id);},
      pause(on){ctx.paused=on;kit.hold(on);n.pause(on);},
      stats:()=>({recordings:[...results.values()],peakSpeech,peakVoiceElements,peakCaptions,problems:[...problems],roomTone,lowestWelcomeGain,webglRequests:window.__expoGlRequests}),
      close(){clearInterval(timer);n.dispose();kit.stopStreams();masterTap.disconnect();analyser.disconnect();}
    };
    return manifest;
  },speed);
  await page.waitForFunction(()=>completeAudio.ready());
  const events=manifest.filter(r=>r.set==='event').sort((a,b)=>a.archive.number-b.archive.number);
  const first=events[0].id;
  await beginGameAudio(page);await page.evaluate(()=>{completeAudio.startSample();completeAudio.begin();});
  await page.waitForFunction(()=>completeAudio.n.debug().pa.seconds>1);
  await page.evaluate(()=>completeAudio.cross());
  await page.waitForFunction(id=>completeAudio.n.debug().voice.id===id&&completeAudio.n.debug().voice.seconds>.8,first,{timeout:45000});
  await page.evaluate(()=>completeAudio.listenHere());
  await page.waitForFunction(id=>completeAudio.n.debug().finished[id]===1,first,{timeout:45000});
  const marks=await page.evaluate(()=>completeAudio.stopSample());
  await finishGameAudio(page,path.join(dir,'opening.webm'));
  report.opening={fullSpeed:true,contents:marks,originalRoomUnloaded:true,archiveMovedToSecondStation:true};
  const captions=marks.map((m,i)=>(i+1)+'\n'+stamp(m.start)+' --> '+stamp(m.end)+'\n'+m.text+'\n').join('\n');
  await writeFile(path.join(dir,'opening.srt'),captions+'\n');
  const remaining=events.slice(1).concat(manifest.filter(row=>row.set!=='event'&&!['pa_arrive_1','pa_timeline'].includes(row.id)));
  let index=0;
  for(const row of remaining){
    const completion=page.evaluate(id=>completeAudio.record(id),row.id);
    if(index===0){
      await page.waitForFunction(id=>completeAudio.n.debug().voice.id===id&&completeAudio.n.debug().voice.seconds>1,row.id);
      await page.evaluate(()=>{void completeAudio.n.planter('planter_10');});
      await page.waitForFunction(()=>completeAudio.n.debug().speech.queue.includes('planter_10'));
      const waiter=await page.evaluate(()=>completeAudio.n.debug().speech);
      assert.equal(waiter.playing,row.id);assert.ok(!waiter.interrupted);
      report.queuedPlanter={active:row.id,queued:'planter_10',interrupted:false};
      await page.evaluate(()=>completeAudio.pause(true));
      const heldAt=await page.evaluate(()=>completeAudio.n.debug().voice.seconds);
      await page.waitForTimeout(450);
      const heldUntil=await page.evaluate(()=>completeAudio.n.debug().voice.seconds);
      assert.equal(heldAt,heldUntil);
      await page.evaluate(()=>completeAudio.pause(false));
      report.pause={id:row.id,at:heldAt,heldUntil};
    }
    let timeout;
    try {
      const observed=await Promise.race([completion,new Promise((_,reject)=>{timeout=setTimeout(()=>reject(Error('Playback timeout '+row.id)),70000);})]);
      assert.equal(observed?.id,row.id,'unexpected archive progression');
      assert.equal(observed.nativeEnded,1,row.id+' released before its native ended event');
      if(index===0){
        await page.waitForFunction(()=>completeAudio.n.debug().voice.id==='planter_10');
        report.queuedPlanter.startedAfter=observed.nativeEnded;
        report.queuedPlanter.caption=await page.evaluate(()=>completeAudio.n.debug().voice.subtitle);
      }
    } finally {clearTimeout(timeout);}
    index++;
    if(index%12===0){Object.assign(report,await page.evaluate(()=>completeAudio.stats()));await save();console.log(report.recordings.length+'/96 completed');}
  }
  Object.assign(report,await page.evaluate(()=>completeAudio.stats()));
  for(const row of manifest){
    const actual=report.recordings.find(r=>r.id===row.id);assert(actual,row.id);
    assert.equal(actual.nativeEnded,1,row.id);assert.equal(actual.seekEvents,0,row.id);
    assert.equal(actual.endedWithFlag,true,row.id);assert.equal(actual.captionMismatches,0,row.id);
    assert.equal(actual.visibleSubtitleAtEnd,row.subtitle,row.id);assert.equal(actual.lastNonemptySubtitle,row.subtitle,row.id);
    assert(actual.samples>0&&actual.peakMasterRms>.00005&&Math.abs(actual.endedAt-actual.decodedSeconds)<.08,row.id);
    assert(report.requests[row.id]?.every(s=>s===200||s===206),row.id);
  }
  assert.equal(report.recordings.length,96);assert.equal(report.peakSpeech,1);assert.equal(report.peakVoiceElements,1);assert.equal(report.peakCaptions,1);
  assert.equal(report.webglRequests,0);assert.deepEqual(report.problems,[]);assert.deepEqual(report.errors,[]);
  assert(report.roomTone.frames>0&&report.roomTone.seconds>=15,'Kevin must complete fifteen seconds of captioned room tone before his real recording');
  await page.evaluate(()=>completeAudio.close());
  await context.close();
  const exec=promisify(execFile);
  await exec('ffmpeg',['-hide_banner','-loglevel','error','-y','-threads','2','-filter_threads','1','-f','lavfi','-i','color=c=0x172823:s=960x540:r=5',
    '-i','opening.webm','-vf',"subtitles=opening.srt:force_style='FontName=Segoe UI,FontSize=24,Alignment=5,MarginL=36,MarginR=36,Outline=1,Shadow=0'",
    '-shortest','-c:v','libx264','-threads','2','-preset','ultrafast','-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart','opening-captioned.mp4'],
    {cwd:dir,windowsHide:true});
  report.passed=true;
}catch(error){
  report.errors.push(String(error));report.passed=false;process.exitCode=1;
  if(page&&!page.isClosed()){
    try{Object.assign(report,await page.evaluate(()=>window.completeAudio?.stats()));
      report.lastState=await page.evaluate(()=>window.completeAudio?.n.debug());}catch{}
  }
}
finally{report.ended=new Date().toISOString();await save();await browser.close();}
console.log(JSON.stringify({passed:report.passed,recordings:report.recordings.length,errors:report.errors}));
