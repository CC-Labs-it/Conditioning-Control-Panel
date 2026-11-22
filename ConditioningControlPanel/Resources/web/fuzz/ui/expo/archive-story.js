import {screenHome} from './archive-screens.js';

// Whichever screen is started plays the next record of the story, so one screen holds many records and rests on the
// last one it showed. Once every record has been watched, a screen replays its own last one.
export function createArchiveStory(rows, savedRecord, save, savedScreens = [], home = screenHome) {
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
  // history[i] is the screen record i was watched on. An older save may not know (null), or may name a screen that has
  // since been retired: that record rests on the screen which took over its part of the room.
  const history = [], last = new Map();
  const saved = Array.isArray(savedScreens) ? savedScreens : [];
  for(let i=0;i<count;i++){
    const slot=(typeof saved[i]==='string'&&home(saved[i]))||null;
    history.push(slot);if(slot)last.set(slot,records[i]);
  }
  function persist() { save?.(records[count-1].id,[...history]); }
  return {
    get completed() { return count; },
    get total() { return records.length; },
    get next() { return records[count]; },
    get previous() { return records[count-1]; },
    get displayed() { return current?.started ? current.row : records[count-1]; },
    get current() { return current; },
    get lastSlot() { return history.at(-1); },
    /** The last record this screen finished, which is what it rests on. */
    assigned(slot) { return last.get(slot); },
    /** What the screen shows now: the record playing on it, else the one it rests on. */
    at(slot) { return current?.started && current.slot===slot ? current.row : last.get(slot); },
    /** Would a press here start something: a new record while any remain, else this screen's own last one. */
    canStart(slot) { return !current && (count < records.length || last.has(slot)); },
    request(slot) {
      if (current) return null;
      const fresh=records[count], row=fresh||last.get(slot);
      if (!row) return null;
      current = {slot, row, review:!fresh};
      return current;
    },
    start(ticket) {
      if(!ticket || ticket!==current || ticket.started)return;
      ticket.started=true;
    },
    finish(ticket, completed) {
      if (!ticket || ticket !== current) return;
      current = null;
      if (!completed || ticket.review) return;
      count++;
      history.push(ticket.slot);
      last.set(ticket.slot,ticket.row);persist();
    }
  };
}
