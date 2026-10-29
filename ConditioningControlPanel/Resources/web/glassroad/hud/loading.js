/** An opaque cover owns boot until a complete menu frame is ready. GPU warm-up stays behind it.
 *  A failure never dead-ends: fail(reason) names the real reason in small text and offers Retry (onRetry).
 *  The watchdog: a visible cover whose bar has not moved (value or stage) for LOAD_STALL_MS calls onStall once,
 *  so a killed GPU process or a compile that never answers ends on fail + Retry, never on a frozen bar (iPhone,
 *  2026-10-28). The clock pauses while the tab is hidden, and one tick that came late (a long synchronous compile
 *  held the main thread: busy, not dead) counts at most two ticks. */
export const LOAD_STALL_MS = 20000;
export const LOAD_TICK_MS = 1000;
export function createLoading(doc=globalThis.document,{stallMs=LOAD_STALL_MS,tickMs=LOAD_TICK_MS,now=()=>performance.now()}={}) {
  const root=doc.getElementById('race-loading');let progress=0,finished=false,failed=false,revision=0,timer=0,retry=null;
  let stage='',watch=0,idle=0,lastTick=0,stall=null;
  const q=s=>root?root.querySelector(s):null;
  const retryBtn=q('.boot-retry');
  if(retryBtn)retryBtn.addEventListener('click',()=>{if(retry)retry();});
  const disarm=()=>{if(watch)clearInterval(watch);watch=0;};
  const tick=()=>{const t=now(),dt=Math.min(Math.max(0,t-lastTick),2*tickMs);lastTick=t;
    if(finished||failed||doc.hidden||root?.hidden)return;
    idle+=dt;if(idle<stallMs)return;
    disarm();try{stall?.({stage,progress,idleMs:idle});}catch(e){console.error('[glassroad] loading stall handler',e);}
  };
  const arm=()=>{if(watch||!root||finished||failed)return;idle=0;lastTick=now();watch=setInterval(tick,tickMs);watch?.unref?.();};
  return {
    start(){revision++;clearTimeout(timer);progress=0;finished=false;failed=false;stage='';disarm();
      if(!root)return;root.hidden=false;root.classList.remove('boot-leaving','boot-failed');doc.body.dataset.boot='loading';
      const reason=q('.boot-reason');if(reason){reason.textContent='';reason.hidden=true;}if(retryBtn)retryBtn.hidden=true;
      arm();
    },
    calm(on){root?.classList.toggle('boot-calm',!!on);},
    set(stageText,value){if(!root||finished||failed)return;const next=Math.max(progress,Math.min(1,Number(value)||0));
      if(next>progress||stageText!==stage)idle=0;   // any movement (bar or stage) restarts the stall clock
      progress=next;stage=stageText;arm();
      root.querySelector('.boot-stage').textContent=stageText;
      const percent=Math.round(progress*100);root.querySelector('[role=progressbar]').setAttribute('aria-valuenow',String(percent));
      root.querySelector('.boot-fill').style.transform=`scaleX(${progress})`;root.querySelector('.boot-percent').textContent=percent+'%';
      root.style.setProperty('--boot-left',`calc(${progress*100}% - ${progress*51}px)`);
    },
    async finish(){if(!root||finished)return;finished=true;disarm();const mine=revision;
      await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
      if(mine!==revision)return;
      doc.body.dataset.boot='ready';root.classList.add('boot-leaving');
      timer=setTimeout(()=>{if(mine===revision)root.hidden=true;},700);
    },
    dispose(){revision++;clearTimeout(timer);disarm();},
    /** The cover stays, names the reason and offers Retry. reason: a short line (core/failure.js failureLine).
     *  Late progress from the failed load no longer paints over it; the next start() clears it. */
    fail(reason=''){if(!root)return;revision++;clearTimeout(timer);disarm();finished=false;failed=true;root.hidden=false;root.classList.remove('boot-leaving');doc.body.dataset.boot='loading';
      root.querySelector('.boot-stage').textContent='Could not open the garage.';root.classList.add('boot-failed');
      const line=q('.boot-reason');if(line){line.textContent=String(reason||'');line.hidden=!reason;}
      if(retryBtn){retryBtn.hidden=false;try{retryBtn.focus({preventScroll:true});}catch(e){}}
    },
    /** fn runs on the Retry button (one handler; the last wins). */
    onRetry(fn){retry=typeof fn==='function'?fn:null;},
    /** fn({ stage, progress, idleMs }) runs once per stalled load (one handler; the last wins). */
    onStall(fn){stall=typeof fn==='function'?fn:null;},
  };
}
