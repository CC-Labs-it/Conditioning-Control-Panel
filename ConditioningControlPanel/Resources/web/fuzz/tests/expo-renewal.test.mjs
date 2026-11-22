import test from 'node:test';
import assert from 'node:assert/strict';
import {visitTicket,COUNTERFOILS} from '../ui/expo/ticket.js';
import {modelVisitor,departureEnding} from '../ui/expo/renewal-endings.js';
import {tillReceipt,tillPrompt,tillScreen} from '../ui/expo/settlement.js';
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
test('the one till button reads what its next press does, from the door to the Renewal door',()=>{
  const visitor=(stamps,extra={})=>{
    const ticket=visitTicket();ticket.issue();for(const mark of COUNTERFOILS.slice(0,stamps).flat())ticket.stamp(mark);
    return {ticket,file:{taps:100},presses:12,...extra};
  };
  const short=visitor(3),home=visitor(9);
  // At rest the screen says how far the ticket is from the way home, in the trail card's own words.
  assert.deepEqual(tillScreen(short).rows,['Carry one souvenir to the till.','Stock follows your ticket.','3 of 12. 5 more and you may go home.']);
  assert.equal(tillScreen(home).rows[2],'9 of 12. The exit is on sale at the gift shop.');
  assert.equal(tillPrompt(short),'Use the till');
  // A souvenir in the hand is what the button takes, at any step.
  for(const step of [{},{souvenir:'Spat'},{souvenir:'Spat',contract:true},{souvenir:'Spat',contract:true,boughtExit:true}])
    assert.equal(tillPrompt(visitor(9,{...step,carry:{key:'souvenir:Pressed'}}),0),'Set souvenir on the counter');
  assert.equal(tillPrompt(visitor(9,{carry:{key:'tea'}})),'Use the till');
  // Three questions, each with the one answer on offer.
  const asked=visitor(9,{souvenir:'Pressed'});
  assert.deepEqual([0,1,2].map(page=>[tillScreen(asked,page).title,tillPrompt(asked,page)]),
    [['Add a bag?','YES'],['Round up to full agreement?','YES'],['Stop being asked?','NO']]);
  assert.deepEqual(tillScreen(asked,0).rows,['The moved button','YES']);
  assert.equal(tillPrompt(asked,1,true),'Listening');
  // Settlement filed: the next press is the Exit with eight stamps, the day ticket under eight. No door loops between.
  assert.equal(tillPrompt(visitor(9,{souvenir:'Pressed',contract:true}),3),'Buy Exit');
  assert.equal(tillPrompt(visitor(8,{souvenir:'Pressed',contract:true}),3),'Buy Exit');
  assert.equal(tillPrompt(visitor(7,{souvenir:'Pressed',contract:true}),3),'Collect day ticket');
  assert.deepEqual(tillScreen(visitor(7,{souvenir:'Pressed',contract:true}),3),{title:'Settlement filed',rows:['7 of 12 stamps','Day ticket available','0 Yes, settled']});
  assert.equal(tillScreen(visitor(9,{souvenir:'Pressed',contract:true}),3).rows[1],'Exit sold separately');
  const bought=visitor(9,{souvenir:'Pressed',contract:true,boughtExit:true});
  assert.equal(tillPrompt(bought,3),'Use the till');assert.equal(tillScreen(bought,3).title,'Renewal');
  // The guide names the same steps and never the green door.
  const at={x:0,y:0,z:0};
  assert.equal(trailTask('shop',at,visitor(3)).title,'Choose one souvenir');
  assert.equal(trailTask('shop',at,asked).title,'Answer the question');
  assert.equal(trailTask('shop',at,visitor(9,{souvenir:'Pressed',contract:true})).title,'Buy Exit');
  assert.equal(trailTask('shop',at,visitor(7,{souvenir:'Pressed',contract:true})).title,'Collect a day ticket');
  assert.equal(trailTask('shop',at,bought).title,'Walk through the Renewal door');
  assert.equal(routeFor('souvenir:Pressed').finishes,true);
});
