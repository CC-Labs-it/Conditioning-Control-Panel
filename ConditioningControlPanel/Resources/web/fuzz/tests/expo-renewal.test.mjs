import test from 'node:test';
import assert from 'node:assert/strict';
import {visitTicket,COUNTERFOILS} from '../ui/expo/ticket.js';
import {modelVisitor,departureEnding} from '../ui/expo/renewal-endings.js';
import {tillReceipt} from '../ui/expo/settlement.js';
test('Habitat has two impressions, twelve boxes fill, and a complete herd earns the photo',()=>{
  const data=new Map(),store={get:k=>data.get(k),set:(k,v)=>data.set(k,v)},t=visitTicket(store);
  t.issue();for(const mark of COUNTERFOILS.flat().filter(m=>m!=='Kept'))t.stamp(mark);
  assert.equal(t.count,11);assert.equal(t.complete,false);t.stamp('Kept');
  assert.equal(t.count,12);assert.equal(visitTicket(store).complete,true);
  const state={ticket:t,file:{herd:Array(12)}};
  assert.equal(departureEnding('renewal',state),'full');assert.equal(departureEnding('finger',state),'finger');
  t.withdraw('Kept');assert.equal(departureEnding('renewal',state),'renewal');
});
test('a deviation is retained as a swatch and contributes once to the printed settlement',()=>{
  const ticket=visitTicket();for(const id of ['agenda','queued','left-button','glass-kept','dome-kept','heater-kept'])ticket.book(id,id);
  assert.equal(modelVisitor(ticket),true);ticket.book('tap','Tapped',true);ticket.book('tap','Tapped',true);
  assert.equal(modelVisitor(ticket),false);
  const receipt=new Map(tillReceipt({ticket,file:{taps:100},presses:12},7));
  assert.equal(receipt.get('Total agreements'),'7');assert.equal(receipt.get('Deviations converted to Yes'),'1');
  assert.equal(receipt.get('First Press register'),'112');assert.equal(receipt.get('History absorbed'),'7 of 40');
});
test('settlement survives re-entry and converts each new deviation once',()=>{
  const data=new Map(),store={get:k=>data.get(k),set:(k,v)=>data.set(k,v)};
  let ticket=visitTicket(store);
  ticket.book('comply','Used the queue');ticket.book('first','Tapped the glass',true);
  assert.equal(ticket.balance,1);assert.equal(ticket.outstanding,1);assert.equal(ticket.settle(),2);
  ticket=visitTicket(store);assert.equal(ticket.balance,2);assert.equal(ticket.outstanding,0);
  ticket.book('next','Removed the rope',true);assert.equal(ticket.outstanding,1);
  assert.equal(ticket.settle(),3);assert.equal(ticket.settle(),3);assert.equal(ticket.ledger.size,3);
});
