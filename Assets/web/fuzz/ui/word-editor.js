import {wordPalettes,wordStyles,wordScales,readWordLook,paintWord,EDITOR_PRICE} from './unlocks/basics/word-looks.js';
import {whole} from '../sim/format.js';
import {paletteOf} from './palette.js';
import {BRAND} from '../data/brand.js';

export function wordEditor({engine,ui,host,prefs}){
  const doc=host.ownerDocument;
  const make=(tag,cls,parent,text)=>{
    const el=doc.createElement(tag);el.className=cls;
    if(text)el.textContent=text;
    parent.append(el);return el;
  };
  const panel=make('div','word-editor',host);
  const sample=make('canvas','word-swatch',panel);
  sample.setAttribute('role','img');sample.setAttribute('aria-label','Live preview of your following word style');
  const opts=readWordLook(prefs().wordLook);
  const buttons=[];
  const theme=()=>{const p=paletteOf(engine.run);return p.themes.length===1?{a:p.a,b:p.spiral[1]}:p;};
  const swatchColors=id=>id==='theme'?theme():wordPalettes[id];
  function choices(label,key,entries){
    const set=make('fieldset','',panel);make('legend','',set,label);
    const list=make('div','word-options word-'+key,set);
    for(const [id,name] of entries){
      const b=make('button','',list);b.type='button';b.dataset.wordChoice=key+'-'+id;
      if(key==='palette'){
        const v=make('i','word-colors',b),colors=swatchColors(id);
        v.style.background='linear-gradient(90deg,'+colors.a+' 50%,'+colors.b+' 50%)';v.setAttribute('aria-hidden','true');
      }
      b.append(doc.createTextNode(name));buttons.push({b,key,id});
      b.onclick=()=>{
        if(engine.paused||engine.stopped)return;
        opts[key]=id;
        if(prefs().wordEditor){prefs().wordLook={...opts};engine.emit('cursor-style');engine.save();}
        refresh();paint();
      };
    }
  }
  choices('Colors','palette',Object.entries(wordPalettes).map(([id,p])=>[id,p.name]));
  choices('Lettering','style',Object.entries(wordStyles));
  choices('Size','size',Object.keys(wordScales).map(id=>[id,id[0].toUpperCase()+id.slice(1)]));
  const footer=make('div','word-editor-footer',panel);
  const toggleLabel=make('label','',footer),toggle=make('input','',toggleLabel);toggle.type='checkbox';
  toggleLabel.append(doc.createTextNode(' Following words'));
  toggle.onchange=()=>{prefs().words=toggle.checked;engine.emit('cursor-style');engine.save();};
  const buy=make('button','word-unlock',footer);buy.type='button';
  buy.onclick=()=>{
    const bag=prefs();
    if(bag.wordEditor||engine.paused||engine.stopped||engine.run.fuzz<EDITOR_PRICE)return;
    if(engine.spend(EDITOR_PRICE,'word_editor')!==EDITOR_PRICE)return;
    bag.wordEditor=true;bag.wordLook={...opts};bag.words=true;
    engine.emit('cursor-style');engine.save();refresh();
  };
  const applied=make('span','word-applied',footer,'Editor unlocked');applied.setAttribute('role','status');
  const g=sample.getContext('2d');
  let raf=0,last=-1000,stopped=false;
  function paint(){
    if(!panel.isConnected||!sample.clientWidth)return;
    const w=sample.clientWidth,h=sample.clientHeight,dpr=Math.min(2,doc.defaultView.devicePixelRatio||1);
    if(sample.width!==Math.round(w*dpr)||sample.height!==Math.round(h*dpr)){sample.width=Math.round(w*dpr);sample.height=Math.round(h*dpr);}
    g.setTransform(dpr,0,0,dpr,0,0);g.clearRect(0,0,w,h);
    const calm=ui.gentle()||ui.reduced(),time=calm?640:engine.run.clock;
    const colors=swatchColors(opts.palette),shift=calm?0:Math.sin(time*.0013)*5;
    for(let i=0;i<3;i++){
      const x=[.2,.50,.80][i]*w+shift,y=h*(.6-.15*Math.sin(i*1.3+time*.0007));
      paintWord(g,{text:['DRIFT','RELAX','SINK'][i],x,y,size:[12,16,13][i]*wordScales[opts.size],slot:i,time,alpha:[.78,1,.88][i],style:opts.style,colors});
    }
  }
  function frame(now){
    if(stopped)return;
    if(!doc.hidden&&!engine.paused&&!engine.suspended&&!engine.stopped&&now-last>33){paint();last=now;}
    raf=doc.defaultView.requestAnimationFrame(frame);
  }
  function refresh(){
    for(const {b,key,id} of buttons)b.setAttribute('aria-pressed',String(opts[key]===id));
    const owned=prefs().wordEditor===true;buy.hidden=owned;applied.hidden=!owned;
    buy.disabled=engine.run.fuzz<EDITOR_PRICE;
    buy.textContent='Unlock editor · '+whole(EDITOR_PRICE)+' '+BRAND.currency;
    toggle.checked=prefs().words??(Boolean(engine.run.owned.word_comet)&&engine.switches.isOn('word_comet'));
  }
  refresh();raf=doc.defaultView.requestAnimationFrame(frame);
  return {refresh,close(){stopped=true;doc.defaultView.cancelAnimationFrame(raf);panel.remove();}};
}
