import test from 'node:test';
import assert from 'node:assert/strict';
import {STOPS,stopSealed,trailMeter,trailTask} from '../ui/expo/trail-stops.js';
import {COUNTERFOILS,visitTicket} from '../ui/expo/ticket.js';

test('the printed trail and till agree on all twelve boxes and the eight-stamp threshold',()=>{
  assert.deepEqual(STOPS.map(s=>s.marks),COUNTERFOILS);
  const t=visitTicket();
  for(const mark of COUNTERFOILS.slice(0,6).flat())t.stamp(mark);
  t.stamp('Kept');assert.equal(t.count,6);
  t.stamp('Fed');assert.match(trailMeter(t.count),/1 more/);
  t.stamp('Blinked');assert.match(trailMeter(t.count),/exit is on sale/);
});
test('discovery and old ticket impressions reveal only the staff item actually found',()=>{
  let record={issued:true,stamps:['Read'],ledger:[]};
  const store={get:()=>record,set:(_,v)=>{record=v;}},t=visitTicket(store);
  assert.deepEqual(STOPS.slice(-3).map(s=>stopSealed(s,t)),[true,true,false]);
  t.discover('Unplugged');
  const resumed=visitTicket(store);
  assert.deepEqual(STOPS.slice(-3).map(s=>stopSealed(s,resumed)),[true,false,false]);
  assert.equal(resumed.count,1);
});
test('only newly earned marks or deviations make landing slips, including both parts of the Habitat box',()=>{
  const t=visitTicket(),events=[],off=t.subscribe(e=>events.push(e));
  t.stamp('Fed');t.stamp('Kept');t.stamp('Kept');t.book('chair','Sat in the chair',true);t.book('chair','Sat in the chair',true);
  t.withdraw('Kept');t.stamp('Kept',{restore:true});off();t.stamp('Read');
  assert.deepEqual(events,[{kind:'stamp',mark:'Fed',count:0,remaining:['Kept']},{kind:'stamp',mark:'Kept',count:1},{kind:'deviation',label:'Sat in the chair'}]);
});
test('hands take precedence over rooms, and directions follow the actual turnstile, balcony and reading progress',()=>{
  const p={x:.54,y:0,z:35.33},ticket=visitTicket();
  assert.match(trailTask('atrium',p,{ticket}).title,/turnstile/);
  ticket.issue();assert.deepEqual(trailTask('atrium',p,{ticket}).target,[7.8,41.1]);
  for(const id of ['service','hall','atrium','secret','shop']){
    const tea=trailTask(id,p,{carry:{key:'tea'},ticket});assert.match(tea.where,/Grand Hall, upstairs · \d+ m/);
    assert.match(trailTask(id,p,{carry:{key:'signed-form'},ticket}).where,/gift shop till/i);
  }
  const holding={carry:{key:'tea'},ticket},start=trailTask('service',{x:16.65,y:0,z:26.10},holding);
  assert.deepEqual(start.target,[16.74,28.90]);
  const below=trailTask('hall',{x:11,y:0,z:11.89},holding);assert.ok(Number(below.where.match(/(\d+) m/)[1])>15);
  const dock=trailTask('hall',{x:10.88,y:3.4,z:11.89},holding);assert.match(dock.where,/0 m/);
  assert.match(trailTask('secret',p,{readClauses:new Set(['1','2'])}).where,/2 of 8/);
  assert.match(trailTask('secret',p,{readClauses:new Set('12345678')}).title,/chair/);
});
