import { bubbleInk } from './canvas-lettering.js';
import { dealPrintPoses, drawPhotoPrint, flashFlight, PRINT_DELAY, PRINT_LIFE } from './slot-flight.js';
import { loadRewardPictures } from './reward-pictures.js';
import { markPopPicture } from '../../pop-picture.js';
import { createLoomKit } from '../../../../backroom/shared/hypno/loom.js';
import { wordPlan, wordBlock, tunnelLevelAt, ZOOM } from '../../../../backroom/shared/hypno/callout.js';

// The viewport owns each flight. Reel origins are captured once, in normalized coordinates.
export function slotEffects(ctx) {
  const doc = ctx.button.el.ownerDocument;
  const canvas = ctx.el('canvas', 'position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:20;', null, ctx.layer('fx'));
  canvas.dataset.slotEffects = '';
  // Idle, the canvas leaves the page: each of these is a full-screen layer the GPU would blend every frame (ui/perf.js).
  canvas.hidden = true;
  markPopPicture(canvas);
  const g = canvas.getContext('2d'), loom = createLoomKit(), tile = doc.createElement('canvas');
  tile.width = 512; tile.height = 342;
  let active = null, width = 0, height = 0;
  const clamp = n => Math.max(0, Math.min(1, n));
  function clear() {
    const previous=active;
    active=null;
    previous?.controller?.abort();
    g.clearRect(0,0,width,height);
    canvas.hidden=true;
  }
  function size() {
    const r=canvas.getBoundingClientRect();
    if(width!==r.width || height!==r.height){
      width=r.width; height=r.height;
      canvas.width=Math.max(1,Math.round(width)); canvas.height=Math.max(1,Math.round(height));
    }
    return r;
  }
  function start(kind,scene,options,word) {
    clear();
    canvas.hidden=false;
    if(kind!=='picture'){
      active={kind,at:ctx.state.clock,plan:wordPlan(word,{chain:['LET GO','SINK'],seed:Math.floor(ctx.state.clock)})};
      return;
    }
    const r=size();
    const origins=[0,1,2].map(i=>{
      const p=scene?.portrait(i);
      return p && width && height
        ? {x:(p.x-p.w/2-r.left)/width,y:(p.y-p.h/2-r.top)/height,w:p.w/width,h:p.h/height}
        : {x:.48,y:.48,w:.04,h:.04};
    });
    const burst={kind,origins,poses:dealPrintPoses(),controller:new AbortController(),photos:null,at:ctx.state.clock};
    active=burst;
    function ready(rows) {
      if(active!==burst || burst.controller.signal.aborted)return;
      const source=ctx.pictures.source;
      const noPhotos=source==='none' || (source==='own' && !ctx.pictures.ownCount);
      if(!rows.length && noPhotos && options?.fallback) rows=[{image:options.fallback,credit:null}];
      if(!rows.length){clear();return;}
      burst.photos=rows;
      burst.at=ctx.state.clock;
    }
    loadRewardPictures(ctx,options?.preferred,burst.controller.signal).then(ready,()=>ready([]));
  }
  function draw() {
    if(!active)return;
    size();g.clearRect(0,0,width,height);
    const age=ctx.state.clock-active.at, calm=ctx.gentle||ctx.reduced;
    if(active.kind==='picture'){
      if(!active.photos){
        if(age>10000)clear();
        return;
      }
      if(age>=PRINT_LIFE+(active.photos.length-1)*PRINT_DELAY)return clear();
      active.photos.forEach((photo,i)=>{
        const n=active.origins[i], im=photo.image;
        const origin={x:n.x*width,y:n.y*height,w:n.w*width,h:n.h*height};
        const frame=flashFlight(origin,width,height,(im.naturalWidth||im.width)/(im.naturalHeight||im.height),
          age-i*PRINT_DELAY,calm,active.poses[i]);
        drawPhotoPrint(g,frame,photo);
      });
    } else if (age>4500) {
      clear();
    } else if (active.kind === 'spiral') {
      loom.setStill(calm); loom.paint(tile,'screen',{now:calm?0:ctx.state.clock});
      g.globalAlpha=clamp(age/1250)*clamp((4500-age)/500)*.7;g.drawImage(tile,0,0,width,height);g.globalAlpha=1;
    } else {
      g.fillStyle=`rgba(8,4,14,${tunnelLevelAt(active.plan,age)})`;g.fillRect(0,0,width,height);
      const ink = bubbleInk(canvas);
      for (const w of active.plan.words) {
        const t=age-w.onsetMs, rise=calm?500:w.inMs, duration=rise+w.holdMs+w.outMs;
        if(t<0||t>duration)continue;
        const fit=wordBlock(w.shown,{view:{w:width,h:height}}), font=fit.sizeVh*height/100;
        const scale=calm?1:ZOOM.from+(ZOOM.to-ZOOM.from)*clamp(t/ZOOM.zoomMs);
        g.save();g.globalAlpha=clamp(t/rise)*clamp((duration-t)/w.outMs);g.translate(width/2,height/2);g.scale(w.reversed?-scale:scale,scale);
        g.font=`700 ${font}px ${ink?.font || '"Segoe UI",sans-serif'}`;g.textAlign='center';g.textBaseline='middle';
        g.lineWidth=font*(ink ? .075 : .018);g.strokeStyle=ink?.edge || '#e958ce';
        if (ink) g.lineJoin='round';
        const grad=g.createLinearGradient(-font,0,font,0);
        const stops=ink ? [ink.fill,ink.alt] : w.stops;
        stops.forEach((color,i)=>grad.addColorStop(i/Math.max(1,stops.length-1),color));g.fillStyle=grad;
        fit.lines.forEach((line,i)=>{const y=(i-(fit.lines.length-1)/2)*font*1.05;g.strokeText(line,0,y);g.fillText(line,0,y);});g.restore();
      }
    }
  }
  for(const event of ['pause','suspend','ending','pictures'])ctx.on(event,clear);
  ctx.cleanup(()=>{clear();loom.dispose();});
  return {start,draw,clear,get active(){return active!==null;}};
}
