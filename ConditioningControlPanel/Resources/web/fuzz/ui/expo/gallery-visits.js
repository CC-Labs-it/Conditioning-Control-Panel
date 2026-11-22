import {dial,write} from './staff-fixtures.js';
import {printedPanel} from './display.js';

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
      // The two clear east-wall bays keep the desk and its hanging rail across the aisle from the signal racks.
      const wallYaw=-Math.PI/2;
      const off=[],items=[],hooks=[],records=[];
      const desk=place('annex_corrections_desk',[10.61,0,46.85],wallYaw);
      const printSpacing=.69;
      desk.scale.x=printSpacing/.629; // Match the five brass rests to the framed prints.
      const sign=place('annex_record',[11.077,1.52,desk.position.z],wallYaw);sign.scale.set(1.45,.62,1);
      write(dial(api,sign,'record_face',off),'Corrections Desk','Hang the photographs.','Archive accessions   /   Either face accepted');
      function update(){
        for(let i=0;i<5;i++){
          const key='print-'+i,slot=hung.findIndex(row=>row?.i===i),item=items[i];
          item.visible=state.carry?.key!==key;
          if(slot>=0){
            const hook=hooks[slot],faceIn=hung[slot].back;
            const cordOffset=faceIn ? .041 : .129;
            item.position.set(hook.position.x-cordOffset,hook.position.y-.311,hook.position.z);
            item.rotation.set(0,wallYaw+(faceIn?Math.PI:0),0);
          }else{
            item.position.set(desk.position.x+.195,.845,desk.position.z+(i-2)*printSpacing);
            item.rotation.set(-.12,wallYaw,0,'YXZ');
          }
          const row=hung[i];write(records[i],row?ordinals[row.order]:'Corrections Desk',row?(row.back?'Withheld by the visitor.':photographs[row.i][1]+'. As filed.'):'Empty hook','ACCESSION   '+String(i+1).padStart(2,'0'));
        }
      }
      for(let i=0;i<5;i++){
        const [kind,label]=photographs[i],z=50.59+i*.86,h=place('annex_hook',[11.066,1.8,z],wallYaw);
        hooks.push(h);const plaque=place('annex_record',[11.077,1.035,z],wallYaw);plaque.scale.y=.54;
        records.push(dial(api,plaque,'record_face',off,640,380));
        const item=place('annex_photo_'+kind,[0,0,0]);items.push(item);
        off.push(api.register(item,'Carry '+label,()=>{
          if(!api.hold('print-'+i,item,label)){api.say('The desk can take the print you are carrying.');return;}
          const slot=hung.findIndex(row=>row?.i===i);if(slot>=0)hung[slot]=null;
          api.trailTask('corrections',id);update();
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
      const off=[],plate=place('annex_agenda',[-26,0,103],Math.PI/2);
      const notice=place('annex_sign',[-26.85,0,103.91],1.96);
      notice.scale.set(1.64,1.45,1.30);
      const panel=dial(api,notice,'sign_face',off,960,420),leaves=[];
      plate.traverse(mesh=>{
        if(!mesh.isMesh||mesh.material?.name!=='Print')return;
        const label=printedPanel(T,mesh,{width:640,height:240,emission:.16});
        label.draw((g,w,h)=>{
          g.fillStyle='#184638';g.fillRect(0,0,w,h);g.textAlign='center';
          g.fillStyle='#f3ddb2';g.font='bold 44px Georgia';g.fillText('ATTENDANCE',w/2,69);
          g.fillStyle='#fff4d9';g.font='bold 98px Georgia';g.fillText('Stand here',w/2,180,w-32);
        });
        off.push(()=>label.dispose());
      });
      // A central meeting aisle keeps the route back to the entrance open.
      for(const [x,z,r] of [[-29.4,101.77,-Math.PI/2],[-29.4,104.23,Math.PI/2]]){
        const n=place('annex_forum_leaf',[x,0,z],r);leaves.push([n,r]);
      }
      const block=new T.Mesh(new T.BoxGeometry(.10,.99,2.46));block.name='Meeting gate';block.position.set(-29.4,.495,103);
      api.walker.add('agenda-gate',block,[],id);block.geometry.dispose();block.material.dispose();off.push(()=>api.walker.remove('agenda-gate'));
      forumView={panel,leaves,page:'',open:0};
      const readNotice=()=>api.say(finished?'Attendance recorded. The gate is open.':'Stand still on the green floor disc for 40 seconds to open the gate. Moving restarts the clock.');
      off.push(api.register(plate,'Read the attendance disc',readNotice),api.register(notice,'Read the meeting instructions',readNotice));
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
      clean.push(api.annexProps(group,place=>{const off=[],sign=place('annex_sign',[X2-.60,0,Z1+5*PITCH+.66],Math.PI/6);
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
        if(!queue)api.trailTask('queue',id);state.queueProgress=++queue;
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
      const remaining=Math.max(0,40-Math.floor(meeting)),v=forumView;
      const page=finished?'filed':inside?'countdown-'+remaining:'invitation';
      if(v.page!==page){
        v.page=page;
        const body=finished?'Attendance recorded. The gate is open.':inside?
          'Stand still.\nGate opens in '+remaining+' s.':'Stand still on the green disc for 40 seconds to open the gate.';
        write(v.panel,'Sir Reginald’s meeting',body);
      }
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
