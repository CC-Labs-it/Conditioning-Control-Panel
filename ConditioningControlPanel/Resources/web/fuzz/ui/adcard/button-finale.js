/* The eye itself occupies the missing button. Every other part of the run is
 * an individual object in world space. Only the eye's orientation can change. */
import {captureButtonBoard} from './button-board-capture.js';
import {captureButtonScreen,createButtonSky,loadFinaleStyle} from './button-finale-screen.js';
import {createCabinetStock} from './button-tunnel-stock.js';
import {writeScore} from '../emi-score.js';
import {LITE} from '../perf.js';

export {captureButtonScreen};
export const BUTTON_FINALE_MS = 30000;
const CONTACTS = [4220,6340,8230,9120,11480,14030,14810,16590,18830,20300,21270,23520,25110,26980,28560];
const UNIT = 740; // CSS world pixels per metre.
const clamp = (v, min=0, max=1) => Math.min(max,Math.max(min,v));
const smooth = v => {v=clamp(v);return v*v*(3-2*v);};
const mix = (a,b,k) => a+(b-a)*k;
const TAU = 2*Math.PI;
const wrap = v => Math.atan2(Math.sin(v),Math.cos(v));
const strengthAt = n => .74+.22*(.5+.5*Math.sin(n*2.71+.6));
const cursorUrl=new URL('../../assets/arcade/button-cursor.webp',import.meta.url).href;
const lensControls='#game-controls,#pause,#cards,[data-button-review-controls]';

function pieceKind(p) {
  if (p.kind==='button' || p.kind==='backdrop') return null;
  if (p.kind==='object' && (p.owner==='stray' || p.owner==='e_bounce' || p.source?.matches?.('[data-stray],[data-bouncing-copy]'))) return null;
  if (p.kind==='shop' && p.source?.classList.contains('ghost')) return 'next';
  if (p.kind==='ad' && p.source?.closest?.('#l-banner')) return 'banner';
  return p.kind || 'object';
}

// Angle around the stationary eye, height below it, radial distance and size,
// all physical lengths in metres. The previous page's upper and lower bands
// survive, while the cabinets and pictures continue around both shoulders.
function spot(type,p,i,n) {
  const k=n<=1?.5:i/(n-1), sign=i%2?-1:1, pair=Math.floor(i/2);
  if(type==='score') return [0,-.30,1.13,.32];
  if(type==='name') return [.20,-.385,1.12,.15];
  if(type==='hud') return [mix(-.37,.36,k),-.44-.025*(i%2),1.16,Math.min(.19,Math.max(.10,p.w/1120))];
  if(type==='shop') return [mix(.46,-.46,k),.27+.023*Math.cos(k*Math.PI),1.08,.104];
  if(type==='next') return [mix(.24,-.24,k),.40,1.12,.063];
  if(type==='banner') return [sign*(.51+pair*.85),-.43,.99,.43];
  if(type==='tab') return [.55+i*TAU/n,.51+.04*Math.sin(i),1.04,Math.max(.067,Math.min(.15,p.w/850))];
  if(type==='emi') return [-.31,.095,.99,.18];
  if(type==='media') {
    const az=i<2?(i?.35:-.35):wrap(1.15+(i-2)*2.39996);
    return [az,i<2?-.12:((i%5)-2)*.16,.99+.04*(i%4),.22];
  }
  if(type==='toy') {
    const principal={
      wheel:[-.60,.022,1.03,.32],dispenser:[.60,.005,1.01,.30],mini_slots:[-.97,-.11,1.03,.39],
      toy_box:[1.03,.14,1.15,.30],mini_breakout:[.79,-.28,1.13,.30],mini_race:[-1.22,.18,1.11,.35],
      a_tick:[1.69,.05,1.05,.21],quiz:[-1.65,-.18,1.16,.25],
    };
    return principal[p.owner] || [sign*(1.36+.38*(pair%5)),(i%3-1)*.19,1.10,.22];
  }
  if(type==='ad') return [-.46+pair*.9,.17+.12*(i%2),1.13,.25];
  return [sign*(.82+.35*(pair%7)),(i%3-1)*.23,1.14+.06*(i%3),Math.min(.28,Math.max(.09,p.w/1100))];
}

const heightLimit = {score:.08,name:.047,hud:.065,shop:.113,next:.066,tab:.046,banner:.065,
  toy:.30,media:.24,emi:.136,ad:.17,object:.13};
function layOut(objects,width,height) {
  const portrait=width/height<.78;
  // Footprints are angles as viewed from the eye. This packing remains valid
  // when looking sideways and never puts two planes through the same ray.
  const used=[{az:0,e:0,hw:portrait?.095:.132,hh:.096}];
  const verticalOffsets=[0,-.105,.105,-.21,.21,-.34,.34,-.49,.49,-.66,.66];
  const turns=[0];for(let i=1;i<=32;i++)turns.push(i*.10,-i*.10);
  function reserve(a,e,hw,hh) {
    let best=null, minimum=Infinity;
    for(const da of turns){
      if(Math.abs(da)>minimum)break;
      for(const de of verticalOffsets){
        const cost=Math.abs(da)+Math.abs(de)*1.22;
        if(cost>=minimum)continue;
        const az=wrap(a+da), elev=e+de;
        if(Math.abs(elev)+hh>1.13)continue;
        if(used.some(b=> Math.abs(wrap(az-b.az)) < hw+b.hw+.034 && Math.abs(elev-b.e) < hh+b.hh+.030))continue;
        best={az,e:elev,hw,hh};minimum=cost;
      }
    }
    const result=best||{az:a,e,hw,hh};used.push(result);return result;
  }
  for(const o of objects){
    const {type,p,i,count,slot}=o;
    let [az,y,radius,physicalWidth]=spot(type,p,i,count);
    const machine=type==='toy'&&['wheel','dispenser','mini_slots','toy_box','mini_breakout','mini_race'].includes(p.owner);
    if(machine) {az*=portrait?.64:.92;physicalWidth*=portrait?.70:.92;}
    if(type==='emi'){physicalWidth=.120;az=portrait?-.32:-.36;y=.095;}
    if(type==='media'&&portrait)physicalWidth*=.76;
    if(portrait){
      if(['hud','banner','shop','next','tab','score','name'].includes(type))y*=1.42;
      if(type==='shop'||type==='next')az*=.80;
    }
    const maxHeight=(heightLimit[type]||.15)*(portrait&&machine?.75:1);
    const unitScale=Math.min(physicalWidth/p.w,maxHeight/p.h);
    const trueW=unitScale*p.w,trueH=unitScale*p.h;
    // EMI's little speech and pet controls extend beyond the body rectangle.
    const hw=Math.atan2(trueW*(type==='emi'?.8:.57),radius),hh=Math.atan2(trueH*(type==='emi'?.82:.57),radius);
    const place=reserve(az,Math.atan2(y,radius),hw,hh);
    const tilt=type==='shop'?-8:type==='banner'?6:machine?(i%2?5:-6):0;
    const rollDeg=type==='score'||type==='name'?0:(i%3-1)*1.5;
    const sc=unitScale*UNIT;
    slot.style.transform=`translate3d(${Math.sin(place.az)*radius*UNIT}px,${Math.tan(place.e)*radius*UNIT}px,${-Math.cos(place.az)*radius*UNIT}px) rotateY(${-place.az}rad) rotateX(${tilt}deg) rotateZ(${rollDeg}deg) scale3d(${-sc},${sc},${sc}) translate(-50%,-50%)`;
  }
}

function makePieces(doc,eye,records) {
  const groups=new Map();
  for(const p of records){
    const type=pieceKind(p);
    if(!type||!p.node||p.w<=0||p.h<=0)continue;
    if(!groups.has(type))groups.set(type,[]);
    groups.get(type).push(p);
  }
  let roll=null,count=null;
  const objects=[];
  for(const [type,group] of groups) group.forEach((p,i)=>{
    const slot=doc.createElement('div');
    slot.className='bfn-piece';slot.dataset.kind=type;slot.dataset.owner=p.owner||'';
    slot.setAttribute('aria-hidden','true');slot.inert=true;
    slot.style.width=p.w+'px';slot.style.height=p.h+'px';
    const cabinet=createCabinetStock(doc,slot,p);
    if(cabinet){slot.classList.add('bfn-stock');cabinet.pose(Math.min(49,p.w*.15),1);}
    else if(['shop','media','banner','toy'].includes(type)){
      const back=doc.createElement('div');back.className='bfn-paper-back';slot.append(back);
    }
    const ink=doc.createElement('div');ink.className='bfn-ink';ink.append(p.node);slot.append(ink);
    eye.append(slot);
    objects.push({p,i,type,count:group.length,slot});
    if(type==='score'){
      const paper=p.node.shadowRoot||p.node;
      count=paper.querySelector('[data-button-printed-count]');roll=paper.querySelector('[data-button-printed-roll]');
    }
  });
  const format=new Intl.NumberFormat('en-US');
  const priority={score:0,name:1,emi:3,toy:4,shop:5,media:6,hud:7,banner:8,next:9,ad:10,tab:12};
  const rank=o=>o.type==='toy'&&['wheel','dispenser','mini_slots'].includes(o.p.owner)?2:(priority[o.type]??11);
  objects.sort((a,b)=>rank(a)-rank(b));
  function write(value) {
    const text=format.format(value);
    if(count)writeScore(count,text,!!count.querySelector('[data-et-digit]'));
    if(roll){
      if(roll.children.length!==text.length)roll.textContent=text;
      else Array.from(roll.children).forEach((column,i)=>{if(column.textContent!==text[i])column.textContent=text[i];});
    }
  }
  return {write,layout:(w,h)=>layOut(objects,w,h)};
}

export function createButtonFinale(ctx,mount,snapshot,{onPress,loop=false}={}) {
  const doc=mount.ownerDocument,win=doc.defaultView;
  loadFinaleStyle(doc);
  const shot=snapshot||captureButtonScreen(ctx), own=shot.board?null:captureButtonBoard(ctx);
  const board=shot.board||own;
  function part(parent,name,tag='div'){const el=doc.createElement(tag);el.className='bfn-'+name;parent.append(el);return el;}
  const root=part(mount,'scene');root.dataset.buttonFinale='';root.tabIndex=0;
  root.setAttribute('aria-label','Look around from the position of the button. Drag or use the arrow keys.');
  const sky=createButtonSky(part(root,'sky','canvas'));
  const stage=part(root,'stage'),eye=part(stage,'eye');
  const contents=makePieces(doc,eye,[...board.surfaces,...(shot.mediaSurfaces||[])]);
  contents.write(shot.score.value);
  own?.activate();
  const pointer=part(root,'cursor'), sheet=part(pointer,'cursor-sheet');
  const img=doc.createElement('img');img.src=cursorUrl;img.alt='';img.draggable=false;sheet.append(img);
  const tone=part(sheet,'cursor-tone');
  const light=part(root,'click-light'), contact=part(root,'contact');
  const caption=part(root,'caption','p');caption.textContent='Stay still.';
  const hint=part(root,'look','p');
  hint.textContent=win.matchMedia?.('(pointer:coarse)').matches?'Drag to look around':'Drag or use the arrow keys to look around';
  const bloom=part(root,'bloom');
  let W=1,H=1,focal=1,yaw=0,pitch=0,id=null,prevX=0,prevY=0,looked=false,destroyed=false;
  let lastDraw=-100,lastTime=0,delivered=-1,skyTime=-100,skyYaw=100,skyPitch=100;
  const held=new Set(), controller=new win.AbortController(),options={signal:controller.signal};
  const allowed=()=>!destroyed&&!ctx.paused&&!root.closest('[inert]')&&doc.visibilityState!=='hidden';
  function release(){held.clear();if(id!==null&&root.hasPointerCapture?.(id))root.releasePointerCapture(id);id=null;}
  function resize(){
    const r=root.getBoundingClientRect();W=r.width||win.innerWidth;H=r.height||win.innerHeight;focal=Math.min(H*.96,W*.94);
    stage.style.perspective=focal+'px';sky.resize(W,H);skyTime=-100;lastDraw=-100;
    contents.layout(W,H);
    update(lastTime);
  }
  const ro=win.ResizeObserver?new win.ResizeObserver(resize):null;ro?.observe(root);if(!ro)win.addEventListener('resize',resize,options);resize();
  root.addEventListener('pointerdown',e=>{
    if(!allowed()||id!==null||(e.pointerType==='mouse'&&e.button!==0)||e.target.closest(lensControls))return;
    id=e.pointerId;prevX=e.clientX;prevY=e.clientY;root.setPointerCapture?.(id);root.focus({preventScroll:true});e.preventDefault();
  },options);
  root.addEventListener('pointermove',e=>{
    if(e.pointerId!==id||!allowed())return;
    const dx=e.clientX-prevX,dy=e.clientY-prevY, sensitivity=2.65/Math.max(300,Math.min(W,H));
    if(Math.abs(dx)+Math.abs(dy)>1)looked=true;
    yaw=wrap(yaw-dx*sensitivity);pitch=clamp(pitch+dy*sensitivity,-1.22,1.22);prevX=e.clientX;prevY=e.clientY;
  },options);
  for(const type of ['pointerup','pointercancel','lostpointercapture'])root.addEventListener(type,e=>{if(e.pointerId===id)id=null;},options);
  const arrows=new Set(['ArrowLeft','ArrowRight','ArrowUp','ArrowDown']);
  win.addEventListener('keydown',e=>{
    if(!arrows.has(e.code)||!allowed()||e.altKey||e.ctrlKey||e.metaKey||e.target?.closest?.(lensControls))return;
    held.add(e.code);looked=true;e.preventDefault();
  },options);
  win.addEventListener('keyup',e=>held.delete(e.code),options);win.addEventListener('blur',release,options);
  ctx.on?.('pause',release);ctx.on?.('suspend',release);
  root.focus({preventScroll:true});

  function update(elapsedMs){
    if(destroyed)return;
    const t=loop?Math.max(0,elapsedMs||0):clamp(elapsedMs||0,0,BUTTON_FINALE_MS),dt=clamp((t-lastTime)/1000,0,.12);lastTime=t;
    const calm=!!(ctx.gentle||ctx.reduced), turn=1.18*dt;
    if(held.size&&allowed()){
      yaw=wrap(yaw+turn*(Number(held.has('ArrowRight'))-Number(held.has('ArrowLeft'))));
      pitch=clamp(pitch+turn*(Number(held.has('ArrowUp'))-Number(held.has('ArrowDown'))),-1.22,1.22);
    }
    const cycle=loop?Math.floor(t/BUTTON_FINALE_MS):0, local=t-cycle*BUTTON_FINALE_MS;
    let j=0;while(j<CONTACTS.length&&CONTACTS[j]<=local)j++;
    const n=cycle*CONTACTS.length+j-1;
    const when=n<0?-Infinity:Math.floor(n/CONTACTS.length)*BUTTON_FINALE_MS+CONTACTS[n%CONTACTS.length];
    const nextAt=!loop&&j===CONTACTS.length?Infinity:(j===CONTACTS.length?(cycle+1)*BUTTON_FINALE_MS+CONTACTS[0]:cycle*BUTTON_FINALE_MS+CONTACTS[j]);
    const age=t-when;
    const hit=n>delivered;
    if(hit){
      delivered=n;contents.write(shot.score.value+n+1);
      if(age<260)onPress?.({index:n,strength:strengthAt(n),pan:.12*Math.sin(n*2.11)});
    }
    if(!hit&&t-lastDraw<(LITE?29:13)&&t>=lastDraw)return;lastDraw=t;
    const after=1-smooth(Math.max(0,age-48)/612),before=1-smooth((nextAt-t)/730),press=Math.max(after,before);
    const impact=Number.isFinite(age)&&age<300?Math.exp(-age/82)*strengthAt(n):0;
    const shake=calm?0:impact;
    const sx=shake?shake*1.75*Math.cos(age*.056):0,sy=shake?-shake*2.15*Math.cos(age*.049):0;
    const nod=shake?shake*.0034*Math.cos(age*.044):0;
    eye.style.transform=`translateZ(${focal}px) translate(${sx}px,${sy}px) rotateX(${pitch+nod}rad) rotateY(${yaw}rad)`;
    if(t-skyTime>=(LITE?65:40)||Math.abs(yaw-skyYaw)>.005||Math.abs(pitch-skyPitch)>.005){sky.paint(yaw,pitch,focal,t/1000,calm);skyTime=t;skyYaw=yaw;skyPitch=pitch;}
    root.dataset.look=(yaw.toFixed(3)+','+pitch.toFixed(3));
    // At rest the cursor is a small object beyond the suspended UI. On its
    // approach the distance to the eye falls to 7 cm; the tip meets our POV.
    const s=t/1000,arrival=smooth((t-640)/2400),az=.12+.14*Math.cos(s*.82)+.48*(1-arrival),elev=.08+.11*Math.sin(s*.65);
    const heading=wrap(az-yaw), vertical=elev-pitch;
    const pull=smooth(press), radius=mix(1.61,.072,press);
    const x=W*.5+focal*Math.tan(clamp(heading,-1.35,1.35))*(1-pull);
    const y=H*.5-focal*Math.tan(clamp(vertical,-1.2,1.2))*(1-pull);
    pointer.style.opacity=String(arrival*clamp((1.62-Math.abs(heading)*(1-pull))/.32));
    pointer.style.transform=`translate3d(${x}px,${y}px,0)`;
    // The original 515 px cursor art points from (380.5,463.5) to (64.5,22.5).
    // Hinge at that inked tip, about the perpendicular to the (316,441) shaft.
    // Local +shaft then travels into negative Z. Projection magnifies the
    // approaching tip and independently foreshortens the more distant tail.
    const scale=focal*.111/(515*radius);
    const bank=-16+10*press+5*Math.sin(s*.9)*(1-pull);
    const pitchToEye=mix(12,55,pull);
    sheet.style.transform=`perspective(${focal}px) rotateZ(${bank}deg) rotate3d(-441,316,0,${pitchToEye}deg) scale3d(${scale},${scale},${scale})`;
    tone.style.opacity=String(.12+.76*pull);
    light.style.opacity=String((calm?.35:.46)*impact);
    contact.style.opacity=String((calm?.35:.85)*impact);
    caption.style.opacity=String(smooth((t-2000)/1100)*(.72+.28*(1-smooth((local-11000)/2800)*(1-smooth((local-21500)/3200)))));
    hint.style.opacity=String(looked?0:smooth((t-850)/500)*(loop?1:1-smooth((t-7200)/750)));
    bloom.style.opacity=String(1-smooth(t/1650));
  }
  update(0);
  function destroy(){if(destroyed)return;release();destroyed=true;controller.abort();ro?.disconnect();root.remove();own?.dispose();}
  return {update,destroy};
}
