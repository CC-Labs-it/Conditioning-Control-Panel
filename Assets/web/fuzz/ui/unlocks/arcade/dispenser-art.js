import {createCapsulePalette} from './capsule-palette.js';
import {prizeFor} from '../../../sim/dispenser-rewards.js';
import {amethystEnamel} from '../../dispenser-enamel.js';
// Accepted dispenser composition. One bounded pile, driven only by active game time.
const base = 'assets/arcade/dispenser/';
const clamp = n => Math.max(0, Math.min(1, n));
const ease = n => { n = clamp(n); return n*n*(3-2*n); };
const mix = (a,b,t) => a+(b-a)*t;
export function createDispenserArt(ctx, canvas) {
  const g = canvas.getContext('2d'), doc = canvas.ownerDocument;
  const buffer = doc.createElement('canvas'); buffer.width=328; buffer.height=580;
  const b = buffer.getContext('2d');
  const mask = doc.createElement('canvas'); mask.width=328; mask.height=580;
  const mg = mask.getContext('2d');
  const images = {}, abort = new AbortController();
  let colors;
  let data=null, dead=false, start=null, caught=null, cycle=0, pile=null, plan=null, lastTime=-1, saved=false, lastMode='';
  const names=['dispenser-shell','globe-mask','dispenser-crank','catch-tray','outlet-aperture-mask','outlet-exit-mask','reward-star','sparkle-gold','sparkle-white',...['red','gold','purple'].flatMap(c=>['capsule-'+c,'capsule-'+c+'-upper','capsule-'+c+'-lower'])];
  Promise.all([fetch(base+'motion.json',{signal:abort.signal}).then(r=>{if(!r.ok)throw Error('Dispenser motion missing');return r.json();}),...names.map(name=>new Promise((resolve,reject)=>{const im=new Image(); im.onload=()=>{images[name]=im;resolve();};im.onerror=reject;im.src=base+name+'.webp';}))]).then(([d])=>{if(dead)return;data=d;pile=d.frames[0].map(x=>({...x}));
    const rest=ctx.store.get('artPile');
    if(Array.isArray(rest)&&rest.length===58&&rest.every(p=>Number.isFinite(p.x)&&p.x>=15&&p.x<=145&&Number.isFinite(p.y)&&p.y>=45&&p.y<=155&&Number.isFinite(p.diameter)&&p.diameter>0&&p.diameter<=40&&Number.isFinite(p.angle)&&['red','gold','purple'].includes(p.color))){pile=rest.map(p=>({...p}));cycle=1;}
    colors=createCapsulePalette(d.frames,cycle===1?rest:null);
    // Cache the two enamel parts at twice the output resolution before animating them.
    images['dispenser-shell']=amethystEnamel(images['dispenser-shell'],560);
    images['dispenser-crank']=amethystEnamel(images['dispenser-crank'],314);
    // Cyan uses the same glossy capsule material with a runtime hue variant.
    for(const suffix of ['', '-upper', '-lower']) {
      const im=images['capsule-red'+suffix],c=doc.createElement('canvas');c.width=im.width;c.height=im.height;
      const cg=c.getContext('2d');cg.drawImage(im,0,0);
      const pixels=cg.getImageData(0,0,c.width,c.height),v=pixels.data;
      for(let i=0;i<v.length;i+=4){
        const red=v[i],green=v[i+1],blue=v[i+2];
        if(red>green*1.5&&red>blue*1.15){const low=Math.min(green,blue);v[i]=low;v[i+1]=low+(red-low)*.88;v[i+2]=red;}
      }
      cg.putImageData(pixels,0,0);images['capsule-cyan'+suffix]=c;
    }
    lastTime=-1;}).catch(e=>{if(!dead)console.error('Dispenser artwork unavailable',e);});
  function image(c,name,x,y,w,angle=0,alpha=1,h=null) {
    const im=images[name]; if(!im||w<=0||alpha<=0)return;
    h??=w*im.height/im.width;c.save();c.translate(x,y);c.rotate(-angle*Math.PI/180);c.globalAlpha=alpha;c.drawImage(im,-w/2,-h/2,w,h);c.restore();
  }
  function sample(t) {
    const f=Math.min(78,Math.max(0,t*20)),i=Math.floor(f),u=f-i;
    const first=data.frames[i],second=new Map(data.frames[Math.min(78,i+1)].map(x=>[x.id,x]));
    return first.map(a=>{const z=second.get(a.id)||a;return {...a,x:mix(a.x,z.x,u),y:mix(a.y,z.y,u),angle:mix(a.angle,z.angle,u),opacity:mix(a.opacity??1,z.opacity??1,u)};});
  }
  function nextPlan() {
    const targets=new Map(pile.map(p=>[p.id,{...p}]));
    for(const [a,b] of [[34,35],[30,31]]) {const x=targets.get(a),y=targets.get(b);[x.x,y.x]=[y.x,x.x];}
    // A small upper-row exchange stays in the curved glass; edge capsules stay supported.
    const upper=targets.get(38); upper.x=upper.x<80?84:73;
    plan=pile.map(p=>{const z=targets.get(p.id),dx=z.x-p.x;return {from:{...p},to:z,on:.7+ctx.rng()*.3,end:3.15+ctx.rng()*.45,angle:p.angle-dx/(p.diameter/2)*180/Math.PI*.87};});
    // Rear capsules settle below the front row. Recycle only when covered by that row.
    for(const p of plan.filter(p=>p.from.layer===0&&p.from.x>58&&p.from.x<104)) {
      p.to.y=Math.min(126,p.from.y+7+ctx.rng()*4);
    }
    const refill=plan.find(p=>p.from.id===200);
    if(refill){refill.recycle=true;refill.to.x=80;refill.to.y=100;}
  }
  function begin() {start=ctx.state.clock;caught=null;cycle++;saved=false;if(data&&cycle>1)nextPlan();lastTime=-1;}
  function draw(state) {
    if(!data||dead)return;
    const now=ctx.state.clock,calm=ctx.gentle||ctx.reduced,color=state.prize||'red',reward=prizeFor(color);
    const stamp=[now,state.phase,color,caught,calm].join(':');if(lastTime===stamp)return;lastTime=stamp;
    let t=start==null?0:Math.max(0,(now-start)/1000);
    const mode=[state.phase,calm,start,caught].join(':');
    const moving=(start!=null&&t<3.9)||(caught!=null&&now-caught<1650)||state.phase==='dissolve';
    if(!moving&&lastMode===mode)return;
    lastMode=mode;
    // A restored in-progress dispense resumes at its saved mechanical position.
    if(start==null&&state.phase==='drop'){start=now-(3900-state.left);t=(3900-state.left)/1000;cycle=1;}
    if(start!=null) {
      if(cycle===1)pile=sample(calm?3.9:Math.min(t,3.9));
      else if(plan) {
        pile=plan.map(p=>{const q=calm?1:ease((t-p.on)/(p.end-p.on));
          if(p.recycle&&!calm){
            const cover=plan.find(a=>a.from.id===38),v=ease((t-2.6)/1);
            if(t<2.6)return {...p.from,y:mix(p.from.y,155,ease((t-1.35)/1.2)),opacity:1-ease((t-2.35)/.2)};
            return {...p.from,x:mix(cover.to.x,p.to.x,v),y:mix(cover.to.y,p.to.y,v),opacity:ease((t-2.6)/.18)};
          }
          return {...p.from,x:mix(p.from.x,p.to.x,q),y:mix(p.from.y,p.to.y,q),angle:mix(p.from.angle,p.angle,q)};});
      }
      if(t>=3.9&&!saved){ctx.store.set('artPile',pile.map(p=>({...p,shade:colors.get(p.id)})));saved=true;}
    }
    g.setTransform(2,0,0,2,0,0);g.clearRect(0,0,164,290);
    b.setTransform(2,0,0,2,0,0);b.clearRect(0,0,164,290);
    for(const p of pile){
      image(b,'capsule-'+colors.get(p.id),p.x,p.y,p.diameter,p.angle,p.opacity??1);
    }
    b.globalCompositeOperation='destination-in';b.drawImage(images['globe-mask'],12,0,140,140*1319/849);b.globalCompositeOperation='source-over';
    g.drawImage(buffer,0,0,164,290);
    g.drawImage(images['dispenser-shell'],12,0,140,140*1319/849);
    const idx=Math.min(78,Math.floor(t*20)),action=data.actionFrames[idx];
    image(g,'dispenser-crank',12+140*.49469965,140*1319/849*.74374526,78.4,calm?0:(start==null?0:action.crankAngle));
    const tray=images['catch-tray'];g.drawImage(tray,38,221.7161,88,88*tray.height/tray.width);
    const age=caught==null?null:(now-caught)/1000;
    const visible=['drop','catch','dissolve'].includes(state.phase);
    if(age!=null&&age<1.6) {
      const q=calm?1:ease(age/.65),d=data.extracted.diameter;
      image(g,'capsule-'+color+'-upper',82-21*q,206-d/4+9*q-10*Math.sin(Math.PI*q),d,calm?0:-23*q,1-clamp((age-1.1)/.5));
      image(g,'capsule-'+color+'-lower',82+17*q,206+d/4+2*q,d,calm?0:8*q,1-clamp((age-1.1)/.5));
      image(g,'reward-star',82,206-10*q,5+19*q,0,q*(1-clamp((age-1.1)/.5)));
      if(!calm)for(let i=0;i<(color==='gold'?36:24);i++){
        const p=(age-.12-(i%4)*.09)/.9;if(p<=0||p>=1)continue;
        const a=i*2.39996,r=10+(color==='purple'?1-ease(p):ease(p))*(25+i%3*5);
        g.save();g.translate(82+Math.cos(a)*r,198+Math.sin(a)*r*.65);g.rotate(a+p*3);g.globalAlpha=1-p;g.fillStyle=i%3?reward.color:reward.accent;
        if(i%2){g.fillRect(-2,-1,4,2);}else{g.fillRect(-.7,-4,1.4,8);g.fillRect(-4,-.7,8,1.4);}g.restore();
      }
    } else if(visible&&(t>=2.7||calm||state.phase!=='drop')) {
      const p=calm||state.phase!=='drop'?{x:82,y:206,angle:0,sillClearance:1}:{...action.prize,y:170+(action.prize.y-170)*36/65,angle:action.prize.angle*36/65};
      b.clearRect(0,0,164,290);image(b,'capsule-'+color,p.x,p.y,data.extracted.diameter,calm?0:p.angle,state.phase==='dissolve'?state.left/650:1);
      if(t<3.9&&!calm&&state.phase==='drop'){
        b.globalCompositeOperation='destination-in';
        // Blend nested masks while crossing the lower sill, leaving the top rim opaque.
        mg.clearRect(0,0,328,580);mg.globalCompositeOperation='source-over';mg.globalAlpha=1-p.sillClearance;mg.drawImage(images['outlet-aperture-mask'],0,0);
        mg.globalCompositeOperation='lighter';mg.globalAlpha=p.sillClearance;mg.drawImage(images['outlet-exit-mask'],0,0);mg.globalAlpha=1;
        b.drawImage(mask,0,0,164,290);b.globalCompositeOperation='source-over';
      }
      g.drawImage(buffer,0,0,164,290);
    }
    canvas.dataset.capsules=String(pile.length);canvas.dataset.prize=color;
  }
  ctx.cleanup(()=>{dead=true;abort.abort();});
  return {get ready(){return !!data;},start:begin,catch(){caught=ctx.state.clock;lastTime=-1;},draw};
}
