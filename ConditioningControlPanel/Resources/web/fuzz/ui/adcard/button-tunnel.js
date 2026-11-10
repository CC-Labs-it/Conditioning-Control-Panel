import { createTunnelSurfaces, SURFACE_CSS } from './button-tunnel-surfaces.js';
import { LITE } from '../perf.js';
import { createTunnelRenderer, tunnelLens } from './button-tunnel-render.js';

const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const mix = (a, b, t) => a + (b - a) * t;
const ease = (a, b, x) => { const t = clamp((x - a) / (b - a)); return t * t * (3 - 2 * t); };
const GATE = 70;

const SHEET = `
.bt-passage {position:absolute;inset:0;overflow:hidden;pointer-events:auto;touch-action:none;
  user-select:none;-webkit-user-select:none;outline:none;background:transparent}
.bt-passage[hidden]{display:none!important}
.bt-passage>canvas {display:block;width:100%;height:100%;pointer-events:none}
.bt-passage-hint,.bt-passage-step {position:absolute;z-index:2;font:600 13px/1.25 system-ui,sans-serif;
  color:#fff3f8;letter-spacing:.035em;text-shadow:0 1px 4px #160a20}
.bt-passage-hint {left:50%;bottom:max(18px,env(safe-area-inset-bottom));transform:translateX(-50%);
  margin:0;padding:8px 16px;border-radius:20px;background:#1c1029c9;text-align:center;white-space:nowrap;pointer-events:none}
.bt-passage-step {right:max(18px,env(safe-area-inset-right));bottom:max(28px,env(safe-area-inset-bottom));
  touch-action:none;min-width:83px;min-height:54px;border:1px solid #ffcce2b5;border-radius:24px;
  background:#30172fec;color:#fff4fa;box-shadow:inset 0 0 13px #fca4d139,0 2px 14px #14081780;cursor:pointer}
.bt-passage-step[aria-pressed=true]{background:#633053;border-color:#ffe6f4}
.bt-passage-step[hidden],.bt-passage-hint[hidden]{display:none}
@media(max-width:560px){.bt-passage-hint{bottom:max(92px,env(safe-area-inset-bottom));font-size:12px}}
`;

function loadStyle(doc){
  if(doc.getElementById('button-passage-style'))return;
  const style=doc.createElement('style');style.id='button-passage-style';style.textContent=SHEET+SURFACE_CSS;doc.head.append(style);
}

export function createButtonTunnel(ctx,parent,surfaces=[]){
  const doc=parent.ownerDocument,win=doc.defaultView;
  loadStyle(doc);
  const rect=parent.getBoundingClientRect();

  const host=doc.createElement('div');host.className='bt-passage';host.tabIndex=-1;
  host.setAttribute('role','region');host.setAttribute('aria-label','Button tunnel');
  host.hidden=true;
  parent.append(host);
  const renderer=createTunnelRenderer(doc,host,LITE);
  const stage=createTunnelSurfaces(doc,host,surfaces,rect,tunnelLens);
  const touch=!!win.matchMedia?.('(pointer: coarse)').matches;
  const hint=doc.createElement('p');hint.className='bt-passage-hint';
  hint.textContent=touch?'Hold Walk to move · Drag to look':'W or ↑ to walk · Drag to look';
  const step=doc.createElement('button');step.type='button';step.className='bt-passage-step';step.textContent='↑ Walk';
  step.setAttribute('aria-label','Walk forward');step.setAttribute('aria-pressed','false');
  step.hidden=!touch;
  host.append(hint,step);
  let width=rect.width || win.innerWidth,height=rect.height || win.innerHeight;
  function resize(){const at=host.getBoundingClientRect();width=at.width||win.innerWidth;height=at.height||win.innerHeight;}
  const observer=win.ResizeObserver?new win.ResizeObserver(resize):null;observer?.observe(host);
  const events=new win.AbortController(),options={signal:events.signal};
  win.addEventListener('resize',resize,options);
  const s={time:0,z:0,x:0,y:0,yaw:0,pitch:0,spin:0,entry:0,progress:0,capture:0,calm:!!(ctx.gentle||ctx.reduced)};
  let yaw=0,pitch=0,side=0,startX=0,startY=0,lookId=null,walkId=null,walkKey=false;
  let idle=0,stepPhase=0,speed=0,grip=0,lastPaint=-Infinity,active=false,closed=false,walked=false;
  const keys=new Set();
  const bindings=['KeyW','ArrowUp','KeyA','KeyD','ArrowLeft','ArrowRight','KeyS','ArrowDown'];
  function allowed(){return active&&!closed&&s.capture===0&&!doc.hidden&&!doc.querySelector('#pause:not([hidden])');}
  function reset(){
    keys.clear();walkKey=false;walkId=null;lookId=null;step.setAttribute('aria-pressed','false');
  }
  function keyDown(e){
    if(!allowed()||e.altKey||e.ctrlKey||e.metaKey||!bindings.includes(e.code))return;
    if(e.target!==step && e.target?.closest?.('input,textarea,select,[contenteditable=true],#cards,#game-controls'))return;
    keys.add(e.code);e.preventDefault();
  }
  win.addEventListener('keydown',keyDown,options);
  win.addEventListener('keyup',e=>keys.delete(e.code),options);
  win.addEventListener('blur',reset,options);doc.addEventListener('visibilitychange',reset,options);
  step.addEventListener('pointerdown',e=>{
    if(!allowed()||walkId!==null)return;
    walkId=e.pointerId;step.setPointerCapture?.(e.pointerId);step.setAttribute('aria-pressed','true');e.preventDefault();
  },options);
  function releaseWalk(e){if(walkId===e.pointerId){walkId=null;step.setAttribute('aria-pressed','false');}}
  for(const type of ['pointerup','pointercancel','lostpointercapture'])step.addEventListener(type,releaseWalk,options);
  step.addEventListener('keydown',e=>{if(allowed()&&['Space','Enter'].includes(e.code)){walkKey=true;e.preventDefault();}},options);
  step.addEventListener('keyup',e=>{if(['Space','Enter'].includes(e.code))walkKey=false;},options);
  host.addEventListener('pointerdown',e=>{
    if(!allowed()||e.target===step||lookId!==null||(e.pointerType==='mouse'&&e.button!==0))return;
    lookId=e.pointerId;startX=e.clientX;startY=e.clientY;host.setPointerCapture?.(lookId);
  },options);
  host.addEventListener('pointermove',e=>{
    if(!allowed()||e.pointerId!==lookId)return;
    const ratio=3/Math.max(340,Math.min(width,height));
    yaw-=(e.clientX-startX)*ratio;pitch+=(e.clientY-startY)*ratio;
    yaw=clamp(yaw,-Math.PI,Math.PI);pitch=clamp(pitch,-.92,.92);startX=e.clientX;startY=e.clientY;
  },options);
  function releaseLook(e){if(e.pointerId===lookId)lookId=null;}
  for(const type of ['pointerup','pointercancel','lostpointercapture'])host.addEventListener(type,releaseLook,options);
  ctx.on?.('pause',reset);ctx.on?.('suspend',reset);
  const state={progress:0,capture:0,walking:false,done:false};
  function update(dtSeconds){
    if(closed||state.done)return state;
    const dt=clamp(Number(dtSeconds)||0,0,.25);
    if(!active){active=true;host.hidden=false;resize();host.focus({preventScroll:true});}
    s.calm=!!(ctx.gentle||ctx.reduced);
    s.time+=dt;s.entry=ease(0,3.15,s.time);
    const forward=walkKey||walkId!==null||keys.has('KeyW')||keys.has('ArrowUp');
    const backward=keys.has('KeyS')||keys.has('ArrowDown');
    if(forward)walked=true;
    if(s.z<GATE&&grip===0){
      const strafe=Number(keys.has('KeyD'))-Number(keys.has('KeyA'));
      const turn=Number(keys.has('ArrowRight'))-Number(keys.has('ArrowLeft'));
      yaw=clamp(yaw+turn*dt*1.12,-Math.PI,Math.PI);
      side=clamp(side+strafe*dt*1.45,-1.16,1.16);
      idle=forward||backward?0:idle+dt;
      // A person can stop and look. A never-operated ending eases into an
      // assisted walk so a touchless run still reaches the recap.
      const assistance=walked?ease(4.5,8.5,idle)*1.82:ease(3.5,7,idle)*1.93;
      const intent=forward?3.3:backward?-1.25:assistance;
      speed=mix(speed,intent,1-Math.exp(-dt*4));
      const travel=.05+.95*ease(1.1,6.8,s.time);
      s.z=clamp(s.z+speed*dt*travel,0,GATE);
      stepPhase+=dt*Math.abs(speed)*3.15;
      if(s.time>=54)s.z=Math.min(GATE,s.z+dt*7.5);
      if(s.z>=GATE){grip=.00001;reset();}
    }else{
      grip+=dt;
      s.capture=ease(0,5.6,grip);
      s.z=GATE+7.7*s.capture;
      yaw=mix(yaw,0,1-Math.exp(-dt*1.55));pitch=mix(pitch,0,1-Math.exp(-dt*1.55));
      side=mix(side,0,1-Math.exp(-dt*1.2));
      if(grip>=5.68)state.done=true;
    }
    s.progress=clamp(s.z/GATE);
    const smooth=1-Math.exp(-dt*9.5),motion=s.calm?0:1-s.capture;
    s.yaw=mix(s.yaw,yaw,smooth);s.pitch=mix(s.pitch,pitch,smooth);
    s.x=side+.045*Math.sin(stepPhase*.5)*clamp(Math.abs(speed)/3.3)*motion;
    s.y=.027*Math.sin(stepPhase)*clamp(Math.abs(speed)/3.3)*motion;
    s.spin+=dt*(.009+.035*s.progress*s.progress)*motion*ease(.4,5.3,s.time);
    hint.style.opacity=String(ease(.7,2.0,s.time)*(1-ease(walked?5:10,walked?7:12,s.time)));
    hint.hidden=s.time>12;step.hidden=(!touch && s.time<8)||grip>0||(!touch&&walked);
    state.walking=forward&&grip===0;state.progress=s.progress;state.capture=s.capture;
    if(s.time-lastPaint>=(LITE?1/30:.013)||state.done){
      lastPaint=s.time;
      // Reduced motion holds a still camera through each chapter of the walk.
      // A small pink dissolve masks the cut to the next stationary viewpoint.
      if(s.calm){
        const chapter=Math.min(2,Math.floor(s.progress*3));
        const position=s.z;
        s.z=[0,23,50][chapter];s.x=side;s.y=0;s.yaw=0;s.pitch=0;s.spin=0;
        const fraction=(s.progress*3)%1;
        const dip=ease(.86,1,fraction)*(chapter<2?1:0)+(1-ease(0,.14,fraction))*(chapter>0&&s.progress<1?1:0);
        host.style.backgroundColor='rgba(58,30,63,'+s.entry+')';
        const canvas=host.firstElementChild;canvas.style.opacity=String(1-dip*.9);
        stage.paint(s,width,height);
        renderer.paint(s,width,height);s.z=position;
      }else{
        host.style.backgroundColor='';host.firstElementChild.style.opacity='';
        stage.paint(s,width,height);
        renderer.paint(s,width,height);
      }
    }
    return state;
  }
  function dispose(){
    if(closed)return;closed=true;reset();events.abort();observer?.disconnect();renderer.dispose();stage.dispose();host.remove();
  }
  return {update,dispose};
}
