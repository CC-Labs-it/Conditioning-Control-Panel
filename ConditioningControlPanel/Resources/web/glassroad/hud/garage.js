import {COURSES} from '../track/catalog.js';
import {KARTS,OUTFITS,PRESETS,COLOR_KEYS} from './garageState.js';

export function garageUi({change,select,customize,action,hover,trackId='threshold',online=true,changeTrack}) {
  const root=document.createElement('section');root.id='race-garage';root.hidden=true;root.setAttribute('aria-label','Choose your racer');
  root.innerHTML=`<aside class="g-menu" aria-label="Main menu"><div class="g-title-card"><p>CC LABS</p><div class="g-logo" aria-label="Racing Thoughts"><span>racing</span><b>thoughts</b></div><div class="g-title-rule" aria-hidden="true"></div></div>
      <div class="g-courses" aria-label="Track"></div>
      <nav class="g-menu-options" aria-label="Race options"><button data-g="race" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 21V3m0 1c5-4 9 4 15 0v10c-6 4-10-4-15 0"/></svg><span><b>Race</b><small class="g-track"></small></span><i aria-hidden="true">↗</i></button><button data-g="online" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="8" cy="7" r="3"/><path d="M2 20v-3a6 6 0 0 1 12 0v3m2-16a3 3 0 0 1 0 6m2 3c3 1 4 3 4 7"/></svg><span><b>Race online</b></span><i aria-hidden="true">›</i></button><button data-g="customize" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5a3 3 0 0 1 6 0c0 3-3 3-3 6l9 6v3H3v-3l9-6"/></svg><span><b>Customize EMI</b></span><i aria-hidden="true">›</i></button><button data-g="options" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 4v16m8-16v16m8-16v16M1 9h6m2 6h6m2-8h6"/></svg><span><b>Options</b></span><i aria-hidden="true">›</i></button><button data-g="controls" class="g-menu-row"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M7 7h10c4 0 7 13 3 13l-5-4H9l-5 4C0 20 3 7 7 7Z"/><path d="M6 10v5m-2-2h4m8-2h.01m3 3h.01"/></svg><span><b>Controls</b></span><i aria-hidden="true">›</i></button></nav></aside>
    <div class="g-selector"><h1></h1><div class="g-positions" aria-label="Kart selection"></div><p>Drag to turn · tap a kart to choose</p></div>
    <button class="g-nav previous" aria-label="Previous kart" data-g="previous"></button><button class="g-nav next" aria-label="Next kart" data-g="next"></button>
    <section class="g-custom" hidden aria-label="Customize EMI"><div class="g-custom-top"><div><small>MAKE IT YOURS</small><h2>EMI</h2><p>Drag EMI to turn</p></div><button data-g="done">Done ✓</button></div>
    <div class="g-controls"><div class="g-label">Outfit <span class="g-outfit-name"></span></div><div class="g-outfits"></div>
      <div class="g-label">Colors <button data-g="original">Original</button></div><div class="g-colors"><label>Shell <input type="color" data-color="primary" aria-label="Shell color"></label><label>Accent <input type="color" data-color="secondary" aria-label="Accent color"></label></div><div class="g-palettes"></div>
      <div class="g-label">Pattern</div><div class="g-patterns"></div><p class="g-skin-name"></p></div></section><p class="g-announce" role="status" aria-live="polite"></p>`;
  document.body.append(root);
  const $=s=>root.querySelector(s);
  const make=(parent,label,fn)=>{const b=document.createElement('button');b.type='button';b.textContent=label;b.onclick=fn;$(parent).append(b);return b;};
  for(const c of COURSES){const b=make('.g-courses',c.name,()=>changeTrack?.(c.id));b.dataset.course=c.id;b.setAttribute('aria-pressed',String(c.id===trackId));b.disabled=c.id===trackId;}
  $('[data-g=online]').hidden=!online;
  for(const [id,label]of OUTFITS)make('.g-outfits',label,()=>change({outfit:id})).dataset.outfit=id;
  for(const p of PRESETS){const b=make('.g-palettes','',()=>change({...p,skin:p.name,original:false}));b.setAttribute('aria-label',p.name+' colors');b.dataset.palette=p.name;b.style.background=`linear-gradient(135deg,${p.primary} 50%,${p.secondary} 50%)`;}
  ['Plain','Stripes','Dots','Checks'].forEach((label,pattern)=>{const b=make('.g-patterns','',()=>change({pattern,original:false}));b.dataset.pattern=pattern;b.setAttribute('aria-label',label);});
  for(const [index,[id,label]]of KARTS.entries()){const b=make('.g-positions','',()=>select(index));b.dataset.kart=id;b.setAttribute('aria-label',label);}
  root.addEventListener('click',e=>{const name=e.target.closest('[data-g]')?.dataset.g;if(!name)return;
    if(name==='customize'||name==='done')customize(name==='customize');
    else if(name==='previous'||name==='next')select(name==='next'?1:-1,true);
    else if(name==='original')change({original:true,pattern:0,skin:'Original'});
    else action(name);
  });
  root.addEventListener('input',e=>{const key=e.target.dataset.color;if(key)change({[key]:e.target.value,original:false,skin:'Custom'},true);});
  for(const [selector,dir]of [['.previous',-1],['.next',1]])for(const [event,on]of [['pointerenter',true],['pointerleave',false],['focus',true],['blur',false]])$(selector).addEventListener(event,()=>hover(dir,on));
  return {
    root,
    layout(l){root.style.setProperty("--g-menu",l.menuWidth+"px");root.style.setProperty("--g-stage-center",l.center*100+"%");root.style.setProperty("--g-left-arrow",l.arrowLeft*100+"%");root.style.setProperty("--g-right-arrow",l.arrowRight*100+"%");root.style.setProperty("--g-arrow-y",l.arrowY*100+"%");},
    paint(s,{custom,busy,locked}){
      root.classList.toggle('g-customizing',custom);$('.g-custom').hidden=!custom;
      $('.g-selector h1').textContent=KARTS.find(k=>k[0]===s.kart)[1];
      $('.g-outfit-name').textContent=OUTFITS.find(o=>o[0]===s.outfit)[1];$('.g-skin-name').textContent=s.original?'Original':s.skin;
      for(const k of ['primary','secondary'])$(`[data-color=${k}]`).value=s[k];
      for(const el of root.querySelectorAll('[data-outfit]'))el.setAttribute('aria-pressed',String(el.dataset.outfit===s.outfit));
      for(const el of root.querySelectorAll('[data-palette]'))el.setAttribute('aria-pressed',String(!s.original&&el.dataset.palette===s.skin));
      for(const el of root.querySelectorAll('[data-pattern]'))el.setAttribute('aria-pressed',String(Number(el.dataset.pattern)===s.pattern));
      for(const el of root.querySelectorAll('[data-kart]')){el.disabled=locked.has(el.dataset.kart);el.classList.toggle('locked',el.disabled);el.setAttribute('aria-pressed',String(el.dataset.kart===s.kart));el.setAttribute('aria-label',KARTS.find(k=>k[0]===el.dataset.kart)[1]+(el.disabled?', locked':''));}
      for(const el of root.querySelectorAll('[data-g=race],[data-g=online]'))el.disabled=busy;
      for(const el of root.querySelectorAll('[data-course]'))el.disabled=busy||el.dataset.course===trackId;
      $('.g-announce').textContent=custom?'Customize EMI':KARTS.find(k=>k[0]===s.kart)[1]+' selected';
    },
    track(name,laps){$('.g-track').textContent=name+' · '+laps+' laps';},
    setVisible(on){if(root.hidden===!on)return;root.hidden=!on;document.body.classList.toggle('garage-open',on);if(on)$('[data-g=race]').focus({preventScroll:true});},
    customFocus(on){$(on?'[data-g=done]':'[data-g=customize]').focus({preventScroll:true});},
    dispose(){root.remove();document.body.classList.remove('garage-open');},
  };
}
