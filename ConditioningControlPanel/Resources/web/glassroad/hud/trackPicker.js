import {courseSketch} from './courseSketch.js';

const COLORS={threshold:'#ff88b5',mainframe:'#70e6c3',keep:'#ffce62',boudoir:'#e49cfa'};
/** Native dialog supplies focus trapping and pointer isolation from the live 3D carousel. */
export function createTrackPicker({root,courses,trackId,change}) {
  const dialog=document.createElement('dialog');dialog.className='g-track-picker';dialog.setAttribute('aria-labelledby','g-track-title');
  dialog.innerHTML='<header><div><small>YOUR NEXT RACE</small><h2 id="g-track-title">Choose track</h2></div><button type="button" class="g-track-back">Back</button></header><div class="g-track-grid"></div><p class="g-track-status" role="status"></p>';
  root.append(dialog);let opener=null,loaded=false,leaving=false,timer=0,disposed=false;
  const close=()=>{if(leaving)return;dialog.close();opener?.focus({preventScroll:true});};
  dialog.querySelector('.g-track-back').onclick=close;
  dialog.addEventListener('click',e=>{if(e.target===dialog){const r=dialog.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)close();}});
  const tiles=courses.map(course=>{
    const b=document.createElement('button');b.type='button';b.className='g-track-tile';b.dataset.course=course.id;b.style.setProperty('--course-color',COLORS[course.id]||COLORS.threshold);
    b.setAttribute('aria-pressed',String(course.id===trackId));
    b.innerHTML='<span class="g-course-mode"></span><svg viewBox="0 0 180 88" aria-hidden="true"><path class="g-route-shadow"/><path class="g-route"/><circle r="3"/></svg><strong></strong><small class="g-course-details"></small><span class="g-course-selected" aria-hidden="true">Selected</span>';
    b.querySelector('strong').textContent=course.name;b.querySelector('.g-course-mode').textContent=course.online?'Solo / online':'Solo';
    b.onclick=()=>{if(leaving)return;if(course.id===trackId){close();return;}leaving=true;
      for(const tile of tiles)tile.disabled=true;
      dialog.querySelector('.g-track-status').textContent='Opening '+course.name;
      // The opaque loading cover takes over after navigation. The selected look is already saved on this origin.
      root.classList.add('g-course-leaving');timer=setTimeout(()=>change(course.id),350);
    };
    dialog.querySelector('.g-track-grid').append(b);return b;
  });
  async function loadSketches(){if(loaded)return;loaded=true;
    await Promise.allSettled(courses.map(async(course,i)=>{
      const response=await fetch(new URL('../tracks/'+course.id+'.json',import.meta.url));if(!response.ok)return;
      const sketch=courseSketch(await response.json());if(!sketch||disposed)return;const b=tiles[i];
      for(const path of b.querySelectorAll('path'))path.setAttribute('d',sketch.path);
      const start=b.querySelector('circle');start.setAttribute('cx',sketch.start[0]);start.setAttribute('cy',sketch.start[1]);
      b.querySelector('.g-course-details').textContent=sketch.km+' km · '+sketch.laps+' laps';
    }));
  }
  return {
    get open(){return dialog.open;},
    show(trigger=document.activeElement){if(dialog.open)return;opener=trigger;dialog.showModal();tiles.find(b=>b.dataset.course===trackId)?.focus();loadSketches();},
    nav(dir){if(!dialog.open)return;const buttons=[...tiles,dialog.querySelector('.g-track-back')],i=buttons.indexOf(document.activeElement);
      if(dir==='back'){close();return;}if(dir==='ok'){document.activeElement.click?.();return;}
      const step={up:-2,down:2,left:-1,right:1}[dir];if(step)buttons[(i+step+buttons.length)%buttons.length].focus();
    },
    dispose(){disposed=true;clearTimeout(timer);dialog.remove();},
  };
}
