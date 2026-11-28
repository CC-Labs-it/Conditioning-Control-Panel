const galleries=new Set(['chronology','forum','commons','aquarium']);
export function narratorAwareness(state,cue) {
  const seen=new Set(),visits=[],spoken=new Set();
  let idle=0,index=0,lastAction='',x,z,yaw,pitch,yes=state.ticket.yes,deviations=state.ticket.deviations;
  function note(key,zone,kind='react'){
    if(spoken.has(key))return;
    cue('v4_'+key,{kind,zone,exhibit:'visitor',onStart:()=>spoken.add(key)});
  }
  return {
    enter(room){
      const from=visits.at(-1);
      if(!from && (state.ticket.issued||state.repeatArrival))note('repeat',room);
      if(room==='hall' && ![...seen].some(r=>galleries.has(r)) && !seen.has(room))note('skip',room);
      else if(galleries.has(room)&&seen.has(room))note('lap',room);
      else if(visits.at(-2)===room)note('backtrack',room);
      else if(seen.has(room))note('repeat',room);
      seen.add(room);visits.push(room);if(visits.length>2)visits.shift();idle=0;
    },
    tick(dt,room,camera,actionSerial){
      const p=camera.position, rot=camera.rotation;
      if(x===undefined || Math.hypot(p.x-x,p.z-z)>.025 || Math.abs(yaw-rot.y)+Math.abs(pitch-rot.x)>.018 || lastAction!==actionSerial){
        idle=0;x=p.x;z=p.z;yaw=rot.y;pitch=rot.x;lastAction=actionSerial;
      }else idle+=dt;
      if(idle>=60 && index<3){note(['idle_a','idle_b','idle_c'][index++],room,'ambient');idle=0;}
      if(deviations<state.ticket.deviations)note('broke_rule',room);
      else if(yes<state.ticket.yes)note('kept_rule',room);
      deviations=state.ticket.deviations;yes=state.ticket.yes;
    }
  };
}
