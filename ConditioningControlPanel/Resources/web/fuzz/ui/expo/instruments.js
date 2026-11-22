import * as T from 'three';
import {livePanel} from './display.js';
import {createBuildingPlant} from './building-plant.js';
import {paintInstrument} from './plant-ink.js';

const kinds=['greenhouse','grid','terrace','station'];
function firstMesh(node){
  let result;node?.traverse(child=>{if(!result&&child.isMesh)result=child;});return result;
}
function installIndicator(root){
  const pieces=[];
  root?.traverse(node=>{
    if(!node.isMesh)return;
    const original=node.material,materials=[original].flat().map(m=>m.clone());
    node.material=Array.isArray(original)?materials:materials[0];pieces.push({node,original,materials});
  });
  let previous='';
  return {set(state){
    if(state===previous)return;previous=state;
    const tint=state==='amber'?0xd4a153:state==='on'?0x67cbbb:0x53685c;
    for(const p of pieces)for(const m of p.materials){
      m.color?.setHex(tint);m.emissive?.setHex(tint);
      if(m.emissive)m.emissiveIntensity=state==='off' ? .04 : .62;
    }
  },dispose(){for(const p of pieces){p.node.material=p.original;for(const m of p.materials)m.dispose();}}};
}
function label(kind,detail,p){
  if(kind==='greenhouse')return p.time<p.pulseUntil?'Irrigation pulse active':'Run irrigation pulse';
  if(kind==='terrace')return p.time<p.ventTestUntil?'Intake test active':'Test intake louvres';
  return detail?'Show live readings':'Inspect circuit loads';
}
function signature(kind,p,detail){
  if(kind==='greenhouse')return [p.delivery,p.tank.toFixed(1),p.recovered.toFixed(1),Math.max(0,Math.ceil(p.pulseUntil-p.time))].join('/');
  if(detail)return Object.values(p.circuits).join('/');
  if(kind==='grid')return [p.watts,p.roof,p.history.join(','),p.batteryWatts].join('/');
  if(kind==='station')return [(p.batteryWh/1000).toFixed(2),Math.round(100*p.batteryWh/p.batteryCapacityWh),p.batteryWatts,p.circuits.services].join('/');
  return [Math.round(100*p.air),Math.round(165*p.air),p.lamps,p.zone==='catwalk',p.time<p.ventTestUntil].join('/');
}

export function createInstruments({ctx,camera,register,play,meter,lite}){
  const plant=createBuildingPlant(),displays=new Map(),pages=new Map(),inputs={zone:'atrium',projectors:0,voices:0};
  const signalLamps=new Map(),pulseColour=new T.Color(0xccecc5);
  let wasSignalling=false;
  let sinceSample=0,clock=0;
  function paint(d,force=false){
    const p=plant.state,key=signature(d.kind,p,d.detail);
    if(!force&&key===d.key)return;
    d.key=key;d.updates++;
    d.panel.draw((g,w,h)=>paintInstrument(g,w,h,d.kind,p,d.detail));
    const amber=(d.kind==='greenhouse'&&p.time<p.pulseUntil)||(d.kind==='terrace'&&p.time<p.ventTestUntil)||
      ((d.kind==='grid'||d.kind==='station')&&p.batteryWatts>0);
    const on=d.kind==='greenhouse'?p.delivery>0:d.kind==='grid'?p.batteryWatts<=0:true;
    d.indicator.set(amber?'amber':on?'on':'off');
  }
  function mount(zone,group){
    group.updateMatrixWorld(true);
    const lamps=new Map();
    group.traverse(n=>{
      if(n.isMesh)for(const material of [n.material].flat()){
        if(!material?.emissive||lamps.has(material)||!/(lumin|diffus|lamp|emit)/i.test(material.name))continue;
        lamps.set(material,{colour:material.emissive.clone(),intensity:material.emissiveIntensity});
      }
    });
    signalLamps.set(zone,lamps);
    for(const kind of kinds){
      const screen=firstMesh(group.getObjectByName('expo_display_'+kind));
      if(!screen)continue;
      const object=group.getObjectByName('expo_control_'+kind);
      const d={kind,zone,object,key:null,detail:pages.get(kind)||false,updates:0,until:0,undo:[],
        centre:new T.Box3().setFromObject(screen,true).getCenter(new T.Vector3()),
        indicator:installIndicator(group.getObjectByName('expo_indicator_'+kind)),
        panel:livePanel(T,screen,{width:lite?512:768,height:lite?288:432,emission:.14})};
      if(kind==='terrace'){
        d.blades=[];d.open=-1;
        group.traverse(node=>{
          if(!/^expo_louvre_\d+$/.test(node.name))return;
          const axis=new T.Vector3();axis[['x','y','z'].includes(node.userData.axis)?node.userData.axis:'x']=1;
          d.blades.push({node,axis,rest:node.quaternion.clone(),delta:new T.Quaternion(),
            angle:T.MathUtils.degToRad(node.userData.travelDegrees??36),base:node.userData.restOpening||0});
        });
      }
      function use(){
        if((kind==='greenhouse'&&!plant.pulse())||(kind==='terrace'&&!plant.ventTest()))return;
        if(kind==='grid'||kind==='station'){d.detail=!d.detail;pages.set(kind,d.detail);}
        play('press',.34);paint(d,true);
      }
      const prompt=()=>label(kind,d.detail,plant.state);
      d.undo.push(register(object||screen,prompt,use,2.35));
      if(lite&&object)d.undo.push(register(screen,'',use,2.35));
      displays.set(kind,d);paint(d,true);
    }
  }
  function unmount(zone){
    for(const [m,rest] of signalLamps.get(zone)||[]){m.emissive.copy(rest.colour);m.emissiveIntensity=rest.intensity;}
    signalLamps.delete(zone);
    for(const [kind,d] of displays)if(d.zone===zone){
      for(const release of d.undo)release();d.panel.dispose();d.indicator.dispose();displays.delete(kind);
    }
  }
  function enter(zone){
    inputs.zone=zone;meter(inputs);plant.observe(inputs);
    for(const d of displays.values())if(camera.position.distanceToSquared(d.centre)<=144)paint(d);
  }
  function tick(dt,zone){
    clock+=dt;sinceSample+=dt;inputs.zone=zone;
    if(sinceSample>=.25){sinceSample%=.25;meter(inputs);}
    plant.advance(dt,inputs);
    const p=plant.state,signalling=p.time<(p.signalUntil||0);
    if(signalling||wasSignalling){
      const soft=ctx.gentle||ctx.reduced;
      const level=signalling?(.5-.5*Math.cos(p.time*(soft?1.5:3.5))):0;
      for(const lamps of signalLamps.values())for(const [m,rest] of lamps){
        m.emissive.copy(rest.colour).lerp(pulseColour,level*(soft?.10:.18));m.emissiveIntensity=rest.intensity+.24*level;
      }
    }
    wasSignalling=signalling;
    for(const d of displays.values()){
      if(d.blades?.length){
        const calm=ctx.gentle||ctx.reduced;
        const target=calm?(plant.state.time<plant.state.ventTestUntil ? .94 : .60):plant.state.air;
        const next=d.open<0||calm?target:d.open+(target-d.open)*(1-Math.exp(-dt*7));
        if(Math.abs(next-d.open)>.0001){d.open=next;
          for(const b of d.blades)b.node.quaternion.copy(b.rest).multiply(b.delta.setFromAxisAngle(b.axis,(next-b.base)*b.angle));
        }
      }
      if(clock<d.until)continue;
      d.until=clock+(ctx.gentle||ctx.reduced?5:1.5);
      if(camera.position.distanceToSquared(d.centre)<=144)paint(d);
    }
  }
  function debug(){
    const p=plant.state;
    return {time:p.time,zone:p.zone,waterLitres:p.tank+p.bed+p.returnTank,pumpLpm:p.delivery,
      pulseSeconds:Math.max(0,p.pulseUntil-p.time),ventTestSeconds:Math.max(0,p.ventTestUntil-p.time),airOpening:p.air,projectors:p.lamps,voices:p.voices,
      watts:p.watts,roofWatts:p.roof,batteryWatts:p.batteryWatts,reserveWh:p.batteryWh,historySamples:p.samples,
      panels:[...displays.values()].map(d=>({kind:d.kind,at:d.centre.toArray(),detail:d.detail,uploads:d.updates,physicalControl:!!d.object,louvreCount:d.blades?.length||0}))};
  }
  function signal(){
    const p=plant.state;
    p.signalUntil=p.time+8;plant.observe(inputs);
    for(const d of displays.values())paint(d,true);
  }
  return {mount,unmount,enter,tick,debug,signal,dispose(){for(const d of [...displays.values()])unmount(d.zone);}};
}
