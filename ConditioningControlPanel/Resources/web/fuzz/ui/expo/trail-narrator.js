// Keep an unheard instruction eligible while the visitor is at its stop.
export function trailNarrator(api,state){
  const heard=new Set(),pending=new Set(),stamps=state.ticket.stamps;
  let next=0;
  function offer(id,zone){
    if(heard.has(id)||pending.has(id))return;
    pending.add(id);
    void api.cue('r5_'+id,{zone,kind:'beat',exhibit:'trail '+id,roomWide:true,
      onStart:()=>heard.add(id)}).then(ok=>{if(ok)heard.add(id);pending.delete(id);});
  }
  function tick(dt,zone,p){
    next-=dt;if(next>0)return;next=.35;
    switch(zone){
      case 'atrium':if(state.turnstile&&!state.hoofWorn)offer('welcome',zone);break;
      case 'chronology':if(!stamps.has('Corrected')&&Math.hypot(p.x-10.61,p.z-46.85)<6)offer('corrections',zone);break;
      case 'forum':if(!stamps.has('Attended'))offer('meeting',zone);break;
      case 'commons':if(!stamps.has('Spat'))offer('rematch',zone);break;
      case 'hall':
        if(!stamps.has('Queued')&&p.y<1&&Math.hypot(p.x+7.72,p.z-12.06)<3.7)offer('queue',zone);
        else if(!stamps.has('Pressed')&&p.y<1&&Math.hypot(p.x,p.z+9)<4.9)offer('first_press',zone);
        break;
      case 'hoof':if(!stamps.has('Hoofed')&&!state.hoofWorn)offer('hoof',zone);break;
      case 'habitat':if(!stamps.has('Fed')||!stamps.has('Kept'))offer('habitat',zone);break;
      case 'dome':if(!stamps.has('Stayed'))offer('dome',zone);break;
      case 'service':
        if(api.isCarrying('tea'))offer('tea',zone);
        else if(!state.staffVisit.unplugged&&Math.hypot(p.x-25.42,p.z-27.19)<2.7&&p.z<30.7)offer('heater',zone);
        break;
      case 'secret':if(!stamps.has('Read'))offer(state.readClauses.size>=8?'wool_chair':'corridor',zone);break;
      case 'shop':
        if(api.isCarrying('signed-form'))offer('signed_form',zone);
        else if(!state.souvenir&&!state.contract)offer('shop',zone);
        break;
    }
  }
  return {tick,offer};
}
