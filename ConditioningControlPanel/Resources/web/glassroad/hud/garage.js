import {COURSES} from '../track/catalog.js';
import {createTrackPicker} from './trackPicker.js';
import {KARTS,OUTFITS,PRESETS,COLOR_KEYS,lockLine,PAINTS,UMBRELLAS,TRAILS,PAINT_COLORS,wearable,shopLockLine} from './garageState.js';
import {createRecordPanel} from './record.js';
import {loadRecord} from '../meta/record.js';
import {createCourseMedia,mediaRecap} from './courseMedia.js';

/** The title card logo (art/ui/logo.webp, transparent, 720 x 311). */
export const LOGO=new URL('../art/ui/logo.webp',import.meta.url).href;

/** counter: the host opens the Prize Counter (CONTRACT.md section 7, `open-prize-counter`); the row stays hidden otherwise.
 * deny(line): a dark shop look was pressed (the garage plays the deny sound). */
export function garageUi({change,select,customize,action,hover,trackId='threshold',online=true,changeTrack,counter=false,deny=()=>{}}) {
  const root=document.createElement('section');root.id='race-garage';root.hidden=true;root.setAttribute('aria-label','Choose your racer');
  root.innerHTML=`<aside class="g-menu" aria-label="Main menu"><div class="g-title-card"><p>CC LABS</p><div class="g-logo g-logo-art"><img src="${LOGO}" alt="Racing Thoughts" width="720" height="311" decoding="async" draggable="false"></div><div class="g-title-rule" aria-hidden="true"></div></div>
      <nav class="g-menu-options" aria-label="Race options"><button data-g="race" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 21V3m0 1c5-4 9 4 15 0v10c-6 4-10-4-15 0"/></svg><span><b>Race</b><small class="g-track"></small></span><i aria-hidden="true">↗</i></button><button data-g="tracks" class="g-menu-row g-course-choice"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 18h7a4 4 0 0 0 0-8H8a3 3 0 0 1 0-6h12M4 15v6m-3-3h6M17 1l3 3-3 3"/></svg><span><small>Choose track</small><b class="g-course-name"></b></span><i aria-hidden="true">›</i></button><button data-g="online" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="8" cy="7" r="3"/><path d="M2 20v-3a6 6 0 0 1 12 0v3m2-16a3 3 0 0 1 0 6m2 3c3 1 4 3 4 7"/></svg><span><b>Race online</b></span><i aria-hidden="true">›</i></button><button data-g="customize" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5a3 3 0 0 1 6 0c0 3-3 3-3 6l9 6v3H3v-3l9-6"/></svg><span><b>Customize EMI</b></span><i aria-hidden="true">›</i></button><button data-g="counter" class="g-menu-row g-counter"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M3 9h18l-2-5H5ZM4 9v11h16V9M9 20v-6h6v6"/></svg><span><b>Prize Counter</b></span><i aria-hidden="true">›</i></button><button data-g="record" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 3h11l3 3v15H5Z"/><path d="M9 9h6m-6 4h6m-6 4h3"/></svg><span><b>Record</b></span><i aria-hidden="true">›</i></button><button data-g="options" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 4v16m8-16v16m8-16v16M1 9h6m2 6h6m2-8h6"/></svg><span><b>Options</b></span><i aria-hidden="true">›</i></button><button data-g="controls" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7h10c4 0 7 13 3 13l-5-4H9l-5 4C0 20 3 7 7 7Z"/><path d="M6 10v5m-2-2h4m8-2h.01m3 3h.01"/></svg><span><b>Controls</b></span><i aria-hidden="true">›</i></button></nav></aside>
    <div class="g-selector"><h1></h1><div class="g-positions" aria-label="Kart selection"></div><p>Drag to turn · tap a kart to choose</p><small class="g-lock-note" aria-live="polite"></small></div>
    <button class="g-nav previous" aria-label="Previous kart" data-g="previous"></button><button class="g-nav next" aria-label="Next kart" data-g="next"></button>
    <section class="g-custom" hidden aria-label="Customize EMI"><div class="g-custom-top"><div><small>MAKE IT YOURS</small><h2>EMI</h2><p>Drag EMI to turn</p></div><button data-g="done">Done ✓</button></div>
    <div class="g-controls"><div class="g-label">Outfit <span class="g-outfit-name"></span></div><div class="g-outfits"></div>
      <div class="g-label">Colors <button data-g="original">Original</button></div><div class="g-colors"><label>Shell <input type="color" data-color="primary" aria-label="Shell color"></label><label>Accent <input type="color" data-color="secondary" aria-label="Accent color"></label></div><div class="g-palettes"></div>
      <div class="g-label">Pattern</div><div class="g-patterns"></div><p class="g-skin-name"></p>
      <div class="g-label">Extras</div><div class="g-extras"></div><p class="g-shop-note" aria-live="polite"></p></div></section><p class="g-announce" role="status" aria-live="polite"></p>`;
  root.querySelector('[data-g=online]').hidden=!online;root.querySelector('[data-g=counter]').hidden=!counter;
  document.body.append(root);
  const $=s=>root.querySelector(s);
  let mediaFor=()=>({});
  const courseMedia=createCourseMedia({root,mediaFor:id=>mediaFor(id)});
  const makePicker=()=>createTrackPicker({root,courses:COURSES,trackId,change:id=>changeTrack?.(id),mediaFor:id=>mediaFor(id),customize:(course,trigger)=>courseMedia.show(course,trigger)});
  let picker=makePicker();
  const mediaRow=document.createElement('button');mediaRow.type='button';mediaRow.className='g-menu-row g-main-media';mediaRow.dataset.g='course-media';
  mediaRow.innerHTML='<span><b>Customize track</b><small class="g-main-song"></small><small class="g-main-pictures"></small></span><i aria-hidden="true">›</i>';
  $('[data-g=tracks]').after(mediaRow);
  function refreshMedia(){const recap=mediaRecap(mediaFor(trackId)||{},trackId);$('.g-main-song').textContent='Music: '+recap.song;$('.g-main-pictures').textContent=recap.pictures;picker.refreshMedia();courseMedia.refresh();}
  refreshMedia();
  $('.g-course-name').textContent=COURSES.find(c=>c.id===trackId)?.name||'Threshold';
  const record=createRecordPanel({root,courses:COURSES,load:()=>loadRecord()});let noteTimer=0;
  const make=(parent,label,fn)=>{const b=document.createElement('button');b.type='button';b.textContent=label;b.onclick=fn;$(parent).append(b);return b;};
  let owns=undefined,shopTimer=0,wornTimers=[],wornEl=null;
  // a deny or lock line cuts a worn note short; the worn note owns its own timers
  const stopWorn=()=>{for(const t of wornTimers)clearTimeout(t);wornTimers=[];wornEl?.classList.remove('g-worn');wornEl=null;};
  const plain=el=>{stopWorn();el.classList.remove('g-note-in','g-note-out');};
  // A dark shop look says where it is sold and changes nothing.
  const shop=(kind,id,values)=>()=>{if(wearable(kind,id,owns)){change(values());return;}
    const el=$('.g-shop-note');plain(el);el.textContent=shopLockLine(id);clearTimeout(shopTimer);shopTimer=setTimeout(()=>{el.textContent='';},3200);deny(shopLockLine(id));};
  for(const [id,label]of OUTFITS)make('.g-outfits',label,shop('outfit',id,()=>({outfit:id}))).dataset.outfit=id;
  for(const p of PRESETS){const b=make('.g-palettes','',()=>change({...p,skin:p.name,original:false}));b.setAttribute('aria-label',p.name+' colors');b.dataset.palette=p.name;b.style.background=`linear-gradient(135deg,${p.primary} 50%,${p.secondary} 50%)`;}
  ['Plain','Stripes','Dots','Checks'].forEach((label,pattern)=>{const b=make('.g-patterns','',()=>change({pattern,paint:null,original:false}));b.dataset.pattern=pattern;b.setAttribute('aria-label',label);});
  for(const [id,label]of PAINTS){const b=make('.g-patterns','',shop('paint',id,()=>({paint:id,original:false,...(PAINT_COLORS[id]?{...PAINT_COLORS[id],skin:label}:{})})));b.dataset.paint=id;b.dataset.label=label;}
  for(const [kind,list]of [['umbrella',UMBRELLAS],['trail',TRAILS]])for(const [id,label]of list){
    const b=make('.g-extras',label,shop(kind,id,()=>({[kind]:current[kind]===id?null:id})));b.dataset.extra=kind;b.dataset.extraId=id;}
  let current={};
  for(const [index,[id,label]]of KARTS.entries()){const b=make('.g-positions','',()=>select(index));b.dataset.kart=id;b.setAttribute('aria-label',label);}
  root.addEventListener('click',e=>{const name=e.target.closest('[data-g]')?.dataset.g;if(!name)return;
    if(name==='tracks'){picker.show($('[data-g=tracks]'));return;}
    if(name==='course-media'){courseMedia.show(COURSES.find(c=>c.id===trackId)||COURSES[0],mediaRow);return;}
    if(name==='record'){record.show($('[data-g=record]'));return;}
    if(name==='customize'||name==='done')customize(name==='customize');
    else if(name==='previous'||name==='next')select(name==='next'?1:-1,true);
    else if(name==='original')change({original:true,pattern:0,paint:null,skin:'Original'});
    else action(name);
  });
  root.addEventListener('input',e=>{const key=e.target.dataset.color;if(key)change({[key]:e.target.value,original:false,skin:'Custom'},true);});
  for(const [selector,dir]of [['.previous',-1],['.next',1]])for(const [event,on]of [['pointerenter',true],['pointerleave',false],['focus',true],['blur',false]])$(selector).addEventListener(event,()=>hover(dir,on));
  return {
    root,get trackPicking(){return picker.open||courseMedia.open;},trackNav:dir=>courseMedia.open?courseMedia.nav(dir):picker.nav(dir),get recordOpen(){return record.open;},recordNav:dir=>record.nav(dir),
    setMedia(fn){mediaFor=typeof fn==='function'?fn:()=>({});refreshMedia();},refreshMedia,
    lockNote(kart){const line=lockLine(kart);if(!line)return;const el=$('.g-lock-note');plain(el);el.textContent=KARTS.find(k=>k[0]===kart)[1]+'. '+line;clearTimeout(noteTimer);noteTimer=setTimeout(()=>{el.textContent='';},3200);},
    layout(l){root.style.setProperty("--g-menu",l.menuWidth+"px");root.style.setProperty("--g-stage-center",l.center*100+"%");root.style.setProperty("--g-left-arrow",l.arrowLeft*100+"%");root.style.setProperty("--g-right-arrow",l.arrowRight*100+"%");root.style.setProperty("--g-arrow-y",l.arrowY*100+"%");},
    paint(s,{custom,busy,locked,owned}){
      current=s;owns=owned;
      const dark=(el,kind,id,label)=>{const off=!wearable(kind,id,owns);el.classList.toggle('locked',off);el.title=off?shopLockLine(id):'';el.setAttribute('aria-label',label+(off?', locked. '+shopLockLine(id):''));};
      for(const el of root.querySelectorAll('[data-outfit]'))dark(el,'outfit',el.dataset.outfit,el.textContent);
      for(const el of root.querySelectorAll('[data-paint]')){dark(el,'paint',el.dataset.paint,el.dataset.label);el.setAttribute('aria-pressed',String(!s.original&&s.paint===el.dataset.paint));}
      for(const el of root.querySelectorAll('[data-extra]')){dark(el,el.dataset.extra,el.dataset.extraId,el.textContent);el.setAttribute('aria-pressed',String(s[el.dataset.extra]===el.dataset.extraId));}
      root.classList.toggle('g-customizing',custom);$('.g-custom').hidden=!custom;
      $('.g-selector h1').textContent=KARTS.find(k=>k[0]===s.kart)[1];
      $('.g-outfit-name').textContent=OUTFITS.find(o=>o[0]===s.outfit)[1];$('.g-skin-name').textContent=s.original?'Original':s.skin;
      for(const k of ['primary','secondary'])$(`[data-color=${k}]`).value=s[k];
      for(const el of root.querySelectorAll('[data-outfit]'))el.setAttribute('aria-pressed',String(el.dataset.outfit===s.outfit));
      for(const el of root.querySelectorAll('[data-palette]'))el.setAttribute('aria-pressed',String(!s.original&&el.dataset.palette===s.skin));
      for(const el of root.querySelectorAll('[data-pattern]'))el.setAttribute('aria-pressed',String(!s.paint&&Number(el.dataset.pattern)===s.pattern));
      for(const el of root.querySelectorAll('[data-kart]')){const dark=locked.has(el.dataset.kart),line=dark&&lockLine(el.dataset.kart);el.classList.toggle('locked',dark);el.setAttribute('aria-disabled',String(dark));el.title=line||'';el.setAttribute('aria-pressed',String(el.dataset.kart===s.kart));el.setAttribute('aria-label',KARTS.find(k=>k[0]===el.dataset.kart)[1]+(dark?', locked'+(line?'. '+line:''):''));}
      for(const el of root.querySelectorAll('[data-g=race],[data-g=online],[data-g=tracks]'))el.disabled=busy;
      $('.g-announce').textContent=custom?'Customize EMI':KARTS.find(k=>k[0]===s.kart)[1]+' selected';
    },
    /** The counter's Wear landed (render/garage.js wear): the item's button pulses, its note slides in and out. */
    worn(kind,id,line){const sel={kart:'data-kart',outfit:'data-outfit',paint:'data-paint',umbrella:'data-extra-id',trail:'data-extra-id'}[kind];if(!sel)return;
      const note=$(kind==='kart'?'.g-lock-note':'.g-shop-note');clearTimeout(noteTimer);clearTimeout(shopTimer);plain(note);
      wornEl=[...root.querySelectorAll('['+sel+']')].find(el=>el.getAttribute(sel)===id)||null;
      if(wornEl){void wornEl.offsetWidth;wornEl.classList.add('g-worn');if(kind!=='kart')wornEl.scrollIntoView?.({block:'nearest'});}
      note.textContent=line;void note.offsetWidth;note.classList.add('g-note-in');
      wornTimers=[setTimeout(()=>{note.classList.replace('g-note-in','g-note-out');},2600),setTimeout(()=>{plain(note);note.textContent='';},3200)];},
    track(name,laps){$('.g-track').textContent=name+' · '+laps+(laps===1?' lap':' laps');},
    setCourse(course){
      trackId=course.trackId;online=course.online!==false;
      // Replace only the tiny picker. Its previous selection, pending sketches and delayed
      // navigation belong to the old course, while the wardrobe and carousel stay resident.
      picker.dispose();picker=makePicker();refreshMedia();
      $('[data-g=online]').hidden=!online;
      $('.g-course-name').textContent=COURSES.find(c=>c.id===trackId)?.name||course.track||'Threshold';
      $('.g-track').textContent=course.track+' · '+course.laps+(course.laps===1?' lap':' laps');
      root.classList.remove('g-course-leaving');
    },
    setVisible(on){if(root.hidden===!on)return;if(!on)record.close();root.hidden=!on;document.body.classList.toggle('garage-open',on);if(on)$('[data-g=race]').focus({preventScroll:true});},
    customFocus(on){$(on?'[data-g=done]':'[data-g=customize]').focus({preventScroll:true});},
    dispose(){clearTimeout(noteTimer);clearTimeout(shopTimer);stopWorn();record.dispose();picker.dispose();courseMedia.dispose();root.remove();document.body.classList.remove('garage-open');},
  };
}
