// New screens take the next record in visit order. A completed screening belongs to that screen.
export function createArchiveStory(rows, savedRecord, save, savedScreens = []) {
  const events=rows.filter(row=>row.set==='event');
  // An already cached media manifest predates the digital panels. Its events were stored in visit order.
  const records = events.map((row,i)=>{
    if(row.archive)return row;
    const description=String(row.where||'').replace(/^Timeline:\s*/,'');
    const date=description.lastIndexOf(' (');
    return {...row,archive:{number:i+1,chapter:null,section:'Historical archive',
      title:date<0?description:description.slice(0,date),date:date<0?'':description.slice(date+2).replace(/\)$/,''),
      body:row.subtitle}};
  }).sort((a,b)=>a.archive.number-b.archive.number);
  if (!records.length || records.some((row,i) => row.archive.number !== i + 1)) throw Error('Invalid archive sequence');
  let count = records.findIndex(row => row.id === savedRecord) + 1;
  let current = null;
  const history = [], assigned = new Map(), viewed = new Set();
  const slotId = index => 'slot_'+String(index+1).padStart(2,'0');
  // Older saves contain only the last record. Reconstruct their missing locations in route order.
  const valid = Array.isArray(savedScreens) ? savedScreens.slice(0,count) : [];
  const slots = new Set(records.map((_,i)=>slotId(i)));
  const pending = new Set(valid.filter(slot=>slots.has(slot)));
  for(let i=0;i<count;i++){
    let slot=valid[i];
    if(!slots.has(slot)||assigned.has(slot))slot=[...slots].find(key=>!assigned.has(key)&&!pending.has(key));
    else viewed.add(slot);
    history.push(slot);assigned.set(slot,records[i]);
  }
  function persist() {
    save?.(records[count-1].id,history.map(slot=>viewed.has(slot)?slot:null));
  }
  return {
    get completed() { return count; },
    get total() { return records.length; },
    get next() { return records[count]; },
    get previous() { return records[count-1]; },
    get displayed() { return current?.started ? current.row : records[count-1]; },
    get current() { return current; },
    get lastSlot() { return history.at(-1); },
    assigned(slot) { return assigned.get(slot); },
    at(slot) { return current?.started && current.slot===slot ? current.row : viewed.has(slot) ? assigned.get(slot) : undefined; },
    canStartNew(slot) {
      return !current && count < records.length && !assigned.has(slot);
    },
    request(slot, review = false) {
      if (current) return null;
      const own=assigned.get(slot);
      const row = own || (!review && records[count]);
      if (!row) return null;
      current = {slot, row, review:!!own};
      return current;
    },
    start(ticket) {
      if(!ticket || ticket!==current || ticket.started)return;
      ticket.started=true;
      // An old save can reserve a replay button without proving this screen has ever been viewed.
      if(ticket.review&&!viewed.has(ticket.slot)){viewed.add(ticket.slot);persist();}
    },
    finish(ticket, completed) {
      if (!ticket || ticket !== current) return;
      current = null;
      if (!completed) return;
      if(ticket.review){if(!viewed.has(ticket.slot)){viewed.add(ticket.slot);persist();}return;}
      count++;
      history.push(ticket.slot);
      assigned.set(ticket.slot,ticket.row);
      viewed.add(ticket.slot);persist();
    }
  };
}
