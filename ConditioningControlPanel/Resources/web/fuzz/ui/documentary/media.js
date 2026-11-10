import { needsDomMedia } from '../unlocks/arcade/slot-reel-media.js';
import { mediaReady, releasePlayer } from '../media-cache.js';

/** Display-only DOM media. The shared pictures library owns all bytes and source policy. */
export function createMedia(ctx, { lite = false, setTimer = setTimeout, clearTimer = clearTimeout } = {}) {
  const pictures=ctx.pictures,slots=new Set(),motionSlots=new Set(),promotions=new Set();
  const maxMotion=lite?1:2;
  let paused=false,disposed=false,promotionQueued=false;
  const motion=row=>row?.kind==='clip'||row?.kind==='gif';
  const moving=slot=>!paused&&!ctx.paused&&!ctx.gentle&&!ctx.reduced&&!slot.layer.ownerDocument.hidden;
  const live=(slot,job)=>!disposed&&slots.has(slot)&&slot.pending===job&&pictures.source===job.source;

  function retire(display) {
    if(!display) return;
    const {player}=display;
    player.onload=player.onloadeddata=player.onerror=null;
    if(display.borrowed) releasePlayer(player);
    else {
      if(player.tagName==='VIDEO') player.pause();
      player.removeAttribute('src');
      if(player.tagName==='VIDEO') player.load();
      player.remove();display.lease?.release?.();
    }
  }
  function cancelPending(slot) {
    const job=slot.pending;
    if(!job) return;
    slot.pending=null;job.cancelReady?.();retire(job.display);job.display=null;
    if(!motion(slot.current?.row)) motionSlots.delete(slot);
  }
  function clearCurrent(slot) {
    retire(slot.current);slot.current=null;slot.layer.replaceChildren();slot.layer.hidden=true;
    if(!slot.pending?.reserved) motionSlots.delete(slot);
  }
  function clear(slot) { cancelPending(slot);clearCurrent(slot);promotions.delete(slot); }

  function sync(slot) {
    const display=slot.current;
    if(!display) return;
    const {player,row}=display,on=moving(slot);
    if(row.kind==='clip') {
      if(on&&display.ready) player.play().then(()=>{if(!moving(slot)||slot.current!==display) player.pause();},()=>{});
      else player.pause();
    } else if(row.kind==='gif') {
      if(!on&&!display.held) {
        display.held=true;display.ready=false;
        player.style.visibility='hidden';slot.layer.hidden=true;player.removeAttribute('src');
      } else if(on&&display.held) {
        display.held=false;player.src=display.src;
        if(mediaReady(player)) display.reveal();
      }
    }
  }
  function queuePromotions(all=false) {
    if(disposed) return;
    if(all) for(const slot of slots) if(!motion(slot.current?.row)&&!slot.motionFailed) promotions.add(slot);
    if(promotionQueued) return;
    promotionQueued=true;
    queueMicrotask(()=>{
      promotionQueued=false;
      if(disposed) return;
      for(const slot of [...promotions]) {
        if(!slots.has(slot)||motion(slot.current?.row)||slot.motionFailed) { promotions.delete(slot);continue; }
        if(slot.pending||!moving(slot)) continue;
        if(motionSlots.size>=maxMotion) break;
        promotions.delete(slot);void fill(slot,true);
      }
    });
  }
  function waitReady(slot,job,display,src) {
    const {player}=display,event=display.row.kind==='clip'?'loadeddata':'load';
    return new Promise(resolve=>{
      let settled=false,timer;
      const finish=ok=>{
        if(settled) return;settled=true;clearTimer(timer);
        player.removeEventListener(event,loaded);player.removeEventListener('error',failed);
        job.cancelReady=null;resolve(ok&&live(slot,job));
      };
      const loaded=()=>finish(mediaReady(player)),failed=()=>finish(false);
      job.cancelReady=()=>finish(false);
      player.addEventListener(event,loaded);player.addEventListener('error',failed);
      timer=setTimer(failed,9000);
      if(src!=null) player.src=src;
      if(mediaReady(player)) loaded();
      else if(player.tagName==='IMG'&&player.decode) player.decode().then(loaded,()=>{});
    });
  }
  function localPlayer(row,doc) {
    try { const url=new URL(row.url,doc.baseURI);return ['blob:','data:'].includes(url.protocol)||url.origin===new URL(doc.baseURI).origin; }
    catch { return false; }
  }
  async function fill(slot,promote=false) {
    if(disposed||!slots.has(slot)||!pictures||pictures.source==='none'||slot.pending||motion(slot.current?.row)||(!promote&&slot.current)) return;
    const reserved=moving(slot)&&!slot.motionFailed&&motionSlots.size<maxMotion;
    if(promote&&!reserved) return;
    const job={source:pictures.source,reserved,display:null,cancelReady:null};slot.pending=job;
    // Reserve before awaiting the library so simultaneous screens share one motion cap.
    if(reserved) motionSlots.add(slot);
    else if(moving(slot)&&!slot.motionFailed) promotions.add(slot);
    try {
      let row=reserved?await pictures.next?.({kind:'mixed',allowScrolller:true,previousUrl:slot.current?.row.url}):null;
      if(!live(slot,job)) return;
      if(!row&&!promote) row=pictures.dealStill?.({group:'documentary',allowScrolller:true})||await pictures.next?.({still:true,allowScrolller:true});
      if(!live(slot,job)||!row?.url||(motion(row)&&!reserved)) return;
      if(promote&&(!motion(row)||row.url===slot.current?.row.url)) return;
      if(!motion(row)) { job.reserved=false;motionSlots.delete(slot); }
      const doc=slot.layer.ownerDocument;
      // Remote players must receive referrer policy before their first source assignment.
      const borrowed=typeof pictures.takePlayer==='function'&&localPlayer(row,doc);
      let lease=null;
      if(!borrowed) {
        lease=await pictures.acquire?.(row);
        if(!live(slot,job)) { lease?.release?.();return; }
      }
      const player=borrowed?pictures.takePlayer(row,doc):doc.createElement(row.kind==='clip'?'video':'img');
      const display={player,row,borrowed,lease,ready:false,held:false,reported:false,src:borrowed?player.getAttribute('src'):lease?.url||row.url};
      job.display=display;
      player.className='td-run-picture';player.dataset.kind=row.kind;player.style.visibility='hidden';
      if(job.source==='scrolller'||needsDomMedia(row.url)) {
        player.referrerPolicy='strict-origin';player.setAttribute('referrerpolicy','strict-origin');
      }
      if(row.kind==='clip') {
        player.muted=player.defaultMuted=player.loop=player.playsInline=true;
        player.setAttribute('playsinline','');player.setAttribute('muted','');player.preload='auto';player.disablePictureInPicture=true;
      } else { player.alt='';player.draggable=false;player.decoding='async'; }
      slot.layer.append(player);
      if(!await waitReady(slot,job,display,borrowed?null:display.src)) {
        if(live(slot,job)&&motion(row)) slot.motionFailed=true;
        return;
      }
      if(!live(slot,job)) return;
      retire(slot.current);
      for(const child of [...slot.layer.children]) if(child!==player) child.remove();
      slot.current=display;job.display=null;slot.pending=null;
      const credit=row.credit||(job.source==='scrolller'?'via Scrolller':'');
      if(credit) { const label=doc.createElement('span');label.className='td-run-credit';label.textContent=credit;slot.layer.append(label); }
      display.reveal=()=>{
        if(disposed||slot.current!==display||display.held) return;
        display.ready=true;player.style.visibility='';slot.layer.hidden=false;sync(slot);
        if(!display.reported) { display.reported=true;pictures.noteView?.(); }
      };
      player.onload=player.onloadeddata=display.reveal;
      player.onerror=()=>{
        if(slot.current!==display||display.held) return;
        slot.motionFailed=motion(row);clearCurrent(slot);void fill(slot);queuePromotions(true);
      };
      display.reveal();
    } catch {
      if(live(slot,job)&&job.reserved) slot.motionFailed=true;
    } finally {
      const retryStill=slot.pending===job&&job.reserved&&slot.motionFailed&&!slot.current;
      if(slot.pending===job) {
        job.cancelReady?.();retire(job.display);job.display=null;slot.pending=null;
        if(!motion(slot.current?.row)) motionSlots.delete(slot);
      }
      if(retryStill) void fill(slot);
      queuePromotions();
    }
  }
  const offSource=ctx.on?.('pictures',()=>{
    promotions.clear();
    for(const slot of slots) { clear(slot);slot.motionFailed=false; }
    for(const slot of slots) void fill(slot);
  });
  const offAdded=ctx.on?.('pictures-added',()=>{
    for(const slot of slots) { slot.motionFailed=false;if(!slot.current&&!slot.pending) void fill(slot); }
    queuePromotions(true);
  });
  function unmount(slot) {
    if(!slots.delete(slot)) return;
    const freed=motionSlots.has(slot);clear(slot);slot.layer.remove();
    if(freed) queuePromotions(true);
  }
  return {
    mount(container,exhibit) {
      if(disposed) return ()=>{};
      const layer=container.ownerDocument.createElement('div');layer.className='td-run-media';layer.hidden=true;container.append(layer);
      const slot={layer,exhibit,current:null,pending:null,motionFailed:false};slots.add(slot);void fill(slot);
      return ()=>unmount(slot);
    },
    pause(on) {
      paused=Boolean(on);
      for(const slot of slots) {
        if(!moving(slot)&&slot.pending?.reserved) cancelPending(slot);
        sync(slot);
      }
      if(!paused) {
        for(const slot of slots) if(!slot.current&&!slot.pending) void fill(slot);
        queuePromotions(true);
      }
    },
    debug:()=>({slots:slots.size,motion:motionSlots.size,pending:[...slots].filter(slot=>slot.pending).length,
      clips:[...slots].filter(slot=>slot.current?.row.kind==='clip').length,gifs:[...slots].filter(slot=>slot.current?.row.kind==='gif').length}),
    dispose() { if(disposed) return;disposed=true;offSource?.();offAdded?.();promotions.clear();for(const slot of [...slots]) unmount(slot); },
  };
}
