import { createCabinetStock } from './button-tunnel-stock.js';

// The captured cabinets are actual DOM, including their native video and canvas
// faces. CSS places them in the same camera as the tunnel, without ever sampling
// a remote picture. A paper box behind each print supplies the missing axis.
const UNIT = 90;
const TAU = Math.PI * 2;
const clamp = (x, a = 0, b = 1) => Math.max(a, Math.min(b, x));
const mix = (a, b, k) => a + (b - a) * k;
const smooth = (a, b, x) => { const t = clamp((x-a)/(b-a)); return t*t*(3-2*t); };

export const SURFACE_CSS = `
.bt-prop-stage{position:absolute;inset:0;pointer-events:none;perspective-origin:50% 50%;overflow:hidden}
.bt-prop-camera,.bt-prop{position:absolute;transform-style:preserve-3d;transform-origin:0 0}
.bt-prop-camera{left:50%;top:50%;width:0;height:0}
.bt-prop{left:0;top:0;will-change:transform;pointer-events:none}
.bt-prop-skin,.bt-prop-back,.bt-prop-side{display:block;position:absolute;left:0;top:0;pointer-events:none;
  user-select:none;box-sizing:border-box;backface-visibility:hidden}
.bt-prop-skin{width:100%;height:100%;transform:translateZ(.3px);isolation:isolate}
.bt-prop-back{width:100%;height:100%;border:1px solid #777479;border-radius:4px;backface-visibility:hidden;
  background:repeating-linear-gradient(24deg,#848084 0px,#848084 1px,#888588 1.5px,#8e898a 3px)}
.bt-prop-side{backface-visibility:visible;outline:1px solid #898386}
.bt-prop-edge-x{width:100px;height:100%;transform-origin:0 0;
  background:linear-gradient(to right,#a8a5ab 0%,transparent 7%,transparent 92%,#aea4ac 100%),
    repeating-linear-gradient(91deg,#858387 0px,#8f8b8c 2px,#989596 3px,#949095 5px)}
.bt-prop-edge-y{width:100%;height:100px;transform-origin:0 0;
  background:linear-gradient(to bottom,#a8a5ab 0%,transparent 7%,transparent 92%,#b5abb3 100%),
    repeating-linear-gradient(1deg,#969295 0px,#9c999b 2px,#a5a1a2 3px,#939092 5px)}
.bt-prop-edge-x.bt-prop-far{left:100%}.bt-prop-edge-y.bt-prop-far{top:100%}
`;

function createPiece(doc,camera,surface,i,rect){
  const make=(cls,parent)=>{const e=doc.createElement('div');e.className=cls;parent.append(e);return e;};
  const root=make('bt-prop',camera);
  root.dataset.kind=surface.kind||'';
  root.dataset.owner=surface.owner||'';
  const w=Math.max(1,surface.w),h=Math.max(1,surface.h);
  root.style.width=w+'px';root.style.height=h+'px';
  root.setAttribute('aria-hidden','true');
  const cabinet=createCabinetStock(doc,root,surface);
  const back=cabinet?null:make('bt-prop-back',root);
  const sides=cabinet?[]:[
    make('bt-prop-side bt-prop-edge-x',root),make('bt-prop-side bt-prop-edge-x bt-prop-far',root),
    make('bt-prop-side bt-prop-edge-y',root),make('bt-prop-side bt-prop-edge-y bt-prop-far',root),
  ];
  const face=make('bt-prop-skin',root);
  face.inert=true;
  face.append(surface.node);
  const original=surface.source;
  const anchor=surface.minimized&&surface.anchor?surface.anchor:surface;
  const initialX=(anchor.x+anchor.w*.5-rect.left)/rect.width;
  const initialY=(anchor.y+anchor.h*.5-rect.top)/rect.height;
  const sourceWidth=surface.minimized?Math.min(anchor.w,anchor.h* w/h)*.88:w;
  // Pick alternating sectors, keeping adjacent large prints on opposite sides.
  const theta=i*2.399963+.36;
  const tag=String((surface.owner||'')+' '+(surface.label||'')).toLowerCase();
  const machine=surface.kind==='toy'&&/mini_slots|dispenser|wheel|toy.box|breakout|racing|mini_race/.test(tag);
  const photo=surface.kind==='media',name=surface.kind==='name';
  return {
    root,face,back,sides,cabinet,original,surface,w,h,initialX,initialY,sourceWidth,rectWidth:rect.width,
    theta,i,machine,photo,name,hiddenSource:false,sourceStyle:null,
  };
}

function placeOnRoute(pieces){
  const machineOrder=['mini_slots','dispenser','toy_box','mini_breakout','wheel','mini_race'];
  const cabs=pieces.filter(p=>p.machine).sort((a,b)=>machineOrder.indexOf(a.surface.owner)-machineOrder.indexOf(b.surface.owner));
  const media=pieces.filter(p=>p.photo).slice(0,3),ads=pieces.filter(p=>p.surface.kind==='ad').slice(0,3);
  const signs=pieces.filter(p=>p.name),features=[];
  const mascot=pieces.find(p=>p.surface.kind==='emi');
  if(mascot)signs.push(mascot);
  // Alternate cabinets with other real objects. A dense rack of thumbnails
  // should never steal one of the full-size cabinet passes.
  while(cabs.length||media.length||ads.length||signs.length){
    if(cabs.length)features.push(cabs.shift());
    if(ads.length)features.push(ads.shift());
    if(cabs.length)features.push(cabs.shift());
    if(media.length)features.push(media.shift());
    if(signs.length)features.push(signs.shift());
  }
  if(!features.length)features.push(...pieces.filter(p=>['button','score','toy'].includes(p.surface.kind)).slice(0,5));
  const ranked=new Set(features),fragments=pieces.filter(p=>!ranked.has(p));
  let cabinetSide=1,otherSide=-1;
  for(const [i,p] of features.entries()){
    p.feature=true;
    p.beat=features.length===1?30:10+52*i/(features.length-1);
    p.side=p.machine?cabinetSide:otherSide;
    if(p.machine)cabinetSide*=-1;else otherSide*=-1;
    p.lift=[.66,-.65,.18,-.83,.81,-.26][i%6];
  }
  for(const [i,p] of fragments.entries()){
    p.feature=false;
    p.station=4+66*(i+.5)/Math.max(1,fragments.length);
    p.theta=i*2.399963+.40;
  }
}
function conceal(piece){
  const el=piece.original;
  if(!el?.isConnected||piece.hiddenSource||piece.surface.minimized)return;
  piece.hiddenSource=true;
  piece.sourceStyle=[el.style.getPropertyValue('visibility'),el.style.getPropertyPriority('visibility')];
  el.style.setProperty('visibility','hidden','important');
}

export function createTunnelSurfaces(doc,host,surfaces,rect,lens){
  const stage=doc.createElement('div');stage.className='bt-prop-stage';
  const camera=doc.createElement('div');camera.className='bt-prop-camera';
  stage.append(camera);host.append(stage);
  const records=Array.from(surfaces||[]).filter(r=>r?.node&&r.w>0&&r.h>0);
  const pieces=records.map((r,i)=>createPiece(doc,camera,r,i,rect));
  placeOnRoute(pieces);
  let lastLens=0;
  function paint(s,w,h){
    const focal=h*.5*lens;
    if(focal!==lastLens){stage.style.perspective=focal+'px';lastLens=focal;}
    camera.style.transform='translateZ('+focal+'px) rotateX('+s.pitch+'rad) rotateY('+s.yaw+'rad) '+
      'translate3d('+(-s.x*UNIT)+'px,'+(s.y*UNIT)+'px,'+(s.z*UNIT)+'px)';
    const ratio=w/h;
    const yawC=Math.cos(s.yaw),yawS=Math.sin(s.yaw),pitchC=Math.cos(s.pitch),pitchS=Math.sin(s.pitch);
    for(const p of pieces){
      const {i,theta}=p;
      // The screen dissolves in register. Keep every visible face on the ray
      // through its original pixels, even if the player holds W immediately.
      // At zero ink the same node becomes a wall print at its own distance.
      // Native videos and canvas never have to be cloned or sampled here.
      const stagger=(i*.381966011)%1;
      const releaseAt=3.65+stagger*2.45;
      const releaseEnd=releaseAt+2.60;
      const mounted=p.surface.minimized || s.time>=releaseEnd;
      const fold=mounted?1:0;
      if(!p.surface.minimized&&s.time>.09)conceal(p);
      const nearFade=smooth(releaseAt,releaseEnd,s.time);
      const arrival=p.surface.minimized?3.0+stagger*2.0:releaseEnd;
      const open=mounted?smooth(arrival,arrival+2.2,s.time):smooth(0,.10,s.time)*(1-nearFade);
      const calm=s.calm?0:1;
      const phase=(s.time*(.117+.009*(i%5))+i*.227)%1;
      const stretch=calm*smooth(.19,.40,phase)*(1-smooth(.63,.87,phase));
      const spiral=theta+.64*s.spin*TAU+calm*.12*Math.sin(s.time*.20+i);
      let angle=spiral,x,y,z,stand,hero;
      if(p.feature){
        // The front floats alongside for eight world units. Then it arrests at
        // its station and stretches into one bank, leaving the path open.
        const rel=s.z-p.beat;
        hero=1-smooth(4,12,Math.abs(rel));
        stand=smooth(1,5,rel);
        const carry=clamp((rel+4)/8);
        z=p.beat+2.8+carry*5.3+stretch*.30;
        angle=(p.side>0?0:Math.PI)+p.lift*.49+calm*.30*Math.sin(s.spin*TAU+i*.32);
        const landing=smooth(.18,1,stand);
        const offsetX=p.side*Math.min(3.58,Math.max(1.13,4.8*ratio/lens*.67));
        x=mix(offsetX,Math.cos(angle)*3.86,landing);
        y=mix(p.lift*1.46+calm*.18*Math.sin(i+s.time*.26),Math.sin(angle)*3.86,landing);
      }else{
        z=p.station+stretch*.62;
        x=3.80*Math.cos(angle);y=3.80*Math.sin(angle);
        stand=.81;hero=0;
      }
      const dist=z-s.z;
      const initialZ=4.2+(i%7)*.27;
      const drift=calm*smooth(.2,5.8,s.time);
      const driftX=(Math.sin(theta+s.time*.28)-Math.sin(theta)) * 2.1*drift;
      const driftY=(Math.cos(theta+s.time*.22)-Math.cos(theta)) * 1.8*drift;
      const initialRayX=((p.initialX-.5)*w+driftX)/focal;
      const initialRayY=((.5-p.initialY)*h+driftY)/focal;
      // A little real parallax only while the print fades. The large change in
      // depth happens once that individual print is invisible, never as a fly-in.
      if(p.releaseZ===undefined&&s.time>=releaseAt)p.releaseZ=s.z;
      const slip=clamp((s.z-(p.releaseZ??s.z))*.025,0,initialZ*.025)*nearFade;
      const worldX=mounted?x:s.x+initialRayX*initialZ;
      const worldY=mounted?y:s.y+initialRayY*initialZ;
      const worldZ=mounted?z:s.z+initialZ-slip;
      // Largest machines occupy a substantial part of the view for their own
      // beat. A wide banner or name strip preserves its printed aspect ratio.
      const maxW=p.machine?3.2:p.photo?2.9:p.name?2.85:p.feature?3.0:1.28;
      const maxH=p.machine?2.78:p.photo?2.4:p.feature?2.24:1.08;
      const finalWidth=Math.min(maxW,maxH*p.w/p.h,Math.max(1.12,3.65*ratio))*(p.feature ? .88+.12*hero : 1);
      const initialWidth=(p.sourceWidth/p.rectWidth)*w*initialZ/focal;
      const physWidth=mounted?finalWidth:initialWidth,scale=physWidth*UNIT/p.w;
      const gazeY=.19*Math.atan2(-x,Math.max(1,dist))+.19*Math.sin(spiral+i*.53)+stretch*.12;
      const gazeX=-.095*Math.atan2(y,Math.max(1,dist));
      const turnY=mix(gazeY,Math.atan2(-Math.cos(angle),.33),stand*.92)*fold;
      const turnX=mix(gazeX,-Math.asin(clamp(Math.sin(angle)*.95,-1,1)),stand*.82)*fold;
      const roll=(calm*Math.sin(s.time*.37+i*.92)*.048+stand*.10*Math.sin(angle))*fold;
      const cameraZ=(worldX-s.x)*yawS+(worldZ-s.z)*yawC;
      const viewZ=(worldY-s.y)*pitchS+cameraZ*pitchC;
      const distanceFade=1-fold*smooth(p.feature?13:7,p.feature?24:14,dist);
      const gone=p.surface.node.hidden||distanceFade<.004||s.capture>.77||(mounted&&dist< -14)||viewZ< -Math.max(finalWidth,1);
      if(p.gone!==gone){p.gone=gone;p.root.style.display=gone?'none':'';}
      if(gone)continue;
      p.root.style.transform='translate3d('+(worldX*UNIT)+'px,'+(-worldY*UNIT)+'px,'+(-worldZ*UNIT)+'px) '+
        'rotateY('+turnY+'rad) rotateX('+turnX+'rad) rotateZ('+roll+'rad) scale3d('+scale+','+scale+','+scale+') translate(-50%,-50%)';
      const extrusion=mounted ? .045+stretch*(.47+1.05*s.progress)+stand*.62 : .018*nearFade;
      const thickness=extrusion*UNIT/Math.max(.0001,scale);
      // Set opacity on individual surfaces rather than flattening the 3D box.
      const alpha=open*(1-smooth(.15,.76,s.capture))*distanceFade*(p.feature?1:1-fold*.24);
      const ink=String(alpha);
      if(p.ink!==ink){p.ink=ink;p.face.style.opacity=ink;}
      const stock=alpha*(mounted?1:nearFade*.32);
      if(p.cabinet)p.cabinet.pose(thickness,stock);
      else{
        if(p.thickness!==thickness){
          p.thickness=thickness;
          p.back.style.transform='translateZ('+(-thickness-.35)+'px) rotateY(180deg)';
          for(let k=0;k<4;k++)p.sides[k].style.transform=k<2?
            'rotateY(90deg) scaleX('+thickness/100+')':'rotateX(-90deg) scaleY('+thickness/100+')';
        }
        if(p.stock!==stock){
          p.stock=stock;p.back.style.opacity=String(stock);
          for(const side of p.sides)side.style.opacity=String(stock);
        }
      }
    }
  }
  function dispose(){
    for(const p of pieces){
      if(!p.hiddenSource||!p.original.isConnected)continue;
      const [value,priority]=p.sourceStyle;
      if(p.original.style.getPropertyValue('visibility')==='hidden'&&p.original.style.getPropertyPriority('visibility')==='important'){
        if(value)p.original.style.setProperty('visibility',value,priority);
        else p.original.style.removeProperty('visibility');
      }
    }
    stage.remove();
  }
  return {paint,dispose,count:pieces.length};
}
