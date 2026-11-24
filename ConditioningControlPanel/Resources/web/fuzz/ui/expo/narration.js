import { renewalLines } from './renewal-lines.js';
import {railLines} from './rail-lines.js';
import {archiveGalleries,exemptRecord} from './rail-routes.js';
import { trailLines } from './trail-lines.js';
import { Ray, Vector3 } from 'three';
import { centre, createProjector, createRestingAtlas } from './projectors.js';
import { createSpeechSequence } from './speech-sequence.js';
import { restingSlide, slideIndex } from './slide-program.js';
import { createArchiveStory } from './archive-story.js';
import { archiveDisplays } from './archive-displays.js';
import { createReadingCard } from './reading-card.js';
import { captionPages, captionAt } from './caption-pages.js';
import { planterLines } from './planter.js';

const family = id => id.startsWith('hab_') ? 'hab_1' : id.startsWith('hoof_') ? 'hoof_1' :
  id.startsWith('dome_') ? 'dome_0' : id;
const canonical = id => id?.replace(/^keep_/, 'hab_');
const nearest = (nodes, point) => nodes.reduce((best,node)=>
  !best || node.pos.distanceToSquared(point)<best.pos.distanceToSquared(point) ? node : best,null);

export function createNarration({ctx,camera,scene,root,register,walker,sound,assetURL,lite,owned,viewing=()=>{},releaseView=()=>{}}) {
  const book=new Map(), rooms=new Map(), heard=new Set(), finished=new Map(), unavailable=new Set(), abort=new AbortController();
  for(const row of railLines)book.set(row.id,row);
  const visitedScreens=new Set(),wetHorns=[];
  const restored=ctx.store.get('playedProjectors');
  const playedProjectors=new Set(Array.isArray(restored)?restored.filter(id=>typeof id==='string'):[]);
  let muffled=false;
  function setMuffled(on){
    if(on===muffled)return;
    muffled=on;ctx.audio.muffle?.('expo-guide',on,{hz:675,cut:.76,fadeOutMs:180});
  }
  function muffle(zone,pos){wetHorns.push({zone,pos:pos.clone(),until:ctx.activeMs+25000});}
  const sequence=createSpeechSequence({start,suspend,resume,release,now:()=>ctx.activeMs,nearby});
  const atlas=createRestingAtlas(assetURL);
  const reader=createReadingCard(root,camera);
  const block=new Ray(), right=new Vector3(), delta=new Vector3();
  const subtitle=root.ownerDocument.createElement('div');
  subtitle.className='expo-narration';
  const captions={},pages={};
  for(const key of ['voice','pa']){
    const p=root.ownerDocument.createElement('p');p.setAttribute('aria-live','polite');p.dataset.channel=key;
    subtitle.append(p);captions[key]=p;
  }
  root.append(subtitle);
  let held=ctx.paused, dead=false, check=0, ready=false, planterBook, story;
  sequence.pause(held);
  const load=fetch(assetURL('narration/manifest.json'),{signal:abort.signal}).then(async r=>{
    if(!r.ok)throw new Error('Expo narration manifest HTTP '+r.status);
    const rows=await r.json();for(const row of [...rows,...renewalLines,...trailLines])book.set(row.id,row);
    story=createArchiveStory(rows,ctx.store.get('lastArchiveRecord'),(id,slots)=>{
      ctx.store.set('archiveScreens',slots);
      ctx.store.set('lastArchiveRecord',id);
    },ctx.store.get('archiveScreens'));
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
      const station={id,zone,pos:centre(node),project,index:0,progress:0};
      station.archive=/^(?:slot_\d+|ev_)/.test(id);
      if(station.archive)station.panels=archiveDisplays(group,id,!!story?.assigned(id));
      list.push(station);
      const button=group.getObjectByName('expo_replay_'+id);
      if(station.archive){
        const plaque=group.getObjectByName('expo_caption_'+id);
        if(plaque)undo.push(register(plaque,()=>story?.at(id)?reader.label(id):'Start the projector to read the plaque',()=>reader.toggle(id,story?.at(id)?.archive),3.5));
        if(button)undo.push(register(button,()=>archivePrompt(station),()=>playArchive(station),3.5));
        const review=group.getObjectByName('expo_review_'+id);
        if(review)undo.push(register(review,()=>story?.assigned(id)?archivePrompt(station):'Play this screen first',
          ()=>playArchive(station,true),3.5));
      }else if(button){undo.push(register(button,()=>project&&!playedProjectors.has(id)?'Start projector':'Replay narration',
        ()=>cue(id,{station,replay:true,projector:!!project,kind:project?'beat':'react'}),3.5));}
    });
    rooms.set(zone,{list,horns,undo});
    // A streamed projector can rejoin a narration that continued outside this room.
    const entry=sequence.current;
    if(entry){
      const station=list.find(s=>s.id===entry.stationId);
      if(station){
        entry.station=station;station.index=entry.index||0;station.progress=entry.progress||0;
        if(entry.projector){station.project?.setActive(!!entry.lit,entry.row.slides[station.index]);station.project?.setPaused(held);}
      }
    }
  }
  function unmount(zone){
    const room=rooms.get(zone);if(!room)return;
    sequence.cancel(entry=>entry.zone===zone&&!entry.roaming);
    const current=sequence.current;if(current?.projector&&current.zone===zone)sequence.finish(current,false);
    for(const f of room.undo)f();
    for(const s of room.list){reader.release(s.id);s.panels?.dispose();s.project?.dispose();}
    for(const entry of [sequence.current,...sequence.queue])
      if(entry?.station?.zone===zone)entry.station=null;
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
    // The speaker keeps its position as the visitor walks away. The quiet mix still carries its final words.
    const floor=entry.row.set==='pa' ? .44 : .34;
    const gain=floor+(.94-floor)/(1+(Math.max(0,distance-1.15)/range)**1.65);
    right.set(1,0,0).applyQuaternion(camera.quaternion);
    const width=.4+.54/(1+Math.max(0,distance-4)/7);
    const pan=distance>.01?Math.max(-width,Math.min(width,delta.dot(right)/distance)):0;
    return {gain,pan};
  }
  function caption(key,text,id=''){
    const p=captions[key];
    if(p.dataset.lineId===id&&p.textContent===text)return;
    p.dataset.lineId=id;p.classList.toggle('expo-paged-lore',id==='rail_lore');p.replaceChildren();
    const chunks=pages[key]=captionPages(text);
    if(!text){delete p.dataset.phrase;return;}
    const full=root.ownerDocument.createElement('span');full.className='expo-full-line';full.textContent=text;p.append(full);
    p.dataset.phrase=chunks[0]?.value||text;
  }
  function nearby(entry){
    return (entry.roaming||entry.zone===walker.zone) && (!entry.needed || entry.needed()) &&
      (!entry.anchor||(entry.projector&&entry.lit&&!exemptRecord(entry.stationId))||camera.position.distanceToSquared(entry.anchor)<=36);
  }
  function began(entry){
    if(entry.began)return;
    entry.began=true;
    if(entry.projector&&!exemptRecord(entry.stationId))visitedScreens.add(entry.zone);
    heard.add(entry.row.id);
    if(entry.ticket)story.start(entry.ticket);
    else if(entry.projector&&!playedProjectors.has(entry.stationId)){
      playedProjectors.add(entry.stationId);ctx.store.set('playedProjectors',[...playedProjectors]);
    }
    entry.onStart?.();
  }
  function clearHandle(entry){
    if(!entry.handle)return;
    entry.handle.el?.removeEventListener('ended',entry.onEnd);
    entry.handle.el?.removeEventListener('error',entry.onError);
    entry.handle.el?.removeEventListener('playing',entry.onPlaying);
    entry.handle.el?.removeEventListener('loadedmetadata',entry.onMetadata);
    for(const key of ['waiting','stalled','pause'])entry.handle.el?.removeEventListener(key,entry.onWait);
    entry.handle.stop();entry.handle=null;
  }
  function playFile(key,entry,roomTone,resumeAt=0){
    clearHandle(entry);
    entry.kevin=roomTone;entry.lastMediaTime=resumeAt;entry.waiting=0;entry.awaitingSeek=resumeAt>0;
    const {gain,pan}=acoustic(entry);
    const file=roomTone?'audio/twelve.ogg':(entry.row.set==='planter'?'audio/':'narration/')+entry.row.audio;
    const showCaption=()=>caption(key,roomTone?'[Kevin is speaking]':entry.row.subtitle,entry.row.id);
    if(!entry.projector||entry.began)showCaption();
    function playing(){
      began(entry);showCaption();entry.lit=true;
      if(entry.projector){
        if(!exemptRecord(entry.stationId))viewing(entry);
        entry.station?.project?.setActive(true,entry.row.slides[entry.index||0]);
        if(entry.row.slides.length)click(entry);
      }
    }
    if(!entry.row.audio){playing();return;}
    const volume=gain*(roomTone?.32:1);
    const h=entry.handle=(!roomTone&&entry.tail)||ctx.audio.stream(assetURL(file),{bus:'voice',source:'expo-guide',sourceLabel:'Expo narrator',spatial:true,pan,loop:roomTone,
      gain:volume,autoplay:false,preload:'auto'});
    if(h && h===entry.tail){entry.tail=null;h.setGain(volume);h.setPan?.(pan);}
    entry.onEnd=()=>{if(!roomTone && entry.handle===h && h.el.ended)sequence.finish(entry);};
    entry.onError=()=>{
      if(dead || entry.handle!==h || sequence.current!==entry)return;
      releaseView(entry);
      if(!entry.retryUsed && h?.el && entry.row.set!=='rail'){
        entry.retryUsed=true;
        playFile(key,entry,roomTone,Math.max(entry.lastMediaTime,h.el.currentTime||0));
        return;
      }
      console.warn('Expo recording unavailable',file);
      unavailable.add(entry.row.id);clearHandle(entry);entry.timed=true;entry.kevin=false;
      began(entry);showCaption();
    };
    entry.onPlaying=()=>{
      if(held||entry!==sequence.current)return;
      if(entry.lit){if(entry.projector&&!exemptRecord(entry.stationId))viewing(entry);}else playing();
    };
    entry.onWait=()=>releaseView(entry);
    if(!h?.el){queueMicrotask(entry.onError);return;}
    for(const event of ['waiting','stalled','pause'])h.el.addEventListener(event,entry.onWait);
    entry.onMetadata=()=>{
      if(entry.handle!==h)return;
      if(resumeAt>0)h.el.currentTime=resumeAt;
      entry.awaitingSeek=false;
      if(!held)h.play();
    };
    h.el.addEventListener('ended',entry.onEnd);
    h.el.addEventListener('error',entry.onError);
    h.el.addEventListener('playing',entry.onPlaying);
    if(resumeAt>0 && h.el.readyState<1)h.el.addEventListener('loadedmetadata',entry.onMetadata,{once:true});
    else entry.onMetadata();
  }
  function click(entry){
    const {gain,pan}=acoustic(entry);sound.play('press',.39*gain,pan);
  }
  function start(entry){
    entry.started=true;
    const key=entry.channel;
    unavailable.delete(entry.row.id);
    entry.station ||= locate(entry.stationId);
    if(entry.station){
      entry.station.index=0;entry.station.progress=0;
    }
    const kevin=entry.row.id==='log_kevin';
    if(kevin)entry.tail=ctx.audio.stream(assetURL('narration/'+entry.row.audio),
      {bus:'voice',source:'expo-guide',sourceLabel:'Expo narrator',spatial:true,autoplay:false,preload:'metadata',gain:0});
    playFile(key,entry,kevin);
  }
  function suspend(entry){
    releaseView(entry);entry.handle?.pause();if(entry.projector)entry.station?.project?.setPaused(true);
    caption(entry.channel,'');
  }
  function resume(entry){
    if(!entry.projector||entry.began)caption(entry.channel,entry.kevin?'[Kevin is speaking]':entry.row.subtitle,entry.row.id);
    if(entry.projector){entry.station?.project?.setPaused(false);entry.station?.project?.setActive(!!entry.lit,entry.row.slides[entry.index||0]);}
    if(entry.projector&&!entry.row.audio&&!exemptRecord(entry.stationId))viewing(entry);
    if(!entry.awaitingSeek)entry.handle?.play();
  }
  function release(entry,ok){
    releaseView(entry);
    if(entry.ticket)story.finish(entry.ticket,ok);
    if(ok)finished.set(entry.row.id,(finished.get(entry.row.id)||0)+1);
    if(entry.started){
      clearHandle(entry);entry.tail?.stop();
      if(entry.projector){entry.station?.project?.setPaused(false);entry.station?.project?.setActive(false);}
      if(captions[entry.channel].dataset.lineId===entry.row.id)caption(entry.channel,'');
    }
    entry.resolve(ok);
  }
  let farewell=false;
  function enqueue(id,{closing=false,station=null,replay=false,zone=walker.zone,position=null,kind='react',exhibit,ttl,ticket=null,projector=false,onStart,roomWide=false,needed,roaming=false}={}){
    const row=book.get(id);
    if(dead||!row||zone!==walker.zone||(farewell&&!closing))return Promise.resolve(false);
    if(row.set==='log'&&!owned().includes(id.slice(4)))return Promise.resolve(false);
    const key=row.set==='pa'?'pa':'voice';
    const queued=[sequence.current,...sequence.queue].find(e=>e?.row.id===id);
    if(queued)return queued.promise;
    if(!replay&&heard.has(id))return Promise.resolve(true);
    station ||= locate(id);
    const horn=nearest(rooms.get(zone)?.horns||[],camera.position);
    const origin=position?(position.isVector3?position:new Vector3(...position)):
      (station&&!station.notice?station.pos:null)||horn?.pos||camera.position;
    let resolve;const promise=new Promise(r=>{resolve=r;});
    const local=position || station || exhibit || kind!=='ambient';
    const anchor=local&&!roomWide?(position || station ? origin : camera.position).clone():null;
    const entry={row,zone,station,channel:key,kind,exhibit:exhibit||station?.id||zone,anchor,ttl,ticket,projector,onStart,needed,roaming,
      stationId:station?.id||family(id),pos:origin.clone(),handle:null,
      clock:0,lastMediaTime:0,kevin:false,progress:0,duck:1,resolve,promise};
    sequence.submit(entry);return promise;
  }
  function cue(id,options){
    if(!id||dead)return Promise.resolve(false);
    options={zone:walker.zone,...options};
    if(id.startsWith('ev_')) {
      const begin=()=>{const station=options?.station||locate(id);return station?.archive&&playArchive(station);};
      return ready?Promise.resolve(begin()):load.then(begin);
    }
    return ready||book.has(id)?enqueue(canonical(id),options):load.then(()=>enqueue(canonical(id),options));
  }
  function closingLine(id){
    // Preserve the current recording's tail and reserve the next slot for the departure.
    farewell=true;sequence.cancel(()=>true);
    return cue(id,{closing:true,kind:'beat',replay:true});
  }
  function archivePrompt(station){
    if(!story)return 'Opening archive';
    if(story.current){
      if(story.current.slot!==station.id)return 'Recording in progress';
      return story.current.started?'Playing '+story.current.row.archive.title:'Waiting for narration';
    }
    const own=story.at(station.id);
    if(own)return 'Replay '+own.archive.title;
    if(story.assigned(station.id))return 'Replay archive record';
    if(unavailable.has(story.next?.id))return 'Retry projector';
    return story.next?'Start projector':'Archive complete';
  }
  function playArchive(station,review=false,automatic=false){
    if(dead||held||farewell||!station.project||station.zone!==walker.zone||!rooms.get(station.zone)?.list.includes(station))return;
    if(!ready)return load.then(()=>ready&&playArchive(station,review));
    const ticket=story.request(station.id,review);
    if(!ticket)return;
    if(!automatic&&!heard.has('pa_timeline'))cue('pa_timeline',{zone:station.zone});
    return enqueue(ticket.row.id,{station,zone:station.zone,replay:true,ticket,projector:true,kind:'beat'});
  }
  async function planter(id,options={}){
    if(dead||!planterLines.includes(id))return;
    if(!planterBook){
      planterBook=fetch(assetURL('audio/planter-voices.json'),{signal:abort.signal}).then(r=>{
        if(!r.ok)throw Error('Planter narration HTTP '+r.status);
        return r.json();
      }).catch(e=>{planterBook=null;if(!dead)console.warn(e);return {};});
    }
    const zone=walker.zone,position=camera.position.clone();
    const config=(await planterBook)[id];if(dead||!config)return;
    if(!book.has(id))book.set(id,{...config,id,set:'planter',slides:[]});
    return enqueue(id,{zone,position,...options,kind:'beat',exhibit:'planter'});
  }
  function clearRay(target){
    block.origin.copy(camera.position);block.direction.subVectors(target,camera.position);
    const distance=block.direction.length();if(distance<.09)return true;
    block.direction.multiplyScalar(1/distance);
    return !walker.blocked(block,distance);
  }
  function proximity(){
    const zone=walker.zone,local=rooms.get(zone);
    if(local&&!sequence.queue.length&&!sequence.current?.projector&&archiveGalleries.has(zone)&&!visitedScreens.has(zone)){
      const station=nearest(local.list.filter(s=>s.project&&!exemptRecord(s.id)&&!unavailable.has(s.id)&&s.pos.distanceToSquared(camera.position)<9&&clearRay(s.pos)),camera.position);
      if(station){if(station.archive)playArchive(station,false,true);else cue(station.id,{station,replay:true,projector:true,kind:'beat'});return;}
    }
    const immediate=[];
    for(const room of rooms.values())for(const s of room.list){
      if(s.archive || s.project || heard.has(s.id) || !book.has(s.id))continue;
      const row=book.get(s.id);
      if(unavailable.has(row.id))continue;
      const eligible=row.set==='event'||row.set==='log'||['ex_press','hab_1','pa_timeline','pa_photos','pa_hall4_missing'].includes(s.id);
      if(!eligible)continue;
      const distance=camera.position.distanceTo(s.pos);
      if(distance>3.5)continue;
      if(clearRay(s.pos))immediate.push({s,distance});
    }
    for(const {s} of immediate.sort((a,b)=>a.distance-b.distance))cue(s.id,{station:s,zone:s.zone,kind:'ambient'});
  }
  const visits=new Map();
  let previousZone=null;
  function tick(dt){
    if(dead||held||(!ready&&!sequence.current))return;
    if(previousZone!==walker.zone){previousZone=walker.zone;visits.set(previousZone,(visits.get(previousZone)||0)+1);}
    while(wetHorns.length&&wetHorns[0].until<=ctx.activeMs)wetHorns.shift();
    const speaking=sequence.current;
    setMuffled(!!speaking&&wetHorns.some(h=>h.zone===speaking.zone&&h.pos.distanceToSquared(speaking.pos)<1));
    sequence.cancel(entry=>!nearby(entry));
    if((speaking?.needed||speaking?.projector)&&!nearby(speaking))sequence.finish(speaking,false);
    if(ready&&!reader.update()){check-=dt;if(check<=0){proximity();check=.12;}}
    const e=sequence.current;
    if(e){
      const key=e.channel;
      const el=e.handle?.el,at=el?.currentTime||0;
      if(el && !e.awaitingSeek){
        let advance=at-e.lastMediaTime;
        if(e.kevin&&advance<0&&Number.isFinite(el.duration))advance+=el.duration;
        e.clock+=Math.max(0,advance);e.lastMediaTime=at;
        e.waiting=advance>.0001?0:e.waiting+dt;
        if(e.waiting>4&&!el.ended){e.onError();return;}
      }else if(!e.row.audio||e.timed){e.clock+=dt;}
      else if(e.awaitingSeek&&(e.waiting+=dt)>4){e.onError();return;}
      if(e.kevin&&e.clock>=15){e.roomToneSeconds=e.clock;e.clock=15;playFile(key,e,false);}
      else if(!el&&(!e.row.audio||e.timed)&&e.clock>=e.row.seconds){
        sequence.finish(e);
      }
      const actual=(e.kevin?e.tail?.el:e.handle?.el)?.duration;
      const duration=e.duration=(Number.isFinite(actual)&&actual>0?actual:e.row.seconds)+(e.row.id==='log_kevin'?15:0);
      const progress=e.progress=Math.min(1,e.clock/Math.max(.1,duration));
      const p=captions[key],chunks=pages[key];
      if(chunks?.length>1&&p.dataset.lineId===e.row.id&&(e.row.id==='rail_lore'||root.hasAttribute('data-expo-touch'))){
        const spokenProgress=e.row.id==='log_kevin'?Math.max(0,(e.clock-15)/Math.max(.1,duration-15)):progress;
        const page=captionAt(chunks,spokenProgress);
        if(page!==p.dataset.phrase)p.dataset.phrase=page;
      }
      const i=slideIndex(e.row,progress);
      const changed=i>=0&&i!==(e.index||0);
      if(changed){
        e.index=i;
        if(e.projector&&e.station?.project)click(e);
      }
      if(e.station){
        e.station.index=Math.max(0,i);e.station.progress=progress;
      }
      const {gain,pan}=acoustic(e);
      e.gain=gain*(e.kevin?.32:1);e.pan=pan;
      e.handle?.setGain(e.gain);e.handle?.setPan?.(pan);
    }
    for(const room of rooms.values())for(const s of room.list)if(s.project){
      if(!ready)continue;
      const row=s.archive?story.at(s.id):playedProjectors.has(s.id)?book.get(s.id):undefined;
      s.panels?.show(row,story.total,!!story.assigned(s.id),(visits.get(s.zone)||0)>1);
      s.project.step(dt,ctx.gentle||ctx.reduced,camera.position.distanceTo(s.pos),row?.slides[s.index],s.progress,
                     restingSlide(row),row?.slides[s.index+1]);
    }
  }
  function pause(on){
    held=on;
    sequence.pause(on);
  }
  function dispose(){
    dead=true;abort.abort();setMuffled(false);
    sequence.dispose();
    for(const id of [...rooms.keys()])unmount(id);
    reader.dispose();subtitle.remove();atlas.dispose();
  }
  function debug(){
    const data={heard:[...heard],finished:Object.fromEntries(finished),unavailable:[...unavailable],archive:story&&{
      completed:story.completed,total:story.total,next:story.next?.id,displayed:story.displayed?.id,playingAt:story.current?.slot,lastSlot:story.lastSlot},
      stations:[...rooms.values()].flatMap(r=>r.list.map(s=>({id:s.id,zone:s.zone,pos:s.pos.toArray(),record:s.archive?story?.at(s.id)?.id:playedProjectors.has(s.id)?s.id:undefined,project:s.project?.stats})))};
    for(const key of ['voice','pa']){const e=sequence.current?.channel===key?sequence.current:null;
      data[key]={id:e?.row.id,seconds:e?.clock??0,duration:e?.duration,roomToneSeconds:e?.roomToneSeconds,gain:e?.gain,pan:e?.pan,duck:e?.duck,subtitle:captions[key].textContent,queue:sequence.queue.filter(q=>q.channel===key).map(q=>q.row.id)};}
    data.speech={playing:sequence.current?.row.id,queue:sequence.queue.map(q=>q.row.id),paused:held};
    return data;
  }
  function meter(output){
    output.projectors=0;output.voices=0;
    if(sequence.current?.lit&&!held){
      if(sequence.current.handle?.playing)output.voices++;
      if(sequence.current.projector&&sequence.current.station?.project)output.projectors++;
    }
  }
  function registerPlaque(mesh,description){
    const key=mesh.uuid;
    const off=register(mesh,()=>reader.label(key),()=>reader.toggle(key,description),3.5);
    return ()=>{reader.release(key);off();};
  }
  return {mount,unmount,cue,closingLine,muffle,planter,registerPlaque,get story(){return story;},
    get speaking(){const entry=sequence.current;return !held && !!entry?.lit && !entry.kevin && !!entry.handle?.playing;},
    available:()=>ready&&sequence.available,tick,pause,dispose,debug,meter};
}
