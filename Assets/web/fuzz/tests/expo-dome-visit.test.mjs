import test from 'node:test';
import assert from 'node:assert/strict';
import {domePace,domeVisit,TRANSLATION} from '../ui/expo/dome-visit.js';
import {visitTicket} from '../ui/expo/ticket.js';

test('the Stay ending needs a full minute at depth, then three uninterrupted seconds looking at the sign',()=>{
  const calls=[],visit={ticket:visitTicket()},d=domeVisit(visit,()=>{},e=>calls.push(e));
  assert.equal(domePace(0),1.5);assert.equal(domePace(4),.5);
  d.observe(210,true,1,0,true);assert.equal(d.inscription,null);
  d.observe(30,true,1,30,true);d.observe(30,true,1,30,true);
  assert.equal(d.inscription,'You may stop here.');
  d.observe(2,true,1,2,true);d.observe(.1,true,1,.1,false);assert.deepEqual(calls,[]);
  d.observe(3,true,1,3,true);d.observe(3,true,1,3,true);assert.deepEqual(calls,['stayed']);
  assert.equal(visit.ticket.stamps.has('Stayed'),true);d.observe(.1,false,1,0,false);assert.ok(d.afterglow>.99);
  d.observe(61,false,.7,0,false);assert.equal(d.afterglow,0);
  assert.equal(TRANSLATION,'It is fine. Have some grass.');
});
