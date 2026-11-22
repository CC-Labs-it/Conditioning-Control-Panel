import { visitorHands } from './carried.js';
import { firstPress } from './first-press.js';
import { narratorAwareness } from './awareness.js';
import { livePanel, placard, runMonitor, terminal } from './display.js';
import { createVisit } from './visit.js';
import { createWornHooves, mountHoofLab } from './hoof-lab.js';
import { createDoorGlass } from './door-glass.js';
import {mountShopDoor} from './shop-door.js';
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
  const founding=firstPress(api,state),worn=createWornHooves(api,state);
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
    founding.mount(id,group,bind,surface,clean);
    if (id==='hoof') {
      ui.lab=mountHoofLab(api,group,state,worn);clean.push(ui.lab.dispose);
    }
    if (id==='habitat') {
      const display=surface('habitat_display'), clauses=surface('habitat_plaque');
      let clause=state.file.clauses[0]||'No accepted clauses on file.';
      placard(clauses,'Accepted clauses   /   Visitor record',clause);
      ui.refresh=()=>runMonitor(display,{name:state.file.name,presses:state.file.taps+state.taps,
        yes:state.file.total+state.feeds,pressed:ui.keyHeld,
        time:new Date(Math.max(0,(ctx.state?.clock||0)+state.habitatTime*1000)).toISOString().slice(11,19)});
      bind('habitat_glass','Tap the glass',()=>{ui.keyHeld=true;ui.releaseAt=hudClock+.19;play('glass');cue(state.glass());ui.refresh();},2.6);
      bind('feeding_slot','Feed one Yes',()=>{state.feeds++;play('feed');cue('keep_5');ui.refresh();});
      bind('habitat_plaque','Read next accepted clause',()=>{clause=state.nextClause();placard(clauses,'Accepted clauses   /   Visitor record',clause);if(!state.heard.has('keep_6'))once('keep_6');else say(clause);});
      ui.refresh();
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
      if(doctorFound)bind('alpaca_button','Is he an alpaca?',()=>{
        if(ui.alpacaReturnAt!=null)return;
        play('chime');ui.alpacaReturnAt=hudClock+.32;
      });
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
    if (id==='shop') {
      const till=surface('till_display',{emission:.36});
      ui.refresh=()=>{
        const available=state.contract && state.loops>=2;
        api.setColliderEnabled?.('shop_shelves_before',state.loops===0);
        api.setColliderEnabled?.('shop_shelves_after',state.loops>0);
        terminal(till,{title:available?'Exit (sold separately)':'Gift shop',rows:[
          state.contract?'Your contract: collected':'Framed contract       1 Yes',
          available?'Contract holder price   0 Yes':'Balance               '+(state.contract?'0':'1')+' Yes'
        ],foot:'Visitor Services   /   Till 01',eyebrow:'VISITOR SERVICES   /   POINT OF SALE'});
        if(ui.exitSale)ui.exitSale.visible=available;
        api.setButtonAvailable?.('exit_sale',available);
        if(ui.portal)ui.portal.visible=!!(state.boughtExit||ui.shopTransit)&&ui.swing>.05;
      };
      ui.shelves=['shelf_left','shelf_right'].map(obj);
      for (const shelf of ui.shelves) if(shelf)shelf.userData.expoRest=shelf.position.clone();
      ui.exitSale=obj('exit_sale');ui.portal=obj('shop_wormhole');
      ui.swing=0;ui.exitDoor=mountShopDoor(THREE,group,api);clean.push(()=>ui.exitDoor?.dispose());
      if(ui.portal)ui.portal.position.z-=.235;
      ui.doorGlass=createDoorGlass(THREE,ui.portal);clean.push(()=>ui.doorGlass.dispose());
      ui.home=()=>{
        if(returning||!state.boughtExit)return;
        ui.shopTransit='home';
        returning=true;api.farewell(leave);play('spit');
        cue('pa_leave').then(()=>{departureDone=true;});
      };
      function purchase(){
        if(!state.contract){state.contract=true;play('register');say('Your contract, framed.');}
        else if(state.loops>=2 && !state.boughtExit){state.boughtExit=true;play('door');cue('pa_shop_buy');}
        else say('Please exit through the gift shop.');
        ui.refresh();
      }
      bind('till_display','Use the till',purchase);
      bind('framed_contract','Buy your framed contract',purchase);
      bind('exit_sale','Buy Exit',purchase);
      bind('shop_door',()=>state.boughtExit?'Go home':'Open the exit door',()=>{
        if(ui.shopTransit)return;
        play('door');if(state.boughtExit){ui.home();return;}
        ui.shopTransit='loop';cue(state.shopReturn());ui.refresh();
      },2.6);
      bind('shop_wormhole',()=>state.boughtExit?'Go home':'Returning to the gift shop',ui.home,2.8);ui.refresh();
    }
    if(id==='atrium') {
      const readout=surface('turnstile_counter',{width:672,height:672});
      const tally=()=>readout?.draw((g,w,h)=>{
        g.fillStyle='#153e35';g.fillRect(0,0,w,h);g.textAlign='center';
        g.fillStyle='#f3edda';g.font='bold 60px Georgia,serif';g.fillText(state.hoofAdmitted?'Llama visitor':'Human visitor',w/2,125);
        g.font='40px Georgia,serif';g.fillText('no.',w/2,203);
        g.fillStyle='#113229';g.fillRect(62,233,w-124,225);
        g.fillStyle='#d6df9c';g.font='bold 180px Consolas,monospace';g.fillText(state.turnstile?'1':'0',w/2,417);
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
    here=id;awareness.enter(id);
    if(hallCues[id])once(hallCues[id]);
    if(id==='twelve' && state.file.herd.length===12)later('pa_hall4_full',6);
    if(id==='atrium'&&!state.heard.has('pa_arrive_2'))later('pa_arrive_2',20);
  }
  function tick(dt,id) {
    if(ctx.paused)return;
    worn.tick(dt);
    if(returning&&departureDone){leave();return;}
    hands.tick();hudClock+=dt;awareness.tick(dt,id,camera,api.actionSerial());
    if(hudClock>=900)once('pa_closing');
    pending=pending.filter(event=>{
      if(event.zone!==here)return false;
      if(event.at>hudClock)return true;
      once(event.id);
      return false;
    });
    const ui=zones.get(id)?.ui;
    if (!ui)return;
    if(ui.alpacaReturnAt!=null&&hudClock>=ui.alpacaReturnAt){
      ui.alpacaReturnAt=null;
      void go('atrium',[0,0,36],0).then(()=>cue('pa_alpaca'));
    }
    ui.waterLife?.tick(dt,ctx.gentle||ctx.reduced);
    founding.tick(dt,id);
    if(id==='hall' && Math.hypot(camera.position.x,camera.position.z+9)<5.2)once('pa_hall1');
    if(id==='habitat') {
      const old=Math.floor(state.habitatTime);state.habitatTime+=dt;
      const release=ui.keyHeld && hudClock>=ui.releaseAt;
      if(release)ui.keyHeld=false;
      if(release || Math.floor(state.habitatTime)!==old)ui.refresh();
    }
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
    if(id==='shop'){
      const swingTime=ctx.gentle||ctx.reduced ? .84 : .67;
      const travel=Math.min(dt,1/18);
      ui.swing=clamp(ui.swing+(ui.shopTransit?travel/swingTime:-travel/.92));
      ui.exitDoor?.set(ui.swing);
      if(ui.portal)ui.portal.visible=!!(state.boughtExit||ui.shopTransit)&&ui.swing>.05;
      if(ui.shopTransit)ui.doorGlass.step(dt,ctx.gentle||ctx.reduced);
      if(ui.shopTransit==='loop'&&ui.swing>=.98){
        ui.shopTransit=null;void go('shop',[14.2,0,-5],-Math.PI/2);
      }
      ui.shelves.forEach((node,i)=>{if(node)node.position.z=node.userData.expoRest.z+(i?1:-1)*Math.min(1,state.loops)*1.1;});
      if(state.boughtExit){
        if(Math.abs(camera.position.x-15.03)<.62 && camera.position.z < -13.29)ui.home();
      }
    }
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
  const dispose=()=>{hands.dispose();disposed=true;for(const id of [...zones.keys()])unmount(id);pending=[];};
  function setTurnstileAngle(degrees){
    const ui=zones.get('atrium')?.ui;if(!ui?.setAngle)return false;
    ui.inspectionAngle=Number.isFinite(degrees)?degrees:null;
    ui.setAngle(ui.inspectionAngle??ui.travel*ui.turn*ui.turn*(3-2*ui.turn));
    return true;
  }
  return {mount,unmount,enter,tick,dispose,state,setTurnstileAngle};
}
