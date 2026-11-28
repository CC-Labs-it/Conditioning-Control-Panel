import {slotEffects} from './slot-effects.js';
import {prizeFor} from '../../../sim/dispenser-rewards.js';

// Reuse the slot's viewport flights and callouts. Captured origins stay put while the dock moves.
export function dispenserEffects(ctx) {
  const pictures=slotEffects(ctx),words=slotEffects(ctx);
  const canvas=ctx.el('canvas','position:fixed;inset:0;width:100%;height:100%;pointer-events:none;z-index:21;',null,ctx.layer('fx'));
  canvas.dataset.dispenserEffects='';canvas.hidden=true;   // idle: off the page, not a full-screen layer
  const g=canvas.getContext('2d');
  let active=null;
  // A decorative flash remains available when no picture source is selected.
  const fallback=canvas.ownerDocument.createElement('canvas');fallback.width=640;fallback.height=400;
  const fg=fallback.getContext('2d'),grad=fg.createRadialGradient(320,200,8,320,200,380);
  grad.addColorStop(0,'#fff1e4');grad.addColorStop(.18,'#ff7fbb');grad.addColorStop(1,'#481359');fg.fillStyle=grad;fg.fillRect(0,0,640,400);
  for(let i=0;i<36;i++){const a=i*2.39996,r=30+i*7;fg.fillStyle=i%2?'#ffd670':'#fff';fg.beginPath();fg.arc(320+Math.cos(a)*r,200+Math.sin(a)*r*.7,2+i%5,0,Math.PI*2);fg.fill();}
  function clear(){active=null;canvas.dataset.reward='';pictures.clear();words.clear();g.clearRect(0,0,canvas.width,canvas.height);canvas.hidden=true;}
  function start(color,rect){
    clear();canvas.hidden=false;
    const scene={portrait:()=>({x:rect.left+rect.width/2,y:rect.top+rect.height/2,w:rect.width,h:rect.height})};
    active={color,at:ctx.state.clock,x:(rect.left+rect.width/2)/innerWidth,y:(rect.top+rect.height/2)/innerHeight};canvas.dataset.reward=color;
    if(color==='red'||color==='gold')pictures.start('picture',scene,{fallback},'');
    if(color==='cyan'||color==='gold')words.start('words',scene,null,color==='gold'?'JACKPOT':'RELAX');
    if(color==='purple'){pictures.start('spiral',scene,null,'');words.start('words',scene,null,'MIND DRAIN');}
  }
  function draw(){
    if(pictures.active)pictures.draw();
    if(words.active)words.draw();
    if(!active){if(!canvas.hidden){g.clearRect(0,0,canvas.width,canvas.height);canvas.hidden=true;}return;}
    const r=canvas.getBoundingClientRect();if(canvas.width!==r.width||canvas.height!==r.height){canvas.width=Math.max(1,r.width);canvas.height=Math.max(1,r.height);}
    const w=canvas.width,h=canvas.height;g.clearRect(0,0,w,h);if(!active)return;
    const age=(ctx.state.clock-active.at)/1000,calm=ctx.gentle||ctx.reduced,p=prizeFor(active.color);
    if(age>4.5){active=null;canvas.dataset.reward='';return;}
    if(calm)return;
    const count=active.color==='gold'?72:active.color==='purple'?40:32;
    for(let i=0;i<count;i++){
      const t=age-(i%5)*.06;if(t<0||t>2.5)continue;
      const a=i*2.39996,v=65+(i%9)*12;
      let x=active.x*w+Math.cos(a)*v*t,y=active.y*h+Math.sin(a)*v*t+42*t*t;
      if(active.color==='purple'){const radius=(1-t/2.5)*(40+(i%8)*13);x=active.x*w+Math.cos(a+t*3)*radius;y=active.y*h+Math.sin(a+t*3)*radius*.7;}
      g.save();g.translate(x,y);g.rotate(a+t*(i%2?3:-3));g.globalAlpha=Math.min(1,t*7)*(1-t/2.5);g.fillStyle=i%3?p.color:p.accent;
      if(active.color==='cyan'){g.fillRect(-1,-5,2,10);g.fillRect(-5,-1,10,2);}
      else if(i%3===0){g.beginPath();for(let k=0;k<8;k++){const angle=k*Math.PI/4,rr=k%2?2:6;g.lineTo(Math.cos(angle)*rr,Math.sin(angle)*rr);}g.closePath();g.fill();}
      else g.fillRect(-3,-2,6,4);
      g.restore();
    }
  }
  for(const event of ['pictures','suspend','ending'])ctx.on(event,clear);
  ctx.cleanup(clear);
  return {start,draw,clear};
}
