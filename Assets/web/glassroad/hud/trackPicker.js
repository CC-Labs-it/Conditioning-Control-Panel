import {courseSketch} from './courseSketch.js';
import {MODE_LABEL,resolveMode,saveMode} from '../meta/mode.js';
import {loadOptions} from './options.js';
import {courseTheme} from '../track/themes.js';
import {mediaRecap} from './courseMedia.js';

const COLORS={threshold:'#ff88b5',mainframe:'#e0b0ff',keep:'#ffce62',boudoir:'#e49cfa',goonworks:'#bd006e'};
// Race / Time trial: two small pills above the grid (Logbook TRIAL lane); the pick is saved and sent as glassroad-mode
const MODE_CSS='.g-track-mode{display:flex;gap:8px;margin:0 0 14px}#race-garage .g-track-mode button{min-height:34px;padding:6px 14px;border:2px solid #121220;border-radius:999px;background:#181830;color:#a7a7c1;font-size:13px}#race-garage .g-track-mode button[aria-pressed=true]{background:#cbb9fc;color:#10101b;border-color:#10101b}';
/** The chooser's in-game shot of a course: art/ui/course-<id>.webp (960 x 540) and a -phone variant (480 x 270). */
export function courseShot(id){const at=name=>new URL('../art/ui/'+name+'.webp',import.meta.url).href;
  const id2=COLORS[id]?id:'threshold';return {src:at('course-'+id2),srcset:at('course-'+id2+'-phone')+' 480w, '+at('course-'+id2)+' 960w'};}
/** A tile's details for the solo lap choice (playtest 2026-10-28): "1 lap · 5.4 km", the distance for those laps. */
export function courseDetails(kmPerLap,laps){const n=laps>0?laps|0:1,km=Number(kmPerLap)*n;
  return n+(n===1?' lap':' laps')+(Number.isFinite(km)&&km>0?' · '+km.toFixed(1)+' km':'');}
/** Shows the mode on the garage Race row (the row keeps its own track line). */
function paintMode(root,mode){const b=root.querySelector('[data-g=race] b');if(b)b.textContent=MODE_LABEL[mode]||'Race';}
/** Native dialog supplies focus trapping and pointer isolation from the live 3D carousel. */
export function createTrackPicker({root,courses,trackId,change,mediaFor=()=>({}),customize=()=>{}}) {
  const dialog=document.createElement('dialog');dialog.className='g-track-picker';dialog.setAttribute('aria-labelledby','g-track-title');
  dialog.innerHTML='<header><div><small>YOUR NEXT RACE</small><h2 id="g-track-title">Choose track</h2></div><button type="button" class="g-track-back">Back</button></header><style>'+MODE_CSS+'</style><div class="g-track-mode" role="group" aria-label="Mode"></div><div class="g-track-grid"></div><p class="g-track-status" role="status"></p>';
  root.append(dialog);let opener=null,loaded=false,leaving=false,timer=0,disposed=false;const kms=[];
  // the solo Laps option, read at each show: the pre-race card may have changed it since
  const paintDetails=()=>{const laps=loadOptions().laps;tiles.forEach((b,i)=>{if(kms[i])b.querySelector('.g-course-details').textContent=courseDetails(kms[i],laps);});};
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
    const shot=courseShot(course.id);
    b.innerHTML='<img class="g-course-shot" alt="" aria-hidden="true" width="960" height="540" loading="lazy" decoding="async" draggable="false" sizes="(max-width:699px) 46vw, 340px"><span class="g-course-mode"></span><span class="g-course-map" aria-hidden="true"><svg viewBox="0 0 180 88"><path class="g-route-shadow"/><path class="g-route"/><circle r="3"/></svg></span><span class="g-course-text"><strong></strong><small class="g-course-details"></small></span><span class="g-course-selected" aria-hidden="true">Selected</span>';
    const img=b.querySelector('.g-course-shot');img.setAttribute('srcset',shot.srcset);img.setAttribute('src',shot.src);
    b.querySelector('strong').textContent=course.name;b.querySelector('.g-course-mode').textContent=course.online?'Solo / online':'Solo';
    const theme=courseTheme(course.id);
    if(theme){
      const badge=document.createElement('span');badge.className='g-course-theme';badge.textContent='Theme: '+theme.label;
      const lyrics=document.createElement('small');lyrics.className='g-course-lyrics';lyrics.textContent='Default lyrics: '+theme.lyrics;
      b.querySelector('.g-course-text').append(badge,lyrics);
    }
    b.onclick=()=>{if(leaving)return;if(course.id===trackId){close();return;}leaving=true;
      for(const tile of tiles)tile.disabled=true;
      dialog.querySelector('.g-track-status').textContent='Opening '+course.name;
      // The opaque loading cover takes over after navigation. The selected look is already saved on this origin.
      root.classList.add('g-course-leaving');timer=setTimeout(()=>change(course.id),350);
    };
    const wrap=document.createElement('article');wrap.className='g-course-card';wrap.append(b);
    const footer=document.createElement('div');footer.className='g-course-media';
    const pictures=document.createElement('p');pictures.className='g-course-pictures';
    const song=document.createElement('p');song.className='g-course-song';
    const custom=document.createElement('button');custom.type='button';custom.className='g-course-customize';custom.textContent='Customize';
    custom.setAttribute('aria-label','Customize '+course.name);custom.onclick=()=>customize(course,custom);
    footer.append(pictures,song,custom);wrap.append(footer);dialog.querySelector('.g-track-grid').append(wrap);return b;
  });
  function refreshMedia(){for(const [i,b]of tiles.entries()){const recap=mediaRecap(mediaFor(courses[i].id)||{},courses[i].id),wrap=b.parentElement;
    wrap.querySelector('.g-course-pictures').textContent=recap.pictures;wrap.querySelector('.g-course-song').textContent='Music: '+recap.song;}}
  async function loadSketches(){if(loaded)return;loaded=true;
    await Promise.allSettled(courses.map(async(course,i)=>{
      const response=await fetch(new URL('../tracks/'+course.id+'.json',import.meta.url));if(!response.ok)return;
      const sketch=courseSketch(await response.json());if(!sketch||disposed)return;const b=tiles[i];
      for(const path of b.querySelectorAll('path'))path.setAttribute('d',sketch.path);
      const start=b.querySelector('circle');start.setAttribute('cx',sketch.start[0]);start.setAttribute('cy',sketch.start[1]);
      kms[i]=sketch.km;
    }));
    paintDetails();
  }
  return {
    get open(){return dialog.open;},
    refreshMedia,
    show(trigger=document.activeElement){if(dialog.open)return;opener=trigger;refreshMedia();dialog.showModal();tiles.find(b=>b.dataset.course===trackId)?.focus();paintDetails();loadSketches();},
    nav(dir){if(!dialog.open)return;const buttons=[...dialog.querySelectorAll('button')],i=buttons.indexOf(document.activeElement);
      if(dir==='back'){close();return;}if(dir==='ok'){document.activeElement.click?.();return;}
      const step={up:-2,down:2,left:-1,right:1}[dir];if(step)buttons[(i+step+buttons.length)%buttons.length].focus();
    },
    dispose(){disposed=true;clearTimeout(timer);dialog.remove();},
  };
}
