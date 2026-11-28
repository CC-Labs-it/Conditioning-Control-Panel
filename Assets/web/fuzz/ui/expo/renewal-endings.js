const instructions=[
  ['agenda','Attend the meeting'],['queued','Walk the queue'],['left-button','Leave the First Press alone'],
  ['glass-kept','Leave the glass untapped'],['dome-kept','Move along at the dome'],['heater-kept','Leave the heater connected']
];
export function modelVisitor(ticket){
  return ticket.deviations===0 && instructions.every(([id])=>ticket.ledger.has(id));
}
export function departureEnding(choice,state){
  if(choice==='renewal'&&state.file.herd.length===12&&state.ticket.complete)return 'full';
  return choice;
}
export const endingTitles={renewal:'Renewal',conduct:'Acceptance by Conduct',stayed:'Stayed',finger:'Finger',model:'Model Visitor',full:'The Thirteenth'};
export const endingLines={renewal:'v4_renew',conduct:'v4_conduct',stayed:'v4_stayed_end',finger:'v4_finger_end',model:'v4_model_end',full:'v4_full_end'};
