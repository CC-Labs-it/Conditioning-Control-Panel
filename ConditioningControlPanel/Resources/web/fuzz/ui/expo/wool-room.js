import {COUNTERFOILS} from './ticket.js';
import {dial,layThread,sight,write} from './staff-fixtures.js';

const swatches=COUNTERFOILS.flat();
// The brass numbers also reserve space for acts that this visitor has yet to try.
const deviations=[
  'off-duty-notice','dust-sheet','blank-leaf','blink','unplug','doctor','signal','stapler',
  'rope-post','withheld','wrong-way','queue-skip','planter-spit','ray','loose-end'
];
const duration=seconds=>Math.floor(seconds/60)+':'+String(Math.floor(seconds%60)).padStart(2,'0');

export function createWoolRoom(api,state){
  const {THREE:T,camera}=api,look=sight(api);
  const read=new Set(),impressions=new Map(),withdrawn=[],ticket=state.ticket;
  let room,seated=null,candidate='',dwell=0,clock=0,scroll=0,reknit=0,blockedOnce=false,lastX,seenKey='',disposed=false;
  const unfinished=api.ctx.store?.get('woolReknit');
  if(Array.isArray(unfinished)&&unfinished.length){
    for(const mark of unfinished)ticket.stamp(mark);
    api.ctx.store.set('woolReknit',[]);
  }
  state.readClauses=read;
  function cue(key,kind='react'){return api.cue('v4_'+key,{kind,exhibit:'wool'});}
  function restore(){
    for(const mark of withdrawn)ticket.stamp(mark);
    withdrawn.length=0;
    if(api.ctx.store?.get('woolReknit')?.length)api.ctx.store.set('woolReknit',[]);
  }
  function sit(){
    if(seated){seated=null;api.say('You have stood up.');return;}
    const seat=[84.18,.96,41.80];
    api.walker.place('secret',seat,Math.PI/2);
    camera.lookAt(83.57,.71,41.80);camera.rotation.z=0;
    seated=camera.position.clone();scroll=0;
    ticket.stamp('Read');ticket.book('reader','Sat and read the knitted record');
    void cue('wool_sit','beat').then(ok=>{if(ok&&seated&&!disposed)cue('wool_done','beat');});
  }
  function unravel(){
    if(withdrawn.length)return;
    withdrawn.push(...[...ticket.stamps].slice(-2));
    if(!withdrawn.length)return;
    api.ctx.store?.set('woolReknit',[...withdrawn]);
    for(const s of withdrawn)ticket.withdraw(s);
    reknit=0;ticket.book('loose-end','Pulled Nana’s loose end',true);cue('wool_pull');refresh();
  }
  function lines(){
    const rows=[
      state.file.name,'Entered the Expo. The wool was ready.',
      'Visit time: '+duration(clock),state.file.taps+' presses before admission.',
      state.presses+' presses at the Expo.',state.taps+' taps at the glass.',
      'Longest spit: '+(state.bestSpit||0).toFixed(2)+' m.'
    ];
    for(const [s,at] of impressions)rows.push(duration(at)+'   '+s+'.');
    rows.push('Last row: still on the needles.');
    return rows;
  }
  function updateScarf(){
    if(!room?.pattern)return;
    const rows=lines(),start=Math.floor(scroll/2)%rows.length;
    room.pattern.draw((g,w,h)=>{
      g.fillStyle='#f2e8ce';g.fillRect(0,0,w,h);
      for(let y=0;y<h;y+=12){g.strokeStyle='#daccae';g.beginPath();g.moveTo(0,y);g.lineTo(w,y+2);g.stroke();}
      g.fillStyle='#754052';g.fillRect(0,0,13,h);g.fillRect(w-13,0,13,h);
      g.fillStyle='#143e35';g.font='bold 50px Georgia';g.fillText('The visit',29,70,w-58);
      g.font='29px Georgia';g.fillText('Nana’s working pattern',29,117,w-58);
      for(let i=0;i<8;i++){
        const line=rows[(start+i)%rows.length];
        g.fillStyle=i===0?'#753f4c':'#29433a';g.fillText(line,29,190+i*67,w-58);
      }
    });
  }
  function refresh(){
    if(!room?.racks)return;
    const ledger=[...ticket.ledger.values()].filter(e=>e.deviation);
    const slots=[...swatches,...deviations,...ledger.map(e=>e.id).filter(k=>!deviations.includes(k))];
    const total=room.racks.length*24;
    slots.length=Math.max(slots.length,total);
    const rows=slots.slice(0,total);
    room.racks.forEach((rack,r)=>rack.swatches.forEach((item,i)=>{
      const n=r*24+i,key=rows[n],entry=ticket.ledger.get(key);
      item.visible=ticket.stamps.has(key)||!!entry?.deviation;
      rack.labels[i]=item.visible?(entry?.label||key):'Unclaimed swatch '+String(n+1).padStart(2,'0');
    }));
    updateScarf();
  }
  function mount(id,group,clean){
    if(id!=='secret')return;
    const local={racks:[],panels:[]};room=local;
    clean.push(()=>{room=null;seated=null;candidate='';lastX=undefined;dwell=0;});
    clean.push(api.annexProps(group,place=>{
      const off=[],bind=(n,label,fn,reach=2.5)=>off.push(api.register(n,label,fn,reach));
      for(const [route,z,start,end,bays] of [['out',41,30.4,80.4,41],['back',51,32.6,86.4,46]]){
        const spacing=(end-start-3.9)/(bays-1);
        for(let i=0;i<bays;i++)for(const side of [-1,1]){
          if(route==='out'&&side===1&&i===40)continue;
          const serial=side===-1?i%41+1:81-i%41;
          const x=start+1.95+i*spacing,depth=z+side*(1.20-.232);
          const custom=route==='out'&&side===-1&&i<Math.min(8,state.file.clauses.length);
          const key=custom?'accepted-'+i:'folio-'+serial;
          const pos=new T.Vector3(x,1.365,depth);
          local.panels.push({pos,key});
          if(custom){
            const node=place('annex_record',[x,1.016,depth+.01]);
            write(dial(api,node,'record_face',off,500,570),'From your run',state.file.clauses[i],'Accepted clause   /   '+(i+1));
            bind(node,'Read accepted clause',()=>api.say(state.file.clauses[i]));
          }
        }
      }
      layThread(api,group,place,[[30.40,.019,40.14],[79.25,.019,40.14],[80.31,.019,40.62],[81.1,.019,40.65],[83.57,.019,41.65]],off);
      // Two banks of 24 individual drawers give each impression a home.
      for(const origin of [[82.35,0,37.91],[85.05,0,37.91]]){
        const cabinet=place('annex_cabinet',origin),rack={labels:[],swatches:[]};
        place('annex_numbers_'+local.racks.length,origin);local.racks.push(rack);
        for(let i=0;i<24;i++){
          const x=origin[0]-1.05+(i%6)*.42,y=.20+Math.floor(i/6)*.409;
          const swatch=place('annex_swatch',[x,y,origin[2]+.009],(i%3-1)*.12);
          rack.swatches.push(swatch);
        }
        bind(cabinet,'Read a brass number',hit=>{
          const p=cabinet.worldToLocal(hit.point.clone()),col=Math.max(0,Math.min(5,Math.floor((p.x+1.26)/.42)));
          const row=Math.max(0,Math.min(3,Math.floor((p.y-.17)/.409)));
          const relative=row*6+col,number=24*(local.racks.indexOf(rack))+relative+1;
          api.say('Pigeonhole '+number+'. '+rack.labels[relative]+'.');
        });
      }
      const chair=place('annex_seat',[84.18,.506,41.80]);
      bind(chair,()=>seated?'Stand up':'Sit in the reading chair',sit);
      const invitation=place('annex_read_invite',[84.18,.790,42.075]);
      invitation.scale.setScalar(.60);bind(invitation,()=>seated?'Stand up':'Sit in the reading chair',sit);
      const table=place('annex_scarf',[83.57,0,41.80],Math.PI/2);
      local.pattern=dial(api,table,'scarf_pattern',off,440,720);
      bind(table,'Pull the loose end',unravel,2.15);
      place('annex_thread_label',[83.32,.728,41.61],Math.PI/2).scale.setScalar(.42);
      refresh();return ()=>{for(const f of off.reverse())f();};
    }));
  }
  function enter(id){if(id!=='secret'){seated=null;candidate='';dwell=0;lastX=undefined;restore();}}
  function tick(dt,id){
    clock+=dt;
    for(const mark of ticket.stamps)if(!impressions.has(mark))impressions.set(mark,clock);
    if(withdrawn.length){
      reknit+=dt;
      if(reknit>=2.5){ticket.stamp(withdrawn.shift());api.ctx.store?.set('woolReknit',[...withdrawn]);reknit=0;refresh();}
    }
    if(id!=='secret'||!room?.panels.length)return;
    const p=camera.position;
    if(seated){
      if(Math.hypot(p.x-seated.x,p.z-seated.z)>.24){seated=null;}
      else {camera.position.y=1.18;const was=Math.floor(scroll/2);scroll+=dt;if(was!==Math.floor(scroll/2))updateScarf();}
    }
    if(p.x<80&&Math.abs(p.z-41)<1.23||Math.abs(p.z-51)<1.23){
      const found=room.panels.find(f=>Math.abs(f.pos.x-p.x)<1.5&&Math.abs(f.pos.z-p.z)<2.0&&look.gaze(f.pos,2.45,.17));
      if(!found||found.key!==candidate){candidate=found?.key||'';dwell=0;}
      if(candidate&&!read.has(candidate)){
        dwell+=dt;
        if(dwell>=.9){
          read.add(candidate);api.say(read.size===8?'The archive door is ready.':'Clause read. '+Math.min(8,read.size)+' of 8.');
        }
      }
      // Shift exactly one evenly spaced bay, preserving the view through a straight walk.
      if(read.size<8&&Math.abs(p.z-41)<1.18&&p.x>=71.22&&(lastX===undefined||p.x>lastX)){
        const yaw=camera.rotation.y,pitch=camera.rotation.x;
        api.walker.place('secret',[p.x-1.1525,0,p.z],yaw);camera.rotation.x=pitch;
        if(!blockedOnce){blockedOnce=true;cue('corridor_loop');}
      }
    }
    lastX=p.x;
    const key=[ticket.stamps.size,ticket.ledger.size,withdrawn.length].join('/');
    if(seenKey!==key){seenKey=key;refresh();}
  }
  return {mount,tick,enter,dispose(){disposed=true;restore();}};
}
