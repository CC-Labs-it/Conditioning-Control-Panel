import test from 'node:test';
import assert from 'node:assert/strict';
import {hoofAttempt,hoofContacts,needsFinger} from '../ui/expo/hoof-lab.js';

test('the contact disc covers four actual centres and a visitor can hang it off the corner',()=>{
  assert.deepEqual(hoofContacts('checkbox',0,.38),[0,1,2,3]);
  assert.deepEqual(hoofContacts('checkbox',-.27,.64),[0]);
  assert.deepEqual(hoofContacts('checkbox',.27,.12),[3]);
  assert.deepEqual(hoofContacts('checkbox',.59,.02),[]);
  const edge=hoofAttempt(1,0);
  assert.deepEqual(edge,{edge:true,misses:0,page:'pictures',lines:['v4_hoof_edge','hoof_5']});
});

test('the attendant steps in on the third failed attempt and never describes two caps as four',()=>{
  const first=hoofAttempt(4,0),second=hoofAttempt(2,first.misses),third=hoofAttempt(4,second.misses);
  assert.equal(first.page,'checkbox');assert.equal(second.page,'checkbox');
  assert.deepEqual(first.lines,['hoof_3']);assert.deepEqual(second.lines,[]);
  assert.deepEqual(third,{misses:3,page:'pictures',lines:['hoof_4','hoof_7','hoof_5']});
  assert.deepEqual(hoofAttempt(2,0).lines,['hoof_4']);assert.equal(hoofAttempt(0,2),null);
});

test('the picture test uses its own nine smaller photo positions',()=>{
  assert.deepEqual(hoofContacts('pictures',-.11,.37),[1,3,4,5,7]);
  assert.deepEqual(hoofContacts('pictures',-.43,.68),[0]);
  assert.deepEqual(hoofContacts('pictures',.22,.05),[8]);
});

test('worn hooves cannot press switches and leave room exits, the turnstile and lab available',()=>{
  for(const name of ['press_main','expo_replay_slot_01','expo_control_greenhouse','alpaca_button'])assert.ok(needsFinger(name));
  for(const name of ['shop_door','turnstile_arm','hoof_submit','hoof_panel','habitat_glass','framed_contract'])assert.equal(needsFinger(name),false);
});
