import {galleryVisits} from './gallery-visits.js';
import {createBackOfHouse} from './back-of-house.js';
import {createSpit} from './spit.js';
import {humanHabitat} from './habitat.js';
import { mountSettlement } from './settlement.js';
import { visitorHands } from './carried.js';
import { firstPress } from './first-press.js';
import { narratorAwareness } from './awareness.js';
import { livePanel } from './display.js';
import { createVisit } from './visit.js';
import { createWornHooves, mountHoofLab } from './hoof-lab.js';
import { mountAquariumLife } from './aquarium-life.js';
import { LLAMAS } from '../../data/content/llamas.js';

const hallCues = { hoof:'pa_hall2', habitat:'pa_hall3', twelve:'pa_hall4', shop:'pa_shop_1', atrium:'pa_arrive_1' };
const clamp = n => Math.min(1,Math.max(0,n));

export function createExhibits(api) {
  const {THREE,ctx,camera,register,play,say,go,exit} = api;
  const state = createVisit(ctx), zones = new Map();
  let here = '', hudClock = 0, pending = [], stopShown = -20, returning = false, departureDone=false, disposed=false;
  const cue = api.cue, awareness=narratorAwareness(state,cue);
  const hands=visitorHands(api,state);api.hold=hands.hold;api.drop=hands.drop;
  const gallery=galleryVisits(api,state);
  const staff=createBackOfHouse(api,state),spit=createSpit(api,state);
  const founding=firstPress(api,state),worn=createWornHooves(api,state);
  const habitat=humanHabitat(api,state,founding.habitatPress);
  api.endVisit=async ending=>{
    if(returning||disposed)return;returning=true;state.ending=ending;
    const titles={renewal:'Renewal',conduct:'Acceptance by Conduct',stayed:'Stayed',finger:'Finger',model:'Model Visitor',full:'The Thirteenth'};
    const lines={renewal:'v4_renew',conduct:'v4_conduct',stayed:'v4_stayed_end',finger:'v4_finger_end',model:'v4_model_end',full:'v4_full_end'};
    ctx.store.set('expoEnding',titles[ending]);api.farewell(leave,titles[ending]);
    await cue(lines[ending],{kind:'beat',replay:true});
    if(ending==='conduct'&&!disposed)await cue(state.ticket.stamps.has('Read')?'v4_conduct_read':'v4_conduct_missed',{kind:'beat'});
    departureDone=true;
  };
  function once(id) {
    if (!state.heard.has(id)) cue(id,{kind:'ambient',onStart:()=>state.heard.add(id)});
  }
  function later(id, delay) { pending.push({id, at:hudClock+delay, zone:here}); }

  function mount(id, group) {
    if (zones.has(id)) unmount(id);
    const clean = [], ui = {}, view = {group,clean,ui};
    zones.set(id,view);
    const obj = name => group.getObjectByName(name);
    function bind(name, label, fn, reach=2.3) {
      const node = obj(name);
      if (node) {
        node.userData.expoRest ||= node.position.clone();
        clean.push(register(node,label,fn,reach));
      }
      return node;
    }
    function surface(name, dimensions) {
      const node = obj(name);
      if(!node) return null;
      let face = node.isMesh ? node : null;
      node.traverse(item=>{
        if(item.isMesh && (item.material?.name==='PressReadout' || !face))face=item;
      });
      if (!face) return null;
      const panel=livePanel(THREE,face,dimensions);
      clean.push(()=>panel.dispose()); return panel;
    }
    clean.push(spit.mount(id,group),habitat.mount(id,group));
    gallery.mount(id,group,clean);staff.mount(id,group,bind,surface,clean);founding.mount(id,group,bind,surface,clean);
    if (id==='hoof') {
      ui.lab=mountHoofLab(api,group,state,worn);clean.push(ui.lab.dispose);
    }
    if (id==='twelve') {
      for (const llama of LLAMAS) {
        const found=state.file.herd.includes(llama.id);
        api.setColliderEnabled?.('expo_fixture_log_'+llama.id,found);
        for (const kind of ['statue','portrait','nameplate','log','panel','archive','accession']) {
          const node=obj(kind+'_'+llama.id); if(node)node.visible=found;
        }
        for (const kind of ['cover','scaffold','helmet','redacted']) {
          const node=obj(kind+'_'+llama.id); if(node)node.visible=!found;
        }
        if(!found)bind('redacted_'+llama.id,'Read construction notice',()=>cue('pa_hall4_missing',{replay:true}));
      }
      const doctorFound=state.file.herd.includes('doctor'), alpaca=obj('alpaca_button');
      if(alpaca)alpaca.visible=doctorFound;
      const photo=obj('photo_release');
      ui.photo=photo;ui.aperture=0;
      if(obj('photo_terrace'))obj('photo_terrace').visible=false;
      ui.hatches=[obj('photo_hatch_0'),obj('photo_hatch_1')];
      for(const hatch of ui.hatches)if(hatch)hatch.userData.expoRest=hatch.position.clone();
      if(photo)photo.visible=false;
      if(state.file.herd.length===12)bind('photo_release','Take the group photograph',()=>{
        if(!api.capturePhoto?.())return;
        state.photo=true;play('chime');say('Group photograph filed.');
      });
      bind('chewed_file','Read your file',()=>say(state.file.name+' · '+state.file.taps.toLocaleString()+' presses. The file has been chewed.'));
    }
    if(id==='shop')ui.shop=mountSettlement(api,state,group,bind,surface,clean);
    if(id==='atrium') {
      const readout=surface('turnstile_counter',{width:672,height:672});
      const tally=()=>readout?.draw((g,w,h)=>{
        g.fillStyle='#153e35';g.fillRect(0,0,w,h);g.textAlign='center';
        g.fillStyle='#f3edda';g.font='bold 60px Georgia,serif';g.fillText(state.hoofAdmitted?'Llama visitor':'Human visitor',w/2,125);
        g.font='40px Georgia,serif';g.fillText('no.',w/2,203);
        g.fillStyle='#113229';g.fillRect(62,233,w-124,225);
        g.fillStyle='#d6df9c';g.font='bold 180px Consolas,monospace';g.fillText(state.visitorNumber===2?'2':state.turnstile?'1':'0',w/2,417);
        g.fillStyle='#e4dbc1';g.font='48px Georgia,serif';g.fillText(ui.again?'(again)':'(today)',w/2,563);
      });
      ui.tally=tally;tally();
      ui.count=()=>{
        if(state.turnstile && state.hoofAdmitted===state.hoofWorn)return;
        state.turnstile=true;state.hoofAdmitted=state.hoofWorn;state.ticket.issue();
        ui.turn=0;tally();play('turnstile');
        if(state.hoofWorn)state.ticket.book('hoof-admitted','Admitted as a llama',true);
        cue(state.hoofWorn?'v4_hoof_arrival':'v4_ticket',{kind:'beat',zone:id,exhibit:'turnstile'});
      };
      ui.turnstile=bind('turnstile_arm','Pass the turnstile',ui.count);
      if(ui.turnstile){
        const arm=ui.turnstile,data=arm.userData;
        const bearing=new THREE.Vector3().fromArray(data.pivotThree||[-1.11,.90,35.33]);
        if(data.pivotThree&&arm.getWorldPosition(new THREE.Vector3()).distanceTo(bearing)<.003)ui.pivot=arm;
        else{
          ui.pivot=new THREE.Group();ui.pivot.position.copy(group.worldToLocal(bearing));
          group.add(ui.pivot);ui.pivot.attach(arm);
        }
        const axis=new THREE.Vector3().fromArray(data.axis||[.819152,-.573576,0]).normalize();
        const rest=ui.pivot.quaternion.clone(),delta=new THREE.Quaternion();
        ui.travel=Number.isFinite(data.travelDegrees)?data.travelDegrees:-120;
        ui.setAngle=degrees=>ui.pivot.quaternion.copy(rest).multiply(delta.setFromAxisAngle(axis,THREE.MathUtils.degToRad(degrees)));
        ui.turn=state.turnstile?1:0;
        ui.setAngle(ui.travel*ui.turn);
      }
    }
    if(id==='aquarium'){
      ui.waterLife=mountAquariumLife(THREE,group,ctx.gentle||ctx.reduced);
      clean.push(()=>ui.waterLife.dispose());
    }
    return view;
  }

  function unmount(id) {const view=zones.get(id); if(view){for(const fn of view.clean.reverse())fn?.();zones.delete(id);}}
  function leave(){if(returning&&!disposed){returning=false;exit();}}
  function enter(id) {
    if(id===here)return;
    zones.get(here)?.ui.lab?.leave();
    if(here==='hoof'&&state.hoofWorn&&state.ticket.book('hoof-out','Left wearing the hooves',true))cue('v4_hoof_out',{kind:'beat',zone:id,exhibit:'hooves'});
    worn.light(zones.get(id)?.group);
    zones.get(here)?.ui.shop?.leave();zones.get(id)?.ui.shop?.arrive();zones.get(id)?.ui.tally?.();
    gallery.enter(id,here);staff.enter(id,here);hands.enter(id);here=id;awareness.enter(id);
    if(hallCues[id])once(hallCues[id]);
    if(id==='twelve' && state.file.herd.length===12)later('pa_hall4_full',6);
    if(id==='atrium'&&!state.heard.has('pa_arrive_2'))later('pa_arrive_2',20);
  }
  function tick(dt,id) {
    if(ctx.paused)return;
    worn.tick(dt);spit.tick(dt);habitat.tick(dt,id);
    if(returning&&departureDone){leave();return;}
    gallery.tick(dt,id);staff.tick(dt,id);hands.tick();hudClock+=dt;awareness.tick(dt,id,camera,api.actionSerial());
    if(hudClock>=900)once('pa_closing');
    pending=pending.filter(event=>{
      if(event.zone!==here)return false;
      if(event.at>hudClock)return true;
      once(event.id);
      return false;
    });
    const ui=zones.get(id)?.ui;
    if (!ui)return;
    ui.waterLife?.tick(dt,ctx.gentle||ctx.reduced,gallery.rayApproaches?camera.position:null);
    founding.tick(dt,id);
    if(id==='hall' && Math.hypot(camera.position.x,camera.position.z+9)<5.2)once('pa_hall1');
    if(id==='hoof')ui.lab?.tick(dt);
    if(id==='twelve' && state.file.herd.length===12 && ui.photo){
      if(Math.hypot(camera.position.x,camera.position.z+25.15)<4||ui.aperture>0)
        ui.aperture=clamp(ui.aperture+dt/(ctx.gentle||ctx.reduced?2.8:1.5));
      const t=ui.aperture*ui.aperture*(3-2*ui.aperture);
      ui.hatches.forEach((hatch,i)=>{if(hatch){
        hatch.position.x=hatch.userData.expoRest.x+(i?-1:1)*t*.47;
        hatch.position.y=hatch.userData.expoRest.y-.095*t;
      }});
      ui.photo.visible=ui.aperture>.35;
    }
    ui.shop?.tick(dt);
    if(id==='atrium' && ui.pivot){
      const z=camera.position.z;
      if(Math.abs(camera.position.x)<1.05 && camera.position.y<2.1 && ui.lastZ>=35.33 && z<=35.33)ui.count();
      if(Math.abs(camera.position.x)<1.05 && camera.position.y<2.1 && state.turnstile && ui.lastZ<35.33 && z>=35.33){
        if(state.hoofWorn)ui.count();
        else{state.ticket.book('unarrived','Un-arrived',true);cue('v4_wrong_way',{kind:'react',exhibit:'turnstile'});}
        ui.again=true;ui.tally?.();
      }
      ui.lastZ=z;
      if(state.turnstile&&ui.turn<1){
        ui.turn=clamp(ui.turn+dt/.72);
        if(ui.inspectionAngle==null)ui.setAngle(ui.travel*ui.turn*ui.turn*(3-2*ui.turn));
      }
    }
  }
  const dispose=()=>{staff.dispose();hands.dispose();spit.dispose();worn.dispose();disposed=true;for(const id of [...zones.keys()])unmount(id);pending=[];};
  function setTurnstileAngle(degrees){
    const ui=zones.get('atrium')?.ui;if(!ui?.setAngle)return false;
    ui.inspectionAngle=Number.isFinite(degrees)?degrees:null;
    ui.setAngle(ui.inspectionAngle??ui.travel*ui.turn*ui.turn*(3-2*ui.turn));
    return true;
  }
  return {mount,unmount,enter,tick,dispose,state,hands,spit,endVisit:api.endVisit,setTurnstileAngle};
}
