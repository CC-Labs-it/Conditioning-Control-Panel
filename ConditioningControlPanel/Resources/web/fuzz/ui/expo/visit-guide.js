import { COUNTERFOILS } from './ticket.js';

const instructions = {
  chronology:'Corrections Desk. Hang the photographs.', forum:'Stand on the Agenda plate.',
  commons:'Take your place on the Rematch green.', aquarium:'The gallery has seating for a ray.',
  habitat:'Visitor feeding times are at your discretion.', twelve:'The individual exhibits are open.',
  secret:'Follow the wool. Read the clauses.', catwalk:'The dome is below the catwalk.',
  service:'Visitor access ends at the staff notices.', dome:'There is nothing to see here.'
};
export function visitDirection(route,story,room,position,visit) {
  if(!room)return '';
  if(!visit.ticket?.issued && room==='atrium')return 'Pass the turnstile for your ticket.';
  if(room==='hall')return visit.presses>=25?'Wait beside DO PRESS. The original plinth can seal it.':'First Press. Please observe the sign.';
  if(room==='hoof')return visit.hoof==='pictures'?'Submit your choice on the picture grid.':'Applied Hoof Sciences. Fit or return your hooves.';
  if(room==='shop'){
    if(visit.boughtExit)return 'Walk through the Renewal door.';
    if(visit.contract && visit.loops>=2)return visit.ticket.count<8?'Collect a day ticket at the till.':'Buy Exit at the till.';
    if(visit.contract)return 'Open the green exit door.';
    if(visit.souvenir)return 'Answer the question on the till.';
    if(visit.carry?.key?.startsWith('souvenir:'))return 'Carry your souvenir to the till.';
    return 'Choose one souvenir and carry it to the till.';
  }
  if(room==='atrium')return 'Galleries by the floor line. Grand Hall straight ahead.';
  return instructions[room] || '';
}
export function createVisitGuide(root) {
  const d=root.ownerDocument,box=d.createElement('aside');
  box.className='expo-route-guide expo-counterfoil';
  const title=d.createElement('span'),stubs=d.createElement('div'),balance=d.createElement('span'),hint=d.createElement('strong');
  box.append(title,stubs,balance,hint);root.append(box);stubs.className='expo-stamps';
  const cells=COUNTERFOILS.map(marks=>{const cell=d.createElement('i');cell.title=marks.join(' / ');stubs.append(cell);return cell;});
  let signature='',route;
  return {setRoute(value){route=value;},update(story,room,position,visit){
    const t=visit.ticket;
    const direction=visit.carry?'Carrying '+visit.carry.label+'. '+(visit.carry.key==='rope-post'?'Look at the floor to set it down.':visitDirection(route,story,room,position,visit)):visitDirection(route,story,room,position,visit);
    const key=[t.issued,...t.stamps,t.balance,t.outstanding,direction].join('|');
    if(signature===key)return;signature=key;
    title.textContent=t.issued?'ADMIT ONE HUMAN, 2037':'VISITOR ADMISSION';
    stubs.hidden=balance.hidden=!t.issued;
    cells.forEach((cell,i)=>{const box=COUNTERFOILS[i],filled=box.filter(m=>t.stamps.has(m));cell.dataset.filled=filled.length===box.length?'true':filled.length?'half':'false';
      cell.textContent=filled.length?filled.map(mark=>mark[0]).join(''):'·';cell.setAttribute('aria-label',box.join(' and ')+': '+(filled.join(', ')||'empty'));});
    balance.textContent=t.count+' / 12 stamps   ·   '+t.balance+' Yes   ·   '+t.outstanding+(t.outstanding===1?' Deviation':' Deviations');hint.textContent=direction;
  },dispose(){box.remove();}};
}
