import {railModels} from './rail-model.js';
import {teaDirections} from './tea-directions.js';
import {routeFor,routeGates,threshold,penSegments,distanceToBar} from './rail-routes.js';

export function createRail({ctx,scene,walker,state,voice,play,models=railModels(scene)}){
  const rows=new Map(),cycles={lean:0,idle:0};
  let greeting=false,gates=[],activeRoute=null,bestMetres=Infinity,sample=0;
  let welcome=false,record=null,task='',off=false,contact=false,lean=0,idle=0,nudges=0,progress='';
  function row(id,parts,rooms,instant=false){
    if(parts.some(s=>Math.abs(walker.feet.y-s.y)<1.75&&distanceToBar(s,walker.feet)<.32))return;
    let r=rows.get(id);
    if(!r){
      r={id,parts,rooms,model:models.build(parts),up:false,f:0};rows.set(id,r);
      r.model.pose(0,true);
    }
    if(!r.up){
      // A resumed record may now be playing at a different station.
      if(id==='view'&&r.parts!==parts){r.model.dispose();r.parts=parts;r.rooms=rooms;r.model=models.build(parts);}
      r.up=true;if(instant)r.f=1;
      r.model.pose(r.f,ctx.reduced||ctx.gentle);
    }
  }
  function lower(prefix){for(const r of rows.values())if(r.id.startsWith(prefix))r.up=false;}
  function resetNudges(){lean=idle=nudges=0;contact=false;progress='';bestMetres=Infinity;sample=0;}
  function view(entry){
    if(off||!entry||entry.zone!==walker.zone)return;
    if(record===entry)return;
    clearView();record=entry;resetNudges();
    row('view',penSegments(walker.feet,entry.station.pos),[entry.zone]);play('rail',.42);
  }
  function clearView(entry){
    if(entry&&entry!==record)return;
    record=null;lower('view');
  }
  function clear(){
    for(const r of rows.values())r.model.dispose();rows.clear();
    record=null;welcome=false;task='';gates=[];activeRoute=null;state.railGuide=null;resetNudges();
  }
  function pause(on){
    if(off===on)return;off=on;
    if(on)clear();else if(greeting){welcome=true;row('welcome',penSegments(walker.feet),[walker.zone],true);}
  }
  async function firstArrival(){
    const first=await ctx.store.claimLifetime('railArrived');
    state.repeatArrival=!first||state.ticket.issued||state.ticket.count>0||!!ctx.store.get('lastArchiveRecord')||!!ctx.store.get('expoEnding');
    return !state.repeatArrival;
  }
  function greet(){
    greeting=welcome=true;row('welcome',penSegments(walker.feet),[walker.zone],true);
    voice().cue('rail_welcome',{zone:walker.zone,kind:'beat',roomWide:true,replay:true}).finally(()=>{
      greeting=welcome=false;lower('welcome');resetNudges();
    });
  }
  function updateTask(){
    const key=state.carry?.key||'';
    if(key!==task){
      lower('gate:');task=key;activeRoute=routeFor(key);gates=activeRoute?routeGates(activeRoute):[];resetNudges();
      // The Rail talk is archive record 1 on the first screen now, so a game rail only rises.
      if(activeRoute)play('rail',.42);
    }
    for(const gate of gates){
      const id='gate:'+gate.id;
      if(!rows.get(id)?.up)row(id,[threshold(gate)],gate.rooms);
    }
    return activeRoute;
  }
  function tick(dt){
    if(off)return;
    if(record&&record.zone!==walker.zone)clearView();
    const route=updateTask();
    for(const [id,r] of rows){
      const next=Math.max(0,Math.min(1,r.f+(r.up?dt/.4:-dt/.3)));
      if(next!==r.f){r.f=next;r.model.pose(r.f,ctx.reduced||ctx.gentle);}
      r.model.root.visible=r.f>0&&r.rooms.includes(walker.zone)&&(!id.startsWith('gate:')||!route||route.rooms.includes(walker.zone));
      if(!r.up&&r.f===0){r.model.dispose();rows.delete(id);}
    }
    state.railGuide=welcome?'Listen to the welcome':record?'Listen to the record':null;
    if(!route&&!welcome&&!record)return;
    const p=[task,state.ticket.count,state.ticket.ledger.size,walker.zone,state.readClauses?.size].join('/');
    if(progress!==p){progress=p;idle=0;}
    if(task==='tea'&&(sample-=dt)<=0){
      sample=.5;
      const metres=teaDirections(walker.zone,walker.feet).metres;
      if(metres<bestMetres){bestMetres=metres;idle=0;}
    }
    idle+=dt;lean=contact?lean+dt:0;contact=false;
    if(nudges>=3||!voice().available())return;
    let id;
    if(lean>=1){id=1+cycles.lean++%7;lean=0;}
    else if(idle>=45){id=route?8+cycles.idle++%2:9;}
    if(!id)return;
    idle=0;nudges++;void voice().cue('rail_'+id,{kind:'react',roomWide:true,replay:true});
  }
  function constrain(candidate,previous){
    for(const r of rows.values()){
      if(!r.up||!r.rooms.includes(walker.zone))continue;
      if(r.id.startsWith('gate:')&&!activeRoute?.rooms.includes(walker.zone))continue;
      for(const s of r.parts){
        if(previous.y<s.y-.9||previous.y>s.y+3.2)continue;
        const d=distanceToBar(s,candidate);
        if(d<.29&&d<distanceToBar(s,previous)){
          candidate.x=previous.x;candidate.z=previous.z;contact=true;return;
        }
      }
    }
  }
  return {firstArrival,greet,view,clearView,pause,tick,constrain,clear,
    allows(room){
      if(off||room===walker.zone)return true;
      const route=routeFor(state.carry?.key);
      return !welcome&&!record&&(!route||!route.rooms.includes(walker.zone)||route.rooms.includes(room));
    },
    get welcome(){return welcome;},
    debug:()=>[...rows.values()].map(r=>({id:r.id,up:r.up,f:r.f})),
    dispose(){greeting=false;off=true;clear();models.dispose();}};
}
