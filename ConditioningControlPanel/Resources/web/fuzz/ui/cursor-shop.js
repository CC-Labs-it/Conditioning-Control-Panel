import {wordEditor} from './word-editor.js';
import {trailCollection,stagePrices} from '../data/trail-collection.js';
import {BY_ID} from '../data/items.js';
import {BRAND} from '../data/brand.js';
import {whole} from '../sim/format.js';
import {isOpen,ridersFor} from '../sim/economy.js';
import {gateLine,gateOf,gateOpen,markSeen,newMark,rowOpen,seenRecord,SHOP_GATES} from './shop-gates.js';

const trails=['trail','starburst','word_comet'];
const shapes={
  cursor:'<path d="M2 2v23l6-5 4 9 4-2-4-9h9z" fill="#ff4fa8" stroke="#fff"/>',
  cursor_prism:'<path d="M2 2l21 10-10 3-4 10z" fill="#76ecff" stroke="#261c48" stroke-width="2"/><path d="M3 3l10 12-4 8z" fill="#b987ff"/>',
  cursor_nova:'<path d="M2 2l22 13-10 1-5 9z" fill="#ffd46d" stroke="#442f45" stroke-width="2"/><path d="M22 1l1.5 4.5L28 7l-4.5 1.5L22 13l-1.5-4.5L16 7l4.5-1.5z" fill="#fff2a9" stroke="#604968"/>',
};
const skinUrls=Object.fromEntries(Object.entries(shapes).map(([id,shape])=>[id,
  'url("data:image/svg+xml,'+encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" width="30" height="30" viewBox="0 0 30 30">'+shape+'</svg>')+'") 2 2, auto']));
const cursors=['system',...Object.keys(shapes)];
const modifiers=['ring','hijack'];
const skinPrices={cursor_prism:3600,cursor_nova:5400};
const names={cursor_prism:'Prism Cursor',cursor_nova:'Nova Cursor',none:'No trail',system:'System pointer',ring:'Tap rings'};
const hints={trail:'Fine dust and tiny stars',starburst:'Dense, bright sparkles',word_comet:'Bold words that follow and coast',system:'Your device pointer',cursor:'Rose pink with a white edge',cursor_prism:'Cyan and violet facets',cursor_nova:'Gold pointer with a star',ring:'A ripple where you press',hijack:'Fake cursor in pop-up questions'};

/* Price buttons fill toward the price as the player saves and turn green once it is affordable, like the Button Shop's. */
const FILL_CSS='.cursor-kit .cursor-kit-action[data-price]{position:relative;isolation:isolate;overflow:hidden}'
  +'.cursor-kit .cursor-kit-action[data-price]::before{content:"";position:absolute;inset:0;z-index:-1;margin:0;background:#f4bfd2;transform-origin:left;transform:scaleX(var(--fill,0));transition:transform .3s ease-out}'
  +'.cursor-kit .cursor-kit-action[data-price][data-afford]::before{background:#bfe5cf}'
  +'.cursor-kit .cursor-kit-action[data-price][data-afford]{border-color:#3d6b55}'
  +'body.gentle .cursor-kit .cursor-kit-action[data-price]::before{transition:none}';
function fillPrice(button,price,fuzz){
  if(!price){if(button.dataset.price){delete button.dataset.price;button.style.removeProperty('--fill');button.removeAttribute('data-afford');}return;}
  button.dataset.price=price;
  const fill=Math.min(1,fuzz/price);
  button.style.setProperty('--fill',fill.toFixed(3));
  button.toggleAttribute('data-afford',fill>=1);
}

export function bindCursorShop(engine,ui){
  const doc=ui.doc;
  const prefs=()=>engine.run.store.cursorKit??={};
  const has=id=>Boolean(engine.run.owned[id] || (skinPrices[id] && prefs().skins?.[id]));
  const enabled=id=>has(id)&&engine.switches.isOn(id);
  /* Owner 2026-11-08: shop parts wait for their power-up (ui/shop-gates.js); anything already owned stays usable. */
  const famOpen=id=>rowOpen(engine.run,'family',id,(prefs().stages?.[id]||0)>0);
  const skinOpen=id=>rowOpen(engine.run,'cursor',id,has(id));
  let fresh=[];
  let familyId='',familyTags=[],wordToggle,editor=null;
  let dialog=null,category='Trails',rows=[],lastSkin='',balance=null,status=null,removeTrail=null;
  function activeSkin(){
    const preferred=prefs().skin;
    if(preferred==='system')return preferred;
    return enabled(preferred)&&shapes[preferred]?preferred:enabled('cursor')?'cursor':'system';
  }
  function readyToBuy(){
    const fuzz=engine.run.fuzz;
    if(has('cursor')&&Object.entries(skinPrices).some(([id,cost])=>!has(id)&&skinOpen(id)&&fuzz>=cost))return true;
    if(trails.some(id=>!has(id)&&BY_ID[id]&&isOpen(engine.run,BY_ID[id])&&fuzz>=engine.price(id)))return true;
    if(!has('trail'))return false;
    return trailCollection.some(f=>{const owned=prefs().stages?.[f.id]||0;return famOpen(f.id)&&owned<f.levels.length&&fuzz>=stagePrices[owned];});
  }
  function refresh(){
    const skin=activeSkin();
    if(skin!==lastSkin){
      lastSkin=skin;
      if(skin==='system')doc.documentElement.style.removeProperty('--shop-cursor');
      else doc.documentElement.style.setProperty('--shop-cursor',skinUrls[skin]);
      doc.documentElement.toggleAttribute('data-custom-pointer',skin!=='system');
    }
    /* The cursor door on the tool row wears a dot while something here is affordable and not yet owned. */
    const ready=readyToBuy()?'1':'';
    if(doc.documentElement.dataset.cursorShopReady!==ready)doc.documentElement.dataset.cursorShopReady=ready;
    if(!dialog)return;
    if(!doc.getElementById('cursor-kit-fill-css')){const st=doc.createElement('style');st.id='cursor-kit-fill-css';st.textContent=FILL_CSS;doc.head.append(st);}
    editor?.refresh();
    balance.textContent=whole(engine.run.fuzz)+' '+BRAND.currency;
    const tier=doc.querySelector('[data-cursor-trail]')?.dataset.cursorTrail||'none';
    if(wordToggle){wordToggle.checked=prefs().words??enabled('word_comet');wordToggle.parentElement.hidden=!gateOpen(engine.run,'words');}
    if(removeTrail)removeTrail.disabled=tier==='none';
    for(const {id,label,tag,lock} of familyTags){
      const locked=id ? !prefs().stages?.[id]||!famOpen(id) : !has('trail');
      const accessibleName=label+', '+(locked?'locked':'unlocked');
      if(tag.getAttribute('aria-label')===accessibleName)continue;
      tag.toggleAttribute('data-locked',locked);
      tag.setAttribute('aria-label',accessibleName);
      lock.style.display=locked?'':'none';
    }
    for(const {id,action,detail,stage} of rows){
      if(stage){
        const owned=prefs().stages?.[id]||0, cost=stagePrices[stage-1];
        const worn=tier===id&&prefs().stage===stage;
        action.toggleAttribute('data-worn',worn);
        const gated=!famOpen(id);
        const locked=!has('trail')||gated||owned<stage-1;
        const previous=trailCollection.find(f=>f.id===id).levels[stage-2];
        action.textContent=worn?'Equipped':stage<=owned?'Equip':!has('trail')?'Needs Stardust':gated?gateLine('families'):locked?'Needs '+previous:(stage===1?'Unlock family · ':'Buy · ')+whole(cost);
        action.disabled=worn||locked||(stage>owned&&engine.run.fuzz<cost);
        fillPrice(action,!worn&&!locked&&stage>owned?cost:0,engine.run.fuzz);
        continue;
      }
      const owned=id==='none'||id==='system'||(id==='ring'?has('cursor'):has(id));
      if(!owned){
        if(id==='ring'){action.textContent='Needs Pink Cursor';action.disabled=true;fillPrice(action,0);continue;}
        if(skinPrices[id]){
          const open=has('cursor')&&skinOpen(id);
          action.textContent=!has('cursor')?'Needs Pink Cursor':open?'Buy · '+whole(skinPrices[id]):gateLine(gateOf('cursor',id));
          action.disabled=!open||engine.run.fuzz<skinPrices[id];
          fillPrice(action,open?skinPrices[id]:0,engine.run.fuzz);
          continue;
        }
        const item=BY_ID[id],missing=item.req.find(parent=>!has(parent));
        const cost=engine.price(id);
        action.textContent=missing?'Needs '+BY_ID[missing].name:'Buy · '+whole(cost);
        action.disabled=!isOpen(engine.run,item)||engine.run.fuzz<cost;
        fillPrice(action,missing?0:cost,engine.run.fuzz);
        const riders=ridersFor(engine.run,id);
        detail.textContent=riders.length?'Includes '+riders.map(r=>BY_ID[r].name).join(', '):hints[id];
        continue;
      }
      detail.textContent=hints[id]||'';
      fillPrice(action,0);
      if(modifiers.includes(id)){
        const on=id==='ring'?enabled('cursor')&&prefs().rings!==false:enabled(id);
        action.textContent=on?'Turn off':'Turn on';action.disabled=id==='ring'&&!enabled('cursor');
      }else{
        const selected=(category==='Cursors'?skin:tier)===id;
        action.toggleAttribute('data-worn',selected);
        action.textContent=selected?'Equipped':'Equip';action.disabled=selected;
      }
    }
  }
  function select(id){
    if(engine.paused||engine.stopped)return;
    if(skinPrices[id]&&!has(id)){
      const cost=skinPrices[id];
      if(!has('cursor')||!skinOpen(id)||engine.run.fuzz<cost)return;
      if(engine.spend(cost,'cursor_skin')!==cost)return;
      (prefs().skins??={})[id]=true;
      status.textContent=names[id]+' purchased.';
      celebrate(id);
    }
    const buying=Boolean(BY_ID[id]&&!has(id));
    if(buying){
      const result=engine.buy(id);
      if(!result.ok){status.textContent=result.line||'Purchase unavailable.';refresh();return;}
      status.textContent=BY_ID[id].name+' purchased.';
      celebrate(id);
    }
    if(category==='Trails'){
      if(id!=='none')for(const parent of trails.slice(0,trails.indexOf(id)+1))if(has(parent)&&!enabled(parent))engine.switches.set(parent,true);
      prefs().trail=id;
    }else if(category==='Cursors'&&!modifiers.includes(id)){
      if(id!=='system'&&!enabled(id))engine.switches.set(id,true);
      prefs().skin=id;
    }else if(id==='ring')prefs().rings=prefs().rings===false;
    else if(!buying)engine.switches.set(id,!enabled(id));
    engine.emit('cursor-style');engine.save();refresh();
  }
  function celebrate(key){
    const action=dialog&&doc.querySelector('.cursor-kit [data-cursor-item="'+key+'"]');
    const row=action?.closest('.cursor-kit-row');
    if(!row)return;
    const still=doc.body.classList.contains('gentle')||doc.defaultView.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    let stamp=row.querySelector('.bshop-stamp');
    if(!stamp){stamp=doc.createElement('span');stamp.className='bshop-stamp';stamp.textContent='OWNED';row.style.position='relative';row.append(stamp);}
    const print=row.querySelector('.cursor-kit-print');
    if(still){stamp.animate([{opacity:0},{opacity:1}],{duration:400});return;}
    stamp.animate([{transform:'rotate(-9deg) scale(2.4)',opacity:0},{transform:'rotate(-9deg) scale(.9)',opacity:1,offset:.7},{transform:'rotate(-9deg) scale(1)'}],{duration:360,easing:'ease-in'});
    print?.animate([{scale:'1'},{scale:'1.07'},{scale:'1'}],{duration:340,easing:'ease-out'});
  }
  function equipStage(family,stage){
    if(engine.paused||engine.stopped||!has('trail')||!famOpen(family.id))return;
    const kit=prefs(),owned=kit.stages?.[family.id]||0;
    if(stage>owned){
      const cost=stagePrices[stage-1];
      if(stage!==owned+1||engine.run.fuzz<cost||engine.spend(cost,'cursor_trail')!==cost)return;
      (kit.stages??={})[family.id]=stage;
      status.textContent=stage===1?family.name+' unlocked.':family.levels[stage-1]+' purchased.';
      celebrate(family.id+'-'+stage);
    }
    if(!enabled('trail'))engine.switches.set('trail',true);
    kit.trail=family.id;kit.stage=stage;
    engine.emit('cursor-style');engine.save();refresh();
  }
  function open(options){
    if(options?.category==='Words'||options?.category==='Trails')category=options.category;
    if(dialog||engine.paused||engine.stopped)return;
    function make(tag, className, text) {
      const node=doc.createElement(tag);
      if(className)node.className=className;
      if(text)node.textContent=text;
      return node;
    }
    const el=make('div','cursor-kit'),tabs=make('nav','cursor-kit-tabs');
    tabs.setAttribute('aria-label','Cursor shop sections');
    const controls=make('div','cursor-kit-controls'), list=make('div','cursor-kit-list');
    const footer=make('div','cursor-kit-footer');
    status=make('small','cursor-kit-status');status.setAttribute('role','status');
    balance=make('span','cursor-kit-balance');balance.setAttribute('aria-label','Available '+BRAND.currency);
    const families=make('div','cursor-families');
    families.setAttribute('role','tablist');families.setAttribute('aria-label','Trail families');
    const panelId='cursor-family-upgrades';
    familyId=trailCollection.find(f=>f.id===prefs().trail)?.id||'';
    familyTags=[];
    const choices=[{id:'',name:'Stardust'},...trailCollection];
    for(const {id,name} of choices){
      const tag=make('button','cursor-family-tag');tag.type='button';tag.dataset.family=id;
      const sample=id ? id+'-3' : 'trail-1';
      tag.style.setProperty('--family-art','url("assets/shop/trails/'+sample+'.webp")');
      tag.id='cursor-family-'+(id||'stardust');tag.setAttribute('role','tab');
      tag.setAttribute('aria-controls',panelId);
      const svgNS='http://www.w3.org/2000/svg';
      const lock=doc.createElementNS(svgNS,'svg'),shape=doc.createElementNS(svgNS,'path');
      lock.setAttribute('class','cursor-family-lock');lock.setAttribute('width','11');lock.setAttribute('height','13');
      lock.setAttribute('viewBox','0 0 12 14');lock.setAttribute('aria-hidden','true');lock.setAttribute('focusable','false');
      shape.setAttribute('d','M3.2 6V4a2.8 2.8 0 0 1 5.6 0v2M2 6h8v6.5H2zM6 8.4v1.9');
      shape.setAttribute('fill','none');shape.setAttribute('stroke','currentColor');
      shape.setAttribute('stroke-width','1.3');shape.setAttribute('stroke-linejoin','round');lock.append(shape);
      tag.append(lock,make('span','cursor-family-name',name));
      tag.onclick=()=>{if(familyId!==id){familyId=id;populate();}};
      families.append(tag);familyTags.push({id,label:name,tag,lock});
    }
    families.onkeydown=e=>{
      const index=familyTags.findIndex(entry=>entry.tag===e.target);
      if(index<0)return;
      const count=familyTags.length;
      const next={ArrowLeft:(index+count-1)%count,ArrowRight:(index+1)%count,Home:0,End:count-1}[e.key];
      if(next===undefined)return;
      e.preventDefault();familyTags[next].tag.focus();familyTags[next].tag.click();
    };
    const wordLabel=make('label','cursor-kit-words');wordToggle=make('input');wordToggle.type='checkbox';
    wordToggle.onchange=()=>{
      if(engine.paused||engine.stopped)return;
      prefs().words=wordToggle.checked;engine.emit('cursor-style');engine.save();refresh();
    };
    wordLabel.append(wordToggle,doc.createTextNode(' Following words'));controls.append(families,wordLabel);
    removeTrail=make('button','cursor-kit-remove','Remove trail');removeTrail.type='button';
    removeTrail.dataset.cursorItem='none';removeTrail.onclick=()=>select('none');
    footer.append(balance,status,removeTrail);

    function addProduct(parent,id,name,description,image,stage,onClick){
      const row=make('div','cursor-kit-row'),picture=make('img','cursor-kit-print');
      picture.src=image;picture.alt='';picture.draggable=false;
      const copy=make('div','cursor-kit-copy'),label=make('strong','',name),detail=make('small','',description);
      const action=make('button','cursor-kit-action');action.type='button';
      action.dataset.cursorItem=stage?id+'-'+stage:id;action.onclick=onClick;
      row.setAttribute('role','group');row.setAttribute('aria-label',name);
      copy.append(label,detail);row.append(picture,copy,action);parent.append(row);
      rows.push({id,action,detail,stage});
    }
    function addCursor(parent,id){
      addProduct(parent,id,names[id]||BY_ID[id].name,hints[id],'assets/shop/cursors/'+id+'.png',0,()=>select(id));
    }
    function marks(){
      for(const tab of tabs.children)if(fresh.some(k=>SHOP_GATES[k].tab===tab.dataset.tab))newMark(tab);
      if(fresh.includes('nova'))newMark(list.querySelector('[data-cursor-item="cursor_nova"]')?.closest('.cursor-kit-row'));
      if(fresh.includes('families'))for(const {id,tag} of familyTags)if(id)newMark(tag);
    }
    function populate(){
      editor?.close();editor=null;
      list.replaceChildren();rows=[];status.textContent='';
      for(const tab of tabs.children)tab.setAttribute('aria-pressed',String(tab.dataset.tab===category));
      controls.hidden=removeTrail.hidden=category!=='Trails';
      if(category==='Words'){
        if(!gateOpen(engine.run,'words')){list.append(make('p','shop-gate',gateLine('words')+'.'));marks();refresh();return;}
        editor=wordEditor({engine,ui,host:list,prefs});marks();refresh();return;
      }
      const gallery=make('div','cursor-kit-gallery');
      list.append(gallery);
      if(category==='Cursors'){
        gallery.classList.add('cursor-kit-pointers');
        cursors.forEach(id=>addCursor(gallery,id));
        const more=make('details','cursor-kit-extras'),summary=make('summary','','Cursor modifiers');
        const extras=make('div','cursor-kit-gallery cursor-kit-modifiers');
        modifiers.forEach(id=>addCursor(extras,id));
        more.append(summary,extras);list.append(more);
      }else{
        gallery.id=panelId;gallery.setAttribute('role','tabpanel');
        for(const {id,tag} of familyTags){
          const selected=id===familyId;
          tag.tabIndex=selected?0:-1;
          tag.setAttribute('aria-selected',String(selected));
          if(selected)gallery.setAttribute('aria-labelledby',tag.id);
        }
        const family=trailCollection.find(f=>f.id===familyId);
        if(family){
          family.levels.forEach((name,i)=>addProduct(gallery,family.id,name,family.notes[i],
            'assets/shop/trails/'+family.id+'-'+(i+1)+'.webp',i+1,()=>equipStage(family,i+1)));
        }else{
          trails.forEach(id=>addProduct(gallery,id,BY_ID[id].name,hints[id],
            'assets/shop/trails/'+id+'-1.webp',0,()=>select(id)));
        }
      }
      marks();refresh();
    }
    for(const name of ['Trails','Cursors','Words']){
      const tab=make('button','',name);tab.type='button';tab.dataset.tab=name;tab.onclick=()=>{category=name;populate();};tabs.append(tab);
    }
    el.append(tabs,controls,list,footer);
    /* Opening the shop meets what is new; the marks stay up for this visit. */
    fresh=markSeen(engine.run,'cursor',seenRecord(engine.run));
    dialog=ui.cards.open({className:'cursor-shop-window',title:'Cursor Shop',node:el,buttons:[{label:'Done'}],
      onClose(){editor?.close();editor=null;dialog=null;removeTrail=null;rows=[];familyTags=[];}});
    populate();
  }
  engine.subscribe('open-cursor-shop',open);
  engine.subscribe('buy',({item})=>{
    if(trails.includes(item.id))prefs().trail=item.id;
    if(shapes[item.id])prefs().skin=item.id;
  });
  engine.subscribe('switch',refresh);
  return refresh;
}
