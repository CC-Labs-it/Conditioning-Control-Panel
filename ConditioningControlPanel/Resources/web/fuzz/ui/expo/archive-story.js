import {screenHome} from './archive-screens.js';

/** The Rail talk: record 1, required viewing, and the one record the Atrium's rail holds a visitor for. */
export const RAIL_RECORD = 'rail_lore';

// Whichever screen is started plays the next record of the story, so one screen holds many records and rests on the
// last one it showed. Once every record has been watched, a screen replays its own last one.
// Record 1 (the Rail talk) came after the other forty were numbered. A save that has progress but has never screened it
// (`firstScreened` false) is owed it: it is the next new screening, then the story carries on where the save was.
export function createArchiveStory(rows, savedRecord, save, savedScreens = [], home = screenHome, firstScreened = false) {
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
  // An owed first record: the save's own list of screens starts at record 2.
  // An older pack's manifest has no such record and owes nothing.
  let owed = count > 1 && !firstScreened && records[0].id === RAIL_RECORD;
  let current = null;
  // history[i] is the screen record i was watched on. An older save may not know (null), or may name a screen that has
  // since been retired: that record rests on the screen which took over its part of the room.
  const history = [], last = new Map();
  const saved = Array.isArray(savedScreens) ? savedScreens : [];
  for(let i=0;i<count;i++){
    const at=owed?saved[i-1]:saved[i];
    const slot=(i>0||!owed)&&(typeof at==='string'&&home(at))||null;
    history.push(slot);if(slot)last.set(slot,records[i]);
  }
  function persist() { save?.(records[count-1].id,[...history],true); }
  const upcoming = () => owed ? records[0] : records[count];
  return {
    get completed() { return count - (owed ? 1 : 0); },
    get total() { return records.length; },
    get next() { return upcoming(); },
    get previous() { return records[count-1]; },
    get displayed() { return current?.started ? current.row : records[count-1]; },
    get current() { return current; },
    get lastSlot() { return history.at(-1); },
    /** The last record this screen finished, which is what it rests on. */
    assigned(slot) { return last.get(slot); },
    /** What the screen shows now: the record playing on it, else the one it rests on. */
    at(slot) { return current?.started && current.slot===slot ? current.row : last.get(slot); },
    /** Would a press here start something: a new record while any remain, else this screen's own last one. */
    canStart(slot) { return !current && (!!upcoming() || last.has(slot)); },
    request(slot) {
      if (current) return null;
      const fresh=upcoming(), row=fresh||last.get(slot);
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
      if (owed) {
        // The owed record takes its place at the head of the save; the place in the story does not move.
        owed = false;history[0] = ticket.slot;
        last.set(ticket.slot,ticket.row);persist();
        return;
      }
      count++;
      history.push(ticket.slot);
      last.set(ticket.slot,ticket.row);persist();
    }
  };
}
