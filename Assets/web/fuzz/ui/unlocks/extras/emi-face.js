import {createFace} from '../../../../arcademy/emi/face.js';
import {CHAINS} from '../../../../arcademy/emi/chains.js';
import {frameRect} from '../../perf.js';

export const FACE_CSS = `
@font-face{font-family:'Noto Sans Mono';font-style:normal;font-weight:400 700;font-display:block;src:url('../arcademy/emi/fonts/NotoSansMono-latin.woff2') format('woff2');}
.xe-f{position:absolute;left:34.46%;top:29.46%;width:41.68%;height:37.63%;pointer-events:none;}
.xe-f canvas{display:block;width:100%;height:100%;image-rendering:pixelated;pointer-events:none;}
`;
const moods={happy:'0_0',glee:'(≧◡≦)',annoyed:'¬_¬',rage:'>_<',sleep:'ZzZ',thinking:'0_0'};
export function drawFace(parent,mood){
 const face=parent.ownerDocument.createElement('div');face.className='xe-f';face.dataset.m=mood;parent.appendChild(face);
 const canvas=parent.ownerDocument.createElement('canvas');canvas.setAttribute('aria-hidden','true');face.appendChild(canvas);
 const painter=createFace(canvas);let previous='';
 face.paint=(text,opts={})=>{const key=text+JSON.stringify(opts);if(key===previous)return;previous=key;face.dataset.expression=text;painter.draw(text,opts);};
 face.paint(moods[mood]||moods.happy);return face;
}
export function animateEmiFace(ctx,face,body,visible=()=>true){
 const doc=body.ownerDocument,win=doc.defaultView,canvas=face.firstElementChild;
 let point=null,x=0,y=0,lastClock=ctx.state.clock,lastMood='',at=ctx.state.clock;
 const move=e=>{if(!ctx.paused&&visible()&&e.pointerType!=='touch')point={x:e.clientX,y:e.clientY};};
 const leave=()=>{point=null;};doc.addEventListener('pointermove',move,{passive:true});doc.addEventListener('pointerleave',leave);win.addEventListener('blur',leave);
 // Shown or not comes from a ResizeObserver (0x0 = not rendered), not a per-frame layout read: that read alone forced a
 // full style + layout pass every frame on a busy page.
 let shown=true;const ro=win.ResizeObserver?new win.ResizeObserver(([e])=>{const r=e.contentRect;shown=r.width>0||r.height>0;}):null;
 if(ro){ro.observe(body);ctx.cleanup(()=>ro.disconnect());}
 ctx.frame(()=>{
   const now=ctx.state.clock,dt=Math.max(0,Math.min(100,now-lastClock));lastClock=now;
   if(!visible()||!(ro?shown:body.getClientRects().length))return;
   const mood=face.dataset.m||'happy',calm=ctx.gentle||ctx.reduced;
   if(mood!==lastMood){lastMood=mood;at=now;}
   const chain=CHAINS[{glee:'glee',annoyed:'sus',rage:'rage',thinking:'thinking'}[mood]];
   let text=moods[mood]||moods.happy,opts={};
   if(!calm&&chain){let age=now-at;for(const frame of chain.seq){[text,,opts={}]=frame;if(age<frame[1])break;age-=frame[1];}}
   else if(!calm&&mood==='happy'){
     const age=now-at,t=age%4600;
     text=t>4200&&t<4310?'-_-':t>3300&&t<3560?'^_~':['0_0','^_^','o_o'][Math.floor(age/4600)%3];
   }
   face.paint(text,opts);
   // Desktop scales the 3px cap with body size, retains divisor 60 and eases at .15 per 60Hz frame.
   let tx=0,ty=0;
   if(point&&!calm&&(mood==='happy'||mood==='thinking')){
     const r=frameRect(body),scale=r.width/(body.clientWidth||72),cap=3*r.width/150;
     tx=Math.max(-cap,Math.min(cap,(point.x-r.left-r.width/2)/60))/scale;
     ty=Math.max(-cap,Math.min(cap,(point.y-r.top-r.height/2)/60))/scale;
   }
   const k=calm?1:1-Math.pow(.85,dt/(1000/60));x+=(tx-x)*k;y+=(ty-y)*k;
   if(Math.abs(tx-x)+Math.abs(ty-y)<.02){x=tx;y=ty;}
   const transform=`translate(${x.toFixed(2)}px,${y.toFixed(2)}px)`;
   if(canvas.style.transform!==transform)canvas.style.transform=transform;
 });
 ctx.on('suspend',leave);ctx.on('ending',leave);
 ctx.cleanup(()=>{doc.removeEventListener('pointermove',move);doc.removeEventListener('pointerleave',leave);win.removeEventListener('blur',leave);});
}
