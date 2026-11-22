import {teaDirections} from './tea-directions.js';

// Desk order and wording match the printed Human Activity Trail.
export const STOPS = [
  ['Corrections Desk','Chronology gallery','Hang the five photographs on the hooks.',['Corrected']],
  ['The Meeting','Forum','Stand on the green disc. Do nothing for 40 seconds.',['Attended']],
  ['The Rematch','Commons','Step on the green. Spit as far as you can.',['Spat']],
  ['The Queue','Outside Hoof Sciences','Walk the whole queue. Nobody is in it.',['Queued']],
  ['First Press','Grand Hall','There is a button and a sign. Decide.',['Pressed']],
  ['Hoof Sciences','Hoof lab','Put the hooves on. Take the picture test.',['Hoofed']],
  ['The Habitat','Habitat',"Feed one Yes through the slot. Then sit in the keeper's chair.",['Fed','Kept']],
  ['Lady Lashes','Hall of the Twelve','Look her in the eye. Do not look away.',['Blinked']],
  ['The Dome','Dome','There is nothing to see here. Stay anyway.',['Stayed']],
  ['Tea Round','Staff canteen to reading balcony','Carry the tea upstairs to the Grand Hall balcony. Follow the wool.',['Delivered'],true],
  ['The Heater','Service corridor','Do not unplug.',['Unplugged'],true],
  ["Nana's Record",'Contract corridor','Read eight clauses. Then sit in the reading chair.',['Read'],true]
].map(([name,place,todo,marks,staff=false])=>({name,place,todo,marks,staff}));

const sentences={
  Corrected:'You hung the five photographs.', Attended:'Your attendance is on the minutes.',
  Spat:'You landed a spit on the Rematch range.', Queued:'You walked the whole empty queue.',
  Pressed:'You pressed the founding button.', Hoofed:'You passed the picture test.',
  Fed:'You fed one Yes through the slot.', Kept:"You sat in the keeper's chair.",
  Blinked:'You held Lady Lashes’ gaze.', Stayed:'You stayed under the dome.',
  Delivered:'You delivered the tea to the reading balcony.',
  Unplugged:'You unplugged the building. It was only the heater.', Read:'You sat and read the knitted record.'
};
export function stampSentence(mark,ticket){
  if(mark==='Attended'&&ticket.ledger.has('absent'))return 'The meeting recorded you in your absence.';
  return sentences[mark];
}
export function stopSealed(stop,ticket){
  return stop.staff&&!stop.marks.some(mark=>ticket.discovered?.has(mark)||ticket.stamps.has(mark));
}
export function trailMeter(count){
  if(count===12)return '12 of 12. Gary has initialled the margin.';
  if(count>=8)return count+' of 12. The exit is on sale at the gift shop.';
  return count+' of 12. '+(8-count)+' more and you may go home.';
}
const stopTask=i=>({title:STOPS[i].todo,where:STOPS[i].place,kicker:STOPS[i].name});
const task=(title,where,kicker='Human trail',target)=>({title,where,kicker,target});
export function trailTask(room,p,v,yaw=0){
  const key=v.carry?.key,t=v.ticket, marks=t?.stamps;
  if(key==='tea'){
    const route=teaDirections(room,p);
    return task('Carry the tea to the reading balcony','Grand Hall, upstairs · '+route.metres+' m · follow the wool','Tea round · staff only',route.target);
  }
  if(key==='signed-form')return task('Signed form','Hand it in at the gift shop till.','Lady Lashes');
  if(key==='stapler')return task("Return Greg’s stapler","Staff desk, service corridor",'Office supplies');
  if(key==='rope-post')return task('Set down the rope post','Look at the floor, or return it to the notice.','First Press');
  if(key?.startsWith('souvenir:'))return task('Carry your souvenir to the till','Gift shop');
  if(key?.startsWith('print-'))return task('Hang the photograph on an empty hook','Corrections Desk, along the same wall.');
  if(key?.startsWith('staff-sign-'))return task('Place Bob’s sign','Hall of the Twelve, beside Bob.');
  if(room==='secret'){
    const n=Math.min(8,v.readClauses?.size||0);
    return n<8?task('Read 8 clauses to reach the end','Stop at a frame and look at it · '+n+' of 8',"Nana’s record · staff only"):
      task('Sit in the reading chair','End of the contract corridor · 8 of 8',"Nana’s record · staff only");
  }
  if(v.pressTask && (room==='hall'||(room==='shop'&&v.pressTask.atShop)))return v.pressTask;
  if(room==='atrium'){
    if(!t?.issued)return task('Pass the turnstile for your trail card','Visitor admission · straight ahead','Start here',[0,35.33]);
    if((t?.count||0)>=8)return task('Go to the gift shop','Eight stamps open the way home.','Home');
    const corrections=[7.8,41.1],hall=[0,16];
    const nearCorrections=!marks?.has('Corrected')&&(marks?.has('Pressed')||
      Math.hypot(p.x-corrections[0],p.z-corrections[1])<=Math.hypot(p.x-hall[0],p.z-hall[1]));
    const bearing=Math.atan2(corrections[0]-p.x,p.z-corrections[1])+yaw;
    const onLeft=Math.sin(bearing)<-.30&&Math.cos(bearing)>0;
    const directions=onLeft?'Nearest stop: Corrections Desk, door on the left · Chronology':'Nearest stop: Corrections Desk · through the Chronology doors';
    return task('Collect 8 stamps to go home',nearCorrections?directions:'Nearest stop: First Press, Grand Hall','Start anywhere',nearCorrections?corrections:hall);
  }
  if(room==='service'){
    if(v.staffVisit?.unplugged)return task('Plug Greg back in','The socket is behind you','Optional',[25.93,26.92]);
    return task('Follow the wool','Staff canteen and contract corridor.','Staff access');
  }
  if(room==='forum'&&v.meetingSeconds>0)return v.meetingSeconds>=40?task('Walk through the open gate','Forum · attendance recorded','The Meeting'):task('Stand still on the green disc','Forum · '+Math.ceil(40-v.meetingSeconds)+' s remaining','The Meeting');
  if(room==='hall')return p.x<-6&&p.z>6.7&&p.y<1&&!marks?.has('Queued')?stopTask(3):stopTask(4);
  if(room==='hoof'&&v.hoof==='pictures')return task('Select all squares with a llama. Then press Verify.','Hoof lab · picture grid','Hoof Sciences');
  if(room==='habitat'&&marks?.has('Fed')&&!marks.has('Kept'))return task("Sit in the keeper’s chair",'Enquire at the staff door.','The Habitat');
  if(room==='shop'){
    if(v.boughtExit)return task('Walk through the Renewal door','Gift shop');
    if(v.contract&&v.loops>=2)return task(t.count<8?'Collect a day ticket':'Buy Exit','Gift shop till');
    if(v.contract)return task('Open the green exit door','Gift shop');
    if(v.souvenir)return task('Answer the question','Gift shop till');
    return task('Choose one souvenir','Gift shop · carry it to the till.');
  }
  const index={chronology:0,forum:1,commons:2,hoof:5,habitat:6,twelve:7,dome:8}[room];
  if(index!==undefined)return stopTask(index);
  if(room==='aquarium')return task('Watch the ray','Take a seat in the aquarium.','Aquarium');
  if(room==='catwalk')return task('Find the dome','Below the catwalk.','Dome');
  return task('Collect 8 stamps to go home','Open your trail card to choose a stop.');
}
