import test from 'node:test';
import assert from 'node:assert/strict';
import {visitTicket,COUNTERFOILS} from '../ui/expo/ticket.js';
import {modelVisitor,departureEnding} from '../ui/expo/renewal-endings.js';
import {tillReceipt,TILL_PROMPT,tillScreen} from '../ui/expo/settlement.js';
import {trailTask} from '../ui/expo/trail-stops.js';
import {routeFor} from '../ui/expo/rail-routes.js';
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
  const receipt=new Map(tillReceipt({ticket,file:{taps:100},presses:12},7,41));
  assert.equal(receipt.get('Total agreements'),'7');assert.equal(receipt.get('Deviations converted to Yes'),'1');
  assert.equal(receipt.get('First Press register'),'112');assert.equal(receipt.get('History absorbed'),'7 of 41');
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
test('the shop is one press of the till: three steps on the screen, then the green door for everyone',()=>{
  const visitor=(stamps,extra={})=>{
    const ticket=visitTicket();ticket.issue();for(const mark of COUNTERFOILS.slice(0,stamps).flat())ticket.stamp(mark);
    return {ticket,file:{taps:100},presses:12,...extra};
  };
  // At rest the screen is the whole shop in three steps, whatever the ticket holds.
  const steps=['1  Pick a souvenir','2  Press the till','3  Leave by the green door'];
  for(const stamps of [0,3,9])assert.deepEqual(tillScreen(visitor(stamps)),{title:'Gift shop',rows:steps});
  assert.deepEqual(tillScreen(visitor(9,{carry:{key:'souvenir:Pressed'}})).rows,steps);
  assert.equal(TILL_PROMPT,'Use the till');
  // After the press: the item, and the door. Seven stamps leave by the same door as nine.
  for(const stamps of [0,7,9]){
    const sold=visitor(stamps,{souvenir:stamps?'Pressed':'',contract:true,boughtExit:true});
    assert.deepEqual(tillScreen(sold),{title:'Settlement filed',rows:[stamps?'The moved button':'Day ticket','The green door is open.']});
  }
  // The guide names the same steps.
  const at={x:0,y:0,z:0},bought=visitor(9,{souvenir:'Pressed',contract:true,boughtExit:true});
  assert.equal(trailTask('shop',at,visitor(3)).title,'Choose one souvenir');
  assert.equal(trailTask('shop',at,visitor(3,{carry:{key:'souvenir:Spat'}})).title,'Carry your souvenir to the till');
  assert.equal(trailTask('shop',at,visitor(0)).title,'Use the till');
  assert.equal(trailTask('shop',at,bought).title,'Walk through the Renewal door');
  assert.equal(routeFor('souvenir:Pressed').finishes,true);
});
