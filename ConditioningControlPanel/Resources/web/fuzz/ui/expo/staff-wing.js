import {dial,layThread,sight,useCarry,write} from './staff-fixtures.js';

const notes=[
  'Please leave this notice here.','Please leave the previous notice there.','The previous notice has been left there.',
  'Please leave that fact on record.','The record has its own notice.','Please leave room for the reader.',
  'The reader is now part of the notice.'
];
const locations=[[13.9,0,32.91],[16,0,30.99],[19.4,0,33.0],[21.7,0,30.99],[24.6,0,33.0],[27.0,0,30.99],[31.3,0,32.95]];

export function createStaffWing(api,state) {
  const {THREE:T,camera,root}=api,look=sight(api),rooms=new Map();
  const view=state.staffVisit={signs:1,hardHat:false,unplugged:false,delivered:false,staplerHome:false};
  let pivotYaw=null,signCount=1,powerTime=null,blackout=false,fromDesk=false,signedOut=false;
  const dark=root.ownerDocument.createElement('div'),card=root.ownerDocument.createElement('p');
  dark.style.cssText='position:absolute;inset:0;pointer-events:none;z-index:1;background:#071c15;opacity:0;transition:opacity .8s ease';
  card.style.cssText='position:absolute;inset:0;pointer-events:none;z-index:2;display:none;place-items:center;margin:0;background:#0c1614;color:#f1efdf;text-align:center;font:500 clamp(20px,4vw,45px) Georgia,serif';
  card.textContent='Llama Expo 2037 has stopped.';root.append(dark,card);
  const cue=(key,kind='react',options={})=>api.cue('v4_'+key,{kind,exhibit:'staff',...options});
  function plug() {
    if(powerTime!==null)return;
    view.unplugged=!view.unplugged;
    const node=rooms.get('service')?.plug;
    if(node)node.position.z=view.unplugged?.11:0;
    api.setHeaterPower?.(!view.unplugged);
    if(view.unplugged){
      powerTime=0;lowRooms();cue('unplug','beat');
      state.ticket.stamp('Unplugged');state.ticket.book('unplug','Unplugged the heater',true);
    }else{
      dark.style.background='#071c15';dark.style.opacity='0';lowRooms();cue('heater_on');
    }
  }
  function lowRooms(){
    for(const [id,r] of rooms){
      for(const m of r.threads||[]){
        m.emissive?.setHex(view.unplugged?0xecbb9c:0x000000);if(m.emissive)m.emissiveIntensity=view.unplugged?.55:0;
      }
      if(r.light)r.light.visible=view.unplugged&&id==='service';
    }
  }
  function mount(id,group,clean){
    const routes={
      hall:[[1.36,.025,-16],[10.9,.025,-15.3],[10.9,.025,15.3],[1.1,.025,16]],
      atrium:[[1.1,.025,16],[8.65,.025,18.5],[10.93,.025,31.08],[12,.025,31.08]],
      service:[[12,.025,31.08],[28.13,.025,31.08],[28.15,.025,39.95],[29.02,.025,40.14],[30.40,.025,40.14]],
    };
    if(!routes[id])return;
    const r={};rooms.set(id,r);clean.push(()=>rooms.delete(id));
    clean.push(api.annexProps(group,place=>{
      const off=[];
      const bind=(node,label,fn,reach=2.3)=>off.push(api.register(node,label,fn,reach));
      layThread(api,group,place,routes[id],off);
      r.threads=[];
      group.traverse(n=>{if(n.name==='Wool along the skirting')r.threads.push(...[n.material].flat());});
      if(id==='service'){
        r.signs=locations.map((at,i)=>{
          const n=place('annex_sign',at,i%2===0?Math.PI:0),panel=dial(api,n,'sign_face',off);
          write(panel,'Health and safety',notes[i],'B.   /   '+String(i+1).padStart(2,'0'));
          n.visible=i<signCount&&state.carry?.key!=='staff-sign-'+i;
          bind(n,'Carry the notice',()=>{if(useCarry(api,state,'staff-sign-'+i,n,'Bob’s notice'))cue('bob_sign');});
          return n;
        });
        const box=place('annex_crate',[24.45,0,38.51],Math.PI),hat=place('annex_hat',[24.44,.48,38.52],Math.PI);
        hat.visible=!view.hardHat;
        bind(hat,'Wear the hard hat',()=>{view.hardHat=true;hat.visible=false;api.say('Hard hat fitted. The scaffold is open to staff.');});
        bind(box,()=>view.hardHat?'Return the hard hat':'Read the packing note',()=>{
          if(view.hardHat){view.hardHat=false;hat.visible=true;api.say('Hard hat returned.');}else api.say('Spare visitor. Packed flat.');
        });
        place('annex_heater',[25.52,.836,27.17]);
        const socket=place('annex_socket',[25.93,.96,26.92]);r.plug=socket.getObjectByName('heater_plug');
        r.plug.position.z=view.unplugged?.11:0;
        place('annex_notice',[25.31,1.71,26.79]);
        bind(socket,()=>view.unplugged?'Plug the heater back in':'Unplug the heater',plug);
        r.light=new T.PointLight(0x83e6a6,2.8,6.5,2);r.light.position.set(25.48,2.18,28.0);group.add(r.light);
        off.push(()=>{r.light.removeFromParent();r.light.dispose();});
        const roster=place('annex_rota',[21.65,1.70,27.1],-Math.PI/2);
        write(dial(api,roster,'rota_face',off),'Staff rota','Tomorrow: '+state.file.name,'Reading balcony   /   Tea service');
        bind(roster,'Read tomorrow’s rota',()=>api.say('Tomorrow: '+state.file.name+'. Tea to the reading balcony.'));
        r.tray=place('annex_tea',[18.96,.92,25.45]);r.tray.visible=!view.delivered&&state.carry?.key!=='tea';
        bind(r.tray,'Carry the tea to the reading balcony',()=>{if(useCarry(api,state,'tea',r.tray,'Tea tray'))cue('tea');});
        const desk=place('annex_plinth',[19.32,.84,28.1]);
        const stapler=place('annex_stapler',[19.32,.912,28.1]);stapler.visible=view.staplerHome;
        bind(desk,()=>state.carry?.key==='stapler'?'Return Greg’s stapler':'Inspect desk tray',()=>{
          if(api.drop('stapler')){view.staplerHome=true;stapler.visible=true;state.ticket.book('stapler','Returned Greg’s stapler');cue('stapler');}
          else api.say('Stationery. Greg prefers the left.');
        });
        lowRooms();
      }else if(id==='hall'){
        const docket=place('annex_docket',[10.9,4.40,10.54],-Math.PI/2);
        const tray=place('annex_tea',[10.96,4.30,10.18],-Math.PI/2);tray.visible=view.delivered;
        bind(docket,()=>state.carry?.key==='tea'?'Set down the tea':'Read the tea docket',()=>{
          if(api.drop('tea')){view.delivered=true;tray.visible=true;state.ticket.stamp('Delivered');state.ticket.book('tea','Delivered the balcony tea');cue('tea_done','beat');}
          else api.say(view.delivered?'Tea delivered.':'Tea due from the staff canteen.');
        },3);
      }
      return ()=>{for(const f of off.reverse())f();};
    }));
  }
  function enter(id,old){
    if(old==='service'&&state.carry?.key?.startsWith('staff-sign-'))state.ticket.book('off-duty-notice','Carried a staff notice out',true);
    if(old==='service'&&fromDesk&&!view.unplugged&&!signedOut){state.ticket.book('heater-kept','Left the heater connected');signedOut=true;}
    pivotYaw=null;fromDesk=false;
  }
  function tick(dt,id) {
    if(powerTime!==null){
      powerTime+=dt;
      dark.style.opacity=String(Math.min(.97,powerTime/2.5));
      if(powerTime>=2.6&&!blackout){blackout=true;card.style.display='grid';}
      if(powerTime>=8.6){
        powerTime=null;blackout=false;card.style.display='none';dark.style.background='#125e39';dark.style.opacity='.24';
        cue('heater','beat');
      }
    }
    if(id!=='service')return;
    const pos=camera.position;
    if(Math.hypot(pos.x-25.42,pos.z-27.19)<2.8)fromDesk=true;
    const r=rooms.get('service');
    if(!r?.signs||pos.z<30.7||pos.z>33.3||pos.x<12||pos.x>33.5){pivotYaw=null;return;}
    const yaw=camera.rotation.y;
    if(pivotYaw===null){pivotYaw=yaw;return;}
    if(signCount>=notes.length||Math.abs(Math.atan2(Math.sin(yaw-pivotYaw),Math.cos(yaw-pivotYaw)))<1.65)return;
    const next=r.signs[signCount],at=new T.Vector3().fromArray(locations[signCount]);at.y=.98;
    if(look.visible(at))return;
    pivotYaw=yaw;next.visible=true;view.signs=++signCount;
    if(signCount===notes.length)cue('bob_sign');
  }
  return {mount,tick,enter,dispose(){dark.remove();card.remove();api.setHeaterPower?.(true);}};
}
