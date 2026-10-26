import {courseSketch} from './courseSketch.js';
import {MODE_LABEL,resolveMode,saveMode} from '../meta/mode.js';

const COLORS={threshold:'#ff88b5',mainframe:'#70e6c3',keep:'#ffce62',boudoir:'#e49cfa',goonworks:'#bd006e'};
// Race / Time trial: two small pills above the grid (Logbook TRIAL lane); the pick is saved and sent as glassroad-mode
const MODE_CSS='.g-track-mode{display:flex;gap:8px;margin:0 0 14px}#race-garage .g-track-mode button{min-height:34px;padding:6px 14px;border:2px solid #0b1420;border-radius:999px;background:#102d39;color:#b1ded8;font-size:13px}#race-garage .g-track-mode button[aria-pressed=true]{background:#8ee8d1;color:#10101b;border-color:#10101b}';
/** Shows the mode on the garage Race row (the row keeps its own track line). */
function paintMode(root,mode){const b=root.querySelector('[data-g=race] b');if(b)b.textContent=MODE_LABEL[mode]||'Race';}
/** Native dialog supplies focus trapping and pointer isolation from the live 3D carousel. */
export function createTrackPicker({root,courses,trackId,change}) {
  const dialog=document.createElement('dialog');dialog.className='g-track-picker';dialog.setAttribute('aria-labelledby','g-track-title');
  dialog.innerHTML='<header><div><small>YOUR NEXT RACE</small><h2 id="g-track-title">Choose track</h2></div><button type="button" class="g-track-back">Back</button></header><style>'+MODE_CSS+'</style><div class="g-track-mode" role="group" aria-label="Mode"></div><div class="g-track-grid"></div><p class="g-track-status" role="status"></p>';
  root.append(dialog);let opener=null,loaded=false,leaving=false,timer=0,disposed=false;
  const here=globalThis.location;let mode=resolveMode({search:here?here.search:''});paintMode(root,mode);
  const modes=['race','trial'].map(id=>{const b=document.createElement('button');b.type='button';b.dataset.mode=id;b.textContent=MODE_LABEL[id];
    b.setAttribute('aria-pressed',String(id===mode));
    b.onclick=()=>{if(leaving||id===mode)return;mode=id;saveMode(id);paintMode(root,id);
      for(const x of modes)x.setAttribute('aria-pressed',String(x.dataset.mode===id));
      dialog.querySelector('.g-track-status').textContent=MODE_LABEL[id]+(id==='trial'?', just you and your ghost':'');
      // a ?mode= flag follows the pick, so a reload or a course change keeps it
      try{const u=new URL(here.href);if(u.searchParams.has('mode')){u.searchParams.set('mode',id);history.replaceState(history.state,'',u);}}catch(e){}
      if(typeof CustomEvent==='function')root.dispatchEvent(new CustomEvent('glassroad-mode',{detail:id,bubbles:true}));};
    dialog.querySelector('.g-track-mode').append(b);return b;});
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
    nav(dir){if(!dialog.open)return;const buttons=[...modes,...tiles,dialog.querySelector('.g-track-back')],i=buttons.indexOf(document.activeElement);
      if(dir==='back'){close();return;}if(dir==='ok'){document.activeElement.click?.();return;}
      const step={up:-2,down:2,left:-1,right:1}[dir];if(step)buttons[(i+step+buttons.length)%buttons.length].focus();
    },
    dispose(){disposed=true;clearTimeout(timer);dialog.remove();},
  };
}
