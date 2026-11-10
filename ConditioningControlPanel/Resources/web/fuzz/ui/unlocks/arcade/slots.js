import { slotState, slotStep, beginSpin, finishSpin, COIN_MS } from '../../../sim/arcade.js';
import { sfx } from '../basics/fx.js';
import { slotEffects } from './slot-effects.js';
import { loadReelMedia } from './slot-reel-media.js';
// The same module scene.js imports, so the deal it rotates is the one the reels paint (Loom presets).
import { rotateSpiralDeal } from '../../../../backroom/stations/slot/symbols.js';
import { createReelOverlay } from './slot-reel-overlay.js';
import { autopilot, SLOT_MEAN } from './autopilot.js';  // prestige hook: Autopilot (idle_6)
import { wordsForEverywhere } from '../prestige/trig.js';  // prestige hook: Trigger Words Everywhere (trig_7)

const CSS = `
.mini-slot{position:relative;height:230px;color:var(--ink);font:600 12px/1.3 var(--font,system-ui)}
.mini-slot-cabinet{display:block;width:100%;height:220px;touch-action:none}
.mini-slot-result{position:absolute;left:6px;right:6px;bottom:52px;text-align:center;font-size:11px;line-height:1.25;font-variant-numeric:tabular-nums;text-shadow:0 1px 3px var(--page);pointer-events:none}
.mini-slot-bottom{position:absolute;left:0;right:0;bottom:0;display:flex;align-items:center;justify-content:space-between;gap:8px;padding:0 8px}
.mini-slot-spin{min-height:44px;padding:8px 15px;border:2px solid #2b1e28;border-bottom-width:5px;border-radius:10px;background:#ffcd6e;color:#342234;font:800 14px var(--font,system-ui);cursor:pointer}
.mini-slot-spin:disabled{opacity:.6;cursor:default}.mini-slot-coins{letter-spacing:4px;font-size:20px;color:#bd8623}.mini-slot-next{font-size:10px;letter-spacing:0}
`;
const symbols=['spiral0','sub0','sub1','gif0'];
const MEDIA_SYMBOL=symbols.indexOf('gif0');
// Owner 2026-11-08: the spirals are generated like the real slot. Each reel shows its own Loom field (spiral0-2 are
// different presets) and every pull deals new ones (rotateSpiralDeal), so the reels never repeat one stock spiral.
const reelStrip=i=>symbols.map(id=>id==='spiral0'?`spiral${i}`:id);
const words=['RELAX','DREAM','LET GO','SINK'];
const SUB1=symbols.indexOf('sub1');

export function mountSlots(ctx) {
  ctx.css(CSS);
  let visible=true, scene=null, alive=true, ready=false, art=null, pictureUrl=null, pending=null;
  const effects=slotEffects(ctx);
  const dock=ctx.dock({title:'Mini Slots',edge:'right',width:230,reserveHeight:230,onMin(value){
    visible=!value;
    if(value){effects.clear();holdArt();}else{scene?.resize();if(!art)loadPicture();}
  }});
  visible=dock.dock?.visible!==false;
  const root=ctx.el('section','',null,dock);root.className='mini-slot';root.dataset.arcade='mini_slots';root.dataset.dockSurface='';
  const add=(tag,cls,text,parent=root)=>{const el=ctx.el(tag,'',text,parent);el.className=cls;return el;};
  const canvas=add('canvas','mini-slot-cabinet');canvas.setAttribute('aria-label','Backroom slot cabinet with knight handle');canvas.dataset.dockSurface='';
  const result=add('div','mini-slot-result','Loading cabinet');result.setAttribute('role','status');
  // Media the reel texture cannot take (Scrolller on the website: no CORS) shows over the reel window.
  const overlay=createReelOverlay(ctx,root,result);
  const credit=add('small','mini-slot-credit','via Scrolller');
  credit.style.cssText='position:absolute;right:6px;top:1px;padding:1px 4px;background:#211a32;color:white;font:10px system-ui;pointer-events:none';
  credit.hidden=true;
  const bottom=add('div','mini-slot-bottom'),wallet=add('div','',null,bottom),coins=add('div','mini-slot-coins',null,wallet),next=add('div','mini-slot-next',null,wallet);
  const spin=add('button','mini-slot-spin','Spin',bottom);spin.type='button';
  root.title='Two wins in every six spins. Coins refill every 45 seconds. Drag the casing to move.';
  const s=slotState(ctx.store.get('machine'));ctx.store.set('machine',s);
  let clock=ctx.state.clock,rolling=false,calm=ctx.gentle||ctx.reduced;
  // Trigger Words Everywhere (trig_7): the second word reel shows one of the player's words, a new one each pull.
  // Display only: the reel is painted on the cabinet canvas, so the word is not catchable there.
  const mine=wordsForEverywhere(ctx);let mineAt=Math.floor(ctx.rng()*Math.max(1,mine.length));
  const reelWord=n=>n===1&&mine.length?mine[mineAt%mine.length].toUpperCase():words[n%4];
  const winWord=i=>i===SUB1&&mine.length?mine[mineAt%mine.length].toUpperCase():words[i%4];
  const auto=autopilot(ctx,{dueMs:COIN_MS});  // prestige hook: Autopilot (idle_6), a coin that could not land in a full tray
  const doc=root.ownerDocument;
  const canShow=()=>alive&&visible&&!doc.hidden&&!ctx.paused&&!ctx.state.ended;
  function media(){
    art?.tick(performance.now(),!canShow()||ctx.gentle||ctx.reduced);
    return art?.canvas || null;
  }
  const stops=values=>values.map(v=>v);
  function draw(){
    root.dataset.spinning=String(!!s.pending);
    dock.dock?.setReady(s.coins>0&&!s.pending,`${s.coins} coins ready`);
    coins.textContent='●'.repeat(s.coins)+'○'.repeat(3-s.coins);coins.setAttribute('aria-label',`${s.coins} of 3 coins`);
    next.textContent=s.coins===3?'Tray full':`Next coin ${Math.ceil((COIN_MS-s.elapsed)/1000)}s`;
    spin.disabled=!ready||!!s.pending||!s.coins||ctx.paused;spin.textContent=s.pending?'Spinning':'Spin';
    if(ready)result.textContent=s.result;
  }
  function startVisual(){
    if(!scene||!s.pending)return;
    rolling=true;scene.setLook({gif:media,word:reelWord});scene.answer();
    scene.spin(stops(s.pending.reels));
  }
  function pull(){if(!ready||ctx.paused||!beginSpin(s,ctx.rng))return;auto.reset();mineAt++;rotateSpiralDeal();effects.clear();root.dataset.win='false';startVisual();draw();}
  spin.addEventListener('click',pull);
  const refresh=()=>{scene?.setLook({gif:media,word:reelWord});if(!s.pending)scene?.setStops(stops(s.last));};
  function holdArt(){
    pending?.abort();pending=null;
    art?.tick(performance.now(),true);
  }
  async function loadPicture(){
    if(pending||!canShow()||(s.pending&&art))return;
    const job=new AbortController();
    pending=job;
    try {
      const row=await ctx.pictures.next({kind:'mixed',previousUrl:pictureUrl,allowScrolller:true});
      if(job.signal.aborted||!row||row.url===pictureUrl)return;
      const lease=ctx.pictures.acquire(row);
      let nextArt;
      try {
        nextArt=await loadReelMedia({...row,url:lease.url},job.signal);
        if(nextArt){
          const dispose=nextArt.dispose;
          nextArt.dispose=()=>{try{dispose.call(nextArt);}finally{lease.release();}};
        }
      } finally {if(!nextArt)lease.release();}
      if(!nextArt)return;
      if(job.signal.aborted||!canShow()||(s.pending&&art)){nextArt.dispose();return;}
      overlay.setArt(nextArt.dom?nextArt:null);art?.dispose();art=nextArt;pictureUrl=row.url;
      root.dataset.reelMedia=row.kind;
      credit.hidden=!row.credit;
      if(!s.pending)refresh();
    } catch { /* Unsupported or cancelled media leaves the current reel in place. */ }
    finally {if(pending===job)pending=null;}
  }
  function syncArt(){if(!canShow())holdArt();else if(!art)loadPicture();}
  ctx.on('pictures',()=>{holdArt();overlay.setArt(null);art?.dispose();art=null;pictureUrl=null;delete root.dataset.reelMedia;credit.hidden=true;effects.clear();refresh();loadPicture();});
  ctx.on('pictures-added',()=>{if(!art)loadPicture();});
  ctx.every(12000,loadPicture);
  const stopEffects=()=>{effects.clear();scene?.cancelPull();syncArt();draw();};
  ctx.on('pause',()=>{holdArt();scene?.cancelPull();draw();});ctx.on('resume',()=>{syncArt();draw();});ctx.on('suspend',stopEffects);ctx.on('ending',stopEffects);
  doc.addEventListener('visibilitychange',syncArt);
  ctx.frame(()=>{
    const now=ctx.state.clock,step=Math.max(0,now-clock);slotStep(s,step);clock=now;
    if(auto.step(step,s.coins===3&&!s.pending)){  // prestige hook: Autopilot (idle_6)
      const rates=ctx.rates(),b=canvas.getBoundingClientRect();
      const got=ctx.pay(Math.max(rates.fps*SLOT_MEAN.fps,rates.click*SLOT_MEAN.click)*auto.share,'mini_slots',{x:b.left+b.width/2,y:b.top+b.height/2});
      s.result=`Autopilot +${ctx.fmt(got)} ${ctx.brand.currency}`;
    }
    const nextCalm=ctx.gentle||ctx.reduced;
    if(calm!==nextCalm){calm=nextCalm;effects.clear();media();refresh();scene?.skip();}
    const completed=finishSpin(s);
    if(completed){
      rolling=false;scene?.settle();scene?.setStops(stops(s.last));scene?.reveal(completed.win);root.dataset.win=String(completed.win);
      if(completed.win){
        ctx.emit('sticker', { key: 'slots3' });  // Sticker Book: Three of a Kind
        const rates=ctx.rates(),b=canvas.getBoundingClientRect();
        const got=ctx.pay(Math.max(rates.fps*(completed.big?90:45),rates.click*(completed.big?160:80)),'mini_slots',{x:b.left+b.width/2,y:b.top+b.height/2});
        s.result=`${completed.big?'Spiral sweep':'Lovely little win'} +${ctx.fmt(got)} ${ctx.brand.currency}`;
        sfx(ctx,'sfx/chime1.mp3',.22);
        if(visible&&root.getClientRects().length){
          const photoWin=s.last[0]===3&&ctx.pictures.source!=='none';
          let preferred;
          if(photoWin && art && pictureUrl && root.dataset.reelMedia==='picture'){
            const frame=media()||art.element, still=doc.createElement('canvas');
            const width=frame.naturalWidth||frame.width, height=frame.naturalHeight||frame.height;
            const scale=Math.min(1,900/Math.max(width,height));
            still.width=Math.max(1,Math.round(width*scale));still.height=Math.max(1,Math.round(height*scale));
            still.getContext('2d').drawImage(frame,0,0,still.width,still.height);
            preferred={url:pictureUrl,image:still};
          }
          effects.start(completed.big?'spiral':photoWin?'picture':'words',scene,{preferred},winWord(s.last[0]));
        }
        scene?.marquee('LOVELY LITTLE WIN');
      }else{s.result='No match. The next coin is on its way.';scene?.marquee('NEXT TIME');}
    }
    if(s.pending&&!rolling)startVisual();
    overlay.update({scene,reels:s.last,mediaSymbol:MEDIA_SYMBOL,settled:ready&&!s.pending&&!rolling,show:alive&&visible&&!ctx.state.ended,still:ctx.gentle||ctx.reduced});
    effects.draw();draw();
  });
  const observer=new ResizeObserver(()=>scene?.resize());observer.observe(canvas);
  import('../../../../backroom/stations/slot/scene.js').then(({ createScene }) => createScene({canvas,reduced:false,stillFx:()=>ctx.gentle||ctx.reduced,now:()=>ctx.state.clock,paused:()=>ctx.paused||!visible||!root.getClientRects().length,
    frameWhole:true,caseDraggable:true,onControl:()=>dock.dock?.cancelMove(),handleStyle:0,variant:'rose',onReturn:()=>{},canPull:()=>ready&&!ctx.paused&&!s.pending&&s.coins>0,onLever:pull,onFreeze:()=>{},onReelStop:()=>{}})).then(async loaded=>{
    if(!alive){loaded.dispose();return;}
    scene=loaded;if(scene.missing.length)throw Error('Missing cabinet parts');
    rotateSpiralDeal();scene.setStrips([reelStrip(0),reelStrip(1),reelStrip(2)]);refresh();await scene.rise();
    if(!alive)return;ready=true;root.dataset.cabinetReady='true';root.dataset.knight=String(scene.debug().customHandle);if(s.pending)startVisual();draw();
  }).catch(err=>{if(alive){result.textContent='Cabinet could not load. Reload to try again.';console.error('[mini-slot]',err);}});
  loadPicture();draw();
  ctx.cleanup(()=>{alive=false;holdArt();overlay.dispose();art?.dispose();doc.removeEventListener('visibilitychange',syncArt);observer.disconnect();effects.clear();scene?.dispose();dock.dock?.setReady(false);});
  return {};
}
