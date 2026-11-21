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
  if(room==='atrium' && position.y>1.6)
    return story.completed?'Take the stairs down to the marked gallery doors.':'The admission projector is downstairs, beside the turnstile.';
  const close=slot=>slot.zone===room && Math.abs(position.y-slot.standing[1])<1.1 && Math.hypot(position.x-slot.standing[0],position.z-slot.standing[2])<3;
  const here=route.slots.find(close);
  if(!story.current && !story.completed && room==='atrium'){
    const entry=route.slots[0];
    return Math.hypot(position.x-entry.standing[0],position.z-entry.standing[2])<1.35
      ? 'Face the admission projector to hear the first record.'
      : 'Follow the floor line to the admission projector.';
  }
  if(here && story.current?.slot===here.id) return 'Listen here, or follow the floor line to the next projector.';
  if(here && story.current) return 'Use the large play button to bring the recording here.';
  if(here && !story.current && story.next && story.lastSlot!==here.id)
    return 'Face the screen to hear the next record.';
  if(room==='shop' && story.next && !visit.contract && !visit.loops && !here)
    return 'Visit the postcard projector, or collect your exit contract at the till.';
  const task=roomTask(room,visit);
  if(task!==null && task!==false)return task;
  if(story.completed===story.total) return 'The archive is complete. Exit through the gift shop.';
  const previous=route.slots.find(slot=>slot.id===(story.current?.slot || story.lastSlot));
  if(!previous && room==='atrium')return 'Face the admission projector to resume the archive.';
  const direction=previous?.directions.find(leg=>leg.zone===room);
  if(direction)return direction.text;
  const local=route.slots.filter(slot=>slot.zone===room).sort((a,b)=>
    Math.hypot(position.x-a.standing[0],position.z-a.standing[2])-Math.hypot(position.x-b.standing[0],position.z-b.standing[2]))[0];
  if(local)return 'Look for the projector at '+local.landmark.toLowerCase()+'.';
  return room==='catwalk'?'Return to the atrium by the service stair.':'Follow the signs back to the Grand Hall.';
}

export function createVisitGuide(root, assetURL) {
  const box=root.ownerDocument.createElement('aside');
  box.className='expo-route-guide';
  const progress=root.ownerDocument.createElement('span'), instruction=root.ownerDocument.createElement('strong');
  box.append(progress,instruction);box.hidden=true;root.append(box);
  let route,dead=false;
  const request=new AbortController();
  fetch(assetURL('station-route.json'),{signal:request.signal}).then(async response=>{
    if(!response.ok)throw Error('Expo tour route HTTP '+response.status);
    route=await response.json();
  }).catch(error=>{if(!dead)console.warn(error);});
  return {update(story,room,position,visit) {
    const line=visitDirection(route,story,room,position,visit);
    box.hidden=!line;
    if(!line)return;
    const count=story.current?.row.archive.number || story.next?.archive.number || story.total;
    const label='ARCHIVE  '+String(count).padStart(2,'0')+' / '+story.total;
    if(label!==progress.textContent)progress.textContent=label;
    if(line!==instruction.textContent)instruction.textContent=line;
  },dispose(){dead=true;request.abort();box.remove();}};
}
