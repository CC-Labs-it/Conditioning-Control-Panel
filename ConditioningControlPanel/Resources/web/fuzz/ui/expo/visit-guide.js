import {STOPS,stampSentence,stopSealed,trailMeter,trailTask} from './trail-stops.js';
import {createExpoTaps} from './touch-taps.js';
import {rematchHint} from './rematch.js';

export function visitDirection(route,story,room,position,visit){
  const task=trailTask(room,position,visit);
  return task.title+' · '+task.where;
}

export function createVisitGuide(root,{onOpen=()=>{},canOpen=()=>true}={}){
  const d=root.ownerDocument,phone=d.defaultView.matchMedia('(pointer:coarse)');
  function el(tag,parent,cls,text){
    const node=d.createElement(tag);
    if(cls)node.className=cls;
    if(text)node.textContent=text;
    if(tag==='button')node.type='button';
    parent.append(node);return node;
  }
  function pips(parent){
    const row=el('div',parent,'expo-trail-pips');
    row.setAttribute('aria-hidden','true');
    return STOPS.map(()=>el('i',row));
  }
  const hud=el('aside',root,'expo-route-guide'),open=el('button',hud,'expo-trail-open');
  open.setAttribute('aria-label','Open Human Activity Trail');open.setAttribute('aria-expanded','false');
  el('b',open,'expo-trail-label','HUMAN TRAIL');el('b',open,'expo-trail-mobile-label','Trail');
  const count=el('strong',open),compactCount=el('strong',open,'expo-trail-mobile-count'),mini=pips(open);
  el('small',open,'','8 stamps open the way home');const shortcut=el('kbd',open);
  const task=el('div',hud,'expo-trail-task'),kicker=el('small',task),title=el('strong',task),whereLine=el('span',task);
  const arrow=el('i',whereLine,'expo-task-arrow','↑'),where=el('span',whereLine);arrow.setAttribute('aria-hidden','true');
  const rangeHint=el('span',task,'expo-range-hint');rangeHint.hidden=true;rangeHint.setAttribute('role','status');
  const card=el('section',root,'expo-trail-card');card.hidden=true;card.tabIndex=-1;
  card.setAttribute('role','dialog');card.setAttribute('aria-modal','true');card.setAttribute('aria-label','Human Activity Trail');
  const head=el('header',card);el('small',head,'','Llama Expo 2037 · Visitor Services · Admit one human');
  const heading=el('h2',head,'','Human Activity Trail');
  const intro=el('p',head,'','Twelve stamps. Collect eight and the gift shop lets you go home.');
  const close=el('button',head,'expo-trail-close','Close'),pages=el('div',card,'expo-trail-pages');
  const front=el('div',pages,'expo-trail-front'),list=el('ol',front,'expo-trail-grid');
  const rows=STOPS.map(()=>{
    const row=el('li',list,'expo-trail-stop');
    return {row,number:el('small',row),name:el('strong',row),place:el('span',row),todo:el('p',row),ink:el('em',row)};
  });
  const large=pips(front),meter=el('p',front,'expo-trail-meter'),back=el('div',pages);back.hidden=true;
  el('p',back,'','Yes means you did what a sign asked. Deviation means you did not. The till counts both at the end. Neither one is wrong.');
  const ledger=el('ul',back,'expo-ledger-list');
  const foot=el('footer',card);el('p',foot,'','Staff boxes are not for visitors. Please do not collect them.');
  const flip=el('button',foot),flipLabel=el('span',flip,'expo-trail-flip-label','Turn the card over');
  const flipMobile=el('span',flip,'expo-trail-mobile-label','View ledger');
  flip.setAttribute('aria-pressed','false');
  const moment=el('aside',root,'expo-stamp-moment'),ink=el('strong',moment),number=el('small',moment),plain=el('span',moment);
  const slip=el('aside',root,'expo-deviation-slip');
  for(const node of [moment,slip]){node.hidden=true;node.setAttribute('role','status');}
  const stamps=[],deviations=[];
  let t,release,mode,compact,spitButton,signature='',taskKey='',stampEnd=0,slipEnd=0;
  function cardHeading(){
    const ledgerShown=compact&&!back.hidden;
    heading.textContent=ledgerShown?'Visit ledger':'Human Activity Trail';
    intro.hidden=ledgerShown;card.setAttribute('aria-label',heading.textContent);
  }
  function setLayout(touch){
    if(touch===compact)return;
    compact=touch;hud.toggleAttribute('data-compact',touch);card.toggleAttribute('data-compact',touch);
    (touch?hud:root).append(moment,slip);cardHeading();
  }
  function activePage(){return compact?pages:card;}
  function showBack(visible){
    back.hidden=!visible;front.hidden=visible;flip.setAttribute('aria-pressed',String(visible));
    flipLabel.textContent=visible?'Back to the stamps':'Turn the card over';
    flipMobile.textContent=visible?'View stamps':'View ledger';cardHeading();activePage().scrollTop=0;
  }
  setLayout(phone.matches);
  function showCard(visible){
    if(visible&&!canOpen())return;
    card.hidden=!visible;open.setAttribute('aria-expanded',String(visible));
    if(visible){onOpen();activePage().scrollTop=0;card.focus({preventScroll:true});}
    else{showBack(false);root.focus({preventScroll:true});}
  }
  function toggle(){showCard(card.hidden);}
  function turn(){showBack(back.hidden);}
  const taps=createExpoTaps();
  taps.bind(open,toggle);taps.bind(close,()=>showCard(false));taps.bind(flip,turn);
  card.addEventListener('keydown',event=>{
    if(compact&&['ArrowUp','ArrowDown','PageUp','PageDown','Home','End'].includes(event.key)){
      event.preventDefault();
      const steps={ArrowUp:-40,ArrowDown:40,PageUp:-pages.clientHeight,PageDown:pages.clientHeight,Home:-pages.scrollHeight,End:pages.scrollHeight};
      pages.scrollTop+=steps[event.key];return;
    }
    if(event.key!=='Tab')return;
    event.preventDefault();
    const next=d.activeElement===close?flip:d.activeElement===flip?close:event.shiftKey?flip:close;
    next.focus();
  });
  function collect(event){(event.kind==='stamp'?stamps:deviations).push(event);}
  function watch(ticket){release?.();t=ticket;release=t.subscribe(collect);signature='';}
  function renderTicket(){
    const next=[t.count,t.stamps.size,t.discovered.size,t.ledger.size,...t.stamps].join('|');
    if(signature===next)return;signature=next;
    count.textContent=t.count+' of 12';compactCount.textContent=t.count+'/12';meter.textContent=trailMeter(t.count);
    open.setAttribute('aria-label','Open Human Activity Trail, '+t.count+' of 12 stamps. 8 stamps open the way home.');
    for(const [i,s] of STOPS.entries()){
      const r=rows[i],filled=s.marks.filter(m=>t.stamps.has(m)),sealed=stopSealed(s,t);
      for(const cell of [mini[i],large[i]])cell.dataset.ink=filled.length===s.marks.length?'full':filled.length?'partial':'';
      r.row.dataset.sealed=String(sealed);r.number.textContent=String(i+1).padStart(2,'0')+(s.staff?' · staff':'');
      r.name.textContent=sealed?'Staff only':s.name;r.place.textContent=sealed?'Please do not collect.':s.place;
      r.todo.textContent=sealed?'':s.todo;r.ink.hidden=!filled.length;r.ink.textContent=filled.join(' · ');
    }
    ledger.replaceChildren();
    for(const entry of t.ledger.values()){const row=el('li',ledger);el('span',row,'',entry.label);el('b',row,'',entry.deviation?'Deviation':'Yes');}
    if(!ledger.children.length)el('li',ledger,'','No entries yet.');
  }
  function update(story,room,p,v,elapsed=0,yaw=0,input='keyboard'){
    setLayout(phone.matches||root.hasAttribute('data-expo-touch'));
    if(t!==v.ticket)watch(v.ticket);
    root.toggleAttribute('data-hoof-board',room==='hoof'&&(v.hoof==='practice'||v.hoof==='pictures'));
    renderTicket();
    if(mode!==input){mode=input;shortcut.textContent=input==='gamepad'?'Y · Card':input==='touch'?'Open card':'T · Card';}
    const a=trailTask(room,p,v,yaw),key=[a.title,a.where,a.kicker].join('|');
    if(key!==taskKey){taskKey=key;kicker.textContent=a.kicker;title.textContent=a.title;where.textContent=a.where;}
    arrow.hidden=!a.target;
    if(a.target)arrow.style.transform='rotate('+Math.round((Math.atan2(a.target[0]-p.x,p.z-a.target[1])+yaw)*180/Math.PI)+'deg)';
    if(room==='commons'&&input==='touch')spitButton||=root.querySelector('.expo-spit');
    const hint=rematchHint(room,p,input,!!spitButton?.hidden);
    if(rangeHint.textContent!==hint)rangeHint.textContent=hint;
    rangeHint.hidden=!hint;
    whereLine.hidden=!!hint&&a.kicker==='The Rematch'&&!v.carry?.key;
    kicker.hidden=!!hint&&!!v.carry?.key;
    if(elapsed>=stampEnd){
      const next=compact&&elapsed<slipEnd?null:stamps.shift();moment.hidden=!next;
      if(next){
        ink.textContent=next.mark;
        number.textContent=next.remaining?'The Habitat · '+next.count+' of 12 boxes complete':'Stamp '+next.count+' of 12'+(STOPS.some(s=>s.staff&&s.marks.includes(next.mark))?' · staff only':'');
        const followUp=next.remaining?.includes('Kept')?' Sit in the keeper’s chair to fill this box.':next.remaining?.includes('Fed')?' Feed one Yes through the slot to fill this box.':'';
        plain.textContent=stampSentence(next.mark,t)+followUp;
        stampEnd=elapsed+3;moment.removeAttribute('data-new');void moment.offsetWidth;moment.dataset.new='';
      }
    }
    if(elapsed>=slipEnd){
      const next=compact&&elapsed<stampEnd?null:deviations.shift();slip.hidden=!next;
      if(next){slip.textContent='Deviation noted: '+next.label;slipEnd=elapsed+3;slip.dataset.new='';}
    }
  }
  return {update,watch,toggle,close:()=>showCard(false),get opened(){return !card.hidden;},
    handlePad(pad){
      if(pad.back||pad.trail)showCard(false);
      else if(pad.jump||pad.interact)turn();
      else if(pad.down||pad.up||Math.abs(pad.moveY)>.2)activePage().scrollTop+=pad.down?100:pad.up?-100:Math.round(12*pad.moveY);
    },dispose(){release?.();for(const node of [hud,card,moment,slip])node.remove();}};
}
