import test from 'node:test';
import assert from 'node:assert/strict';
import {habitatReel} from '../ui/expo/habitat.js';
import {BY_ID} from '../data/items.js';

test('the habitat replays real acquisitions and fate cards in run order, then the admissions inventory and counters',()=>{
  const fate=Object.values(BY_ID).find(item=>item.fate&&item.step===1);
  const file={name:'Registry visitor',taps:341,total:832,herd:['greg','nana']};
  const rows=habitatReel({order:['start','title','tunnel',fate.id]},file);
  assert.deepEqual(rows[0],['The first press','Registry visitor','I AGREE was pressed.']);
  assert.deepEqual(rows[1],['Acquisitions',BY_ID.title.name,BY_ID.tunnel.name,fate.name]);
  assert.deepEqual(rows[2],['Fate cards',fate.name]);
  assert.deepEqual(rows[3],['Admission inventory','Greg','Nana Spitwell']);
  assert.deepEqual(rows.at(-2),['The last return','341 presses','832 Yes']);
  assert.deepEqual(rows.at(-1),['Spit in the Llama wormhole?','YES']);
});
