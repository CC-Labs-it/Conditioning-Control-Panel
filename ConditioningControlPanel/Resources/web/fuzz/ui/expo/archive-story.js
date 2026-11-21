// Physical projectors are interchangeable. Only a completed first play advances the archive.
export function createArchiveStory(rows, savedRecord, save) {
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
  let current = null, lastSlot = null;
  const plays = new Map();
  return {
    get completed() { return count; },
    get total() { return records.length; },
    get next() { return records[count]; },
    get previous() { return records[count-1]; },
    get displayed() { return current?.row || records[count] || records[count-1]; },
    get current() { return current; },
    get lastSlot() { return lastSlot; },
    canAutoplay(slot) {
      return !current && count < records.length && (plays.get(slot) || 0) < 3;
    },
    request(slot, review = false) {
      if (current) return null;
      const row = review ? records[count-1] : records[count];
      if (!row) return null;
      current = {slot, row, review};
      return current;
    },
    move(slot) {
      if (current) current.slot = slot;
    },
    finish(ticket, completed) {
      if (!ticket || ticket !== current) return;
      current = null;
      if (!completed || ticket.review) return;
      count++;
      lastSlot = ticket.slot;
      plays.set(lastSlot,(plays.get(lastSlot) || 0) + 1);
      save?.(ticket.row.id);
    }
  };
}
