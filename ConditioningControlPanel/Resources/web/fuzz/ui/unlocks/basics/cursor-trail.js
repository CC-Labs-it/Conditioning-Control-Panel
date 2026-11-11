import {readWordLook,defaultLook} from './word-looks.js';
import {trailCollection} from '../../../data/trail-collection.js';
import {createTrailRenderer} from './trail-renderer.js';
import {gateOpen} from '../../shop-gates.js';

export function mountCursorTrail(ctx) {
  const doc=ctx.layer('fx').ownerDocument, win=doc.defaultView;
  let canvas=null, g=null;
  let look=defaultLook;
  let renderer,renderKey='',dirty=false,width=0,height=0,density=0,calm=false;
  function clear(){
    if(!canvas)return;
    renderer?.clear();dirty=false;g.clearRect(0,0,width,height);canvas.dataset.particles='0';
  }
  function selectTier(){
    const enabled=id=>ctx.has(id)&&ctx.switches.isOn(id);
    // A retained Look can own its upgrade before the usual prerequisites on a new run.
    const baseOn=!ctx.has('trail')||enabled('trail');
    const starsOn=!ctx.has('starburst')||enabled('starburst');
    let tier=null;
    if(baseOn){
      if(starsOn&&enabled('word_comet'))tier='word_comet';
      else if(enabled('starburst'))tier='starburst';
      else if(enabled('trail'))tier='trail';
    }
    const preferred=ctx.state.store.cursorKit?.trail;
    if(preferred==='none')tier=null;
    else if(baseOn && preferred && enabled(preferred) && (preferred!=='word_comet'||starsOn))tier=preferred;
    const prefs=ctx.state.store.cursorKit;
    look=prefs?.wordEditor===true?readWordLook(prefs.wordLook):defaultLook;
    const family=trailCollection.find(item=>item.id===preferred);
    const rank=Math.min(3,Math.floor(prefs?.stages?.[preferred]||0));
    const custom=family&&rank>0&&enabled('trail');
    const effect=custom?family.id:tier;
    const stage=custom?Math.max(1,Math.min(rank,prefs.stage||1)):1;
    const owner=custom?'trail':tier;
    if(owner!==ctx.item.id){
      if(renderer){
        clear();renderer=null;canvas.hidden=true;delete canvas.dataset.cursorTrail;
        canvas.width=canvas.height=1;width=height=density=0;
      }
      return;
    }
    const nextKey=effect+':'+stage;
    if(renderer&&renderKey===nextKey)return;
    if(renderer)clear();
    renderKey=nextKey;
    canvas??=ctx.el('canvas','position:fixed;inset:0;width:100%;height:100%;pointer-events:none;',null,'fx');
    g=canvas.getContext('2d');renderer=createTrailRenderer(effect,stage);
    canvas.hidden=false;canvas.dataset.cursorTrail=effect;canvas.dataset.trailStage=String(stage);canvas.dataset.particles='0';
  }
  function move(e){
    if(!renderer||ctx.paused||ctx.gentle||ctx.reduced||doc.hidden)return;
    renderer.move(e.clientX,e.clientY,ctx.state.clock);dirty=true;
  }
  const leave=()=>renderer?.leave();
  selectTier();
  // Cursor feedback stays at full strength. One steady family keeps each purchase's solo without ambient dimming.
  ctx.stage.register('trail',s=>{if(canvas)canvas.style.opacity=s>=.999?'':s.toFixed(3);});
  ctx.on('buy',selectTier);ctx.on('switch',selectTier);ctx.on('cursor-style',selectTier);
  win.addEventListener('pointermove',move,{passive:true});
  doc.addEventListener('pointerleave',leave);win.addEventListener('blur',leave);
  ctx.frame(()=>{
    if(!renderer)return;
    if(ctx.gentle||ctx.reduced){if(!calm)clear();calm=true;return;}
    calm=false;if(!dirty)return;
    const dpr=Math.min(2,win.devicePixelRatio||1);
    if(width!==win.innerWidth||height!==win.innerHeight||dpr!==density){
      width=win.innerWidth;height=win.innerHeight;density=dpr;
      canvas.width=Math.ceil(width*dpr);canvas.height=Math.ceil(height*dpr);g.setTransform(dpr,0,0,dpr,0,0);
    }
    g.clearRect(0,0,width,height);
    const p=ctx.palette();
    const colors=p.themes.length===1?{a:p.a,b:p.spiral[1]}:p;
    /* Following words belong to Word Comet (owner 2026-11-08); a save that bought the word editor keeps them. */
    const words=gateOpen(ctx.state,'words')&&(ctx.state.store.cursorKit?.words??(ctx.has('word_comet')&&ctx.switches.isOn('word_comet')));
    const visible=renderer.draw(g,ctx.state.clock,colors,words,look);
    canvas.dataset.particles=String(visible);dirty=visible>0;
    // No particles: off the page until the pointer moves again (a full-screen layer is not free on a phone).
    if(canvas.hidden!==!visible)canvas.hidden=!visible;
  });
  ctx.on('pause',leave);ctx.on('suspend',clear);ctx.on('ending',clear);
  ctx.cleanup(()=>{win.removeEventListener('pointermove',move);doc.removeEventListener('pointerleave',leave);win.removeEventListener('blur',leave);clear();});
  return {};
}
