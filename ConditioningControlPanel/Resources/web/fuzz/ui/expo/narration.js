import { Ray, Vector3 } from 'three';
import { centre, createProjector, createRestingAtlas } from './projectors.js';
import { createSpeechSequence } from './speech-sequence.js';
import { restingSlide, slideIndex } from './slide-program.js';

const DWELL_SECONDS = 1;
const family = id => id.startsWith('hab_') ? 'hab_1' : id.startsWith('hoof_') ? 'hoof_1' :
  id.startsWith('dome_') ? 'dome_0' : id;
const canonical = id => id?.replace(/^keep_/, 'hab_');
const nearest = (nodes, point) => nodes.reduce((best,node)=>
  !best || node.pos.distanceToSquared(point)<best.pos.distanceToSquared(point) ? node : best,null);

export function createNarration({ctx,camera,scene,root,register,walker,sound,assetURL,lite,owned}) {
  const book=new Map(), rooms=new Map(), heard=new Set(), finished=new Map(), unavailable=new Set(), abort=new AbortController();
  const sequence=createSpeechSequence({start,suspend,resume,release});
  const atlas=createRestingAtlas(assetURL);
  const block=new Ray(), right=new Vector3(), delta=new Vector3(), screenPoint=new Vector3();
  const subtitle=root.ownerDocument.createElement('div');
  subtitle.className='expo-narration';
  const captions={};
  for(const key of ['voice','pa']){
    const p=root.ownerDocument.createElement('p');p.setAttribute('aria-live','polite');p.dataset.channel=key;
    subtitle.append(p);captions[key]=p;
  }
  root.append(subtitle);
  let held=ctx.paused, dead=false, check=0, ready=false, dwellStation=null, dwellTime=0, planterBook;
  sequence.pause(held);
  const load=fetch(assetURL('narration/manifest.json'),{signal:abort.signal}).then(async r=>{
    if(!r.ok)throw new Error('Expo narration manifest HTTP '+r.status);
    const rows=await r.json();for(const row of rows)book.set(row.id,row);
    const index=await fetch(assetURL('narration/resting-index.json'),{signal:abort.signal});
    if(!index.ok)throw Error('Resting atlas index HTTP '+index.status);
    atlas.arrange(await index.json());
    ready=true;
  }).catch(e=>{if(!dead)console.warn(e);});

  function mount(zone,group){
    group.updateMatrixWorld(true);
    const list=[],horns=[],undo=[];
    group.traverse(node=>{
      const match=/^expo_(?:fixture|screen|lens|station|replay)_(log_([a-z]+))$/.exec(node.name);
      if(match&&!owned().includes(match[2]))node.visible=false;
      // GLTFLoader expands a multi-material node into child meshes with derived names.
      if(node.parent?.name.startsWith('expo_station_')||node.parent?.name.startsWith('expo_horn_'))return;
      if(node.name.startsWith('redacted_') && node.visible && !owned().includes(node.name.slice(9)))
        list.push({id:'pa_hall4_missing',zone,pos:centre(node),notice:true});
      if(node.name.startsWith('expo_horn_'))horns.push({pos:centre(node)});
      if(!node.name.startsWith('expo_station_')||!node.visible)return;
      const id=node.name.slice(13);
      const project=createProjector({group,id,scene,assetURL,lite,atlas});
      const station={id,zone,pos:centre(node),project,index:0,progress:0,last:id};
      list.push(station);
      const button=group.getObjectByName('expo_replay_'+id);
      if(button){undo.push(register(button,'Replay narration',()=>cue(station.last,{station,replay:true}),3.5));}
    });
    rooms.set(zone,{list,horns,undo});
    // A streamed projector can rejoin a narration that continued outside this room.
    for(const entry of [sequence.current,sequence.interrupted]){
      if(!entry)continue;
      const station=list.find(s=>s.id===entry.stationId);
      if(station){
        entry.station=station;station.last=entry.row.id;station.index=entry.index||0;station.progress=entry.progress||0;
        station.project?.setActive(!!entry.lit);station.project?.setPaused(entry===sequence.interrupted||held);
      }
    }
  }
  function unmount(zone){
    const room=rooms.get(zone);if(!room)return;
    for(const f of room.undo)f();
    for(const s of room.list)s.project?.dispose();
    for(const entry of [sequence.current,sequence.interrupted,...sequence.queue])
      if(entry?.station?.zone===zone)entry.station=null;
    if(dwellStation?.zone===zone){dwellStation=null;dwellTime=0;}
    rooms.delete(zone);
  }
  function locate(id){
    const key=family(id);
    for(const room of rooms.values()){const s=room.list.find(s=>s.id===key);if(s)return s;}
    return null;
  }
  function acoustic(entry){
    delta.subVectors(entry.pos,camera.position);
    const distance=delta.length(), range=entry.row.set==='pa'?8:2.65;
    // Keep conversational level at the pedestal, with inverse-distance falloff beyond it.
    const gain=.94/(1+(Math.max(0,distance-1.15)/range)**1.65);
    right.set(1,0,0).applyQuaternion(camera.quaternion);
    const pan=distance>.01?Math.max(-.94,Math.min(.94,delta.dot(right)/distance)):0;
    return {gain,pan};
  }
  function caption(key,text,id=''){
    captions[key].textContent=text;captions[key].dataset.lineId=id;
  }
  function clearHandle(entry){
    if(!entry.handle)return;
    entry.handle.el?.removeEventListener('ended',entry.onEnd);
    entry.handle.el?.removeEventListener('error',entry.onError);
    entry.handle.el?.removeEventListener('playing',entry.onPlaying);
    entry.handle.stop();entry.handle=null;
  }
  function playFile(key,entry,roomTone){
    clearHandle(entry);
    entry.kevin=roomTone;entry.lastMediaTime=0;entry.waiting=0;entry.retried=false;
    const {gain,pan}=acoustic(entry);
    const file=roomTone?'audio/twelve.ogg':(entry.row.set==='planter'?'audio/':'narration/')+entry.row.audio;
    caption(key,roomTone?'[Kevin is speaking]':entry.row.subtitle,entry.row.id);
    if(entry.row.set==='planter'&&!entry.row.audio){entry.lit=true;return;}
    const volume=gain*(roomTone?.32:1);
    const h=entry.handle=(!roomTone&&entry.tail)||ctx.audio.stream(assetURL(file),{bus:'voice',spatial:true,pan,loop:roomTone,
      gain:volume,autoplay:!held,preload:'auto'});
    if(h===entry.tail){entry.tail=null;h.setGain(volume);h.setPan?.(pan);if(!held)h.play();}
    entry.onEnd=()=>{if(!roomTone)sequence.finish(entry);};
    entry.onError=()=>{console.warn('Expo recording unavailable',file);unavailable.add(entry.row.id);clearHandle(entry);};
    entry.onPlaying=()=>{
      if(entry.lit||held||entry!==sequence.current)return;
      entry.lit=true;entry.station?.project?.setActive(true);
      if(entry.station?.project&&entry.row.slides.length)click(entry);
    };
    h.el?.addEventListener('ended',entry.onEnd);
    h.el?.addEventListener('error',entry.onError);
    h.el?.addEventListener('playing',entry.onPlaying);
    if(!h.el||(!h.el.paused&&h.el.readyState>=3))entry.onPlaying();
  }
  function click(entry){
    const {gain,pan}=acoustic(entry);sound.play('press',.39*gain,pan);
  }
  function start(entry){
    const key=entry.channel;
    entry.station ||= locate(entry.stationId);
    if(entry.station){
      entry.station.last=entry.row.id;entry.station.index=0;entry.station.progress=0;
    }
    const kevin=entry.row.id==='log_kevin';
    if(kevin)entry.tail=ctx.audio.stream(assetURL('narration/'+entry.row.audio),
      {bus:'voice',spatial:true,autoplay:false,preload:'metadata',gain:0});
    playFile(key,entry,kevin);
  }
  function suspend(entry){
    entry.handle?.pause();entry.station?.project?.setPaused(true);
    caption(entry.channel,'');
  }
  function resume(entry){
    caption(entry.channel,entry.kevin?'[Kevin is speaking]':entry.row.subtitle,entry.row.id);
    entry.station?.project?.setPaused(false);entry.station?.project?.setActive(!!entry.lit);entry.handle?.play();
  }
  function release(entry,ok){
    if(ok)finished.set(entry.row.id,(finished.get(entry.row.id)||0)+1);
    clearHandle(entry);entry.tail?.stop();entry.station?.project?.setPaused(false);entry.station?.project?.setActive(false);
    if(captions[entry.channel].dataset.lineId===entry.row.id)caption(entry.channel,'');
    entry.resolve();
  }
  function enqueue(id,{station=null,replay=false,zone=walker.zone,position=null,priority=null}={}){
    const row=book.get(id);
    if(dead||!row||(!replay&&heard.has(id)))return Promise.resolve();
    if(row.set==='log'&&!owned().includes(id.slice(4)))return Promise.resolve();
    const key=row.set==='pa'?'pa':'voice';
    // A replay press while a copy is already waiting joins that request.
    const queued=sequence.queue.find(e=>e.row.id===id);if(queued)return queued.promise;
    heard.add(id);station ||= locate(id);
    const horn=nearest(rooms.get(zone)?.horns||[],camera.position);
    const origin=position?(position.isVector3?position:new Vector3(...position)):
      (station&&!station.notice?station.pos:null)||horn?.pos||camera.position;
    let resolve;const promise=new Promise(r=>{resolve=r;});
    const entry={row,zone,station,channel:key,priority,stationId:station?.id||family(id),pos:origin.clone(),handle:null,
      clock:0,lastMediaTime:0,kevin:false,progress:0,duck:1,resolve,promise};
    sequence.submit(entry);return promise;
  }
  function cue(id,options){
    if(!id||dead)return Promise.resolve();
    return ready?enqueue(canonical(id),options):load.then(()=>enqueue(canonical(id),options));
  }
  async function planter(id,options={}){
    if(dead||!['planter_10','planter_60','planter_120'].includes(id))return;
    if(!planterBook){
      planterBook=fetch(assetURL('audio/planter-voices.json'),{signal:abort.signal}).then(r=>{
        if(!r.ok)throw Error('Planter narration HTTP '+r.status);
        return r.json();
      }).catch(e=>{planterBook=null;if(!dead)console.warn(e);return {};});
    }
    const config=(await planterBook)[id];if(dead||!config)return;
    if(!book.has(id))book.set(id,{...config,id,set:'planter',slides:[]});
    return enqueue(id,{...options,priority:'planter'});
  }
  function clearRay(target){
    block.origin.copy(camera.position);block.direction.subVectors(target,camera.position);
    const distance=block.direction.length();if(distance<.09)return true;
    block.direction.multiplyScalar(1/distance);
    return !walker.blocked(block,distance);
  }
  function proximity(dt,checkOther){
    let focused=null,best=Infinity;
    const immediate=[];
    for(const room of rooms.values())for(const s of room.list){
      if(heard.has(s.id)||!book.has(s.id))continue;
      const row=book.get(s.id);
      const eligible=row.set==='event'||row.set==='log'||['ex_press','hab_1','pa_timeline','pa_photos','pa_hall4_missing'].includes(s.id);
      if(!eligible)continue;
      const distance=camera.position.distanceTo(s.pos);
      if(distance>3.5)continue;
      if(!s.project){
        if(checkOther&&clearRay(s.pos))immediate.push({s,distance});
        continue;
      }
      const target=s.project.viewPoint;
      if(delta.subVectors(camera.position,target).dot(s.project.viewNormal)<=0)continue;
      screenPoint.copy(target).project(camera);
      if(screenPoint.z< -1||screenPoint.z>1||Math.abs(screenPoint.x)>.65||Math.abs(screenPoint.y)>.72)continue;
      const score=screenPoint.x**2+screenPoint.y**2+.002*distance;
      if(score<best&&clearRay(target)){focused=s;best=score;}
    }
    for(const {s} of immediate.sort((a,b)=>a.distance-b.distance))cue(s.id,{station:s,zone:s.zone});
    if(focused!==dwellStation){dwellStation=focused;dwellTime=0;}
    else if(focused){
      dwellTime+=dt;
      if(dwellTime>=DWELL_SECONDS){
        if(book.get(focused.id).set==='event'&&!heard.has('pa_timeline'))cue('pa_timeline',{zone:focused.zone});
        cue(focused.id,{station:focused,zone:focused.zone});
        dwellStation=null;dwellTime=0;
      }
    }
  }
  function tick(dt){
    if(dead||held||!ready)return;
    check-=dt;proximity(dt,check<=0);if(check<=0)check=.12;
    const e=sequence.current;
    if(e){
      const key=e.channel;
      const el=e.handle?.el,at=el?.currentTime||0;
      if(el){
        let advance=at-e.lastMediaTime;
        if(e.kevin&&advance<0&&Number.isFinite(el.duration))advance+=el.duration;
        e.clock+=Math.max(0,advance);e.lastMediaTime=at;
        e.waiting=advance>.0001?0:e.waiting+dt;
        if(e.waiting>2&&el.paused&&!e.retried){e.retried=true;e.handle.play();}
        if(e.waiting>15){unavailable.add(e.row.id);clearHandle(e);}
      }else{e.clock+=dt;e.onPlaying?.();}
      if(e.kevin&&e.clock>=15){e.roomToneSeconds=e.clock;e.clock=15;playFile(key,e,false);}
      else if(!el&&e.clock>=e.row.seconds+(e.row.id==='log_kevin'?15:0)){
        sequence.finish(e,e.row.set==='planter'&&!e.row.audio);
      }
      const actual=(e.kevin?e.tail?.el:e.handle?.el)?.duration;
      const duration=e.duration=(Number.isFinite(actual)&&actual>0?actual:e.row.seconds)+(e.row.id==='log_kevin'?15:0);
      const progress=e.progress=Math.min(1,e.clock/Math.max(.1,duration));
      const i=slideIndex(e.row,progress);
      const changed=i>=0&&i!==(e.index||0);
      if(changed){
        e.index=i;
        if(e.station?.project)click(e);
      }
      if(e.station){
        e.station.index=Math.max(0,i);e.station.progress=progress;
      }
      const {gain,pan}=acoustic(e);
      e.gain=gain*(e.kevin?.32:1);e.pan=pan;
      e.handle?.setGain(e.gain);e.handle?.setPan?.(pan);
    }
    for(const room of rooms.values())for(const s of room.list)if(s.project){
      const row=book.get(s.last);
      s.project.step(dt,ctx.gentle||ctx.reduced,camera.position.distanceTo(s.pos),row?.slides[s.index],s.progress,
                     restingSlide(row),row?.slides[s.index+1]);
    }
  }
  function pause(on){
    held=on;
    if(on){dwellStation=null;dwellTime=0;}
    sequence.pause(on);
  }
  function dispose(){
    dead=true;abort.abort();
    sequence.dispose();
    for(const id of [...rooms.keys()])unmount(id);
    subtitle.remove();atlas.dispose();
  }
  function debug(){
    const data={heard:[...heard],finished:Object.fromEntries(finished),unavailable:[...unavailable],stations:[...rooms.values()].flatMap(r=>r.list.map(s=>({id:s.id,zone:s.zone,pos:s.pos.toArray(),project:s.project?.stats})))};
    for(const key of ['voice','pa']){const e=sequence.current?.channel===key?sequence.current:null;
      data[key]={id:e?.row.id,seconds:e?.clock??0,duration:e?.duration,roomToneSeconds:e?.roomToneSeconds,gain:e?.gain,pan:e?.pan,duck:e?.duck,subtitle:captions[key].textContent,queue:sequence.queue.filter(q=>q.channel===key).map(q=>q.row.id)};}
    data.speech={playing:sequence.current?.row.id,interrupted:sequence.interrupted?.row.id,queue:sequence.queue.map(q=>q.row.id),focus:dwellStation?.id,dwellSeconds:dwellTime,paused:held};
    return data;
  }
  function meter(output){
    output.projectors=0;output.voices=0;
    if(sequence.current?.lit&&!held){
      if(sequence.current.handle?.playing)output.voices++;
      if(sequence.current.station?.project)output.projectors++;
    }
  }
  return {mount,unmount,cue,planter,available:()=>ready&&sequence.available,tick,pause,dispose,debug,meter};
}
