import {dial,write} from './staff-fixtures.js';

const photographs=[
  ['flight','First flight'],['loch','The loch survey'],['summit','The summit'],['woods','The woodland census'],['moon','Lunar arrival']
];
const ordinals=['Founding edition','First correction','Accepted revision','Revised original','Final original'];
const X1=-10.85,X2=-7.12,Z1=7.45,PITCH=.79;
const route=[];
for(let lane=4;lane>=0;lane--){
  const z=Z1+(lane+.5)*PITCH,reverse=lane%2===0;
  for(let i=0;i<5;i++)route.push([reverse?X2-i*(X2-X1)/4:X1+i*(X2-X1)/4,z]);
}

function batch(T,room,items,off){
  const groups=new Map(),inv=room.matrixWorld.clone().invert();
  for(const item of items){item.updateWorldMatrix(true,true);item.traverse(n=>{
    if(!n.isMesh)return;
    if(!groups.has(n.geometry))groups.set(n.geometry,{material:n.material,matrices:[]});
    groups.get(n.geometry).matrices.push(inv.clone().multiply(n.matrixWorld));
  });item.visible=false;}
  for(const [geometry,{material,matrices}] of groups){
    const mesh=new T.InstancedMesh(geometry,material,matrices.length);
    matrices.forEach((m,i)=>mesh.setMatrixAt(i,m));mesh.computeBoundingSphere();room.add(mesh);
    off.push(()=>{mesh.removeFromParent();mesh.dispose();});
  }
}
export function galleryVisits(api,state){
  const {THREE:T,camera}=api, t=state.ticket;
  let hung=Array(5).fill(null),order=0,meeting=0,meetingEngaged=false,finished=false,stationary=0,queue=0,lastRoom='',forumView,rayVisit;
  const last=new T.Vector3(Infinity,0,0),meetingOrigin=new T.Vector3();
  const voice=(key,zone,kind='react')=>api.cue('v4_'+key,{zone,kind,exhibit:zone});
  function mount(id,group,clean){
    if(id==='chronology')clean.push(api.annexProps(group,place=>{
      const off=[],items=[],hooks=[],records=[],desk=place('annex_corrections_desk',[5.38,0,49.5],Math.PI/2);
      const sign=place('annex_record',[5.39,.85,51.38],Math.PI/2);sign.scale.set(.75,.58,1);
      write(dial(api,sign,'record_face',off),'Corrections Desk','Hang the photographs.','Archive accessions   /   Either face accepted');
      function update(){
        for(let i=0;i<5;i++){
          const key='print-'+i,slot=hung.findIndex(row=>row?.i===i),item=items[i];
          item.visible=state.carry?.key!==key;
          if(slot>=0){const h=hooks[slot];item.position.set(h.position.x+.10,h.position.y-.307,h.position.z);
            item.rotation.set(0,Math.PI/2+(hung[slot].back?Math.PI:0),0);}
          else {item.position.set(5.4,.84,49.5+(i-2)*.65);item.rotation.set(-.04,Math.PI/2,0);}
          const row=hung[i];write(records[i],row?ordinals[row.order]:'Corrections Desk',row?(row.back?'Withheld by the visitor.':photographs[row.i][1]+'. As filed.'):'Empty hook','ACCESSION   '+String(i+1).padStart(2,'0'));
        }
      }
      for(let i=0;i<5;i++){
        const [kind,label]=photographs[i],z=53.05+i*.86,h=place('annex_hook',[4.65,1.8,z],Math.PI/2);
        hooks.push(h);const plaque=place('annex_record',[4.80,1.035,z],Math.PI/2);plaque.scale.y=.54;
        records.push(dial(api,plaque,'record_face',off,640,380));
        const item=place('annex_photo_'+kind,[0,0,0]);items.push(item);
        off.push(api.register(item,'Carry '+label,()=>{
          if(!api.hold('print-'+i,item,label)){api.say('The desk can take the print you are carrying.');return;}
          const slot=hung.findIndex(row=>row?.i===i);if(slot>=0)hung[slot]=null;
          voice('correct',id);update();
        }));
        function hang(back){
          const key=state.carry?.key||'';
          if(!key.startsWith('print-')){api.say(hung[i]?'This history is on file.':'Carry a photograph here. The brass caption files it face inward.');return;}
          if(hung[i]){api.say('This hook is occupied.');return;}
          const p=Number(key.slice(6));api.drop(key);hung[i]={i:p,back,order:order++%5};api.play('feed');update();
          if(back){t.book('withheld','Hung a photograph face inward',true);voice('withheld',id);}
          if(hung.every(Boolean)){t.stamp('Corrected');t.book('corrections','Filed the corrected photographs');}
        }
        off.push(api.register(h,'Hang the front out',()=>hang(false),2.6),api.register(plaque,'Hang face to the wall',()=>hang(true),2.6));
      }
      off.push(api.register(desk,'Return the print to the desk',()=>{const key=state.carry?.key;if(key?.startsWith('print-'))api.drop(key);update();}));
      update();return ()=>off.reverse().forEach(f=>f());
    }));
    if(id==='forum')clean.push(api.annexProps(group,place=>{
      const off=[],plate=place('annex_agenda',[-26,0,103]),notice=place('annex_record',[-26.9,.92,103.92]);
      const panel=dial(api,notice,'record_face',off);const leaves=[];
      // A central meeting aisle keeps the route back to the entrance open.
      for(const [x,z,r] of [[-29.4,101.77,-Math.PI/2],[-29.4,104.23,Math.PI/2]]){
        const n=place('annex_forum_leaf',[x,0,z],r);leaves.push([n,r]);
      }
      const block=new T.Mesh(new T.BoxGeometry(.10,.99,2.46));block.name='Meeting gate';block.position.set(-29.4,.495,103);
      api.walker.add('agenda-gate',block,[],id);block.geometry.dispose();block.material.dispose();off.push(()=>api.walker.remove('agenda-gate'));
      forumView={panel,leaves,second:-1,open:0};off.push(api.register(plate,'Read the agenda',()=>api.say('Stand on the brass plate. Movement adjourns the meeting.')));
      return ()=>{forumView=null;off.reverse().forEach(f=>f());};
    }));
    if(id==='hall'){
      clean.push(api.props(group,place=>{
        const off=[],parts=[];
        for(let row=0;row<6;row++){
          for(let col=0;col<5;col++){const x=X1+col*(X2-X1)/4,z=Z1+row*PITCH;
            parts.push(place('rope_post',[x,0,z]));
            if(col===4||((row>0&&row<5)&&(row%2?col===3:col===0)))continue;
            const cord=place('rope_span',[x+(X2-X1)/8,0,z]);cord.scale.x=(X2-X1)/8;parts.push(cord);
          }
        }
        batch(T,group,parts,off);return ()=>off.reverse().forEach(f=>f());
      }));
      clean.push(api.annexProps(group,place=>{const off=[],sign=place('annex_sign',[X2+.42,0,Z1+4.6*PITCH],Math.PI/2);
        write(dial(api,sign,'sign_face',off),'Queue here','Current wait: 0 minutes.','Applied Hoof Sciences');return ()=>off.forEach(f=>f());}));
    }
  }
  function enter(id,old){
    if(old==='forum'&&!finished&&meetingEngaged&&id==='chronology'){t.stamp('Attended');t.book('absent','Absent, agreed anyway',true);voice('absent',id);}
    if(id==='hoof'&&!t.stamps.has('Queued')){if(t.book('left-queue','Skipped the empty queue',true))voice('skipped_queue',id);}
    last.set(Infinity,0,0);stationary=0;queue=0;lastRoom=id;
  }
  function tick(dt,id){
    const p=api.walker.feet;
    if(id==='hall'&&queue<route.length&&!t.stamps.has('Queued')&&p.y<.5){
      const [x,z]=route[queue];if(Math.hypot(x-p.x,z-p.z)<.53){
        if(!queue)voice('queue',id);state.queueProgress=++queue;
        if(queue===route.length){t.stamp('Queued');t.book('queued','Walked the entire empty queue');api.play('turnstile');}
      }else if(queue>0 && (p.x<X1-.7||p.x>X2+.7||p.z<Z1-.5||p.z>Z1+PITCH*5+.5))queue=0;
    }
    if(id==='forum'&&forumView){
      const inside=Math.hypot(p.x+26,p.z-103)<.56;
      state.meetingSeconds=meeting;
      if(inside&&!finished){
        if(!meetingEngaged){meetingEngaged=true;meetingOrigin.copy(p);voice('meeting',id);}
        if(p.distanceToSquared(meetingOrigin)>.00045){if(meeting>1)voice('move_meeting',id);meeting=0;meetingOrigin.copy(p);}
        else meeting+=dt;
        if(meeting>=40){finished=true;t.stamp('Attended');t.book('agenda','Stood through the minutes');api.play('door');}
      }else if(!inside&&!finished&&meeting){meeting=0;voice('move_meeting',id);}
      const second=Math.floor(meeting),v=forumView;
      if(v.second!==second){v.second=second;write(v.panel,'Sir Reginald’s meeting',finished?'Carried. The far gate is open.':inside?'Minutes in session.':'Stand on Agenda.',finished?'Adjourned.':second+' / 40 s');}
      v.open=T.MathUtils.damp(v.open,finished?1:0,3,dt);for(const [n,r] of v.leaves)n.rotation.y=r*(1-v.open);
      api.setColliderEnabled('Meeting gate',!finished);
    }
    if(id==='aquarium'){
      if(p.distanceToSquared(last)>.003){stationary=0;last.copy(p);}else stationary+=dt;
      if(stationary>=20 && !rayVisit){rayVisit=true;t.book('ray','Noticed by a ray',true);voice('ray',id);}
    }
  }
  return {mount,tick,enter,get rayApproaches(){return lastRoom==='aquarium'&&stationary>=20;}};
}
