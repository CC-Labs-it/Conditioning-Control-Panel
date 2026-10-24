/** An opaque cover owns boot until a complete menu frame is ready. GPU warm-up stays behind it. */
export function createLoading(doc=globalThis.document) {
  const root=doc.getElementById('race-loading');let progress=0,finished=false;
  return {
    set(stage,value){if(!root||finished)return;progress=Math.max(progress,Math.min(1,Number(value)||0));
      root.querySelector('.boot-stage').textContent=stage;
      const percent=Math.round(progress*100);root.querySelector('[role=progressbar]').setAttribute('aria-valuenow',String(percent));
      root.querySelector('.boot-fill').style.transform=`scaleX(${progress})`;root.querySelector('.boot-percent').textContent=percent+'%';
      root.style.setProperty('--boot-left',`calc(${progress*100}% - ${progress*51}px)`);
    },
    async finish(){if(!root||finished)return;finished=true;
      await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
      doc.body.dataset.boot='ready';root.classList.add('boot-leaving');
      const remove=()=>root.remove();root.addEventListener('transitionend',remove,{once:true});setTimeout(remove,700);
    },
    fail(){if(!root)return;root.querySelector('.boot-stage').textContent='Could not open the garage. Reload to try again.';root.classList.add('boot-failed');},
  };
}
