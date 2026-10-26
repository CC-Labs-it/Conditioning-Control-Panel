/** An opaque cover owns boot until a complete menu frame is ready. GPU warm-up stays behind it.
 *  A failure never dead-ends: fail(reason) names the real reason in small text and offers Retry (onRetry). */
export function createLoading(doc=globalThis.document) {
  const root=doc.getElementById('race-loading');let progress=0,finished=false,revision=0,timer=0,retry=null;
  const q=s=>root?root.querySelector(s):null;
  const retryBtn=q('.boot-retry');
  if(retryBtn)retryBtn.addEventListener('click',()=>{if(retry)retry();});
  return {
    start(){revision++;clearTimeout(timer);progress=0;finished=false;
      if(!root)return;root.hidden=false;root.classList.remove('boot-leaving','boot-failed');doc.body.dataset.boot='loading';
      const reason=q('.boot-reason');if(reason){reason.textContent='';reason.hidden=true;}if(retryBtn)retryBtn.hidden=true;
    },
    calm(on){root?.classList.toggle('boot-calm',!!on);},
    set(stage,value){if(!root||finished)return;progress=Math.max(progress,Math.min(1,Number(value)||0));
      root.querySelector('.boot-stage').textContent=stage;
      const percent=Math.round(progress*100);root.querySelector('[role=progressbar]').setAttribute('aria-valuenow',String(percent));
      root.querySelector('.boot-fill').style.transform=`scaleX(${progress})`;root.querySelector('.boot-percent').textContent=percent+'%';
      root.style.setProperty('--boot-left',`calc(${progress*100}% - ${progress*51}px)`);
    },
    async finish(){if(!root||finished)return;finished=true;const mine=revision;
      await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
      if(mine!==revision)return;
      doc.body.dataset.boot='ready';root.classList.add('boot-leaving');
      timer=setTimeout(()=>{if(mine===revision)root.hidden=true;},700);
    },
    dispose(){revision++;clearTimeout(timer);},
    /** The cover stays, names the reason and offers Retry. reason: a short line (core/failure.js failureLine). */
    fail(reason=''){if(!root)return;revision++;clearTimeout(timer);finished=false;root.hidden=false;root.classList.remove('boot-leaving');doc.body.dataset.boot='loading';
      root.querySelector('.boot-stage').textContent='Could not open the garage.';root.classList.add('boot-failed');
      const line=q('.boot-reason');if(line){line.textContent=String(reason||'');line.hidden=!reason;}
      if(retryBtn){retryBtn.hidden=false;try{retryBtn.focus({preventScroll:true});}catch(e){}}
    },
    /** fn runs on the Retry button (one handler; the last wins). */
    onRetry(fn){retry=typeof fn==='function'?fn:null;},
  };
}
