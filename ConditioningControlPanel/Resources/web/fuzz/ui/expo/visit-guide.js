function roomTask(room,visit) {
  switch(room) {
    case 'hall': return !visit.presses && 'Try the I AGREE button under the glass dome.';
    case 'hoof':
      switch(visit.hoof) {
        case 'idle': return 'Put on the hooves at the console.';
        case 'checkbox': return 'Try to tick one box on the console.';
        case 'pictures': return 'Submit your choice on the picture grid.';
      }
      break;
    case 'habitat': return !visit.taps && 'Tap the habitat glass beside the desk.';
    case 'twelve': return 'Listen at a statue, then return to the Grand Hall.';
    case 'dome': return '';
    case 'secret': return '';
    case 'shop':
      if (!visit.contract) return 'Collect the framed contract at the till.';
      if (visit.loops<2) return 'Try the green exit door.';
      if (!visit.boughtExit) return 'Find “Exit (sold separately)” at the sale counter.';
      return 'Go home through the exit door.';
  }
  return null;
}

export function visitDirection(route,story,room,position,visit) {
  if (!route || !story || !room) return '';
  const distance=slot=>Math.hypot(position.x-slot.standing[0],position.z-slot.standing[2]);
  const close=slot=>slot.zone===room && Math.abs(position.y-slot.standing[1])<1.1 && distance(slot)<2.5;
  const here=route.slots.filter(close).sort((a,b)=>distance(a)-distance(b))[0];
  if(room==='atrium' && position.y>1.6 && !story.completed && !story.current && !here)
    return 'The admission projector is downstairs, beside the turnstile.';
  if(!story.current && !story.completed && room==='atrium' && (!here || here===route.slots[0])){
    const entry=route.slots[0];
    return Math.hypot(position.x-entry.standing[0],position.z-entry.standing[2])<1.35
      ? 'Face the admission projector to hear the first record.'
      : 'Follow the floor line to the admission projector.';
  }
  if(here && story.current?.slot===here.id) return 'Listen here. This screen keeps its record for replay.';
  if(here && story.current) return 'The current recording will finish before another can start.';
  if(here && story.canAutoplay(here.id))return 'Face this screen to hear the next record.';
  if(room==='shop' && story.next && !visit.contract && !visit.loops && !here && !story.assigned('slot_14'))
    return 'Visit the postcard projector, or collect your exit contract at the till.';
  const task=roomTask(room,visit);
  if(!here && task!=null && task!==false && !story.completed)return task;
  if(story.completed===story.total) return 'The archive is complete. Exit through the gift shop.';
  const order=route.tour || route.slots.map(s=>s.id);
  const targetId=order.find(id=>!story.assigned(id) && id!==story.current?.slot);
  const target=route.slots.find(s=>s.id===targetId);
  if(!target)return 'Listen to the final record.';
  if(target.zone===room){
    const dy=target.standing[1]-position.y;
    if(Math.abs(dy)>1.2)return 'Take the '+(room==='service'?'service ':'')+'stairs '+(dy>0?'up':'down')+' to '+target.landmark+'.';
    return 'Find the screen at '+target.landmark+' ('+Math.round(distance(target))+' m).';
  }
  // The signed floor route contains the public doors in both travel directions.
  const queue=[{zone:room,first:null}],seen=new Set([room]);
  for(let n=0;n<queue.length;n++){
    const step=queue[n];
    for(const slot of route.slots){
      const legs=slot.directions || [];
      const i=legs.findIndex((leg,j)=>leg.zone===step.zone && j+1<legs.length);
      if(i<0)continue;
      const zone=legs[i+1].zone,first=step.first || legs[i].text;
      if(zone===target.zone){
        if(room==='atrium' && position.y>1.6)return 'Take the stairs down to the marked gallery doors.';
        return first;
      }
      if(!seen.has(zone)){queue.push({zone,first});seen.add(zone);}
    }
  }
  return task || (room==='catwalk'?'Return to the atrium by the service stair.':'Follow the signs back to the Grand Hall.');
}

export function createVisitGuide(root) {
  const box=root.ownerDocument.createElement('aside');
  box.className='expo-route-guide';
  const progress=root.ownerDocument.createElement('span'), instruction=root.ownerDocument.createElement('strong');
  box.append(progress,instruction);box.hidden=true;root.append(box);
  let route;
  return {setRoute(value){route=value;},update(story,room,position,visit) {
    const line=visitDirection(route,story,room,position,visit);
    box.hidden=!line;
    if(!line)return;
    const count=story.current?.row.archive.number || story.next?.archive.number || story.total;
    const label='ARCHIVE  '+String(count).padStart(2,'0')+' / '+story.total;
    if(label!==progress.textContent)progress.textContent=label;
    if(line!==instruction.textContent)instruction.textContent=line;
  },dispose(){box.remove();}};
}
