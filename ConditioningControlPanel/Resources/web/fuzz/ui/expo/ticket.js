export const COUNTERFOILS = [
  ['Corrected'], ['Attended'], ['Spat'], ['Queued'], ['Pressed'], ['Hoofed'],
  ['Fed','Kept'], ['Blinked'], ['Stayed'], ['Delivered'], ['Unplugged'], ['Read']
];
const impressions = new Set(COUNTERFOILS.flat());
export function visitTicket(store) {
  const saved = store?.get('visitorTicket') || {};
  const stamps = new Set((Array.isArray(saved.stamps) ? saved.stamps : []).filter(s=>impressions.has(s)));
  const ledger = new Map((Array.isArray(saved.ledger) ? saved.ledger : [])
    .filter(row=>row && typeof row.id==='string' && typeof row.label==='string').map(row=>[row.id,row]));
  let issued=!!saved.issued,deviations=[...ledger.values()].filter(row=>row.deviation).length;
  let settled=Math.max(0,Math.min(deviations,Number(saved.settled)||0));
  function save() { store?.set('visitorTicket',{issued,settled,stamps:[...stamps],ledger:[...ledger.values()]}); }
  return {
    stamps,ledger,get issued(){return issued;},
    issue(){if(!issued){issued=true;save();}},
    get count(){return COUNTERFOILS.filter(box=>box.every(mark=>stamps.has(mark))).length;},
    get balance(){return ledger.size-deviations+settled;},
    get outstanding(){return deviations-settled;},
    settle(){if(settled!==deviations){settled=deviations;save();}return this.balance;},
    get complete(){return stamps.size===impressions.size;},
    get yes(){return ledger.size-deviations;},
    get deviations(){return deviations;},
    stamp(mark){if(!impressions.has(mark)||stamps.has(mark))return false;stamps.add(mark);save();return true;},
    withdraw(mark){if(stamps.delete(mark))save();},
    book(id,label,deviation=false){if(ledger.has(id))return false;ledger.set(id,{id,label,deviation});if(deviation)deviations++;save();return true;}
  };
}
